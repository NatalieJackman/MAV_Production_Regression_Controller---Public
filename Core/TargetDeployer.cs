using Crestron.SimplSharp.Ssh.Sftp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Text;

namespace MAV.ProductionRegressionController
{
    public sealed class DeploymentResult
    {
        public bool SlotWasOccupied { get; set; }
        public string BackupCpzPath { get; set; }
        public string BackupConfigPath { get; set; }
        public string ProcessorStateBefore { get; set; }
        public string ProcessorStateAfter { get; set; }
    }

    public sealed class TargetDeployer
    {
        private readonly ProcessorTransport _transport;
        private readonly Html5Deployer _html5;
        private readonly ControllerLogger _log;

        public TargetDeployer(ProcessorTransport transport, Html5Deployer html5, ControllerLogger log)
        {
            _transport = transport; _html5 = html5; _log = log;
        }

        public string ScanSlots(TargetSettings target)
        {
            return _transport.ExecuteConsole(target, new[] { "progreg", "appstat" }, 15000);
        }

        public DeploymentResult Deploy(RunState state)
        {
            TargetSettings target = state.Target;
            PackageInfo package = state.Package;
            if (target.ProgramSlot < 1 || target.ProgramSlot > 10) throw new InvalidOperationException("Program slot must be between 1 and 10.");
            if (state.SelfTarget && target.ProgramSlot == state.ControllerSlot)
                throw new InvalidOperationException("Refusing to overwrite the Regression Controller's own program slot " + state.ControllerSlot + ".");

            DeploymentResult result = new DeploymentResult();
            string remoteProgram = string.Format("/program{0:d2}", target.ProgramSlot);
            string configRemote = package.PackageType == MavPackageType.VtcFarm
                ? "/user/Config/VTCFarm/SystemDefinition.json"
                : "/user/Config/SystemDefinition.json";

            state.Phase = "Preflight"; state.Progress = 5;
            _log.Info("Preflight target=" + target.Address + " slot=" + target.ProgramSlot + " selfTarget=" + state.SelfTarget);
            _transport.EnsureDirectory(target, remoteProgram);
            _transport.EnsureDirectory(target, RemoteParent(configRemote));
            result.ProcessorStateBefore = ReadVerifiedRegistry(target);
            if (state.SelfTarget && !SlotLooksOccupied(result.ProcessorStateBefore, (int)state.ControllerSlot))
                throw new InvalidOperationException("Self-target PROGREG did not positively identify the controller slot. Deployment refused.");
            result.SlotWasOccupied = SlotLooksOccupied(result.ProcessorStateBefore, target.ProgramSlot);

            state.Phase = "Backup"; state.Progress = 12;
            string backupRoot = Path.Combine(state.EvidenceFolder, "Backup");
            Crestron.SimplSharp.CrestronIO.Directory.CreateDirectory(backupRoot);
            if (result.SlotWasOccupied)
            {
                string downloadedRoot = Path.Combine(backupRoot, "DownloadedProgram" + target.ProgramSlot.ToString("00"));
                List<string> downloadedFiles = new List<string>();
                int downloadedCount = _transport.DownloadDirectory(target, remoteProgram, downloadedRoot, downloadedFiles);
                if (downloadedCount <= 0) throw new InvalidOperationException("Target Program " + target.ProgramSlot + " was occupied but its deployed program directory downloaded with no files.");
                string recreatedRoot = Path.Combine(backupRoot, "RecreatedPrograms");
                Crestron.SimplSharp.CrestronIO.Directory.CreateDirectory(recreatedRoot);
                string recreatedCpz = Path.Combine(recreatedRoot, "app" + target.ProgramSlot.ToString("00") + ".cpz");
                RecreateAndVerifyCpz(downloadedRoot, downloadedFiles, recreatedCpz);
                result.BackupCpzPath = recreatedCpz;
                _log.Pass("Program " + target.ProgramSlot + " downloaded, recreated as CPZ, and verified. SHA-256=" + FileHash.Sha256(recreatedCpz));
            }
            if (_transport.Exists(target, configRemote))
            {
                string local = Path.Combine(backupRoot, "SystemDefinition.json");
                _transport.Download(target, configRemote, local);
                result.BackupConfigPath = local;
                _log.Pass("Existing configuration backed up.");
            }
            if (result.SlotWasOccupied && string.IsNullOrEmpty(result.BackupCpzPath) && !target.AllowReplaceWithoutRecoverableCpz)
                throw new InvalidOperationException("Target slot appears occupied but no recoverable CPZ/LPZ package was found.");

            // Commit gate: open through the same CrestronIO contract that SFTP uses; compare immutable identity.
            LocalFileIO.Probe(package.CpzPath);
            LocalFileIO.Probe(package.ConfigPath);
            if (!string.Equals(FileHash.Sha256(package.CpzPath), package.CpzSha256, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(FileHash.Sha256(package.ConfigPath), package.ConfigSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Staged package changed or cannot be reopened through CrestronIO; target left untouched.");
            Newtonsoft.Json.Linq.JObject.Parse(LocalFileIO.ReadAllText(package.ConfigPath, 8L*1024L*1024L));
            if (!string.IsNullOrWhiteSpace(result.BackupCpzPath)) LocalFileIO.Probe(result.BackupCpzPath);
            if (!string.IsNullOrWhiteSpace(result.BackupConfigPath)) LocalFileIO.Probe(result.BackupConfigPath);
            _log.Pass("Deployment commit preflight: CrestronIO reopened CPZ/config/backups, identity verified before KILLPROG.");

            state.Phase = "Replace slot"; state.Progress = 25;
            bool modified = false;
            try
            {
                if (result.SlotWasOccupied)
                {
                    modified = true; // KILLPROG is asynchronous: the processor may change even if SSH times out.
                    _transport.ExecuteConsole(target, new[] { "killprog -p:" + target.ProgramSlot }, 15000);
                    WaitForSlotState(target, target.ProgramSlot, false, 90000);
                }
                modified = true; // Any subsequent deletion/upload enters rollback, including on transport error.
                _transport.EnsureDirectory(target, remoteProgram);
                foreach (SftpFile f in _transport.ListDirectory(target, remoteProgram))
                {
                    if (!f.IsRegularFile) continue;
                    string ext = Path.GetExtension(f.Name).ToLowerInvariant();
                    if (ext == ".cpz" || ext == ".lpz") _transport.Delete(target, f.FullName);
                }

                state.Phase = "Upload"; state.Progress = 38;
                string remoteCpz = remoteProgram + "/" + package.CpzFileName;
                _transport.Upload(target, package.CpzPath, remoteCpz);
                _transport.Upload(target, package.ConfigPath, configRemote);
                modified = true;

                state.Phase = "HTML5"; state.Progress = 52;
                _html5.DeployForPackage(target, package.PackageType);

                state.Phase = "Program load"; state.Progress = 64;
                _transport.ExecuteConsole(target, new[] { "progload -p:" + target.ProgramSlot }, 30000);
                result.ProcessorStateAfter = WaitForSlotState(target, target.ProgramSlot, true, 120000);
                _log.Pass("Program " + target.ProgramSlot + " loaded and visible after deployment.");
                return result;
            }
            catch
            {
                if (modified && !string.IsNullOrEmpty(result.BackupCpzPath))
                {
                    try { Rollback(target, package, result); }
                    catch (Exception rollbackEx) { _log.Error("Automatic rollback failed; manual intervention needed: " + rollbackEx); }
                }
                else if (modified) _log.Error("Target may have changed, but no backup CPZ exists. Verify slot/target state manually.");
                throw;
            }
        }

        private void Rollback(TargetSettings target, PackageInfo package, DeploymentResult result)
        {
            _log.Warn("Attempting automatic rollback of Program " + target.ProgramSlot + ".");
            string current = ReadVerifiedRegistry(target);
            if (SlotLooksOccupied(current, target.ProgramSlot))
            {
                _transport.ExecuteConsole(target, new[] { "killprog -p:" + target.ProgramSlot }, 15000);
                WaitForSlotState(target, target.ProgramSlot, false, 90000);
            }
            string remoteProgram = string.Format("/program{0:d2}", target.ProgramSlot);
            _transport.EnsureDirectory(target, remoteProgram);
            _transport.Upload(target, result.BackupCpzPath, remoteProgram + "/" + Path.GetFileName(result.BackupCpzPath));
            if (!string.IsNullOrEmpty(result.BackupConfigPath))
            {
                string configRemote = package.PackageType == MavPackageType.VtcFarm ? "/user/Config/VTCFarm/SystemDefinition.json" : "/user/Config/SystemDefinition.json";
                _transport.Upload(target, result.BackupConfigPath, configRemote);
            }
            _transport.ExecuteConsole(target, new[] { "progload -p:" + target.ProgramSlot }, 30000);
            WaitForSlotState(target, target.ProgramSlot, true, 120000);
            _log.Pass("Rollback package reloaded and independently confirmed via PROGREG.");
        }

        private static void RecreateAndVerifyCpz(string sourceDirectory, IList<string> sourceFiles, string cpzPath)
        {
            // SFTP downloads used CrestronIO. Do not switch to System.IO.ZipFile.CreateFromDirectory:
            // System.IO's CP4N filesystem view was the cause of the Preview 008/009 missing-file bug.
            if (sourceFiles == null || sourceFiles.Count == 0) throw new InvalidOperationException("Cannot archive an empty rollback package.");
            using (MemoryStream buffer = new MemoryStream())
            {
                using (ZipArchive archive = new ZipArchive(buffer, ZipArchiveMode.Create, true))
                {
                    foreach (string source in sourceFiles)
                    {
                        LocalFileIO.Probe(source);
                        string relative = LocalPaths.Normalize(source).Substring(LocalPaths.Normalize(sourceDirectory).TrimEnd('/').Length).TrimStart('/');
                        ZipArchiveEntry entry = archive.CreateEntry(relative, CompressionLevel.Optimal);
                        using (System.IO.Stream output = entry.Open())
                        using (Crestron.SimplSharp.CrestronIO.FileStream input = new Crestron.SimplSharp.CrestronIO.FileStream(
                            LocalPaths.Normalize(source), Crestron.SimplSharp.CrestronIO.FileMode.Open,
                            Crestron.SimplSharp.CrestronIO.FileAccess.Read, Crestron.SimplSharp.CrestronIO.FileShare.Read))
                        {
                            byte[] bytes = new byte[32768]; int n;
                            while ((n = input.Read(bytes, 0, bytes.Length)) > 0) output.Write(bytes, 0, n);
                        }
                    }
                }
                if (buffer.Length > 256L * 1024L * 1024L)
                    throw new InvalidOperationException("Rollback archive exceeds 256 MB controller memory safety threshold. Target untouched.");
                LocalFileIO.WriteAllBytes(cpzPath, buffer.ToArray());
            }
            LocalFileIO.Probe(cpzPath);
            using (MemoryStream compressed = new MemoryStream(LocalFileIO.ReadAllBytes(cpzPath, 256L*1024L*1024L)))
            using (ZipArchive verify = new ZipArchive(compressed, ZipArchiveMode.Read, false))
            {
                Dictionary<string, ZipArchiveEntry> entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (ZipArchiveEntry entry in verify.Entries) entries[entry.FullName] = entry;
                foreach (string source in sourceFiles)
                {
                    string relative = LocalPaths.Normalize(source).Substring(LocalPaths.Normalize(sourceDirectory).TrimEnd('/').Length).TrimStart('/');
                    ZipArchiveEntry member;
                    if (!entries.TryGetValue(relative, out member) || member.Length != LocalFileIO.Length(source))
                        throw new InvalidOperationException("Rollback CPZ verification failed: " + relative);
                }
            }
        }

        private string ReadVerifiedRegistry(TargetSettings target)
        {
            string output = _transport.ExecuteConsole(target, new[] { "progreg" }, 15000);
            if (string.IsNullOrWhiteSpace(output) || output.IndexOf("CP4N>", StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException("PROGREG response missing processor prompt; slot state is unknown. No destructive action permitted.");
            bool registered = Regex.IsMatch(output, @"(?im)^\s*Program\s+\d+\s+is\s+registered\b");
            bool explicitlyEmpty = Regex.IsMatch(output, @"(?i)(no\s+(?:programs?\s+(?:are\s+)?registered|registered)|no\s+registered\s+programs?|0\s+programs?\s+registered)");
            if (!registered && !explicitlyEmpty)
                throw new InvalidOperationException("PROGREG did not provide an authoritative registry/empty result. Refusing to infer an empty slot from partial output.");
            return output;
        }
        private string WaitForSlotState(TargetSettings target, int slot, bool occupied, int timeoutMs)
        {
            int stable = 0; string last = string.Empty;
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    last = ReadVerifiedRegistry(target);
                    if (SlotLooksOccupied(last, slot) == occupied) stable++;
                    else stable = 0;
                    if (stable >= 2) return last;
                }
                catch (Exception ex) { stable = 0; _log.Warn("Transient/incomplete PROGREG while waiting for Program " + slot + ": " + ex.Message); }
                Crestron.SimplSharpPro.CrestronThread.Thread.Sleep(1000);
            }
            throw new InvalidOperationException("Timed out waiting for two stable PROGREG confirmations: slot=" + slot + " occupied=" + occupied + ". Processor state may be transitioning; rollback or operator recovery required.");
        }
        private static bool SlotLooksOccupied(string output, int slot)
        {
            return Regex.IsMatch(output ?? string.Empty, @"(?im)^\s*Program\s+" + slot + @"\s+is\s+registered\b");
        }
        private static string RemoteParent(string path) { int i = path.LastIndexOf('/'); return i > 0 ? path.Substring(0, i) : string.Empty; }
    }
}
