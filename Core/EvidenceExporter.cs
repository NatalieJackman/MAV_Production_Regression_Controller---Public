using Newtonsoft.Json;
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace MAV.ProductionRegressionController
{
    public sealed class EvidenceExporter
    {
        private readonly ControllerLogger _log;
        public EvidenceExporter(ControllerLogger log) { _log = log; }

        public void WriteMetadata(RunState state, TargetSdkLogObserver observer)
        {
            Crestron.SimplSharp.CrestronIO.Directory.CreateDirectory(state.EvidenceFolder);
            object safeTarget = state.Target == null ? null : new
            {
                state.Target.Address,
                state.Target.ProgramSlot,
                state.Target.UseHttpsCws,
                state.Target.RunMode
            };
            object payload = new
            {
                state.SessionId,
                state.Status,
                state.Phase,
                state.Detail,
                state.Progress,
                state.StartedUtc,
                state.CompletedUtc,
                Target = safeTarget,
                state.SelfTarget,
                state.ControllerSlot,
                Package = state.Package,
                Checks = state.Checks,
                ControllerLog = _log.SessionLogPath,
                TargetSdkJournal = observer == null ? null : observer.LogPath,
                TargetSdkStatus = observer == null ? null : observer.Status(),
                CombinedTranscript = Path.Combine(state.EvidenceFolder, "Combined-Transcript.log")
                // Each RegressionCheck.TargetEvidence points to a local observer sequence range.
                // No PASS is inferred solely from the presence of a log message.
            };
            LocalFileIO.WriteAllBytes(Path.Combine(state.EvidenceFolder, "RegressionReport.json"), Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload, Formatting.Indented)));
        }


        public string CombinedTranscript(ControllerLogger controller, TargetSdkLogObserver target)
        {
            List<TimelineLine> lines = new List<TimelineLine>();
            foreach (ControllerLogEntry e in controller.RecentSnapshot())
                lines.Add(new TimelineLine { Stamp = e.TimestampUtc, Text = e.TimestampUtc.ToString("o") + " [CONTROLLER/" + e.Level + "] " + e.Message });
            if (target != null)
                foreach (TargetSdkLogEntry e in target.RecentSnapshot())
                    lines.Add(new TimelineLine { Stamp = e.ObservedUtc, Text = e.ObservedUtc.ToString("o") + " [TARGET E" + e.Epoch + "/#" + e.TargetSequence + "/" + e.Level + "] " +
                        (string.IsNullOrEmpty(e.TestStep) ? "" : "[STEP " + e.TestStep + "] ") + e.Message + " [TargetTimestamp=" + e.TargetTimestamp + "]" });
            lines.Sort(delegate(TimelineLine a, TimelineLine b) { return a.Stamp.CompareTo(b.Stamp); });
            StringBuilder sb = new StringBuilder();
            foreach (TimelineLine line in lines) sb.AppendLine(line.Text);
            return sb.ToString();
        }

        public void WriteCombined(RunState state, TargetSdkLogObserver observer)
        {
            Crestron.SimplSharp.CrestronIO.Directory.CreateDirectory(state.EvidenceFolder);
            LocalFileIO.WriteAllBytes(Path.Combine(state.EvidenceFolder, "Combined-Transcript.log"), Encoding.UTF8.GetBytes(CombinedTranscript(_log, observer)));
        }

        private sealed class TimelineLine
        {
            public DateTime Stamp { get; set; }
            public string Text { get; set; }
        }
    }
}
