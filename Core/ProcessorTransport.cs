using Crestron.SimplSharp;
using Crestron.SimplSharp.Ssh;
using Crestron.SimplSharp.Ssh.Sftp;
using Crestron.SimplSharpPro.CrestronThread;
using System;
using System.Collections.Generic;
using System.Text;
using CDirectory = Crestron.SimplSharp.CrestronIO.Directory;
using CFile = Crestron.SimplSharp.CrestronIO.File;
using CFileAccess = Crestron.SimplSharp.CrestronIO.FileAccess;
using CFileMode = Crestron.SimplSharp.CrestronIO.FileMode;
using CFileShare = Crestron.SimplSharp.CrestronIO.FileShare;
using CFileStream = Crestron.SimplSharp.CrestronIO.FileStream;

namespace MAV.ProductionRegressionController
{
    public sealed class ProcessorTransport
    {
        private readonly ControllerLogger _log;
        private readonly TargetConsoleTranscript _console;
        public ProcessorTransport(ControllerLogger log, TargetConsoleTranscript console) { _log = log; _console = console; }

        public string ExecuteConsole(TargetSettings target, IEnumerable<string> commands, int timeoutMs)
        {
            if (target == null) throw new ArgumentNullException("target");
            SshClient client = null;
            ShellStream shell = null;
            try
            {
                client = new SshClient(target.Address, target.Username, target.Password);
                client.Connect();
                shell = client.CreateShellStream("xterm", 100, 30, 1000, 700, 8192);
                Thread.Sleep(500);
                Drain(shell);
                foreach (string command in commands)
                {
                    _log.Trace("Console [" + target.Address + "]: " + command);
                    if (_console != null) _console.Command(command);
                    shell.WriteLine(command);
                }
                shell.WriteLine(string.Empty);
                StringBuilder output = new StringBuilder();
                int elapsed = 0;
                int quiet = 0;
                while (elapsed < timeoutMs)
                {
                    if (shell.DataAvailable)
                    {
                        string data = shell.Read();
                        if (!string.IsNullOrEmpty(data)) { output.Append(data); if (_console != null) _console.Output(data); }
                        quiet = 0;
                    }
                    else
                    {
                        quiet += 100;
                        if (quiet >= 1200 && output.Length > 0) break;
                    }
                    Thread.Sleep(100);
                    elapsed += 100;
                }
                string text = output.ToString();
                if (!string.IsNullOrWhiteSpace(text)) _log.Trace("Console response: " + Compact(text, 3000));
                return text;
            }
            finally
            {
                if (shell != null) try { shell.Dispose(); } catch { }
                if (client != null) { try { if (client.IsConnected) client.Disconnect(); } catch { } try { client.Dispose(); } catch { } }
            }
        }

        public void EnsureDirectory(TargetSettings target, string remotePath)
        {
            WithSftp(target, delegate(SftpClient sftp)
            {
                string normalized = NormalizeRemote(remotePath);
                string[] parts = normalized.Trim('/').Split('/');
                string current = string.Empty;
                foreach (string part in parts)
                {
                    if (string.IsNullOrWhiteSpace(part)) continue;
                    current += "/" + part;
                    if (sftp.Exists(current)) continue;
                    _log.Trace("Creating remote directory: " + current);
                    sftp.CreateDirectory(current);
                    if (!sftp.Exists(current)) throw new InvalidOperationException("Unable to create remote directory " + current);
                }
            });
        }

        public List<SftpFile> ListDirectory(TargetSettings target, string remotePath)
        {
            List<SftpFile> result = new List<SftpFile>();
            WithSftp(target, delegate(SftpClient sftp)
            {
                if (!sftp.Exists(remotePath)) return;
                foreach (SftpFile file in sftp.ListDirectory(remotePath, null)) result.Add(file);
            });
            return result;
        }

        public bool Exists(TargetSettings target, string remotePath)
        {
            bool exists = false;
            WithSftp(target, delegate(SftpClient sftp) { exists = sftp.Exists(NormalizeRemote(remotePath)); });
            return exists;
        }

        public void Upload(TargetSettings target, string localPath, string remotePath)
        {
            string crestronLocalPath = NormalizeLocalPathForCrestron(localPath);
            if (string.IsNullOrWhiteSpace(crestronLocalPath))
                throw new InvalidOperationException("Local upload path is empty.");
            if (!CFile.Exists(crestronLocalPath))
                throw new System.IO.FileNotFoundException("Local staged file was not found before SFTP upload: " + crestronLocalPath, crestronLocalPath);

            string parent = RemoteParent(remotePath);
            if (!string.IsNullOrEmpty(parent)) EnsureDirectory(target, parent);
            WithSftp(target, delegate(SftpClient sftp)
            {
                long bytes;
                using (CFileStream sizeStream = new CFileStream(crestronLocalPath, CFileMode.Open, CFileAccess.Read, CFileShare.Read)) bytes = sizeStream.Length;
                _log.Info("Uploading " + crestronLocalPath + " (" + bytes + " bytes) -> " + remotePath);
                using (CFileStream stream = new CFileStream(crestronLocalPath, CFileMode.Open, CFileAccess.Read, CFileShare.Read))
                {
                    sftp.UploadFile(stream, NormalizeRemote(remotePath), true, null);
                }
            });
        }

        public void Download(TargetSettings target, string remotePath, string localPath)
        {
            string crestronLocalPath = NormalizeLocalPathForCrestron(localPath);
            string parent = LocalParent(crestronLocalPath);
            if (!string.IsNullOrEmpty(parent) && !CDirectory.Exists(parent)) CDirectory.CreateDirectory(parent);
            WithSftp(target, delegate(SftpClient sftp)
            {
                using (CFileStream stream = new CFileStream(crestronLocalPath, CFileMode.Create, CFileAccess.Write, CFileShare.Read))
                {
                    _log.Info("Downloading " + remotePath + " -> " + crestronLocalPath);
                    sftp.DownloadFile(NormalizeRemote(remotePath), stream, null);
                    stream.Flush();
                }
            });
        }

        public int DownloadDirectory(TargetSettings target, string remoteRoot, string localRoot, List<string> downloadedFiles)
        {
            if (downloadedFiles == null) throw new ArgumentNullException("downloadedFiles");
            int count = 0;
            string crestronLocalRoot = NormalizeLocalPathForCrestron(localRoot);
            if (!CDirectory.Exists(crestronLocalRoot)) CDirectory.CreateDirectory(crestronLocalRoot);
            WithSftp(target, delegate(SftpClient sftp)
            {
                string root = NormalizeRemote(remoteRoot).TrimEnd('/');
                if (!sftp.Exists(root)) throw new InvalidOperationException("Remote directory does not exist: " + root);
                DownloadDirectoryRecursive(sftp, root, crestronLocalRoot, downloadedFiles, ref count);
            });
            return count;
        }

        private void DownloadDirectoryRecursive(SftpClient sftp, string remoteRoot, string localRoot, List<string> downloadedFiles, ref int count)
        {
            foreach (SftpFile item in sftp.ListDirectory(remoteRoot, null))
            {
                if (item.Name == "." || item.Name == "..") continue;
                string local = LocalCombine(localRoot, item.Name);
                if (item.IsDirectory)
                {
                    if (!CDirectory.Exists(local)) CDirectory.CreateDirectory(local);
                    DownloadDirectoryRecursive(sftp, NormalizeRemote(item.FullName), local, downloadedFiles, ref count);
                }
                else if (item.IsRegularFile)
                {
                    using (CFileStream stream = new CFileStream(local, CFileMode.Create, CFileAccess.Write, CFileShare.Read))
                    {
                        _log.Trace("Downloading archive member " + item.FullName);
                        sftp.DownloadFile(NormalizeRemote(item.FullName), stream, null);
                        stream.Flush();
                    }
                    count++;
                    downloadedFiles.Add(local);
                }
            }
        }

        public void Delete(TargetSettings target, string remotePath)
        {
            WithSftp(target, delegate(SftpClient sftp) { if (sftp.Exists(remotePath)) sftp.DeleteFile(remotePath); });
        }

        private void WithSftp(TargetSettings target, Action<SftpClient> work)
        {
            SftpClient client = null;
            try
            {
                client = new SftpClient(target.Address, target.Username, target.Password);
                client.Connect();
                work(client);
            }
            finally
            {
                if (client != null) { try { if (client.IsConnected) client.Disconnect(); } catch { } try { client.Dispose(); } catch { } }
            }
        }

        private static string Drain(ShellStream shell)
        {
            StringBuilder b = new StringBuilder();
            while (shell != null && shell.DataAvailable) b.Append(shell.Read());
            return b.ToString();
        }

        private static string Compact(string s, int max) { s = (s ?? string.Empty).Replace("\0", string.Empty); return s.Length <= max ? s : s.Substring(0, max) + "..."; }
        private static string NormalizeRemote(string path) { return (path ?? string.Empty).Replace('\\', '/'); }
        private static string NormalizeLocalPathForCrestron(string path) { return LocalPaths.Normalize(path); }
        private static string LocalCombine(string left, string right) { return (LocalPaths.Normalize(left) ?? string.Empty).TrimEnd('/') + "/" + (right ?? string.Empty).Replace('\\', '/').TrimStart('/'); }
        private static string LocalParent(string path)
        {
            string p = LocalPaths.Normalize(path) ?? string.Empty;
            int slash = p.LastIndexOf('/');
            return slash > 0 ? p.Substring(0, slash) : string.Empty;
        }
        private static string RemoteParent(string remotePath)
        {
            string p = NormalizeRemote(remotePath);
            int slash = p.LastIndexOf('/');
            return slash > 0 ? p.Substring(0, slash) : string.Empty;
        }
    }
}
