using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace MAV.ProductionRegressionController
{
    public sealed class OfflineBundleArtifact
    {
        public string RelativePath { get; set; }
        public string FileName { get; set; }
        public long Bytes { get; set; }
        public string Kind { get; set; }
    }

    public sealed class OfflineBundleInventory
    {
        public string BundleFileName { get; set; }
        public DateTime? ImportedUtc { get; set; }
        public string LastError { get; set; }
        public string CacheRoot { get; set; }
        public List<OfflineBundleArtifact> CpzFiles { get; set; }
        public List<OfflineBundleArtifact> ConfigFiles { get; set; }
        public OfflineBundleInventory()
        {
            CpzFiles = new List<OfflineBundleArtifact>();
            ConfigFiles = new List<OfflineBundleArtifact>();
        }
    }

    /// <summary>
    /// Browser-uploaded, zero-internet package transport. A laptop may obtain the bundle by any
    /// approved means (cloud download, removable media, secure transfer, etc.) and upload it to
    /// the Regression Controller over the local processor network.
    /// </summary>
    public sealed class OfflineBundleService
    {
        private const int MaxEntries = 5000;
        private const long MaxExpandedBytes = 4L * 1024L * 1024L * 1024L;
        // Input ZIP is capped at 256 MB in this recovery preview to avoid a CP4N memory overcommit.
        private readonly ControllerLogger _log;
        private readonly object _sync = new object();
        private readonly string _root = LocalPaths.OfflineBundles;
        private readonly string _activeRoot;
        private string _uploadPath;
        private long _expectedBytes;
        private string _bundleFileName;
        private DateTime? _importedUtc;
        private string _lastError;
        private readonly List<OfflineBundleArtifact> _cpz = new List<OfflineBundleArtifact>();
        private readonly List<OfflineBundleArtifact> _config = new List<OfflineBundleArtifact>();

        public OfflineBundleService(ControllerLogger log)
        {
            _log = log;
            Crestron.SimplSharp.CrestronIO.Directory.CreateDirectory(_root);
            _activeRoot = Path.Combine(_root, "Active");
            Crestron.SimplSharp.CrestronIO.Directory.CreateDirectory(_activeRoot);
        }

        public string StartUpload(string fileName, long expectedBytes)
        {
            string safeName = Path.GetFileName(fileName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(safeName)) throw new InvalidOperationException("Offline bundle file name is required.");
            if (!string.Equals(Path.GetExtension(safeName), ".zip", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Offline bundle must be a .zip file.");
            if (expectedBytes <= 0) throw new InvalidOperationException("Offline bundle must not be empty.");

            string upload = Path.Combine(_root, "upload-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + safeName);
            lock (_sync)
            {
                LocalFileIO.WriteAllBytes(upload, new byte[0]);
                _uploadPath = upload;
                _expectedBytes = expectedBytes;
                _bundleFileName = safeName;
                _lastError = null;
            }
            _log.Info("Offline bundle upload started: " + safeName + " (" + expectedBytes + " bytes).");
            return upload;
        }

        public long AppendChunk(string base64)
        {
            string path;
            lock (_sync) path = _uploadPath;
            if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("Offline bundle upload has not been started.");
            byte[] bytes = Convert.FromBase64String(base64 ?? string.Empty);
            lock (_sync)
            {
                LocalFileIO.Append(path, bytes);
                return LocalFileIO.Length(path);
            }
        }

        public OfflineBundleInventory CompleteUpload()
        {
            string upload;
            long expected;
            lock (_sync) { upload = _uploadPath; expected = _expectedBytes; _lastError = null; }
            if (string.IsNullOrWhiteSpace(upload) || !LocalFileIO.Exists(upload)) throw new InvalidOperationException("Offline bundle upload is missing.");
            long actual = LocalFileIO.Length(upload);
            if (actual != expected) throw new InvalidOperationException("Offline bundle length mismatch. Expected " + expected + " bytes, received " + actual + ".");

            try
            {
                ResetActiveRoot();
                List<OfflineBundleArtifact> cpz = new List<OfflineBundleArtifact>();
                List<OfflineBundleArtifact> config = new List<OfflineBundleArtifact>();
                long expanded = 0;
                int count = 0;

                using (MemoryStream zipMemory = new MemoryStream(LocalFileIO.ReadAllBytes(upload, 256L * 1024L * 1024L)))
                using (ZipArchive archive = new ZipArchive(zipMemory, ZipArchiveMode.Read, false))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (++count > MaxEntries) throw new InvalidOperationException("Offline bundle contains too many files.");
                        if (string.IsNullOrWhiteSpace(entry.Name)) continue;
                        expanded += entry.Length;
                        if (expanded > MaxExpandedBytes) throw new InvalidOperationException("Offline bundle expands beyond the 4 GB safety limit.");

                        string relative = NormalizeRelative(entry.FullName);
                        string destination = Path.Combine(_activeRoot, relative.Replace('/', Path.DirectorySeparatorChar));
                        string parent = Path.GetDirectoryName(destination);
                        if (!string.IsNullOrWhiteSpace(parent)) Crestron.SimplSharp.CrestronIO.Directory.CreateDirectory(parent);
                        using (Stream input = entry.Open())
                        using (Crestron.SimplSharp.CrestronIO.FileStream output = new Crestron.SimplSharp.CrestronIO.FileStream(LocalPaths.Normalize(destination), Crestron.SimplSharp.CrestronIO.FileMode.Create, Crestron.SimplSharp.CrestronIO.FileAccess.Write, Crestron.SimplSharp.CrestronIO.FileShare.Read))
                        {
                            byte[] bytes = new byte[32768]; int n;
                            while ((n = input.Read(bytes, 0, bytes.Length)) > 0) output.Write(bytes, 0, n);
                            output.Flush();
                        }

                        string ext = Path.GetExtension(entry.Name).ToLowerInvariant();
                        if (ext == ".cpz" || ext == ".json")
                        {
                            OfflineBundleArtifact artifact = new OfflineBundleArtifact
                            {
                                RelativePath = relative,
                                FileName = entry.Name,
                                Bytes = entry.Length,
                                Kind = ext == ".cpz" ? "cpz" : "config"
                            };
                            if (ext == ".cpz") cpz.Add(artifact); else config.Add(artifact);
                        }
                    }
                }

                if (cpz.Count == 0) throw new InvalidOperationException("Offline bundle does not contain a CPZ file.");
                if (config.Count == 0) throw new InvalidOperationException("Offline bundle does not contain a JSON configuration file.");
                cpz.Sort(delegate(OfflineBundleArtifact a, OfflineBundleArtifact b) { return string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase); });
                config.Sort(delegate(OfflineBundleArtifact a, OfflineBundleArtifact b) { return string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase); });

                lock (_sync)
                {
                    _cpz.Clear(); _cpz.AddRange(cpz);
                    _config.Clear(); _config.AddRange(config);
                    _importedUtc = DateTime.UtcNow;
                    _lastError = null;
                }
                _log.Pass("Offline bundle imported. " + cpz.Count + " CPZ and " + config.Count + " JSON file(s) available. Internet access was not used.");
                return Inventory();
            }
            catch (Exception ex)
            {
                lock (_sync) _lastError = ex.Message;
                _log.Error("Offline bundle import failed: " + ex.Message);
                throw;
            }
        }

        public OfflineBundleInventory Inventory()
        {
            OfflineBundleInventory result = new OfflineBundleInventory();
            lock (_sync)
            {
                result.BundleFileName = _bundleFileName;
                result.ImportedUtc = _importedUtc;
                result.LastError = _lastError;
                result.CpzFiles.AddRange(_cpz);
                result.ConfigFiles.AddRange(_config);
            }
            result.CacheRoot = _activeRoot;
            return result;
        }

        public void Stage(string cpzRelativePath, string configRelativePath, UploadStore uploads)
        {
            if (uploads == null) throw new ArgumentNullException("uploads");
            OfflineBundleArtifact cpz = Find(cpzRelativePath, true);
            OfflineBundleArtifact config = Find(configRelativePath, false);
            string cpzPath = Path.Combine(_activeRoot, cpz.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            string configPath = Path.Combine(_activeRoot, config.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            uploads.StageExisting("cpz", cpzPath);
            uploads.StageExisting("config", configPath);
            _log.Pass("Offline package staged: " + cpz.RelativePath + " + " + config.RelativePath);
        }

        private OfflineBundleArtifact Find(string relativePath, bool cpz)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) throw new InvalidOperationException("Select both a CPZ and JSON file from the offline bundle.");
            lock (_sync)
            {
                List<OfflineBundleArtifact> list = cpz ? _cpz : _config;
                foreach (OfflineBundleArtifact artifact in list)
                    if (string.Equals(artifact.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase)) return artifact;
            }
            throw new InvalidOperationException("Selected offline bundle file is not in the current imported inventory.");
        }

        private void ResetActiveRoot()
        {
            try { if (Crestron.SimplSharp.CrestronIO.Directory.Exists(_activeRoot)) Crestron.SimplSharp.CrestronIO.Directory.Delete(_activeRoot, true); } catch { }
            Crestron.SimplSharp.CrestronIO.Directory.CreateDirectory(_activeRoot);
        }

        private static string NormalizeRelative(string name)
        {
            string value = (name ?? string.Empty).Replace('\\', '/').TrimStart('/');
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("Offline bundle contains an invalid file path.");
            string[] parts = value.Split('/');
            foreach (string part in parts)
            {
                if (part == ".." || part == "." || string.IsNullOrWhiteSpace(part))
                    throw new InvalidOperationException("Offline bundle contains an unsafe path: " + value);
            }
            return string.Join("/", parts);
        }
    }
}
