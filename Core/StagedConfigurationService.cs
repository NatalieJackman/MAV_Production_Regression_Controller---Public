using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Text;
using System.Linq;

namespace MAV.ProductionRegressionController
{
    // Only edits the Regression Controller's STAGED copy, never the target's live configuration.
    public sealed class StagedConfigurationService
    {
        private readonly UploadStore _uploads;
        private readonly object _sync = new object();
        private const int MaxJsonBytes = 1024 * 1024 * 2;
        public StagedConfigurationService(UploadStore uploads) { _uploads = uploads; }

        public object Read()
        {
            lock (_sync)
            {
                string path = RequirePath();
                string content = ReadUtf8(path);
                return new { JsonText = content, ConfigSha256 = FileHash.Sha256(path), FileName = Path.GetFileName(path), Bytes = Encoding.UTF8.GetByteCount(content) };
            }
        }
        public object Validate(string jsonText)
        {
            JObject o = Parse(jsonText);
            return new { Valid = true, Name = Convert.ToString(o["Name"] ?? o["SystemName"] ?? ""),
                ProgramType = Convert.ToString(o["ProgramType"] ?? o["SystemType"] ?? "Detected from topology"),
                Properties = o.Properties().Count(), Bytes = Encoding.UTF8.GetByteCount(jsonText) };
        }
        public object Save(string jsonText, string expectedSha256)
        {
            // Reject concurrent/late browser saves rather than silently overwriting a newer staged config.
            JObject parsed = Parse(jsonText);
            lock (_sync)
            {
                string path = RequirePath();
                string current = FileHash.Sha256(path);
                if (string.IsNullOrWhiteSpace(expectedSha256) || !string.Equals(current, expectedSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The staged JSON has changed since you loaded it. Reload before saving so no changes are lost.");
                byte[] bytes = Encoding.UTF8.GetBytes(parsed.ToString(Formatting.Indented) + Environment.NewLine);
                string tempPath = path + ".edit.tmp";
                LocalFileIO.WriteAllBytes(tempPath, bytes);
                // Keep an original recovery copy and swap in the validated version.
                string backupPath = path + ".previous.json";
                if (LocalFileIO.Exists(backupPath)) Crestron.SimplSharp.CrestronIO.File.Delete(backupPath);
                LocalFileIO.Copy(path, backupPath);
                try
                {
                    Crestron.SimplSharp.CrestronIO.File.Delete(path);
                    LocalFileIO.Copy(tempPath, path); Crestron.SimplSharp.CrestronIO.File.Delete(tempPath);
                }
                catch
                {
                    if (!LocalFileIO.Exists(path) && LocalFileIO.Exists(backupPath)) LocalFileIO.Copy(backupPath, path);
                    throw;
                }
                _uploads.RefreshConfigLength();
                return new { JsonText = Encoding.UTF8.GetString(bytes, 0, bytes.Length), ConfigSha256 = FileHash.Sha256(path), PreviousCopy = backupPath };
            }
        }
        private string RequirePath()
        {
            string path = _uploads.ConfigPath;
            if (string.IsNullOrWhiteSpace(path) || !LocalFileIO.Exists(path)) throw new InvalidOperationException("Stage a CPZ and configuration before editing JSON.");
            return path;
        }
        private static JObject Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Configuration JSON is empty.");
            if (Encoding.UTF8.GetByteCount(text) > MaxJsonBytes) throw new InvalidOperationException("Configuration exceeds the 2 MB editor limit.");
            try { return JObject.Parse(text); }
            catch (JsonReaderException ex) { throw new InvalidOperationException("Invalid JSON at line " + ex.LineNumber + ", position " + ex.LinePosition + ": " + ex.Message); }
        }
        private static string ReadUtf8(string path)
        {
            return LocalFileIO.ReadAllText(path, MaxJsonBytes);
        }
    }
}
