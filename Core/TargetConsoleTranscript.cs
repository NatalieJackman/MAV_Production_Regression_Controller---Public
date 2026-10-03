using Crestron.SimplSharp.CrestronIO;
using System;
using System.Collections.Generic;
using System.Text;

namespace MAV.ProductionRegressionController
{
    public sealed class TargetConsoleEntry
    {
        public long Sequence { get; set; }
        public DateTime TimestampUtc { get; set; }
        public string Kind { get; set; }
        public string Text { get; set; }
    }

    public sealed class TargetConsoleTranscript
    {
        private readonly object _sync = new object();
        private readonly List<TargetConsoleEntry> _entries = new List<TargetConsoleEntry>();
        private readonly string _root = LocalPaths.Console;
        private readonly string _path;
        private long _sequence;

        public TargetConsoleTranscript()
        {
            Directory.CreateDirectory(_root);
            _path = Path.Combine(_root, "target-console.log");
        }

        public string LogPath { get { return _path; } }

        public void Command(string command) { Append("CMD", "> " + (command ?? string.Empty)); }
        public void Output(string text) { Append("OUT", text ?? string.Empty); }
        public void System(string text) { Append("SYS", text ?? string.Empty); }

        public void Clear()
        {
            lock (_sync)
            {
                _entries.Clear();
                _sequence = 0;
                try { using (FileStream stream = new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.Read)) { stream.Flush(); } } catch { }
            }
        }

        public IList<TargetConsoleEntry> GetAfter(long afterSequence, int maxEntries)
        {
            int limit = maxEntries <= 0 ? 500 : Math.Min(maxEntries, 1000);
            List<TargetConsoleEntry> result = new List<TargetConsoleEntry>();
            lock (_sync)
            {
                foreach (TargetConsoleEntry entry in _entries)
                {
                    if (entry.Sequence <= afterSequence) continue;
                    result.Add(entry);
                    if (result.Count >= limit) break;
                }
            }
            return result;
        }

        public string ReadFullLog()
        {
            lock (_sync)
            {
                if (File.Exists(_path))
                {
                    try
                    {
                        using (FileStream stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            if (stream.Length > 16 * 1024 * 1024) throw new InvalidOperationException("Console log exceeds export limit.");
                            byte[] bytes = new byte[(int)stream.Length]; int readTotal = 0; int read;
                            while (readTotal < bytes.Length && (read = stream.Read(bytes, readTotal, bytes.Length - readTotal)) > 0) readTotal += read;
                            return Encoding.UTF8.GetString(bytes, 0, readTotal);
                        }
                    }
                    catch { /* Fall through to in-memory evidence if storage is unavailable. */ }
                }
                StringBuilder b = new StringBuilder();
                foreach (TargetConsoleEntry e in _entries)
                    b.AppendLine(Format(e));
                return b.ToString();
            }
        }

        private void Append(string kind, string text)
        {
            if (text == null) text = string.Empty;
            TargetConsoleEntry entry;
            lock (_sync)
            {
                entry = new TargetConsoleEntry
                {
                    Sequence = ++_sequence,
                    TimestampUtc = DateTime.UtcNow,
                    Kind = kind,
                    Text = text.Replace("\0", string.Empty)
                };
                _entries.Add(entry);
                if (_entries.Count > 5000) _entries.RemoveRange(0, 500);
                try
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(Format(entry) + Environment.NewLine);
                    using (FileStream stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read))
                    { stream.Write(bytes, 0, bytes.Length); stream.Flush(); }
                }
                catch { /* Live console continues when the local log cannot be persisted. */ }
            }
        }

        private static string Format(TargetConsoleEntry e)
        {
            return e.TimestampUtc.ToString("o") + " [" + e.Kind + "] " + e.Text;
        }
    }
}
