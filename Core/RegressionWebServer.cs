using Crestron.SimplSharp.CrestronIO;
using Crestron.SimplSharp.WebScripting;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace MAV.ProductionRegressionController
{
    public sealed class RegressionWebServer
    {
        private readonly RegressionController _controller;
        private HttpCwsServer _server;
        public RegressionWebServer(RegressionController controller) { _controller = controller; }

        public void Start()
        {
            _server = new HttpCwsServer("/regression");
            Add("ui", new TextHandler("text/html", EmbeddedWeb.IndexHtml));
            Add("ui/app.js", new TextHandler("application/javascript", EmbeddedWeb.AppJs));
            Add("ui/styles.css", new TextHandler("text/css", EmbeddedWeb.StylesCss));
            Add("status", new JsonHandler(ctx => ApiEnvelope.Ok(SafeState(), "Controller status.")));
            Add("session/configure", new JsonHandler(ctx => _controller.WithIdleMutation(() => { _controller.Configure(Read<SessionConfigureRequest>(ctx)); return ApiEnvelope.Ok(SafeState(), "Target configured."); })));
            Add("target/slots", new JsonHandler(ctx => ApiEnvelope.Ok(new { Raw = _controller.ScanSlots(), ControllerSlot = _controller.State.ControllerSlot, SelfTarget = _controller.State.SelfTarget }, "Processor slot state returned.")));
            Add("upload/start", new JsonHandler(ctx => _controller.WithIdleMutation(() => { UploadStartRequest r = Read<UploadStartRequest>(ctx); string p = _controller.Uploads.Start(r.Kind, r.FileName, r.Size); _controller.State.Status = RunStatus.Uploading; return ApiEnvelope.Ok(new { Path = p }, "Upload started."); })));
            Add("upload/chunk", new JsonHandler(ctx => _controller.WithIdleMutation(() => { UploadChunkRequest r = Read<UploadChunkRequest>(ctx); long n = _controller.Uploads.Append(r.Kind, r.Base64); return ApiEnvelope.Ok(new { Bytes = n, Index = r.Index }, "Chunk accepted."); })));
            Add("upload/complete", new JsonHandler(ctx => _controller.WithIdleMutation(() => { _controller.Uploads.EnsureComplete(); return ApiEnvelope.Ok(new { Cpz = _controller.Uploads.CpzPath, Config = _controller.Uploads.ConfigPath }, "Upload complete and byte lengths verified."); })));
            Add("package/analyze", new JsonHandler(ctx => _controller.WithIdleMutation(() => ApiEnvelope.Ok(_controller.AnalyzePackage(), "Package analyzed."))));
            Add("offline/status", new JsonHandler(ctx => ApiEnvelope.Ok(_controller.GetOfflineBundleInventory(), "Offline bundle status returned.")));
            Add("offline/upload/start", new JsonHandler(ctx => _controller.WithIdleMutation(() => { OfflineBundleStartRequest r = Read<OfflineBundleStartRequest>(ctx); return ApiEnvelope.Ok(new { Path = _controller.StartOfflineBundleUpload(r.FileName, r.Size) }, "Offline bundle upload started."); })));
            Add("offline/upload/chunk", new JsonHandler(ctx => _controller.WithIdleMutation(() => { OfflineBundleChunkRequest r = Read<OfflineBundleChunkRequest>(ctx); return ApiEnvelope.Ok(new { Bytes = _controller.AppendOfflineBundleChunk(r.Base64), Index = r.Index }, "Offline bundle chunk accepted."); })));
            Add("offline/upload/complete", new JsonHandler(ctx => _controller.WithIdleMutation(() => ApiEnvelope.Ok(_controller.CompleteOfflineBundleUpload(), "Offline bundle imported and inventoried."))));
            Add("offline/stage", new JsonHandler(ctx => _controller.WithIdleMutation(() => { OfflineBundleStageRequest r = Read<OfflineBundleStageRequest>(ctx); return ApiEnvelope.Ok(_controller.StageOfflineBundlePackage(r.CpzRelativePath, r.ConfigRelativePath), "Offline bundle package staged and analyzed."); })));
            Add("cloud/status", new JsonHandler(ctx => ApiEnvelope.Ok(_controller.GetCloudInventory(), "Dropbox CurrentBuild status returned.")));
            Add("cloud/auth/status", new JsonHandler(ctx => ApiEnvelope.Ok(_controller.GetDropboxAuthorizationStatus(), "Dropbox authorization status returned.")));
            Add("cloud/auth/start", new JsonHandler(ctx => ApiEnvelope.Ok(_controller.BeginDropboxAuthorization(), "Dropbox authorization started.")));
            Add("cloud/auth/complete", new JsonHandler(ctx => { DropboxAuthCompleteRequest r = Read<DropboxAuthCompleteRequest>(ctx); return ApiEnvelope.Ok(_controller.CompleteDropboxAuthorization(r.SessionId, r.Code), "Dropbox authorization complete."); }));
            Add("cloud/auth/disconnect", new JsonHandler(ctx => ApiEnvelope.Ok(_controller.DisconnectDropboxAuthorization(), "Dropbox authorization removed.")));
            Add("cloud/sync", new JsonHandler(ctx => { _controller.StartCloudSync(null); return ApiEnvelope.Ok(_controller.GetCloudInventory(), "Dropbox CurrentBuild inventory synchronization started."); }));
            Add("cloud/stage", new JsonHandler(ctx => _controller.WithIdleMutation(() => { CloudStageRequest r = Read<CloudStageRequest>(ctx); return ApiEnvelope.Ok(_controller.StageCloudPackage(r.CpzRelativePath, r.ConfigRelativePath), "Cloud package staged and analyzed."); })));
            Add("package/config/get", new JsonHandler(ctx => ApiEnvelope.Ok(_controller.ReadStagedConfiguration(), "Staged JSON loaded.")));
            Add("package/config/validate", new JsonHandler(ctx => { ConfigEditRequest r = Read<ConfigEditRequest>(ctx); return ApiEnvelope.Ok(_controller.ValidateStagedConfiguration(r.JsonText), "JSON is valid."); }));
            Add("package/config/save", new JsonHandler(ctx => _controller.WithIdleMutation(() => { ConfigEditRequest r = Read<ConfigEditRequest>(ctx); return ApiEnvelope.Ok(_controller.SaveStagedConfiguration(r.JsonText, r.ExpectedSha256), "Changes saved to staged JSON (not deployed)." ); })));
            Add("console/connect", new JsonHandler(ctx => _controller.WithIdleMutation(() => { _controller.ConnectTargetConsole(); return ApiEnvelope.Ok(_controller.GetTargetConsoleStatus(), "Live target console connected."); })));
            Add("console/disconnect", new JsonHandler(ctx => _controller.WithIdleMutation(() => { _controller.DisconnectTargetConsole(); return ApiEnvelope.Ok(_controller.GetTargetConsoleStatus(), "Live target console disconnected."); })));
            Add("console/status", new JsonHandler(ctx => ApiEnvelope.Ok(_controller.GetTargetConsoleStatus(), "Live target console status returned.")));
            Add("console/send", new JsonHandler(ctx => _controller.WithIdleMutation(() => { ConsoleCommandRequest r = Read<ConsoleCommandRequest>(ctx); return ApiEnvelope.Ok(new { Output = _controller.ExecuteTargetConsole(r.Command) }, "Console command completed."); })));
            Add("console/preset", new JsonHandler(ctx => _controller.WithIdleMutation(() => { ConsolePresetRequest r = Read<ConsolePresetRequest>(ctx); return ApiEnvelope.Ok(new { Output = _controller.ExecuteConsolePreset(r.Preset) }, "Console preset completed."); })));
            Add("console/poll", new JsonHandler(ctx => { ConsolePollRequest r = Read<ConsolePollRequest>(ctx); return ApiEnvelope.Ok(_controller.Console.GetAfter(r.AfterSequence, r.MaxEntries), "Target console entries returned."); }));
            Add("console/clear", new JsonHandler(ctx => { _controller.Console.Clear(); return ApiEnvelope.Ok(new { Cleared = true }, "Target console transcript cleared."); }));
            Add("console/export", new TextHandler("text/plain", () => _controller.ExportConsole()));
            Add("workflow/deploy-test", new JsonHandler(ctx => { _controller.StartWorkflow(true, Read<MaintenanceRequest>(ctx).MaintenanceAcknowledged); return ApiEnvelope.Ok(SafeState(), "Deploy + regression started."); }));
            Add("workflow/test-only", new JsonHandler(ctx => { _controller.StartWorkflow(false, Read<MaintenanceRequest>(ctx).MaintenanceAcknowledged); return ApiEnvelope.Ok(SafeState(), "Regression started against currently loaded program."); }));
            Add("workflow/abort", new JsonHandler(ctx => { _controller.Abort(); return ApiEnvelope.Ok(SafeState(), "Abort requested."); }));
            Add("logs/poll", new JsonHandler(ctx => { LogPollRequest r = Read<LogPollRequest>(ctx); return ApiEnvelope.Ok(_controller.Log.GetAfter(r.AfterSequence, r.MaxEntries), "Controller log entries returned."); }));
            Add("logs/export", new TextHandler("text/plain", () => _controller.ExportLog()));
            Add("target-sdk/connect", new JsonHandler(ctx => _controller.WithIdleMutation(() => { _controller.ConnectTargetDiagnostics(); return ApiEnvelope.Ok(_controller.TargetSdkLog.Status(), "Target SDK observation started."); })));
            Add("target-sdk/disconnect", new JsonHandler(ctx => _controller.WithIdleMutation(() => { _controller.DisconnectTargetDiagnostics(); return ApiEnvelope.Ok(_controller.TargetSdkLog.Status(), "Target SDK observation stopped."); })));
            Add("target-sdk/status", new JsonHandler(ctx => ApiEnvelope.Ok(_controller.TargetSdkLog.Status(), "External target SDK journal status.")));
            Add("target-sdk/poll", new JsonHandler(ctx => { LogPollRequest r = Read<LogPollRequest>(ctx); return ApiEnvelope.Ok(_controller.TargetSdkLog.GetAfter(r.AfterSequence, r.MaxEntries), "External target SDK journal entries returned."); }));
            Add("target-sdk/export", new TextHandler("application/x-ndjson", () => _controller.ExportTargetDiagnostics()));
            Add("evidence/combined", new TextHandler("text/plain", () => _controller.ExportCombined()));
            Add("evidence/report", new TextHandler("application/json", () => _controller.ExportReport()));
            _server.HttpRequestHandler = new NotFoundHandler();
            if (!_server.Register()) throw new InvalidOperationException("Unable to register /cws/regression.");
        }

        private object SafeState()
        {
            RunState s = _controller.State;
            return new
            {
                s.SessionId, s.Status, s.Phase, s.Detail, s.Progress, s.StartedUtc, s.CompletedUtc,
                Target = s.Target == null ? null : new { s.Target.Address, s.Target.ProgramSlot, s.Target.UseHttpsCws, s.Target.RunMode },
                s.Package, s.SelfTarget, s.ControllerSlot, Checks = s.Checks, LogPath = _controller.Log.SessionLogPath
            };
        }

        private void Add(string route, IHttpCwsHandler handler) { HttpCwsRoute r = new HttpCwsRoute(route); r.RouteHandler = handler; _server.Routes.Add(r); }
        private static T Read<T>(HttpCwsContext context)
        {
            if (context == null || context.Request == null || context.Request.InputStream == null || context.Request.ContentLength <= 0) return Activator.CreateInstance<T>();
            StreamReader reader = context.Request.ContentEncoding == null ? new StreamReader(context.Request.InputStream) : new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
            string text = reader.ReadToEnd();
            return string.IsNullOrWhiteSpace(text) ? Activator.CreateInstance<T>() : JsonConvert.DeserializeObject<T>(text);
        }
        private static void Json(HttpCwsContext context, int code, object payload)
        {
            context.Response.StatusCode = code; context.Response.ContentType = "application/json"; context.Response.Charset = "utf-8"; context.Response.AppendHeader("Cache-Control", "no-store"); context.Response.Write(JsonConvert.SerializeObject(payload), true);
        }

        private sealed class JsonHandler : IHttpCwsHandler
        {
            private readonly Func<HttpCwsContext, ApiEnvelope> _handler;
            public JsonHandler(Func<HttpCwsContext, ApiEnvelope> handler) { _handler = handler; }
            public void ProcessRequest(HttpCwsContext context)
            {
                try { Json(context, 200, _handler(context)); }
                catch (Exception ex) { Json(context, 500, ApiEnvelope.Fail(ex.Message)); }
            }
        }
        private sealed class TextHandler : IHttpCwsHandler
        {
            private readonly string _contentType; private readonly Func<string> _content;
            public TextHandler(string contentType, string content) { _contentType = contentType; _content = delegate { return content; }; }
            public TextHandler(string contentType, Func<string> content) { _contentType = contentType; _content = content; }
            public void ProcessRequest(HttpCwsContext context) { context.Response.StatusCode = 200; context.Response.ContentType = _contentType; context.Response.Charset = "utf-8"; context.Response.AppendHeader("Cache-Control", "no-store"); context.Response.Write(_content() ?? string.Empty, true); }
        }
        private sealed class NotFoundHandler : IHttpCwsHandler { public void ProcessRequest(HttpCwsContext c) { Json(c, 404, ApiEnvelope.Fail("Regression Controller route not found.")); } }
        private sealed class MaintenanceRequest { public bool MaintenanceAcknowledged { get; set; } }
        private sealed class ConfigEditRequest { public string JsonText { get; set; } public string ExpectedSha256 { get; set; } }
        private sealed class LogPollRequest { public long AfterSequence { get; set; } public int MaxEntries { get; set; } }
        private sealed class ConsolePollRequest { public long AfterSequence { get; set; } public int MaxEntries { get; set; } }
        private sealed class ConsoleCommandRequest { public string Command { get; set; } }
        private sealed class ConsolePresetRequest { public string Preset { get; set; } }
        private sealed class DropboxAuthCompleteRequest { public string SessionId { get; set; } public string Code { get; set; } }
        private sealed class CloudStageRequest { public string CpzRelativePath { get; set; } public string ConfigRelativePath { get; set; } }
        private sealed class OfflineBundleStartRequest { public string FileName { get; set; } public long Size { get; set; } }
        private sealed class OfflineBundleChunkRequest { public int Index { get; set; } public string Base64 { get; set; } }
        private sealed class OfflineBundleStageRequest { public string CpzRelativePath { get; set; } public string ConfigRelativePath { get; set; } }
    }
}
