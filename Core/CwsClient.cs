using Crestron.SimplSharp.Net.Http;
using Crestron.SimplSharp.Net.Https;
using Newtonsoft.Json.Linq;
using System;
using System.Text;

namespace MAV.ProductionRegressionController
{
    public sealed class CwsClient
    {
        private readonly ControllerLogger _log;
        public CwsClient(ControllerLogger log) { _log = log; }

        public JObject Get(TargetSettings target, string path) { return Request(target, "GET", path, null); }
        public JObject GetQuiet(TargetSettings target, string path) { return RequestCore(target, "GET", path, null, false); }
        public JObject PostQuiet(TargetSettings target, string path, object body) { return RequestCore(target, "POST", path, body == null ? "{}" : Newtonsoft.Json.JsonConvert.SerializeObject(body), false); }
        public JObject Post(TargetSettings target, string path, object body) { return Request(target, "POST", path, body == null ? "{}" : Newtonsoft.Json.JsonConvert.SerializeObject(body)); }

        public JObject Request(TargetSettings target, string method, string path, string jsonBody)
        {
            return RequestCore(target, method, path, jsonBody, true);
        }

        private JObject RequestCore(TargetSettings target, string method, string path, string jsonBody, bool verbose)
        {
            string scheme = target.UseHttpsCws ? "https" : "http";
            string url = scheme + "://" + target.Address + (path.StartsWith("/") ? path : "/" + path);
            if (verbose) _log.Trace("CWS " + method + " " + url);
            string text;
            if (target.UseHttpsCws)
            {
                HttpsClient client = new HttpsClient();
                try
                {
                    client.TimeoutEnabled = true;
                    client.Timeout = 10;
                    if (target.AllowUntrustedHttps) { client.PeerVerification = false; client.HostVerification = false; }
                    text = method.Equals("POST", StringComparison.OrdinalIgnoreCase)
                        ? client.Post(url, Encoding.UTF8.GetBytes(jsonBody ?? "{}"))
                        : client.Get(url);
                }
                finally { try { client.Dispose(); } catch { } }
            }
            else
            {
                HttpClient client = new HttpClient();
                try
                {
                    client.TimeoutEnabled = true;
                    client.Timeout = 10;
                    text = method.Equals("POST", StringComparison.OrdinalIgnoreCase)
                        ? client.Post(url, Encoding.UTF8.GetBytes(jsonBody ?? "{}"))
                        : client.Get(url);
                }
                finally { try { client.Dispose(); } catch { } }
            }
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Empty CWS response from " + path);
            JObject obj = JObject.Parse(text);
            JToken success = obj["Success"] ?? obj["success"];
            if (success != null && success.Type == JTokenType.Boolean && !success.Value<bool>())
                throw new InvalidOperationException(Convert.ToString(obj["Message"] ?? obj["message"] ?? "CWS request failed."));
            return obj;
        }
    }
}
