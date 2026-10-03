using System;
using System.Text;
using CFile = Crestron.SimplSharp.CrestronIO.File;
using CDirectory = Crestron.SimplSharp.CrestronIO.Directory;
using CFileStream = Crestron.SimplSharp.CrestronIO.FileStream;
using CFileMode = Crestron.SimplSharp.CrestronIO.FileMode;
using CFileAccess = Crestron.SimplSharp.CrestronIO.FileAccess;
using CFileShare = Crestron.SimplSharp.CrestronIO.FileShare;

namespace MAV.ProductionRegressionController
{
    // Single canonical CP4N-local file I/O contract. All stage->SFTP files pass through here.
    internal static class LocalFileIO
    {
        public static string Path(string path) { return LocalPaths.Normalize(path); }
        public static bool Exists(string path) { return CFile.Exists(Path(path)); }
        public static void EnsureParent(string path)
        {
            string normalized = Path(path);
            int index = normalized.LastIndexOf('/');
            if (index > 0)
            {
                string directory = normalized.Substring(0, index);
                if (!CDirectory.Exists(directory)) CDirectory.CreateDirectory(directory);
            }
        }
        public static long Length(string path)
        {
            using (CFileStream stream = new CFileStream(Path(path), CFileMode.Open, CFileAccess.Read, CFileShare.Read)) return stream.Length;
        }
        public static byte[] ReadAllBytes(string path, long maxBytes)
        {
            using (CFileStream stream = new CFileStream(Path(path), CFileMode.Open, CFileAccess.Read, CFileShare.Read))
            {
                if (stream.Length > maxBytes || stream.Length > int.MaxValue) throw new InvalidOperationException("Local file exceeds permitted in-memory read size: " + path);
                byte[] data = new byte[(int)stream.Length]; int offset = 0; int n;
                while (offset < data.Length && (n = stream.Read(data, offset, data.Length - offset)) > 0) offset += n;
                if (offset != data.Length) throw new InvalidOperationException("Local file ended early: " + path);
                return data;
            }
        }
        public static string ReadAllText(string path, long maxBytes) { return Encoding.UTF8.GetString(ReadAllBytes(path, maxBytes)).TrimStart('\ufeff'); }
        public static void WriteAllBytes(string path, byte[] bytes)
        {
            EnsureParent(path);
            using (CFileStream stream = new CFileStream(Path(path), CFileMode.Create, CFileAccess.Write, CFileShare.Read))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(); }
        }
        public static void Append(string path, byte[] bytes)
        {
            EnsureParent(path);
            using (CFileStream stream = new CFileStream(Path(path), CFileMode.Append, CFileAccess.Write, CFileShare.Read))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(); }
        }
        public static void Copy(string source, string destination)
        {
            EnsureParent(destination);
            using (CFileStream input = new CFileStream(Path(source), CFileMode.Open, CFileAccess.Read, CFileShare.Read))
            using (CFileStream output = new CFileStream(Path(destination), CFileMode.Create, CFileAccess.Write, CFileShare.Read))
            { byte[] buffer = new byte[32768]; int n; while ((n = input.Read(buffer,0,buffer.Length)) > 0) output.Write(buffer,0,n); output.Flush(); }
        }
        public static void Probe(string path)
        {
            string normalized = Path(path);
            if (string.IsNullOrWhiteSpace(normalized) || !Exists(normalized)) throw new InvalidOperationException("CP4N staged file not visible to CrestronIO: " + normalized);
            using (CFileStream stream = new CFileStream(normalized, CFileMode.Open, CFileAccess.Read, CFileShare.Read))
            { byte[] one = new byte[1]; if (stream.Length <= 0 || stream.Read(one,0,1) != 1) throw new InvalidOperationException("CP4N staged file empty/unreadable: " + normalized); }
        }
    }
}
