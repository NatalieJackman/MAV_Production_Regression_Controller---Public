using System;
using System.IO;

namespace MAV.ProductionRegressionController
{
    public sealed class UploadStore
    {
        private readonly object _sync = new object();
        private readonly string _root = LocalPaths.Staging;
        private string _cpzPath;
        private string _configPath;
        private long _cpzExpected;
        private long _configExpected;

        public UploadStore() { Crestron.SimplSharp.CrestronIO.Directory.CreateDirectory(_root); }
        public string CpzPath { get { lock (_sync) { return _cpzPath; } } }
        public string ConfigPath { get { lock (_sync) { return _configPath; } } }

        public string Start(string kind, string fileName, long expectedSize)
        {
            string safeName = Path.GetFileName(fileName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(safeName)) throw new InvalidOperationException("File name is required.");
            string normalized = NormalizeKind(kind);
            string extension = Path.GetExtension(safeName).ToLowerInvariant();
            if (normalized == "cpz" && extension != ".cpz") throw new InvalidOperationException("Program upload must be a .cpz file.");
            if (normalized == "config" && extension != ".json") throw new InvalidOperationException("Configuration upload must be a .json file.");

            string path = Path.Combine(_root, normalized + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + safeName);
            lock (_sync)
            {
                LocalFileIO.WriteAllBytes(path, new byte[0]);
                if (normalized == "cpz") { _cpzPath = path; _cpzExpected = expectedSize; }
                else { _configPath = path; _configExpected = expectedSize; }
            }
            return path;
        }


        public string StageExisting(string kind, string sourcePath)
        {
            string normalized = NormalizeKind(kind);
            if (string.IsNullOrWhiteSpace(sourcePath) || !LocalFileIO.Exists(sourcePath)) throw new FileNotFoundException("Staged source file was not found.", sourcePath);
            string extension = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (normalized == "cpz" && extension != ".cpz") throw new InvalidOperationException("Program source must be a .cpz file.");
            if (normalized == "config" && extension != ".json") throw new InvalidOperationException("Configuration source must be a .json file.");
            string safeName = Path.GetFileName(sourcePath);
            string destination = Path.Combine(_root, normalized + "-cloud-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + safeName);
            lock (_sync)
            {
                LocalFileIO.Copy(sourcePath, destination);
                long length = LocalFileIO.Length(destination);
                if (normalized == "cpz") { _cpzPath = destination; _cpzExpected = length; }
                else { _configPath = destination; _configExpected = length; }
            }
            return destination;
        }

        public long Append(string kind, string base64)
        {
            string path = GetPath(kind);
            if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("Upload has not been started.");
            byte[] bytes = Convert.FromBase64String(base64 ?? string.Empty);
            lock (_sync)
            {
                LocalFileIO.Append(path, bytes);
                return LocalFileIO.Length(path);
            }
        }


        public void RefreshConfigLength()
        {
            lock (_sync)
            {
                if (string.IsNullOrEmpty(_configPath) || !LocalFileIO.Exists(_configPath)) throw new InvalidOperationException("No staged configuration exists.");
                _configExpected = LocalFileIO.Length(_configPath);
            }
        }

        public void EnsureComplete()
        {
            lock (_sync)
            {
                if (string.IsNullOrEmpty(_cpzPath) || !LocalFileIO.Exists(_cpzPath)) throw new InvalidOperationException("CPZ upload is missing.");
                if (string.IsNullOrEmpty(_configPath) || !LocalFileIO.Exists(_configPath)) throw new InvalidOperationException("Configuration upload is missing.");
                long cpzLength = LocalFileIO.Length(_cpzPath);
                long configLength = LocalFileIO.Length(_configPath);
                if (_cpzExpected > 0 && cpzLength != _cpzExpected) throw new InvalidOperationException("CPZ upload length mismatch. Expected " + _cpzExpected + " bytes, received " + cpzLength + ".");
                if (_configExpected > 0 && configLength != _configExpected) throw new InvalidOperationException("Configuration upload length mismatch. Expected " + _configExpected + " bytes, received " + configLength + ".");
                if (cpzLength <= 0 || configLength <= 0) throw new InvalidOperationException("Uploaded package files must be nonempty.");
            }
        }
        public string GetPath(string kind)
        {
            string normalized = NormalizeKind(kind);
            lock (_sync) { return normalized == "cpz" ? _cpzPath : _configPath; }
        }

        private static string NormalizeKind(string kind)
        {
            string k = (kind ?? string.Empty).Trim().ToLowerInvariant();
            if (k == "cpz" || k == "program") return "cpz";
            if (k == "config" || k == "json" || k == "systemdefinition") return "config";
            throw new InvalidOperationException("Upload kind must be cpz or config.");
        }
    }
}
