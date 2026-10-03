using System.IO;
using FileStream = Crestron.SimplSharp.CrestronIO.FileStream;
using FileMode = Crestron.SimplSharp.CrestronIO.FileMode;
using FileAccess = Crestron.SimplSharp.CrestronIO.FileAccess;
using FileShare = Crestron.SimplSharp.CrestronIO.FileShare;
using File = Crestron.SimplSharp.CrestronIO.File;
using Directory = Crestron.SimplSharp.CrestronIO.Directory;
using Crestron.SimplSharpPro.CrestronThread;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;

namespace MAV.ProductionRegressionController
{
    // External observer: reads the TARGET SDK diagnostic journal over CWS. It never
    // registers device IPIDs, calls the target's native self-test, or writes to its log.
    public sealed class TargetSdkLogEntry
    {
        public long LocalSequence { get; set; }
        public int Epoch { get; set; }
        public long TargetSequence { get; set; }
        public DateTime ObservedUtc { get; set; }
        public string TargetTimestamp { get; set; }
        public string Level { get; set; }
        public string Message { get; set; }
        public bool IsMultiline { get; set; }
        public string TestStep { get; set; }
    }

    public sealed class TargetLogStepEvidence
    {
        public string Step { get; set; }
        public long FromLocalSequence { get; set; }
        public long ToLocalSequence { get; set; }
        public int EventsObserved { get; set; }
        public long FirstTargetSequence { get; set; }
        public long LastTargetSequence { get; set; }
        public bool FeedConnected { get; set; }
        public int ErrorEvents { get; set; }
        public int WarningEvents { get; set; }
    }

    public sealed class TargetSdkLogObserver
    {
        private readonly object _sync = new object();
        private readonly object _pollSync = new object();
        private readonly CwsClient _cws;
        private readonly ControllerLogger _controllerLog;
        private readonly List<TargetSdkLogEntry> _entries = new List<TargetSdkLogEntry>();
        private TargetSettings _target;
        private string _root;
        private string _logPath;
        private string _currentStep = string.Empty;
        private int _generation;
        private bool _active;
        private bool _connected;
        private bool _everConnected;
        private int _epoch;
        private long _cursor;
        private long _localSequence;
        private long _observedCount;
        private string _lastError = string.Empty;
        private DateTime _lastSyncUtc;
        private Thread _thread;

        public TargetSdkLogObserver(CwsClient cws, ControllerLogger controllerLog)
        {
            _cws = cws;
            _controllerLog = controllerLog;
        }

        public bool Active { get { lock (_sync) return _active; } }
        public bool Connected { get { lock (_sync) return _connected; } }
        public string LogPath { get { lock (_sync) return _logPath; } }
        public long LastLocalSequence { get { lock (_sync) return _localSequence; } }
        public long ObservedCount { get { lock (_sync) return _observedCount; } }

        public object Status()
        {
            lock (_sync)
            {
                return new
                {
                    Active = _active, Connected = _connected, EverConnected = _everConnected,
                    Target = _target == null ? string.Empty : _target.Address,
                    Path = _root, Epoch = _epoch, TargetSequence = _cursor,
                    LocalSequence = _localSequence, ObservedCount = _observedCount,
                    LastSyncUtc = _lastSyncUtc, LastError = _lastError, LogPath = _logPath,
                    CurrentStep = _currentStep
                };
            }
        }

        public void Start(TargetSettings target, MavPackageType packageType, string evidenceFolder)
        {
            if (target == null || string.IsNullOrWhiteSpace(target.Address))
                throw new InvalidOperationException("Configure a target before observing its SDK log.");
            if (packageType != MavPackageType.Room && packageType != MavPackageType.VtcFarm)
                throw new InvalidOperationException("Select an explicit Room or VTC Farm test profile before connecting target diagnostics.");
            if (string.IsNullOrWhiteSpace(evidenceFolder))
                throw new InvalidOperationException("An evidence destination is required for the target SDK log.");
            Directory.CreateDirectory(evidenceFolder);
            lock (_sync)
            {
                _generation++;
                _active = true;
                _connected = false;
                _everConnected = false;
                _target = new TargetSettings {
                    Address = target.Address, Username = target.Username, Password = target.Password,
                    ProgramSlot = target.ProgramSlot, UseHttpsCws = target.UseHttpsCws,
                    AllowUntrustedHttps = target.AllowUntrustedHttps
                };
                _root = packageType == MavPackageType.VtcFarm ? "/cws/vtcfarm" : "/cws/api";
                _logPath = System.IO.Path.Combine(evidenceFolder, "Target-SDK-Logs.jsonl");
                _entries.Clear(); _cursor = 0; _localSequence = 0; _observedCount = 0;
                _epoch = 0; _lastError = string.Empty; _lastSyncUtc = DateTime.MinValue;
                _currentStep = string.Empty;
                using (FileStream fs = new FileStream(_logPath, FileMode.Create, FileAccess.Write, FileShare.Read)) fs.Flush();
                _thread = new Thread(PollWorker, _generation, Thread.eThreadStartOptions.Running);
            }
            _controllerLog.Info("External SDK log observer started for " + target.Address + " " + _root + ".");
        }

        public void Stop()
        {
            lock (_sync) { _generation++; _active = false; _connected = false; _currentStep = string.Empty; }
        }

        private object PollWorker(object generationObject)
        {
            int generation = (int)generationObject;
            while (true)
            {
                lock (_sync) if (!_active || generation != _generation) break;
                CaptureNow();
                Thread.Sleep(850);
            }
            return null;
        }

        // May also be called synchronously from a regression step to tighten event correlation.
        // Network errors never make a black-box action pass or fail by themselves.
        public void CaptureNow()
        {
            if (!System.Threading.Monitor.TryEnter(_pollSync)) return;
            try
            {
                TargetSettings target; string root; int generation;
                lock (_sync)
                {
                    if (!_active || _target == null) return;
                    target = _target; root = _root; generation = _generation;
                }
                try
                {
                    JObject summary = _cws.GetQuiet(target, root + "/system/diagnostics/summary");
                    JToken payload = summary["Value"] ?? summary["value"];
                    if (payload == null || payload.Type != JTokenType.Object)
                        throw new InvalidOperationException("Target diagnostics summary did not return Value.");
                    JToken seqValue = payload["LogSequence"] ?? payload["logSequence"];
                    if (seqValue == null) throw new InvalidOperationException("Target diagnostics summary omitted LogSequence.");
                    long reportedSequence = seqValue.Value<long>();
                    lock (_sync)
                    {
                        if (generation != _generation || !_active) return;
                        if (_everConnected && reportedSequence < _cursor)
                        {
                            _epoch++; _cursor = 0;
                            _controllerLog.Warn("Target SDK journal sequence reset (program restart); new epoch=" + _epoch + ".");
                        }
                    }
                    // Journal is cursor based. Collect at most 8 x 500 per polling cycle,
                    // advancing by target sequence and deduplicating across polls.
                    for (int page = 0; page < 8; page++)
                    {
                        long after;
                        lock (_sync) { if (generation != _generation || !_active) return; after = _cursor; }
                        JObject response = _cws.PostQuiet(target, root + "/system/diagnostics/logs", new { AfterSequence = after, MaxEntries = 500 });
                        JArray events = response["Value"] as JArray ?? response["value"] as JArray;
                        if (events == null) throw new InvalidOperationException("Target diagnostics/logs must return a Value array.");
                        int accepted = 0;
                        foreach (JToken evt in events)
                        {
                            JToken id = evt["Sequence"] ?? evt["sequence"];
                            if (id == null) continue;
                            long sourceSequence = id.Value<long>();
                            lock (_sync)
                            {
                                if (generation != _generation || !_active) return;
                                if (sourceSequence <= _cursor) continue;
                                TargetSdkLogEntry entry = new TargetSdkLogEntry {
                                    LocalSequence = ++_localSequence, TargetSequence = sourceSequence,
                                    Epoch = _epoch, ObservedUtc = DateTime.UtcNow,
                                    TargetTimestamp = Convert.ToString(evt["Timestamp"] ?? evt["timestamp"] ?? ""),
                                    Level = Convert.ToString(evt["Level"] ?? evt["level"] ?? ""),
                                    Message = Convert.ToString(evt["Message"] ?? evt["message"] ?? ""),
                                    IsMultiline = Convert.ToString(evt["IsMultiline"] ?? evt["isMultiline"] ?? "false").Equals("true", StringComparison.OrdinalIgnoreCase),
                                    TestStep = _currentStep
                                };
                                _cursor = sourceSequence;
                                _observedCount++;
                                _entries.Add(entry);
                                if (_entries.Count > 7500) _entries.RemoveRange(0, _entries.Count - 7500);
                                AppendEvidence(entry);
                                accepted++;
                            }
                        }
                        if (events.Count < 500 || accepted == 0) break;
                    }
                    lock (_sync)
                    {
                        if (generation != _generation) return;
                        bool recovered = !_connected;
                        _connected = true; _everConnected = true;
                        _lastError = string.Empty; _lastSyncUtc = DateTime.UtcNow;
                        if (recovered) _controllerLog.Info("Target SDK diagnostic feed connected: " + root);
                    }
                }
                catch (Exception ex)
                {
                    lock (_sync)
                    {
                        if (generation != _generation) return;
                        if (_connected || string.IsNullOrEmpty(_lastError))
                            _controllerLog.Warn("Target SDK log feed waiting: " + ex.Message);
                        _connected = false; _lastError = ex.Message;
                    }
                }
            }
            finally { System.Threading.Monitor.Exit(_pollSync); }
        }

        private void AppendEvidence(TargetSdkLogEntry entry)
        {
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(entry) + "\r\n");
                using (FileStream fs = new FileStream(_logPath, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    fs.Write(bytes, 0, bytes.Length); fs.Flush();
                }
            }
            catch (Exception ex)
            {
                // Journal remains live even if the evidence filesystem becomes unavailable.
                if (_lastError != "Evidence write: " + ex.Message)
                    _controllerLog.Warn("Target diagnostic evidence write failure: " + ex.Message);
                _lastError = "Evidence write: " + ex.Message;
            }
        }

        public long BeginStep(string name)
        {
            CaptureNow();
            lock (_sync) { _currentStep = name ?? string.Empty; return _localSequence; }
        }

        public TargetLogStepEvidence EndStep(string name, long fromLocalSequence)
        {
            // Allow near-immediate target events to arrive, without pretending an absence
            // in the log proves a failed AV command.
            Thread.Sleep(180);
            CaptureNow();
            TargetLogStepEvidence result = new TargetLogStepEvidence {
                Step = name, FromLocalSequence = fromLocalSequence
            };
            lock (_sync)
            {
                result.ToLocalSequence = _localSequence;
                result.FeedConnected = _connected;
                foreach (TargetSdkLogEntry e in _entries)
                {
                    if (e.LocalSequence <= fromLocalSequence) continue;
                    if (result.EventsObserved == 0) result.FirstTargetSequence = e.TargetSequence;
                    result.LastTargetSequence = e.TargetSequence;
                    result.EventsObserved++;
                    if (IsError(e.Level)) result.ErrorEvents++;
                    else if (IsWarning(e.Level)) result.WarningEvents++;
                }
                if (_currentStep == name) _currentStep = string.Empty;
            }
            return result;
        }

        private static bool IsError(string level)
        {
            int n;
            if (int.TryParse(level, out n)) return n <= 1;
            return string.Equals(level, "critical", StringComparison.OrdinalIgnoreCase) || string.Equals(level, "error", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWarning(string level)
        {
            int n;
            if (int.TryParse(level, out n)) return n == 2;
            return string.Equals(level, "warn", StringComparison.OrdinalIgnoreCase) || string.Equals(level, "warning", StringComparison.OrdinalIgnoreCase);
        }

        public List<TargetSdkLogEntry> GetAfter(long afterSequence, int maxEntries)
        {
            int limit = maxEntries <= 0 ? 500 : Math.Min(maxEntries, 1000);
            List<TargetSdkLogEntry> result = new List<TargetSdkLogEntry>();
            lock (_sync)
                foreach (TargetSdkLogEntry entry in _entries)
                {
                    if (entry.LocalSequence <= afterSequence) continue;
                    result.Add(entry);
                    if (result.Count >= limit) break;
                }
            return result;
        }

        public List<TargetSdkLogEntry> RecentSnapshot()
        {
            lock (_sync) return new List<TargetSdkLogEntry>(_entries);
        }

        public string ExportRecent()
        {
            string path;
            lock (_sync) path = _logPath;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                try
                {
                    using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        if (fs.Length > 16 * 1024 * 1024)
                            return JsonConvert.SerializeObject(new { Warning = "Target SDK journal exceeds the 16 MB browser-export cap; retrieve the complete Target-SDK-Logs.jsonl from its evidence directory.", Path = path });
                        byte[] bytes = new byte[(int)fs.Length];
                        int read = 0;
                        while (read < bytes.Length)
                        {
                            int n = fs.Read(bytes, read, bytes.Length - read);
                            if (n <= 0) break;
                            read += n;
                        }
                        return Encoding.UTF8.GetString(bytes, 0, read);
                    }
                }
                catch { /* In-memory fallback below. */ }
            }
            StringBuilder b = new StringBuilder();
            foreach (TargetSdkLogEntry e in RecentSnapshot()) b.AppendLine(JsonConvert.SerializeObject(e));
            return b.ToString();
        }
    }
}
