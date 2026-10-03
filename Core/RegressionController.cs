using Crestron.SimplSharpPro.CrestronThread;
using System;
using System.IO;

namespace MAV.ProductionRegressionController
{
    public sealed class RegressionController
    {
        public readonly ControllerLogger Log = new ControllerLogger();
        public readonly TargetConsoleTranscript Console = new TargetConsoleTranscript();
        public TargetSdkLogObserver TargetSdkLog { get; private set; }
        public readonly UploadStore Uploads = new UploadStore();
        public StagedConfigurationService Configuration { get; private set; }
        public readonly RunState State = new RunState();
        public CloudPackageService Cloud { get; private set; }
        public OfflineBundleService OfflineBundles { get; private set; }

        private ProcessorTransport _transport;
        private CwsClient _cws;
        private TargetDeployer _deployer;
        private RegressionRunner _runner;
        private EvidenceExporter _exporter;
        private RegressionWebServer _web;
        private Thread _worker;
        private readonly object _operationSync = new object();
        private bool _workflowBusy;
        public T WithIdleMutation<T>(Func<T> action)
        {
            lock (_operationSync)
            {
                if (_workflowBusy) throw new InvalidOperationException("A regression workflow is running; package/config mutations and manual console writes are locked.");
                return action();
            }
        }
        private Thread _cloudWorker;
        private LiveConsoleSession _liveConsole;

        public void Start()
        {
            Configuration = new StagedConfigurationService(Uploads);
            _transport = new ProcessorTransport(Log, Console);
            _liveConsole = new LiveConsoleSession(Console);
            Cloud = new CloudPackageService(Log);
            OfflineBundles = new OfflineBundleService(Log);
            _cws = new CwsClient(Log);
            TargetSdkLog = new TargetSdkLogObserver(_cws, Log);
            _deployer = new TargetDeployer(_transport, new Html5Deployer(_transport, Log), Log);
            _runner = new RegressionRunner(Log, _cws, TargetSdkLog);
            _exporter = new EvidenceExporter(Log);
            _web = new RegressionWebServer(this);
            _web.Start();
            State.ControllerSlot = SelfTargetGuard.ControllerSlot;
            Log.BeginSession("controller-startup-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            Log.Info("Regression Controller online. Controller slot=" + State.ControllerSlot + ". Static IPIDs used by controller: none.");
        }



        public OfflineBundleInventory GetOfflineBundleInventory()
        {
            if (OfflineBundles == null) throw new InvalidOperationException("Offline bundle service is not initialized.");
            return OfflineBundles.Inventory();
        }

        public string StartOfflineBundleUpload(string fileName, long size)
        {
            if (OfflineBundles == null) throw new InvalidOperationException("Offline bundle service is not initialized.");
            return OfflineBundles.StartUpload(fileName, size);
        }

        public long AppendOfflineBundleChunk(string base64)
        {
            if (OfflineBundles == null) throw new InvalidOperationException("Offline bundle service is not initialized.");
            return OfflineBundles.AppendChunk(base64);
        }

        public OfflineBundleInventory CompleteOfflineBundleUpload()
        {
            if (OfflineBundles == null) throw new InvalidOperationException("Offline bundle service is not initialized.");
            return OfflineBundles.CompleteUpload();
        }

        public PackageInfo StageOfflineBundlePackage(string cpzRelativePath, string configRelativePath)
        {
            if (OfflineBundles == null) throw new InvalidOperationException("Offline bundle service is not initialized.");
            OfflineBundles.Stage(cpzRelativePath, configRelativePath, Uploads);
            Uploads.EnsureComplete();
            return AnalyzePackage();
        }

        public CloudInventory GetCloudInventory()
        {
            if (Cloud == null) throw new InvalidOperationException("Cloud service is not initialized.");
            return Cloud.Inventory();
        }

        public DropboxAuthorizationStatus GetDropboxAuthorizationStatus()
        {
            if (Cloud == null) throw new InvalidOperationException("Cloud service is not initialized.");
            return Cloud.AuthorizationStatus();
        }

        public DropboxAuthorizationStart BeginDropboxAuthorization()
        {
            if (Cloud == null) throw new InvalidOperationException("Cloud service is not initialized.");
            return Cloud.BeginAuthorization();
        }

        public DropboxAuthorizationStatus CompleteDropboxAuthorization(string sessionId, string code)
        {
            if (Cloud == null) throw new InvalidOperationException("Cloud service is not initialized.");
            return Cloud.CompleteAuthorization(sessionId, code);
        }

        public DropboxAuthorizationStatus DisconnectDropboxAuthorization()
        {
            if (Cloud == null) throw new InvalidOperationException("Cloud service is not initialized.");
            Cloud.DisconnectAuthorization();
            return Cloud.AuthorizationStatus();
        }

        public void StartCloudSync(string sharedFolderUrl)
        {
            if (Cloud == null) throw new InvalidOperationException("Cloud service is not initialized.");
            CloudInventory current = Cloud.Inventory();
            if (current.SyncRunning) throw new InvalidOperationException("A cloud synchronization is already running.");
            _cloudWorker = new Thread(CloudWorker, sharedFolderUrl, Thread.eThreadStartOptions.Running);
        }

        private object CloudWorker(object state)
        {
            try { Cloud.Sync(Convert.ToString(state)); }
            catch { }
            return null;
        }

        public PackageInfo StageCloudPackage(string cpzRelativePath, string configRelativePath)
        {
            if (Cloud == null) throw new InvalidOperationException("Cloud service is not initialized.");
            Cloud.Stage(cpzRelativePath, configRelativePath, Uploads);
            Uploads.EnsureComplete();
            return AnalyzePackage();
        }

        public string ExecuteTargetConsole(string commands)
        {
            RequireTarget();
            if (string.IsNullOrWhiteSpace(commands)) throw new InvalidOperationException("Console command is required.");
            string[] lines = commands.Replace("\r", string.Empty).Split('\n');
            System.Collections.Generic.List<string> list = new System.Collections.Generic.List<string>();
            foreach (string line in lines)
            {
                string cmd = (line ?? string.Empty).Trim();
                if (cmd.Length == 0) continue;
                if (cmd.Length > 512) throw new InvalidOperationException("Console commands are limited to 512 characters per line.");
                list.Add(cmd);
            }
            if (list.Count == 0) throw new InvalidOperationException("Console command is required.");
            if (_liveConsole != null && _liveConsole.Connected)
            {
                foreach (string cmd in list) _liveConsole.Send(cmd);
                return "Sent to live console.";
            }
            return _transport.ExecuteConsole(State.Target, list, 30000);
        }

        public void ConnectTargetConsole()
        {
            RequireTarget();
            _liveConsole.Connect(State.Target);
        }

        public void DisconnectTargetConsole()
        {
            if (_liveConsole != null) _liveConsole.Disconnect();
        }

        public object GetTargetConsoleStatus()
        {
            return new { Connected = _liveConsole != null && _liveConsole.Connected, Target = _liveConsole == null ? null : _liveConsole.Target };
        }

        private string SendConsoleCommands(string[] commands, int timeoutMs)
        {
            if (_liveConsole != null && _liveConsole.Connected)
            {
                foreach (string cmd in commands) _liveConsole.Send(cmd);
                return "Sent to live console.";
            }
            return _transport.ExecuteConsole(State.Target, commands, timeoutMs);
        }

        public string ExecuteConsolePreset(string preset)
        {
            RequireTarget();
            string name = (preset ?? string.Empty).Trim().ToLowerInvariant();
            int slot = State.Target.ProgramSlot;
            if (name == "state") return SendConsoleCommands(new[] { "progreg", "appstat", "progcomments:" + slot, "err plogcurrent" }, 30000);
            if (name == "registry") return SendConsoleCommands(new[] { "progreg" }, 15000);
            if (name == "appstat") return SendConsoleCommands(new[] { "appstat" }, 15000);
            if (name == "comments") return SendConsoleCommands(new[] { "progcomments:" + slot }, 15000);
            if (name == "errors") return SendConsoleCommands(new[] { "err plogcurrent" }, 15000);
            if (name == "load") return SendConsoleCommands(new[] { "progload -p:" + slot }, 30000);
            if (name == "kill") return SendConsoleCommands(new[] { "killprog -p:" + slot }, 30000);
            throw new InvalidOperationException("Unknown console preset: " + preset);
        }

        public string ExportConsole() { return Console.ReadFullLog(); }

        private MavPackageType ObservationProfile()
        {
            if (State.Target != null && State.Target.TestProfile != MavPackageType.Unknown) return State.Target.TestProfile;
            return State.Package == null ? MavPackageType.Unknown : State.Package.PackageType;
        }

        public void ConnectTargetDiagnostics()
        {
            RequireTarget();
            MavPackageType profile = ObservationProfile();
            if (profile == MavPackageType.Unknown)
                throw new InvalidOperationException("Select an existing Farm/Room test profile or stage a package first.");
            string folder = System.IO.Path.Combine(LocalPaths.Evidence, "manual-target-observation-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            TargetSdkLog.Start(State.Target, profile, folder);
        }

        public void DisconnectTargetDiagnostics() { if (TargetSdkLog != null) TargetSdkLog.Stop(); }
        public string ExportTargetDiagnostics() { return TargetSdkLog == null ? string.Empty : TargetSdkLog.ExportRecent(); }
        public string ExportCombined() { return _exporter.CombinedTranscript(Log, TargetSdkLog); }


        public void Configure(SessionConfigureRequest request)
        {
            if (request == null) throw new ArgumentNullException("request");
            if (_liveConsole != null && _liveConsole.Connected) _liveConsole.Disconnect();
            if (TargetSdkLog != null) TargetSdkLog.Stop();
            if (string.IsNullOrWhiteSpace(request.Address)) throw new InvalidOperationException("Target address is required.");
            if (string.IsNullOrWhiteSpace(request.Username)) throw new InvalidOperationException("Target username is required.");
            if (request.ProgramSlot < 1 || request.ProgramSlot > 10) throw new InvalidOperationException("Target slot must be 1-10.");
            RunMode mode = string.Equals(request.RunMode, "health", StringComparison.OrdinalIgnoreCase) ? RunMode.HealthCheck : RunMode.FullRegression;
            MavPackageType testProfile = MavPackageType.Unknown;
            if (string.Equals(request.TestProfile, "farm", StringComparison.OrdinalIgnoreCase)) testProfile = MavPackageType.VtcFarm;
            else if (string.Equals(request.TestProfile, "room", StringComparison.OrdinalIgnoreCase)) testProfile = MavPackageType.Room;
            TargetSettings target = new TargetSettings
            {
                Address = request.Address.Trim(), Username = request.Username.Trim(), Password = request.Password ?? string.Empty,
                ProgramSlot = request.ProgramSlot, UseHttpsCws = request.UseHttpsCws, AllowUntrustedHttps = request.AllowUntrustedHttps,
                AllowReplaceWithoutRecoverableCpz = request.AllowReplaceWithoutRecoverableCpz, RunMode = mode, TestProfile = testProfile
            };
            bool self = SelfTargetGuard.IsSelf(target.Address);
            if (self && target.ProgramSlot == SelfTargetGuard.ControllerSlot) throw new InvalidOperationException("Target slot is this Regression Controller's protected slot.");
            State.Target = target; State.SelfTarget = self; State.ControllerSlot = SelfTargetGuard.ControllerSlot;
            Log.Info("Target configured: " + target.Address + " Program " + target.ProgramSlot + (self ? " (self-target)" : ""));
        }

        public object ReadStagedConfiguration() { return Configuration.Read(); }
        public object ValidateStagedConfiguration(string jsonText) { return Configuration.Validate(jsonText); }
        public object SaveStagedConfiguration(string jsonText, string expectedSha256)
        {
            if (State.Status == RunStatus.Deploying || State.Status == RunStatus.WaitingForCws || State.Status == RunStatus.Testing)
                throw new InvalidOperationException("Cannot edit the staged configuration while deployment or regression is running.");
            object result = Configuration.Save(jsonText, expectedSha256);
            AnalyzePackage(); // refresh the authoritative configuration hash and package classification.
            Log.Info("Staged SystemDefinition edited and reanalyzed; no live processor configuration was changed.");
            return result;
        }

        public PackageInfo AnalyzePackage()
        {
            PackageInfo package = PackageClassifier.Analyze(Uploads.CpzPath, Uploads.ConfigPath);
            State.Package = package;
            State.Status = RunStatus.Ready;
            State.Phase = "Ready";
            State.Detail = package.PackageType + " detected: " + package.DetectionReason;
            Log.Pass("Package detected as " + package.PackageType + ". CPZ SHA-256=" + package.CpzSha256 + " Config SHA-256=" + package.ConfigSha256);
            return package;
        }

        public string ScanSlots()
        {
            RequireTarget();
            return _deployer.ScanSlots(State.Target);
        }

        public void StartWorkflow(bool deployFirst, bool maintenanceAcknowledged)
        {
            lock (_operationSync)
            {
                if (_workflowBusy) throw new InvalidOperationException("A regression operation is already running.");
            RequireTarget();
            if (State.Target.RunMode == RunMode.FullRegression && !maintenanceAcknowledged)
                throw new InvalidOperationException("Full Regression requires explicit confirmation of an authorized maintenance window before resource-affecting CWS commands can run.");
            // An explicitly selected existing-target profile takes priority for Test Loaded Program.
            // Deploy & Test always uses the actual staged package, even after a prior test-only run.
            if (deployFirst && (State.Package == null || string.IsNullOrEmpty(State.Package.CpzPath))) AnalyzePackage();
            else if (!deployFirst && State.Target.TestProfile != MavPackageType.Unknown)
                State.Package = new PackageInfo { PackageType = State.Target.TestProfile, SystemName = "Existing target program", DetectionReason = "Explicit external test profile; no CPZ deployment." };
            else if (State.Package == null)
                throw new InvalidOperationException("Stage a package, or select Farm/Room under Test Profile to test an existing target program without uploading anything.");
            if (State.Package.PackageType == MavPackageType.Unknown) throw new InvalidOperationException("Test profile could not be determined from the configuration.");
            if (deployFirst && (string.IsNullOrEmpty(State.Package.CpzPath) || string.IsNullOrEmpty(State.Package.ConfigPath)))
                throw new InvalidOperationException("Deploy & Test requires staged CPZ and JSON files. Use Test Loaded Program for external-only validation.");
            if (deployFirst)
            {
                Uploads.EnsureComplete();
                if (!string.Equals(FileHash.Sha256(State.Package.CpzPath), State.Package.CpzSha256, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(FileHash.Sha256(State.Package.ConfigPath), State.Package.ConfigSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Staged CPZ or JSON changed since analysis. Reanalyze before deployment.");
            }
            State.SessionId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + State.Package.PackageType.ToString();
            State.StartedUtc = DateTime.UtcNow; State.CompletedUtc = null; State.AbortRequested = false; State.Checks.Clear();
            State.EvidenceFolder = Path.Combine(LocalPaths.Evidence, State.SessionId);
            Crestron.SimplSharp.CrestronIO.Directory.CreateDirectory(State.EvidenceFolder);
            Log.BeginSession(State.SessionId);
            State.Status = deployFirst ? RunStatus.Deploying : RunStatus.WaitingForCws;
            State.Phase = deployFirst ? "Deployment" : "CWS readiness";
            State.Progress = 1;
            // Set the latch BEFORE starting either observer or workflow worker.
            _workflowBusy = true;
            try
            {
                TargetSdkLog.Start(State.Target, State.Package.PackageType, State.EvidenceFolder);
                _worker = new Thread(Worker, deployFirst, Thread.eThreadStartOptions.Running);
            }
            catch { _workflowBusy = false; State.Status = RunStatus.Failed; throw; }
            }
        }

        private object Worker(object state)
        {
            bool deployFirst = state is bool && (bool)state;
            try
            {
                Log.Info("Workflow started. DeployFirst=" + deployFirst + " Mode=" + State.Target.RunMode);
                _runner.BeginWorkflow(State);
                if (deployFirst) _deployer.Deploy(State);
                if (State.AbortRequested) throw new OperationCanceledException("Abort requested.");
                State.Status = RunStatus.WaitingForCws;
                _runner.WaitForCws(State);
                if (State.AbortRequested) throw new OperationCanceledException("Abort requested.");
                State.Status = RunStatus.Testing;
                _runner.Run(State);

                int fail = 0, warn = 0;
                lock (State.Checks) foreach (RegressionCheck c in State.Checks) { if (c.Status == "FAIL") fail++; else if (c.Status == "WARN") warn++; }
                State.Status = fail > 0 ? RunStatus.Failed : warn > 0 ? RunStatus.Warning : RunStatus.Passed;
                State.Progress = 100; State.Phase = "Complete";
                State.Detail = fail > 0 ? "Regression failed." : warn > 0 ? "Regression passed with warnings." : "Regression passed.";
            }
            catch (OperationCanceledException ex)
            {
                State.Status = RunStatus.Aborted; State.Phase = "Aborted"; State.Detail = ex.Message; Log.Warn(ex.Message);
            }
            catch (Exception ex)
            {
                State.Status = RunStatus.Failed; State.Phase = "Failed"; State.Detail = ex.Message; Log.Error("Workflow failed: " + ex);
            }
            finally
            {
                State.CompletedUtc = DateTime.UtcNow;
                try { TargetSdkLog.CaptureNow(); _exporter.WriteCombined(State, TargetSdkLog); _exporter.WriteMetadata(State, TargetSdkLog); }
                catch (Exception ex) { Log.Warn("Unable to write evidence metadata: " + ex.Message); }
                lock (_operationSync) _workflowBusy = false;
            }
            return null;
        }

        public void Abort() { State.AbortRequested = true; Log.Warn("Abort requested by operator."); }
        public string ExportLog() { return Log.ReadFullLog(); }
        public string ExportReport()
        {
            if (string.IsNullOrEmpty(State.EvidenceFolder)) return "{\"Message\":\"No regression report has been generated yet.\"}";
            string path = Path.Combine(State.EvidenceFolder, "RegressionReport.json");
            return LocalFileIO.Exists(path) ? LocalFileIO.ReadAllText(path, 16L*1024L*1024L) : "{\"Message\":\"Report will be available when the current regression completes.\"}";
        }
        public string GetConsoleStatus() { return "Status=" + State.Status + " Phase=" + State.Phase + " Progress=" + State.Progress + "% Target=" + (State.Target == null ? "not set" : State.Target.Address + ":Program" + State.Target.ProgramSlot); }
        private void RequireTarget() { if (State.Target == null) throw new InvalidOperationException("Configure the target first."); }
    }
}
