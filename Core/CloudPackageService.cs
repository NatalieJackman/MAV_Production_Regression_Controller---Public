using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace MAV.ProductionRegressionController
{
    public sealed class CloudArtifact
    {
        public string RelativePath { get; set; }
        public string FileName { get; set; }
        public long Bytes { get; set; }
        public string Kind { get; set; }
        public string DropboxPath { get; set; }
        public string ContentHash { get; set; }
    }

    public sealed class DropboxAuthorizationStatus
    {
        public bool AppKeyConfigured { get; set; }
        public bool Authorized { get; set; }
        public bool AuthorizationPending { get; set; }
        public DateTime? AuthorizationExpiresUtc { get; set; }
        public string Status { get; set; }
    }

    public sealed class DropboxAuthorizationStart
    {
        public string SessionId { get; set; }
        public string AuthorizeUrl { get; set; }
        public DateTime ExpiresUtc { get; set; }
    }

    public sealed class CloudInventory
    {
        public DateTime? LastSyncUtc { get; set; }
        public bool SyncRunning { get; set; }
        public string LastError { get; set; }
        public string SourceUrl { get; set; }
        public List<CloudArtifact> CpzFiles { get; set; }
        public List<CloudArtifact> ConfigFiles { get; set; }
        public string CacheRoot { get; set; }
        public DropboxAuthorizationStatus Authorization { get; set; }

        public CloudInventory()
        {
            CpzFiles = new List<CloudArtifact>();
            ConfigFiles = new List<CloudArtifact>();
        }
    }

    public sealed class CloudPackageService
    {
        // Production builds use the MAV deployment utility Dropbox application and read-only scopes. Public demo intentionally omits the App Key.
        public const string DropboxAppKey = ""; // PUBLIC DEMO: real application key intentionally omitted for privacy.
        private const string DropboxScope = "files.metadata.read files.content.read";
        private const string PublicDemoPrivacyMessage = "Cloud CurrentBuild is disabled in the public demonstration build because the Dropbox application identifier was intentionally removed for privacy. Offline Bundle and Local Upload remain available.";
        private const string DropboxAuthorizeUrl = "https://www.dropbox.com/oauth2/authorize";
        private const string DropboxTokenUrl = "https://api.dropbox.com/oauth2/token";
        private const string DropboxApiRoot = "https://api.dropboxapi.com/2/";
        private const string DropboxContentRoot = "https://content.dropboxapi.com/2/";

        private readonly ControllerLogger _log;
        private readonly object _sync = new object();
        private readonly string _root = LocalPaths.CloudCache;
        private readonly string _activeRoot;
        private readonly string _authRoot = LocalPaths.DropboxAuth;
        private readonly string _refreshTokenPath;
        private bool _running;
        private string _lastError;
        private DateTime? _lastSyncUtc;
        private string _sourceUrl = "Dropbox API · MAV Utility app root";
        private string _accessToken;
        private DateTime _accessTokenExpiresUtc = DateTime.MinValue;
        private string _pendingSessionId;
        private string _pendingVerifier;
        private DateTime _pendingExpiresUtc = DateTime.MinValue;
        private readonly List<CloudArtifact> _remoteCpz = new List<CloudArtifact>();
        private readonly List<CloudArtifact> _remoteConfig = new List<CloudArtifact>();

        public CloudPackageService(ControllerLogger log)
        {
            _log = log;
            Crestron.SimplSharp.CrestronIO.Directory.CreateDirectory(_root);
            Crestron.SimplSharp.CrestronIO.Directory.CreateDirectory(_authRoot);
            _activeRoot = Path.Combine(_root, "CurrentBuild");
            _refreshTokenPath = Path.Combine(_authRoot, "refresh-token.dat");
            Crestron.SimplSharp.CrestronIO.Directory.CreateDirectory(_activeRoot);
        }

        public DropboxAuthorizationStatus AuthorizationStatus()
        {
            lock (_sync)
            {
                bool configured = !string.IsNullOrWhiteSpace(DropboxAppKey);
                bool hasToken = configured && LocalFileIO.Exists(_refreshTokenPath) && LocalFileIO.Length(_refreshTokenPath) > 0;
                bool pending = configured && !string.IsNullOrWhiteSpace(_pendingSessionId) && _pendingExpiresUtc > DateTime.UtcNow;
                return new DropboxAuthorizationStatus
                {
                    AppKeyConfigured = configured,
                    Authorized = hasToken,
                    AuthorizationPending = pending,
                    AuthorizationExpiresUtc = pending ? (DateTime?)_pendingExpiresUtc : null,
                    Status = !configured ? PublicDemoPrivacyMessage : hasToken ? "Authorized" : pending ? "Waiting for authorization code" : "Not authorized"
                };
            }
        }

        public DropboxAuthorizationStart BeginAuthorization()
        {
            if (string.IsNullOrWhiteSpace(DropboxAppKey))
                throw new InvalidOperationException(PublicDemoPrivacyMessage);

            byte[] verifierBytes = new byte[64];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(verifierBytes);
            string verifier = Base64Url(verifierBytes);
            string challenge;
            using (SHA256 sha = SHA256.Create()) challenge = Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));

            string session = Guid.NewGuid().ToString("N");
            DateTime expires = DateTime.UtcNow.AddMinutes(10);
            lock (_sync)
            {
                _pendingSessionId = session;
                _pendingVerifier = verifier;
                _pendingExpiresUtc = expires;
            }

            string url = DropboxAuthorizeUrl
                + "?client_id=" + Url(DropboxAppKey)
                + "&response_type=code"
                + "&token_access_type=offline"
                + "&scope=" + Url(DropboxScope)
                + "&code_challenge=" + Url(challenge)
                + "&code_challenge_method=S256";

            _log.Info("Dropbox authorization started. Waiting for the single-use authorization code from the operator browser.");
            return new DropboxAuthorizationStart { SessionId = session, AuthorizeUrl = url, ExpiresUtc = expires };
        }

        public DropboxAuthorizationStatus CompleteAuthorization(string sessionId, string code)
        {
            if (string.IsNullOrWhiteSpace(DropboxAppKey)) throw new InvalidOperationException(PublicDemoPrivacyMessage);
            if (string.IsNullOrWhiteSpace(code)) throw new InvalidOperationException("Dropbox authorization code is required.");
            string verifier;
            lock (_sync)
            {
                if (string.IsNullOrWhiteSpace(_pendingSessionId) || !string.Equals(_pendingSessionId, sessionId, StringComparison.Ordinal))
                    throw new InvalidOperationException("Dropbox authorization session is not valid. Start authorization again.");
                if (_pendingExpiresUtc <= DateTime.UtcNow)
                    throw new InvalidOperationException("Dropbox authorization session expired. Start authorization again.");
                verifier = _pendingVerifier;
            }

            string form = Form(new Dictionary<string, string>
            {
                { "code", code.Trim() },
                { "grant_type", "authorization_code" },
                { "client_id", DropboxAppKey },
                { "code_verifier", verifier }
            });
            JObject response = JObject.Parse(PostText(DropboxTokenUrl, "application/x-www-form-urlencoded", form, null));
            string access = Convert.ToString(response["access_token"]);
            string refresh = Convert.ToString(response["refresh_token"]);
            if (string.IsNullOrWhiteSpace(access)) throw new InvalidOperationException("Dropbox OAuth response did not contain an access_token.");
            if (string.IsNullOrWhiteSpace(refresh)) throw new InvalidOperationException("Dropbox authorization succeeded but did not return a refresh_token. Re-authorize with offline access.");

            SaveRefreshToken(refresh);
            SetAccessToken(response);
            lock (_sync)
            {
                _pendingSessionId = null;
                _pendingVerifier = null;
                _pendingExpiresUtc = DateTime.MinValue;
            }
            _log.Pass("Dropbox authorization complete. Refresh token stored locally on this Regression Controller; access tokens will renew automatically.");
            return AuthorizationStatus();
        }

        public void DisconnectAuthorization()
        {
            if (string.IsNullOrWhiteSpace(DropboxAppKey))
            {
                lock (_sync)
                {
                    _accessToken = null;
                    _accessTokenExpiresUtc = DateTime.MinValue;
                    _pendingSessionId = null;
                    _pendingVerifier = null;
                    _pendingExpiresUtc = DateTime.MinValue;
                    _remoteCpz.Clear();
                    _remoteConfig.Clear();
                    _lastSyncUtc = null;
                    _lastError = null;
                }
                _log.Info(PublicDemoPrivacyMessage);
                return;
            }
            lock (_sync)
            {
                _accessToken = null;
                _accessTokenExpiresUtc = DateTime.MinValue;
                _pendingSessionId = null;
                _pendingVerifier = null;
                _pendingExpiresUtc = DateTime.MinValue;
                _remoteCpz.Clear();
                _remoteConfig.Clear();
                _lastSyncUtc = null;
                _lastError = null;
            }
            try { if (LocalFileIO.Exists(_refreshTokenPath)) Crestron.SimplSharp.CrestronIO.File.Delete(_refreshTokenPath); } catch { }
            _log.Info("Dropbox authorization removed from the Regression Controller.");
        }

        public void Sync(string ignoredSharedFolderUrl)
        {
            if (string.IsNullOrWhiteSpace(DropboxAppKey)) throw new InvalidOperationException(PublicDemoPrivacyMessage);
            lock (_sync)
            {
                if (_running) throw new InvalidOperationException("A Dropbox synchronization is already running.");
                _running = true;
                _lastError = null;
            }
            try
            {
                string token = GetAccessToken();
                _log.Info("Dropbox inventory sync started using the MAV Utility OAuth application.");
                List<CloudArtifact> cpz = new List<CloudArtifact>();
                List<CloudArtifact> config = new List<CloudArtifact>();
                EnumerateDropboxFiles(token, cpz, config);
                lock (_sync)
                {
                    _remoteCpz.Clear();
                    _remoteCpz.AddRange(cpz);
                    _remoteConfig.Clear();
                    _remoteConfig.AddRange(config);
                    _lastSyncUtc = DateTime.UtcNow;
                    _sourceUrl = "Dropbox API · Apps/MAV Utility";
                }
                _log.Pass("Dropbox inventory synchronized. " + cpz.Count + " CPZ and " + config.Count + " JSON file(s) available.");
            }
            catch (Exception ex)
            {
                lock (_sync) _lastError = ex.Message;
                _log.Error("Dropbox sync failed: " + ex.Message);
                throw;
            }
            finally { lock (_sync) _running = false; }
        }

        public CloudInventory Inventory()
        {
            CloudInventory inventory = new CloudInventory();
            lock (_sync)
            {
                inventory.LastSyncUtc = _lastSyncUtc;
                inventory.SyncRunning = _running;
                inventory.LastError = _lastError;
                inventory.SourceUrl = _sourceUrl;
                inventory.CpzFiles.AddRange(_remoteCpz);
                inventory.ConfigFiles.AddRange(_remoteConfig);
            }
            inventory.CacheRoot = _activeRoot;
            inventory.Authorization = AuthorizationStatus();
            return inventory;
        }

        public void Stage(string cpzRelativePath, string configRelativePath, UploadStore uploads)
        {
            if (string.IsNullOrWhiteSpace(DropboxAppKey)) throw new InvalidOperationException(PublicDemoPrivacyMessage);
            if (uploads == null) throw new ArgumentNullException("uploads");
            string token = GetAccessToken();
            CloudArtifact cpz = FindArtifact(cpzRelativePath, true);
            CloudArtifact config = FindArtifact(configRelativePath, false);
            string cpzLocal = CacheArtifact(cpz, token);
            string configLocal = CacheArtifact(config, token);
            uploads.StageExisting("cpz", cpzLocal);
            uploads.StageExisting("config", configLocal);
            _log.Pass("Dropbox package staged and content-verified: " + cpz.RelativePath + " + " + config.RelativePath);
        }

        private CloudArtifact FindArtifact(string relativePath, bool cpz)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) throw new InvalidOperationException("A Dropbox file must be selected.");
            lock (_sync)
            {
                List<CloudArtifact> list = cpz ? _remoteCpz : _remoteConfig;
                foreach (CloudArtifact artifact in list)
                    if (string.Equals(artifact.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase)) return artifact;
            }
            throw new InvalidOperationException("Selected Dropbox file is not in the current synchronized inventory. Refresh CurrentBuild and select it again.");
        }

        private string CacheArtifact(CloudArtifact artifact, string token)
        {
            string safe = SafeRelative(artifact.RelativePath);
            string destination = Path.Combine(_activeRoot, safe.Replace('/', Path.DirectorySeparatorChar));
            string parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(parent)) Crestron.SimplSharp.CrestronIO.Directory.CreateDirectory(parent);

            bool current = false;
            if (LocalFileIO.Exists(destination))
            {
                long localBytes = LocalFileIO.Length(destination);
                if (localBytes == artifact.Bytes)
                {
                    if (!string.IsNullOrWhiteSpace(artifact.ContentHash))
                    {
                        try { current = string.Equals(DropboxContentHash(destination), artifact.ContentHash, StringComparison.OrdinalIgnoreCase); }
                        catch { current = false; }
                    }
                    else current = true;
                }
            }
            if (current)
            {
                _log.Trace("Dropbox cache current: " + artifact.RelativePath);
                return destination;
            }

            _log.Info("Dropbox downloading: " + artifact.RelativePath + " (" + artifact.Bytes + " bytes)");
            DownloadDropboxFile(artifact.DropboxPath, destination, token);
            long downloaded = LocalFileIO.Length(destination);
            if (downloaded != artifact.Bytes)
                throw new InvalidOperationException("Dropbox size verification failed for " + artifact.RelativePath + ". Expected " + artifact.Bytes + " bytes, received " + downloaded + ".");
            if (!string.IsNullOrWhiteSpace(artifact.ContentHash))
            {
                string actual = DropboxContentHash(destination);
                if (!string.Equals(actual, artifact.ContentHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Dropbox content-hash verification failed for " + artifact.RelativePath + ".");
            }
            _log.Pass("Dropbox download verified: " + artifact.RelativePath);
            return destination;
        }

        private void EnumerateDropboxFiles(string token, List<CloudArtifact> cpz, List<CloudArtifact> config)
        {
            JObject response = DropboxApiJson("files/list_folder", new JObject
            {
                ["path"] = "",
                ["recursive"] = true,
                ["include_deleted"] = false,
                ["include_mounted_folders"] = true,
                ["include_non_downloadable_files"] = false,
                ["limit"] = 2000
            }, token);
            while (true)
            {
                JArray entries = response["entries"] as JArray;
                if (entries != null)
                {
                    foreach (JToken item in entries)
                    {
                        if (!string.Equals(Convert.ToString(item[".tag"]), "file", StringComparison.OrdinalIgnoreCase)) continue;
                        string path = Convert.ToString(item["path_display"]);
                        if (string.IsNullOrWhiteSpace(path)) path = Convert.ToString(item["path_lower"]);
                        string ext = Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
                        if (ext != ".cpz" && ext != ".json") continue;
                        string relative = SafeRelative(path);
                        CloudArtifact artifact = new CloudArtifact
                        {
                            RelativePath = relative,
                            FileName = Convert.ToString(item["name"]),
                            Bytes = item["size"] == null ? 0 : (long)item["size"],
                            Kind = ext == ".cpz" ? "cpz" : "config",
                            DropboxPath = path,
                            ContentHash = Convert.ToString(item["content_hash"])
                        };
                        if (ext == ".cpz") cpz.Add(artifact); else config.Add(artifact);
                    }
                }
                bool hasMore = response["has_more"] != null && (bool)response["has_more"];
                if (!hasMore) break;
                string cursor = Convert.ToString(response["cursor"]);
                if (string.IsNullOrWhiteSpace(cursor)) throw new InvalidOperationException("Dropbox list_folder did not return a continuation cursor.");
                response = DropboxApiJson("files/list_folder/continue", new JObject { ["cursor"] = cursor }, token);
            }
            cpz.Sort(delegate(CloudArtifact a, CloudArtifact b) { return string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase); });
            config.Sort(delegate(CloudArtifact a, CloudArtifact b) { return string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase); });
        }

        private JObject DropboxApiJson(string endpoint, JObject body, string token)
        {
            Dictionary<string, string> headers = new Dictionary<string, string>();
            headers["Authorization"] = "Bearer " + token;
            string text = PostText(DropboxApiRoot + endpoint, "application/json", body == null ? "{}" : body.ToString(Formatting.None), headers);
            return JObject.Parse(text);
        }

        private void DownloadDropboxFile(string remotePath, string destination, string token)
        {
            try { ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12; } catch { }
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(DropboxContentRoot + "files/download");
            request.Method = "POST";
            request.Timeout = 120000;
            request.ReadWriteTimeout = 120000;
            request.Headers["Authorization"] = "Bearer " + token;
            request.Headers["Dropbox-API-Arg"] = new JObject { ["path"] = remotePath }.ToString(Formatting.None);
            try
            {
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream input = response.GetResponseStream())
                using (Crestron.SimplSharp.CrestronIO.FileStream output = new Crestron.SimplSharp.CrestronIO.FileStream(LocalPaths.Normalize(destination), Crestron.SimplSharp.CrestronIO.FileMode.Create, Crestron.SimplSharp.CrestronIO.FileAccess.Write, Crestron.SimplSharp.CrestronIO.FileShare.Read))
                {
                    byte[] buffer = new byte[65536];
                    int read;
                    while (input != null && (read = input.Read(buffer, 0, buffer.Length)) > 0) output.Write(buffer, 0, read);
                    output.Flush();
                }
            }
            catch (WebException ex) { throw DropboxWebException("Dropbox file download failed", ex); }
        }

        private string GetAccessToken()
        {
            if (string.IsNullOrWhiteSpace(DropboxAppKey)) throw new InvalidOperationException(PublicDemoPrivacyMessage);
            lock (_sync)
            {
                if (!string.IsNullOrWhiteSpace(_accessToken) && _accessTokenExpiresUtc > DateTime.UtcNow.AddMinutes(2)) return _accessToken;
            }
            string refresh = LoadRefreshToken();
            if (string.IsNullOrWhiteSpace(refresh)) throw new InvalidOperationException("Dropbox is not authorized. Click Connect Dropbox and complete the one-time authorization.");
            string form = Form(new Dictionary<string, string>
            {
                { "grant_type", "refresh_token" },
                { "refresh_token", refresh },
                { "client_id", DropboxAppKey }
            });
            JObject response;
            try { response = JObject.Parse(PostText(DropboxTokenUrl, "application/x-www-form-urlencoded", form, null)); }
            catch
            {
                // A bad/revoked refresh token should not remain presented as authorized.
                DisconnectAuthorization();
                throw;
            }
            SetAccessToken(response);
            lock (_sync) return _accessToken;
        }

        private void SetAccessToken(JObject response)
        {
            string token = Convert.ToString(response["access_token"]);
            if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("Dropbox OAuth response did not contain an access_token.");
            int seconds = 14400;
            try { if (response["expires_in"] != null) seconds = Convert.ToInt32(response["expires_in"]); } catch { }
            lock (_sync)
            {
                _accessToken = token;
                _accessTokenExpiresUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, seconds));
            }
        }

        private void SaveRefreshToken(string refreshToken)
        {
            LocalFileIO.WriteAllBytes(_refreshTokenPath, new UTF8Encoding(false).GetBytes(refreshToken.Trim()));
        }
        private string LoadRefreshToken()
        {
            return LocalFileIO.Exists(_refreshTokenPath) ? LocalFileIO.ReadAllText(_refreshTokenPath, 16384).Trim() : null;
        }

        private static string PostText(string url, string contentType, string body, IDictionary<string, string> headers)
        {
            try { ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12; } catch { }
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = contentType;
            request.Timeout = 30000;
            request.ReadWriteTimeout = 30000;
            if (headers != null)
                foreach (KeyValuePair<string, string> header in headers) request.Headers[header.Key] = header.Value;
            byte[] bytes = Encoding.UTF8.GetBytes(body ?? string.Empty);
            request.ContentLength = bytes.Length;
            using (Stream stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
            try
            {
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8)) return reader.ReadToEnd();
            }
            catch (WebException ex) { throw DropboxWebException("Dropbox API request failed", ex); }
        }

        private static Exception DropboxWebException(string prefix, WebException ex)
        {
            string detail = ex == null ? string.Empty : ex.Message;
            try
            {
                if (ex != null && ex.Response != null)
                    using (Stream stream = ex.Response.GetResponseStream())
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        string body = reader.ReadToEnd();
                        if (!string.IsNullOrWhiteSpace(body)) detail += " · " + body;
                    }
            }
            catch { }
            return new InvalidOperationException(prefix + ": " + detail, ex);
        }

        private static string Form(IDictionary<string, string> fields)
        {
            List<string> parts = new List<string>();
            foreach (KeyValuePair<string, string> field in fields) parts.Add(Url(field.Key) + "=" + Url(field.Value ?? string.Empty));
            return string.Join("&", parts.ToArray());
        }

        private static string Url(string text) { return Uri.EscapeDataString(text ?? string.Empty); }
        private static string Base64Url(byte[] bytes) { return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }

        private static string SafeRelative(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("Dropbox returned a file without a path.");
            string cleaned = path.Replace('\\', '/').Trim('/');
            string[] parts = cleaned.Split('/');
            List<string> safe = new List<string>();
            foreach (string part in parts)
            {
                if (string.IsNullOrWhiteSpace(part) || part == ".") continue;
                if (part == "..") throw new InvalidOperationException("Dropbox returned an unsafe path: " + path);
                safe.Add(part);
            }
            if (safe.Count == 0) throw new InvalidOperationException("Dropbox returned an empty relative path.");
            return string.Join("/", safe.ToArray());
        }

        private static string DropboxContentHash(string path)
        {
            const int blockSize = 4 * 1024 * 1024;
            using (Crestron.SimplSharp.CrestronIO.FileStream file = new Crestron.SimplSharp.CrestronIO.FileStream(LocalPaths.Normalize(path), Crestron.SimplSharp.CrestronIO.FileMode.Open, Crestron.SimplSharp.CrestronIO.FileAccess.Read, Crestron.SimplSharp.CrestronIO.FileShare.Read))
            using (MemoryStream digests = new MemoryStream())
            using (SHA256 blockSha = SHA256.Create())
            using (SHA256 finalSha = SHA256.Create())
            {
                byte[] buffer = new byte[blockSize];
                while (true)
                {
                    int read = 0;
                    while (read < buffer.Length)
                    {
                        int n = file.Read(buffer, read, buffer.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    if (read <= 0) break;
                    byte[] digest = blockSha.ComputeHash(buffer, 0, read);
                    digests.Write(digest, 0, digest.Length);
                    if (read < buffer.Length) break;
                }
                byte[] all = digests.ToArray();
                byte[] final = finalSha.ComputeHash(all);
                StringBuilder hex = new StringBuilder(final.Length * 2);
                foreach (byte b in final) hex.Append(b.ToString("x2"));
                return hex.ToString();
            }
        }
    }
}
