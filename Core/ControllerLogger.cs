using Crestron.SimplSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MAV.ProductionRegressionController
{
    public sealed class ControllerLogEntry
    {
        public long Sequence { get; set; }
        public DateTime TimestampUtc { get; set; }
        public string Level { get; set; }
        public string Message { get; set; }
    }

    public sealed class ControllerLogger
    {
        private readonly object _sync = new object();
        private readonly List<ControllerLogEntry> _entries = new List<ControllerLogEntry>();
        private long _sequence;
        private string _sessionLogPath;

        public string SessionLogPath
        {
            get { lock (_sync) { return _sessionLogPath; } }
        }

        public void BeginSession(string sessionId)
        {
            string root = LocalPaths.Logs;
            Crestron.SimplSharp.CrestronIO.Directory.CreateDirectory(root);

            lock (_sync)
            {
                _entries.Clear();
                _sequence = 0;
                _sessionLogPath = Path.Combine(root, "Regression-" + sessionId + ".log");
                LocalFileIO.WriteAllBytes(
                    _sessionLogPath,
                    Encoding.UTF8.GetBytes("MAV Production Regression Controller\r\n"));
            }

            Info("Session started: " + sessionId);
        }

        public void Trace(string message) { Write("TRACE", message); }
        public void Info(string message)  { Write("INFO", message); }
        public void Pass(string message)  { Write("PASS", message); }
        public void Warn(string message)  { Write("WARN", message); }
        public void Error(string message) { Write("FAIL", message); }

        private void Write(string level, string message)
        {
            if (message == null)
                message = string.Empty;

            ControllerLogEntry entry;
            string line;

            lock (_sync)
            {
                entry = new ControllerLogEntry
                {
                    Sequence = ++_sequence,
                    TimestampUtc = DateTime.UtcNow,
                    Level = level,
                    Message = message
                };

                _entries.Add(entry);

                if (_entries.Count > 6000)
                    _entries.RemoveRange(0, _entries.Count - 6000);

                line = string.Format(
                    "{0:o} [{1}] #{2} {3}\r\n",
                    entry.TimestampUtc,
                    level,
                    entry.Sequence,
                    message);

                if (!string.IsNullOrEmpty(_sessionLogPath))
                    LocalFileIO.Append(_sessionLogPath, Encoding.UTF8.GetBytes(line));
            }

            // Public-demonstration build:
            // Keep processor-visible diagnostics without depending on the private
            // MAV Logging.dll assembly. The controller's own session/evidence log
            // above remains the authoritative regression transcript.
            try
            {
                string processorMessage = "[MAV-REGRESSION] " + message;

                switch (level)
                {
                    case "FAIL":
                        ErrorLog.Error(processorMessage);
                        break;

                    case "WARN":
                        ErrorLog.Warn(processorMessage);
                        break;

                    case "PASS":
                        ErrorLog.Ok(processorMessage);
                        break;

                    case "INFO":
                        ErrorLog.Info(processorMessage);
                        break;

                    default:
                        ErrorLog.Notice(processorMessage);
                        break;
                }
            }
            catch
            {
                // Processor error-log mirroring is supplemental. A failure here
                // must never interrupt regression execution or evidence logging.
            }
        }

        public List<ControllerLogEntry> GetAfter(long afterSequence, int max)
        {
            if (max <= 0 || max > 1000)
                max = 500;

            List<ControllerLogEntry> result = new List<ControllerLogEntry>();

            lock (_sync)
            {
                foreach (ControllerLogEntry e in _entries)
                {
                    if (e.Sequence <= afterSequence)
                        continue;

                    result.Add(e);

                    if (result.Count >= max)
                        break;
                }
            }

            return result;
        }

        public List<ControllerLogEntry> RecentSnapshot()
        {
            lock (_sync)
                return new List<ControllerLogEntry>(_entries);
        }

        public string ReadFullLog()
        {
            lock (_sync)
            {
                if (string.IsNullOrEmpty(_sessionLogPath) ||
                    !LocalFileIO.Exists(_sessionLogPath))
                    return string.Empty;

                return LocalFileIO.ReadAllText(
                    _sessionLogPath,
                    16L * 1024L * 1024L);
            }
        }
    }
}
