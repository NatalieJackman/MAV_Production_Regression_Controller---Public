namespace MAV.ProductionRegressionController
{
    internal static class EmbeddedWeb
    {
        public static readonly string IndexHtml = @"<!doctype html>
<html lang=""en"">
<head><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width,initial-scale=1""><title>MAV Regression | Configure · Deploy · Test</title><link rel=""stylesheet"" href=""/cws/regression/ui/styles.css""></head>
<body data-site-theme=""night"" data-editor-appearance=""mav"">
<div class=""shell"">
  <aside class=""sidebar"">
    <div class=""sideBrand""><span class=""brandMark"">M</span><div><strong>MAV</strong><small>TEST PLATFORM</small></div></div>
    <div class=""sideCaption"">WORKSPACE</div>
    <nav aria-label=""Workspace navigation"">
      <a href=""#workspace-target"">Target processor</a>
      <a href=""#workspace-package"">Package library</a>
      <a href=""#workspace-json"">JSON configuration</a>
      <a href=""#workspace-deploy"">Deploy &amp; regression</a>
      <a href=""#workspace-tests"">Test results</a>
      <a href=""#workspace-console"">Target console</a>
      <a href=""#workspace-target-sdk"">Target SDK log</a>
      <a href=""#workspace-logs"">SDK log</a>
    </nav>
    <div class=""sidebarFoot""><span class=""smallDot""></span> Zero static IPIDs<br><small>Independent controller · Preview 010 Public Demo</small></div>
  </aside>
  <div class=""shellContent"">
    <header class=""topbar"">
      <div class=""brandBlock""><div class=""eyebrow"">MAV / ENGINEERING / VALIDATION</div><h1>Configure · Deploy · Regression</h1><p>One appliance for program delivery, JSON configuration, black-box verification and evidence.</p></div>
      <div class=""headerTools""><span class=""chip dirty"" title=""Privacy-sensitive identifiers and cloud credentials are intentionally omitted from this public demonstration build."">PUBLIC DEMO · PRIVACY SANITIZED</span><label class=""inlineSelect"">Palette <select id=""siteTheme""><option value=""night"">MAV Night</option><option value=""day"">MAV Day</option></select></label><div id=""globalState"" class=""state idle"">IDLE</div></div>
    </header>
    <main>
  <section class=""panel"" style=""border-left:4px solid var(--yellow)""><strong>Public demonstration build</strong><p class=""help"">Privacy-sensitive room identifiers, personnel labels, and the Dropbox application key have been removed or replaced. Features that depend on those private values are visibly disabled or use sanitized examples; the regression/deployment architecture is otherwise unchanged.</p></section>

  <section class=""summaryGrid"" aria-label=""Current session summary"">
    <div class=""summaryCard""><span>Target</span><strong id=""summaryTarget"">Not configured</strong><small id=""summarySlot"">Select a processor and slot</small></div>
    <div class=""summaryCard""><span>Package</span><strong id=""summaryPackage"">Not staged</strong><small id=""summaryPackageDetail"">Local upload or cloud cache</small></div>
    <div class=""summaryCard""><span>Workflow</span><strong id=""summaryWorkflow"">Idle</strong><small id=""summaryProgress"">0% complete</small></div>
  </section>

  <div class=""workGrid"">
    <section id=""workspace-target"" class=""panel"">
      <div class=""panelHead"">
        <div><span class=""step"">01</span><h2>Target processor</h2></div>
        <button id=""scan"" class=""ghost small"" type=""button"">Scan slots</button>
      </div>
      <div class=""formGrid targetGrid"">
        <label>Address<input id=""address"" placeholder=""192.168.1.50""></label>
        <label>Username<input id=""username"" autocomplete=""username"" value=""admin""></label>
        <label>Password<input id=""password"" type=""password"" autocomplete=""current-password""></label>
        <label>Program slot<input id=""slot"" type=""number"" min=""1"" max=""10"" value=""2""></label>
      </div>
      <div class=""optionRow"">
        <label class=""check""><input id=""https"" type=""checkbox""><span>HTTPS CWS</span></label>
        <label class=""check""><input id=""untrusted"" type=""checkbox""><span>Allow self-signed HTTPS</span></label>
        <label class=""check dangerCheck""><input id=""labOverride"" type=""checkbox""><span>LAB override: replace without recoverable CPZ</span></label>
        <label class=""inlineSelect"">Mode<select id=""mode""><option value=""full"">Full regression</option><option value=""health"">Health check</option></select></label>
        <label class=""inlineSelect"">Test profile<select id=""targetProfile""><option value=""auto"">Use staged package</option><option value=""farm"">Existing VTC Farm</option><option value=""room"">Existing MAV Room</option></select></label>
      </div>
      <div class=""actions""><button id=""configure"" class=""primary"" type=""button"">Configure target</button></div>
      <pre id=""slotOutput"" class=""miniConsole"">Target not configured.</pre>
    </section>

    <section id=""workspace-package"" class=""panel packagePanel"">
      <div class=""panelHead""><div><span class=""step"">02</span><h2>Package source</h2></div><span class=""chip"">CPZ + JSON</span></div>
      <div class=""sourceTabs"" role=""tablist"" aria-label=""Package source"">
        <button id=""tabCloud"" class=""tab active"" type=""button"">Cloud CurrentBuild</button>
        <button id=""tabOffline"" class=""tab"" type=""button"">Offline bundle</button>
        <button id=""tabLocal"" class=""tab"" type=""button"">Local upload</button>
      </div>

      <div id=""cloudSource"" class=""sourcePane active"">
        <div class=""cloudStatusRow"">
          <div><strong>Cloud CurrentBuild · privacy-disabled demo</strong><small>The production build supports Dropbox OAuth/PKCE. The application key is intentionally omitted from this public repository.</small></div>
          <div class=""cloudChips""><span id=""dropboxAuthChip"" class=""chip neutral"">Not authorized</span><span id=""cloudStatusChip"" class=""chip neutral"">Not synced</span></div>
        </div>
        <div class=""cloudAuthCard"">
          <div><strong>Cloud authorization disabled in public demo</strong><p>This demonstrational source intentionally omits the real Dropbox application identifier. Use Offline Bundle or Local Upload to exercise the same staging and regression path.</p></div>
          <div class=""actions cloudAuthActions""><button id=""connectDropbox"" class=""primary"" type=""button"">Connect Dropbox</button><button id=""disconnectDropbox"" class=""ghost"" type=""button"">Disconnect</button></div>
        </div>
        <div class=""actions""><button id=""syncCloud"" type=""button"">Sync CurrentBuild inventory</button><button id=""refreshCloud"" class=""ghost"" type=""button"">Refresh status</button></div>
        <div class=""formGrid two"">
          <label>Cloud CPZ<select id=""cloudCpz""><option value="""">Authorize and sync to populate…</option></select></label>
          <label>Cloud configuration<select id=""cloudConfig""><option value="""">Authorize and sync to populate…</option></select></label>
        </div>
        <div class=""actions""><button id=""stageCloud"" class=""primary"" type=""button"">Download + stage selected package</button></div>
        <p id=""cloudMeta"" class=""help"">Sync reads Dropbox metadata only. The selected CPZ and JSON are downloaded and Dropbox-content-hash verified when you stage them.</p>
      </div>


      <div id=""offlineSource"" class=""sourcePane"">
        <div class=""cloudStatusRow"">
          <div><strong>Offline regression bundle</strong><small>Upload a ZIP from the browser. The laptop may obtain it by any approved path; the CP4N itself does not need Internet access.</small></div>
          <div><span id=""offlineStatusChip"" class=""chip neutral"">No bundle</span></div>
        </div>
        <div class=""offlineTransferNote""><strong>Browser transfer boundary</strong><p>If the operator laptop can reach both an approved package source and this processor, download the bundle on the laptop and upload it here. If it cannot, bring the same ZIP by the site's approved offline transfer process. Deployment and regression are identical afterward.</p></div>
        <label>Regression bundle ZIP<input id=""offlineBundle"" type=""file"" accept="".zip,application/zip""></label>
        <div class=""actions""><button id=""importOffline"" class=""primary"" type=""button"">Import offline bundle</button><button id=""refreshOffline"" class=""ghost"" type=""button"">Refresh inventory</button></div>
        <div class=""formGrid two"">
          <label>Bundle CPZ<select id=""offlineCpz""><option value="""">Import a bundle to populate…</option></select></label>
          <label>Bundle configuration<select id=""offlineConfig""><option value="""">Import a bundle to populate…</option></select></label>
        </div>
        <div class=""actions""><button id=""stageOffline"" class=""primary"" type=""button"">Stage selected package</button></div>
        <p id=""offlineMeta"" class=""help"">No Internet connection is required by the Regression Controller for this workflow.</p>
      </div>

      <div id=""localSource"" class=""sourcePane"">
        <div class=""formGrid two"">
          <label>Program CPZ<input id=""cpz"" type=""file"" accept="".cpz""></label>
          <label>SystemDefinition / configuration<input id=""config"" type=""file"" accept="".json,application/json""></label>
        </div>
        <div class=""actions""><button id=""upload"" class=""primary"" type=""button"">Upload + analyze</button></div>
      </div>

      <div id=""packageCard"" class=""package muted"">No package staged.</div>
    </section>
  </div>


  <section id=""workspace-json"" class=""panel jsonPanel"" aria-labelledby=""jsonTitle"">
    <div class=""panelHead"">
      <div><span class=""step"">03</span><h2 id=""jsonTitle"">Configuration workspace</h2><p>View, edit and validate the staged SystemDefinition before deployment.</p></div>
      <span id=""jsonStatus"" class=""chip neutral"" role=""status"">No staged JSON</span>
    </div>
    <div class=""editorToolbar"">
      <button id=""jsonLoad"" class=""ghost small"" type=""button"">Load staged JSON</button>
      <button id=""jsonFormat"" class=""ghost small"" type=""button"">Format</button>
      <button id=""jsonValidate"" class=""ghost small"" type=""button"">Validate</button>
      <button id=""jsonRevert"" class=""ghost small"" type=""button"">Revert edits</button>
      <label class=""inlineSelect"">Appearance<select id=""editorAppearance""><option value=""mav"">MAV syntax</option><option value=""vscode"">VS Code syntax</option></select></label>
      <div class=""toolbarPush""></div>
      <button id=""jsonDownload"" class=""ghost small"" type=""button"">Export JSON</button>
      <button id=""jsonSave"" class=""primary small"" type=""button"">Save to staging</button>
    </div>
    <div class=""editorNote"">Changes stay in the Regression Controller’s staged copy. They will <strong>not</strong> overwrite cloud files or the target processor until a separately confirmed deployment. Unsaved edits block Deploy &amp; Test.</div>
    <div class=""editorGrid"">
      <div class=""editorWrap"">
        <div class=""editorCaption"">EDITABLE JSON <span id=""editorFile"">—</span></div>
        <div class=""codeFrame""><pre id=""jsonLines"" class=""lineNumbers"" aria-hidden=""true"">1</pre><textarea id=""jsonEditor"" spellcheck=""false"" autocapitalize=""off"" autocomplete=""off"" autocorrect=""off"" aria-label=""SystemDefinition JSON editor"" placeholder=""Stage a CPZ and configuration to edit JSON…""></textarea></div>
      </div>
      <div class=""editorWrap"">
        <div class=""editorCaption"">SYNTAX PREVIEW <span id=""editorSize"">0 bytes</span></div>
        <pre id=""jsonPreview"" class=""syntaxPreview"" aria-label=""JSON syntax preview"">No staged configuration loaded.</pre>
      </div>
    </div>
    <p id=""jsonFeedback"" class=""editorFeedback"" aria-live=""polite"">Load a staged configuration to begin.</p>
  </section>

  <section id=""workspace-deploy"" class=""panel deployPanel"">
    <div class=""panelHead""><div><span class=""step"">04</span><h2>Deploy & regression</h2></div><span id=""phaseBadge"" class=""chip neutral"">Idle</span></div>
    <p class=""bodyCopy"">The controller operates independently in its own program slot. It preflights and backs up the chosen target slot, stages the CPZ/edited JSON/HTML5 resources, and runs external CWS contract checks. A native target self-test, when available, is not a substitute for independent end-to-end validation.</p>
    <label class=""confirm""><input id=""confirmDestructive"" type=""checkbox""><span>I understand that Deploy + Full Workflow may archive, stop, replace, and reload the selected program slot and write configuration / HTML5 resources to the target processor.</span></label>
    <label class=""confirm maintenance""><input id=""confirmMaintenance"" type=""checkbox""><span>I confirm an authorized maintenance window for Full Regression. External test actions may reserve Farm codecs or change active AV routing. Health Check remains read-only.</span></label>
    <div class=""actions""><button id=""deployTest"" class=""success"" type=""button"">Deploy + Full Workflow</button><button id=""testOnly"" class=""ghost"" type=""button"">Test loaded program</button><button id=""abort"" class=""danger"" type=""button"">Abort</button></div>
    <div class=""progress""><div id=""bar""></div></div>
    <div id=""phase"" class=""phase"">Idle</div>
    <p class=""help"">Health Check = read-only external probes. Full Regression = controlled CWS stimulus and assertions; requires no active real-room assignment. Actual MAVE/EISC media compatibility still needs an approved field fixture.</p>
  </section>

  <div class=""resultsGrid"">
    <section id=""workspace-tests"" class=""panel"">
      <div class=""panelHead""><div><h2>Regression checks</h2><p>Live PASS / WARN / FAIL / SKIP results</p></div><div class=""headActions""><button id=""refresh"" class=""ghost small"" type=""button"">Refresh</button><button id=""exportReport"" class=""ghost small"" type=""button"">Export report</button></div></div>
      <div id=""checks"" class=""checksList""><div class=""empty"">No checks yet.</div></div>
    </section>

    <section id=""workspace-console"" class=""panel consolePanel"">
      <div class=""panelHead""><div><h2>Target console</h2><p>Persistent SSH shell · live processor output · operator commands</p></div><div class=""headActions""><span id=""consoleState"" class=""chip neutral"">Disconnected</span><button id=""connectConsole"" class=""ghost small"" type=""button"">Connect</button><button id=""disconnectConsole"" class=""ghost small"" type=""button"">Disconnect</button><button id=""clearConsole"" class=""ghost small"" type=""button"">Clear</button><button id=""exportConsole"" class=""ghost small"" type=""button"">Export</button></div></div>
      <div class=""presetBar"">
        <button data-preset=""state"" type=""button"">State snapshot</button>
        <button data-preset=""registry"" type=""button"">PROGREG</button>
        <button data-preset=""appstat"" type=""button"">APPSTAT</button>
        <button data-preset=""comments"" type=""button"">Slot comments</button>
        <button data-preset=""errors"" type=""button"">Current errors</button>
        <button data-preset=""load"" class=""warnAction"" type=""button"">PROGLOAD slot</button>
        <button data-preset=""kill"" class=""dangerAction"" type=""button"">KILLPROG slot</button>
      </div>
      <pre id=""targetConsole"" class=""terminal"" aria-live=""polite"">Target console ready. Configure a target to begin.</pre>
      <form id=""consoleForm"" class=""commandRow"">
        <label class=""srOnly"" for=""consoleCommand"">Console command</label>
        <input id=""consoleCommand"" placeholder=""Type a Crestron console command…"" autocomplete=""off"">
        <button id=""sendConsole"" type=""submit"">Send</button>
      </form>
      <p class=""help"">Commands are sent to the currently configured target. Destructive preset buttons require confirmation.</p>
    </section>
  </div>

  <section id=""workspace-target-sdk"" class=""panel logPanel targetSdkPanel"">
    <div class=""panelHead"">
      <div><h2>Target application · live SDK log</h2><p>Independent CWS journal observer · source sequence / restart epoch / regression-step correlation</p></div>
      <div class=""headActions"">
        <span id=""targetSdkState"" class=""chip neutral"" role=""status"">Not connected</span>
        <button id=""connectTargetSdk"" class=""ghost small"" type=""button"">Observe loaded program</button>
        <button id=""disconnectTargetSdk"" class=""ghost small"" type=""button"">Stop</button>
        <button id=""exportTargetSdk"" class=""ghost small"" type=""button"">Export target log</button>
        <button id=""exportCombined"" class=""ghost small"" type=""button"">Export combined</button>
      </div>
    </div>
    <p id=""targetSdkMeta"" class=""help"">Starts automatically for a regression run. To observe an already-loaded program, configure the target and select a Farm or Room test profile, then press Observe.</p>
    <div class=""logFilters""><label class=""check""><input type=""checkbox"" id=""targetSdkAutoScroll"" checked> Follow latest</label>
      <label>Filter events<input id=""targetSdkFilter"" placeholder=""Filter by message, level, or test step"" autocomplete=""off""></label></div>
    <pre id=""targetSdkLog"" class=""terminal targetSdkTerminal"" aria-label=""Target SDK diagnostic log"">Waiting for target SDK diagnostic CWS feed…</pre>
    <p class=""help"">Source timestamps are preserved; displayed order uses observation time, epoch and sequence. This is not the SSH console and does not use target-native self-test. Missing diagnostics produce a warning, never a false test PASS.</p>
  </section>

  <section id=""workspace-logs"" class=""panel logPanel"">
    <div class=""panelHead""><div><h2>Controller / SDK log</h2><p id=""logMeta"">SDK-backed session log · live CWS view</p></div><button id=""exportLog"" class=""ghost small"" type=""button"">Export log</button></div>
    <pre id=""log"" class=""terminal controllerTerminal"">Controller UI loaded.</pre>
  </section>

    </main>
  </div>
</div>

<div id=""dropboxAuthModal"" class=""modalBackdrop"" hidden>
  <section class=""modalCard"" role=""dialog"" aria-modal=""true"" aria-labelledby=""dropboxAuthTitle"">
    <div class=""modalHead""><div><span class=""eyebrow"">MAV · DROPBOX AUTHORIZATION</span><h2 id=""dropboxAuthTitle"">Paste the Dropbox authorization code</h2></div><button id=""closeDropboxAuth"" class=""modalClose"" type=""button"" aria-label=""Close authorization dialog"">×</button></div>
    <p>Dropbox has been opened in another browser tab. Sign in if needed, click <strong>Allow</strong>, then copy the short single-use authorization code Dropbox displays and paste it below.</p>
    <div class=""authSteps""><span>1 · Allow MAV Utility</span><span>2 · Copy code</span><span>3 · Paste here</span></div>
    <label>Authorization code<input id=""dropboxAuthCode"" autocomplete=""off"" autocapitalize=""off"" spellcheck=""false"" placeholder=""Paste Dropbox code…""></label>
    <input id=""dropboxAuthSession"" type=""hidden"">
    <p id=""dropboxAuthFeedback"" class=""help"" aria-live=""polite"">This code is single-use. The Regression Controller never displays or logs the resulting access/refresh tokens.</p>
    <div class=""modalActions""><a id=""reopenDropboxAuth"" class=""buttonLink"" target=""_blank"" rel=""noopener"">Open Dropbox again</a><div class=""toolbarPush""></div><button id=""cancelDropboxAuth"" class=""ghost"" type=""button"">Cancel</button><button id=""submitDropboxAuth"" class=""primary"" type=""button"">Authorize controller</button></div>
  </section>
</div>
<script src=""/cws/regression/ui/app.js""></script></body></html>
";
        public static readonly string StylesCss = @"
:root{--bg:#07111f;--sidebar:#081626;--panel:#0e1d30;--panel2:#13263d;--line:#29435f;--text:#e8eef6;--muted:#91a5bb;--blue:#58a6ff;--green:#46d39a;--yellow:#f0b85a;--red:#ff6d75;--terminal:#071321;--btntext:#051421;--editorbg:#091725;--key:#62c4ff;--string:#8fe1c0;--number:#f0b85a}
body[data-site-theme=""day""]{--bg:#eef3f8;--sidebar:#f7f9fc;--panel:#fff;--panel2:#f4f7fb;--line:#c9d5e2;--text:#172231;--muted:#42566b;--blue:#0969da;--green:#1f883d;--yellow:#9a6700;--red:#cf222e;--terminal:#f5f8fc;--btntext:#fff;--editorbg:#fff;--key:#0067a3;--string:#1c7a5b;--number:#a85f00}
*{box-sizing:border-box}html{scroll-behavior:smooth}body{margin:0;min-height:100%;background:var(--bg);color:var(--text);font:14px/1.5 'Segoe UI',Arial,sans-serif}button,input,select,textarea{font:inherit}button{cursor:pointer}button:focus-visible,input:focus-visible,select:focus-visible,textarea:focus-visible,a:focus-visible{outline:2px solid var(--blue);outline-offset:2px}
.shell{display:grid;grid-template-columns:224px minmax(0,1fr);min-height:100vh}.sidebar{position:sticky;top:0;align-self:start;height:100vh;background:var(--sidebar);border-right:1px solid var(--line);display:flex;flex-direction:column;padding:24px 15px}.sideBrand{display:flex;gap:10px;align-items:center;padding:0 8px 27px}.brandMark{width:36px;height:36px;border-radius:10px;display:grid;place-items:center;background:var(--blue);color:var(--btntext);font-size:21px;font-weight:900}.sideBrand strong,.sideBrand small{display:block}.sideBrand small{color:var(--muted);font-size:10px;letter-spacing:.11em}.sideCaption{padding:0 12px 8px;color:var(--muted);font-size:10px;font-weight:800;letter-spacing:.14em}.sidebar nav{display:grid;gap:4px}.sidebar nav a{color:var(--muted);text-decoration:none;padding:10px 12px;border-radius:8px;font-size:13px}.sidebar nav a:hover,.sidebar nav a:focus{background:var(--panel2);color:var(--text)}.sidebarFoot{margin-top:auto;border-top:1px solid var(--line);padding:15px 10px;color:var(--muted);font-size:12px}.sidebarFoot small{font-size:11px}.smallDot{display:inline-block;width:7px;height:7px;background:var(--green);border-radius:100%;margin-right:6px}
.shellContent{min-width:0}.topbar{display:flex;justify-content:space-between;gap:20px;align-items:center;padding:26px clamp(16px,2.8vw,44px);border-bottom:1px solid var(--line);background:var(--sidebar)}.eyebrow{font-size:11px;font-weight:800;letter-spacing:.13em;color:var(--blue)}h1{font-size:25px;letter-spacing:-.025em;margin:3px 0}h2{margin:0;font-size:17px}p{margin:0}.topbar p{color:var(--muted);font-size:12px}.headerTools{display:flex;align-items:center;gap:13px;flex-wrap:wrap}.headerTools select{margin:0}.state{border:1px solid var(--line);padding:8px 14px;border-radius:7px;font-weight:800;letter-spacing:.05em;color:var(--muted);min-width:105px;text-align:center}.state.Passed,.state.passed{color:var(--green);border-color:var(--green)}.state.Warning,.state.warning{color:var(--yellow)}.state.Failed,.state.failed,.state.Aborted,.state.aborted{color:var(--red)}.state.Deploying,.state.WaitingForCws,.state.Testing,.state.Uploading,.state.Ready{color:var(--blue)}main{max-width:1640px;margin:auto;padding:23px clamp(16px,2.5vw,40px) 65px}.summaryGrid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px;margin-bottom:16px}.summaryCard,.panel{background:var(--panel);border:1px solid var(--line);border-radius:9px}.summaryCard{padding:14px 17px;display:grid;gap:3px;min-width:0}.summaryCard span{font-size:10px;letter-spacing:.12em;color:var(--muted);text-transform:uppercase;font-weight:800}.summaryCard strong{font-size:16px;text-overflow:ellipsis;overflow:hidden;white-space:nowrap}.summaryCard small{font-size:11px;color:var(--muted)}.workGrid,.resultsGrid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:14px}.panel{padding:18px;margin-bottom:14px;min-width:0;scroll-margin-top:15px}.panelHead{display:flex;justify-content:space-between;align-items:flex-start;gap:12px;margin-bottom:15px}.panelHead>div:first-child{min-width:0}.panelHead p{color:var(--muted);font-size:12px;margin-top:2px}.step{float:left;margin-right:9px;background:var(--panel2);border:1px solid var(--line);border-radius:5px;padding:2px 7px;color:var(--blue);font-size:11px}.formGrid{display:grid;gap:12px}.formGrid.targetGrid{grid-template-columns:1.6fr 1fr 1fr .55fr}.formGrid.two{grid-template-columns:1fr 1fr}label{color:var(--muted);font-size:12px;font-weight:650}input,select,textarea{display:block;margin-top:5px;width:100%;min-width:0;padding:9px 10px;border-radius:6px;border:1px solid var(--line);background:var(--panel2);color:var(--text)}input[type=file]{font-size:11px}.optionRow{display:flex;align-items:center;flex-wrap:wrap;gap:14px;margin-top:12px}.check,.inlineSelect{display:flex;align-items:center;gap:6px}.check input,.confirm input{width:auto;margin:0}.inlineSelect select{width:auto}.dangerCheck{color:var(--red)}.actions,.headActions,.editorToolbar,.presetBar{display:flex;gap:8px;flex-wrap:wrap;align-items:center}.actions{margin-top:14px}button{padding:9px 13px;border:1px solid var(--line);border-radius:6px;background:var(--panel2);color:var(--text);font-weight:700;font-size:12px}button:hover:not(:disabled){border-color:var(--blue)}button:disabled{opacity:.45;cursor:not-allowed}.primary{background:var(--blue);border-color:var(--blue);color:var(--btntext)}.success{color:var(--green);border-color:var(--green)}.danger,.dangerAction{color:var(--red);border-color:var(--red)}.ghost{background:var(--panel2)}.small{padding:6px 9px;font-size:11px}.chip{display:inline-flex;align-items:center;padding:4px 9px;border-radius:5px;font-size:11px;font-weight:750;background:var(--panel2);color:var(--blue);border:1px solid var(--line)}.chip.neutral{color:var(--muted)}.chip.valid{color:var(--green)}.chip.dirty{color:var(--yellow)}.chip.invalid{color:var(--red)}.sourceTabs{display:flex;gap:7px;border-bottom:1px solid var(--line);margin-bottom:13px}.tab{border:0;border-radius:0;border-bottom:2px solid transparent;color:var(--muted);background:transparent}.tab.active{border-bottom-color:var(--blue);color:var(--blue)}.sourcePane{display:none}.sourcePane.active{display:block}.cloudStatusRow,.package{padding:12px;background:var(--panel2);border:1px solid var(--line);border-radius:7px}.cloudStatusRow{display:flex;justify-content:space-between;gap:9px;margin-bottom:11px}.cloudStatusRow strong,.cloudStatusRow small{display:block}.cloudStatusRow small,.help{color:var(--muted);font-size:11px}.help{margin-top:9px}.package{margin-top:14px;line-height:1.65}.package.muted{color:var(--muted)}.package code{font-size:11px;color:var(--blue)}.miniConsole,.terminal{white-space:pre-wrap;overflow:auto;overflow-wrap:anywhere;background:var(--terminal);color:var(--text);border:1px solid var(--line);padding:13px;border-radius:7px;font:11px/1.55 Consolas,Menlo,monospace;margin:11px 0 0}.miniConsole{max-height:160px}.terminal{height:325px}.controllerTerminal{height:280px}.bodyCopy{color:var(--muted);font-size:12px}.confirm,.editorNote{display:flex;align-items:flex-start;gap:8px;padding:10px 12px;margin-top:12px;background:var(--panel2);border-left:3px solid var(--yellow);border-radius:5px;font-size:12px;color:var(--text)}.editorNote{display:block;margin:10px 0 13px}.progress{height:7px;margin-top:17px;background:var(--panel2);border-radius:6px;overflow:hidden}.progress>div{height:100%;width:0;background:var(--green);transition:width .2s}.phase{font-size:12px;color:var(--muted);margin-top:7px}.checksList{display:grid;gap:4px}.checkResult{display:grid;grid-template-columns:65px minmax(115px,.7fr) 1fr;gap:9px;padding:8px 4px;border-bottom:1px solid var(--line);font-size:12px}.resultBadge{font-weight:900}.PASS{color:var(--green)}.WARN{color:var(--yellow)}.FAIL{color:var(--red)}.SKIP{color:var(--muted)}.resultDetail{color:var(--muted)}.empty{color:var(--muted);padding:13px}.presetBar{margin:0 0 10px}.presetBar button{font-size:11px;padding:6px 8px}.warnAction{color:var(--yellow)}.commandRow{display:grid;grid-template-columns:1fr auto;gap:8px;margin-top:9px}.commandRow input{margin:0;font-family:Consolas,monospace}.srOnly{position:absolute;width:1px;height:1px;margin:-1px;padding:0;overflow:hidden;clip:rect(0,0,0,0)}
/* MAV System Definition JSON editor */
.jsonPanel{border-top:2px solid var(--blue)}.editorToolbar{margin:0 0 10px}.editorToolbar .inlineSelect select{margin:0}.toolbarPush{flex:1}.editorGrid{display:grid;grid-template-columns:1fr 1fr;gap:12px}.editorWrap{min-width:0}.editorCaption{display:flex;justify-content:space-between;gap:8px;padding:7px 10px;font-size:10px;letter-spacing:.085em;font-weight:750;color:var(--muted);background:var(--panel2);border:1px solid var(--line);border-bottom:0;border-radius:6px 6px 0 0}.editorCaption span{overflow:hidden;text-overflow:ellipsis;white-space:nowrap;letter-spacing:0;font-weight:400}.codeFrame{display:grid;grid-template-columns:42px minmax(0,1fr);border:1px solid var(--line);background:var(--editorbg);border-radius:0 0 6px 6px;overflow:hidden}.lineNumbers{height:344px;margin:0;padding:11px 6px 11px 3px;text-align:right;overflow:hidden;user-select:none;pointer-events:none;color:var(--muted);opacity:.6;background:var(--panel2);font:12px/1.5 Consolas,monospace}.codeFrame textarea{height:344px;padding:11px 10px;margin:0;border:0;border-radius:0;background:var(--editorbg);resize:vertical;white-space:pre;overflow:auto;tab-size:2;color:var(--text);font:12px/1.5 Consolas,monospace}.syntaxPreview{height:344px;margin:0;border:1px solid var(--line);border-radius:0 0 6px 6px;padding:11px;overflow:auto;white-space:pre;tab-size:2;background:var(--editorbg);font:12px/1.5 Consolas,monospace;color:var(--text)}.syntaxPreview .key{color:var(--key)}.syntaxPreview .str{color:var(--string)}.syntaxPreview .num,.syntaxPreview .bool{color:var(--number)}.syntaxPreview .null{color:var(--muted)}body[data-editor-appearance=""vscode""][data-site-theme=""night""]{--editorbg:#1e1e1e;--key:#9cdcfe;--string:#ce9178;--number:#b5cea8}body[data-editor-appearance=""vscode""][data-site-theme=""day""]{--editorbg:#fff;--key:#0451a5;--string:#a31515;--number:#098658}.editorFeedback{margin:10px 0 0;font-size:12px;color:var(--muted)}.editorFeedback.ok{color:var(--green)}.editorFeedback.error{color:var(--red)}.editorFeedback.warn{color:var(--yellow)}
@media(max-width:1300px){.workGrid,.resultsGrid,.editorGrid{grid-template-columns:1fr}.formGrid.targetGrid{grid-template-columns:1fr 1fr}}@media(max-width:850px){.shell{display:block}.sidebar{height:auto;position:static;padding:10px 14px}.sideBrand{padding:0 0 9px}.sideCaption,.sidebarFoot{display:none}.sidebar nav{display:flex;overflow-x:auto;gap:5px}.sidebar nav a{white-space:nowrap;padding:7px 9px}.topbar{padding:17px;flex-wrap:wrap}main{padding:14px}.summaryGrid{grid-template-columns:1fr 1fr 1fr}}@media(max-width:580px){.summaryGrid,.formGrid.targetGrid,.formGrid.two{grid-template-columns:1fr}.checkResult{grid-template-columns:60px 1fr}.checkResult .resultDetail{grid-column:1/-1}.cloudStatusRow{flex-direction:column}.terminal{height:250px}.headerTools{width:100%;justify-content:space-between}.panel{padding:12px}.editorToolbar{align-items:stretch}}

/* Independent target application SDK journal */
.targetSdkPanel{border-top:2px solid var(--green)}
.targetSdkPanel .headActions{justify-content:flex-end}
.targetSdkTerminal{min-height:320px;height:380px;white-space:pre-wrap}
.logFilters{display:flex;align-items:center;gap:18px;flex-wrap:wrap;margin-top:10px}
.logFilters>label:last-child{display:flex;align-items:center;gap:8px;flex:1;min-width:220px}
.logFilters input[type=text],.logFilters input:not([type]){max-width:430px;margin:0}
#targetSdkMeta{margin-top:0;margin-bottom:8px}
.targetSdkPanel .chip.valid{border-color:var(--green)}
@media(max-width:580px){.targetSdkPanel .headActions{justify-content:flex-start}.logFilters>label:last-child{min-width:100%;display:block}.logFilters input:not([type]){max-width:none;width:100%;margin-top:5px}}

/* Dropbox OAuth / PKCE */
.cloudChips{display:flex;gap:6px;align-items:center;flex-wrap:wrap;justify-content:flex-end}
.cloudAuthCard{display:flex;align-items:center;justify-content:space-between;gap:18px;padding:12px;background:var(--panel2);border:1px solid var(--line);border-radius:7px;margin-bottom:11px}
.cloudAuthCard strong{font-size:12px;color:var(--text)}.cloudAuthCard p{font-size:11px;color:var(--muted);margin-top:3px;max-width:760px}.cloudAuthActions{margin:0;flex-shrink:0}
.modalBackdrop{position:fixed;inset:0;z-index:1000;background:rgba(1,8,16,.72);display:grid;place-items:center;padding:20px}.modalBackdrop[hidden]{display:none}.modalCard{width:min(620px,100%);background:var(--panel);color:var(--text);border:1px solid var(--line);border-radius:10px;padding:20px;box-shadow:0 24px 80px rgba(0,0,0,.38)}.modalHead{display:flex;align-items:flex-start;justify-content:space-between;gap:16px;margin-bottom:14px}.modalHead h2{font-size:20px;margin-top:3px}.modalClose{font-size:22px;line-height:1;padding:5px 9px;background:transparent}.modalCard>p{color:var(--muted);font-size:12px}.authSteps{display:grid;grid-template-columns:repeat(3,1fr);gap:8px;margin:15px 0}.authSteps span{border:1px solid var(--line);background:var(--panel2);padding:8px;border-radius:6px;color:var(--muted);font-size:11px;text-align:center}.modalCard label{display:block;margin-top:14px}.modalCard input{font-family:Consolas,monospace;font-size:15px}.modalActions{display:flex;align-items:center;gap:8px;flex-wrap:wrap;margin-top:18px}.buttonLink{display:inline-flex;align-items:center;padding:8px 11px;border:1px solid var(--line);border-radius:6px;color:var(--blue);text-decoration:none;font-size:12px;font-weight:700;background:var(--panel2)}.buttonLink:hover{border-color:var(--blue)}
@media(max-width:700px){.cloudAuthCard{display:block}.cloudAuthActions{margin-top:10px}.authSteps{grid-template-columns:1fr}.modalCard{padding:15px}}

/* Preview 006 - offline/browser transfer path */
.offlineTransferNote{padding:12px;background:var(--panel2);border:1px solid var(--line);border-left:3px solid var(--blue);border-radius:7px;margin-bottom:12px}.offlineTransferNote strong{font-size:12px;color:var(--text)}.offlineTransferNote p{font-size:11px;color:var(--muted);margin-top:3px;line-height:1.5}
";
        public static readonly string AppJs = @"const $=s=>document.querySelector(s);
let logCursor=0,consoleCursor=0,busy=false,cloudSyncWasRunning=false;
let sdkCursor=0,sdkPath="""",sdkLines=[],sdkPollBusy=false;
let dropboxAuthPopup=null,dropboxSyncAfterAuth=false;

async function api(path,body){
  const o={cache:'no-store'};
  if(body!==undefined){o.method='POST';o.headers={'Content-Type':'application/json'};o.body=JSON.stringify(body)}
  const r=await fetch('/cws/regression/'+path,o),t=await r.text();let j;
  try{j=JSON.parse(t)}catch{throw new Error(t||('HTTP '+r.status))}
  if(!r.ok||j.Success===false)throw new Error(j.Message||('HTTP '+r.status));return j.Value;
}
function message(e){alert(e?.message||String(e))}
function esc(v){return String(v??'').replace(/[&<>""']/g,m=>({'&':'&amp;','<':'&lt;','>':'&gt;','""':'&quot;',""'"":'&#39;'}[m]))}
function fmtBytes(n){n=Number(n||0);if(n<1024)return n+' B';if(n<1048576)return(n/1024).toFixed(1)+' KB';if(n<1073741824)return(n/1048576).toFixed(1)+' MB';return(n/1073741824).toFixed(2)+' GB'}

async function configure(){try{const v=await api('session/configure',{Address:$('#address').value,Username:$('#username').value,Password:$('#password').value,ProgramSlot:Number($('#slot').value),UseHttpsCws:$('#https').checked,AllowUntrustedHttps:$('#untrusted').checked,AllowReplaceWithoutRecoverableCpz:$('#labOverride').checked,RunMode:$('#mode').value,TestProfile:$('#targetProfile').value});renderState(v);$('#targetConsole').textContent+='\n[configured] '+$('#address').value+' Program '+$('#slot').value}catch(e){message(e)}}
async function scan(){try{$('#slotOutput').textContent='Scanning…';const v=await api('target/slots',{});$('#slotOutput').textContent=`Controller slot: ${v.ControllerSlot}\nSelf target: ${v.SelfTarget}\n\n${v.Raw||''}`}catch(e){$('#slotOutput').textContent='FAILED: '+e.message}}

function bytesToBase64(bytes){let bin='';const step=0x8000;for(let i=0;i<bytes.length;i+=step)bin+=String.fromCharCode.apply(null,bytes.subarray(i,Math.min(i+step,bytes.length)));return btoa(bin)}
async function uploadOne(kind,file){await api('upload/start',{Kind:kind,FileName:file.name,Size:file.size});const chunkSize=256*1024;let idx=0;for(let offset=0;offset<file.size;offset+=chunkSize){const b=new Uint8Array(await file.slice(offset,offset+chunkSize).arrayBuffer());await api('upload/chunk',{Kind:kind,Index:idx++,Base64:bytesToBase64(b)});$('#phase').textContent=`Uploading ${file.name}: ${Math.min(100,Math.round((offset+b.length)/file.size*100))}%`}}
async function upload(){if(jsonDirty&&!confirm('Discard unsaved JSON edits and stage a different local configuration?'))return;const cpz=$('#cpz').files[0],cfg=$('#config').files[0];if(!cpz||!cfg)return alert('Select both the CPZ and JSON configuration.');try{busy=true;await uploadOne('cpz',cpz);await uploadOne('config',cfg);await api('upload/complete',{});const p=await api('package/analyze',{});renderPackage(p);await loadStagedJson(true)}catch(e){message(e)}finally{busy=false}}

function renderPackage(p){if(!p)return;$('#packageCard').className='package';const type=p.PackageTypeName||({0:'Unknown',1:'Room',2:'VTC Farm'}[p.PackageType]||p.PackageType);$('#packageCard').innerHTML=`<strong>${esc(type)}</strong> · ${esc(p.SystemName||'Unnamed system')}<br><small>${esc(p.DetectionReason||'')}</small><br><code>CPZ ${esc((p.CpzSha256||'').slice(0,20))}…</code><br><code>CFG ${esc((p.ConfigSha256||'').slice(0,20))}…</code>`;$('#summaryPackage').textContent=type;$('#summaryPackageDetail').textContent=p.SystemName||'Analyzed package'}

function setSource(source){const cloud=source==='cloud',offline=source==='offline',local=source==='local';$('#tabCloud').classList.toggle('active',cloud);$('#tabOffline').classList.toggle('active',offline);$('#tabLocal').classList.toggle('active',local);$('#cloudSource').classList.toggle('active',cloud);$('#offlineSource').classList.toggle('active',offline);$('#localSource').classList.toggle('active',local)}

function showDropboxModal(show){$('#dropboxAuthModal').hidden=!show;if(show){$('#dropboxAuthCode').value='';setTimeout(()=>$('#dropboxAuthCode').focus(),30)}}
async function beginDropboxAuth(syncAfter=false){dropboxSyncAfterAuth=syncAfter;try{dropboxAuthPopup=window.open('about:blank','MAVDropboxAuthorization');const a=await api('cloud/auth/start',{});$('#dropboxAuthSession').value=a.SessionId||'';$('#reopenDropboxAuth').href=a.AuthorizeUrl;$('#dropboxAuthFeedback').textContent='Authorization session expires '+new Date(a.ExpiresUtc).toLocaleTimeString()+'. Paste the single-use code Dropbox displays.';if(dropboxAuthPopup){try{dropboxAuthPopup.location.href=a.AuthorizeUrl}catch{}}else{$('#dropboxAuthFeedback').textContent='Your browser blocked the new tab. Use “Open Dropbox again”, then paste the code here.'}showDropboxModal(true);await refreshCloud()}catch(e){if(dropboxAuthPopup)try{dropboxAuthPopup.close()}catch{};message(e)}}
async function completeDropboxAuth(){const code=$('#dropboxAuthCode').value.trim(),session=$('#dropboxAuthSession').value;if(!code)return $('#dropboxAuthFeedback').textContent='Paste the authorization code Dropbox displayed.';try{$('#submitDropboxAuth').disabled=true;$('#dropboxAuthFeedback').textContent='Exchanging authorization code…';await api('cloud/auth/complete',{SessionId:session,Code:code});showDropboxModal(false);await refreshCloud();if(dropboxSyncAfterAuth){dropboxSyncAfterAuth=false;await syncCloud()}}catch(e){const msg=e.message||String(e);$('#dropboxAuthFeedback').textContent=msg+(msg.includes('NameResolution')||msg.toLowerCase().includes('resolve')?' · The processor appears to be offline. Cancel and use Offline Bundle or Local Upload; no regression features are lost.':'')}finally{$('#submitDropboxAuth').disabled=false}}
async function disconnectDropbox(){if(!confirm('Remove the stored Dropbox authorization from this Regression Controller?'))return;try{await api('cloud/auth/disconnect',{});await refreshCloud()}catch(e){message(e)}}
async function syncCloud(){try{const auth=await api('cloud/auth/status');if(!auth.Authorized)return beginDropboxAuth(true);await api('cloud/sync',{});cloudSyncWasRunning=true;await refreshCloud()}catch(e){showCloudUnavailable(e)}}
async function refreshCloud(){try{const c=await api('cloud/status');renderCloud(c)}catch(e){showCloudUnavailable(e)}}
function renderCloud(c){if(!c)return;const auth=c.Authorization||{},authChip=$('#dropboxAuthChip');
  if(auth.AppKeyConfigured===false){authChip.textContent='Privacy disabled';authChip.className='chip dirty';$('#connectDropbox').textContent='Cloud disabled';$('#connectDropbox').disabled=true;$('#disconnectDropbox').disabled=true;$('#syncCloud').disabled=true;$('#stageCloud').disabled=true;$('#cloudCpz').disabled=true;$('#cloudConfig').disabled=true;$('#cloudStatusChip').textContent='Public demo';$('#cloudStatusChip').className='chip dirty';$('#cloudMeta').textContent='Cloud CurrentBuild is intentionally disabled in the public demonstration build because the Dropbox application key was removed for privacy. Offline Bundle and Local Upload remain fully functional.';populateCloudSelect($('#cloudCpz'),[],'Disabled for privacy…');populateCloudSelect($('#cloudConfig'),[],'Disabled for privacy…');return;}
  authChip.textContent=auth.Authorized?'Dropbox authorized':auth.AuthorizationPending?'Code pending':'Not authorized';authChip.className='chip '+(auth.Authorized?'valid':auth.AuthorizationPending?'dirty':'neutral');$('#connectDropbox').textContent=auth.Authorized?'Reconnect Dropbox':'Connect Dropbox';$('#disconnectDropbox').disabled=!auth.Authorized;
  const chip=$('#cloudStatusChip');if(c.SyncRunning){chip.textContent='Syncing inventory…';chip.className='chip';cloudSyncWasRunning=true}else if(c.LastError){chip.textContent='Cloud unavailable';chip.className='chip dirty';$('#cloudMeta').textContent='Cloud is optional. Last Dropbox error: '+c.LastError+' · Offline Bundle and Local Upload remain fully available.'}else if(c.LastSyncUtc){chip.textContent='Inventory current';chip.className='chip valid';$('#cloudMeta').textContent=`Last inventory sync: ${new Date(c.LastSyncUtc).toLocaleString()} · ${c.CpzFiles?.length||0} CPZ · ${c.ConfigFiles?.length||0} JSON · selected files download when staged`}else{chip.textContent='Not synced';chip.className='chip neutral';if(auth.Authorized)$('#cloudMeta').textContent='Dropbox is authorized. Sync CurrentBuild inventory to populate the package selectors.'}
  populateCloudSelect($('#cloudCpz'),c.CpzFiles||[],auth.Authorized?'Select a CPZ…':'Authorize Dropbox first…');populateCloudSelect($('#cloudConfig'),c.ConfigFiles||[],auth.Authorized?'Select a configuration…':'Authorize Dropbox first…');
  if(cloudSyncWasRunning&&!c.SyncRunning){cloudSyncWasRunning=false;if(!c.LastError)$('#cloudMeta').textContent=`Dropbox inventory sync complete · ${c.CpzFiles?.length||0} CPZ · ${c.ConfigFiles?.length||0} JSON · files download when staged`}
}
function populateCloudSelect(el,items,placeholder){const current=el.value;el.innerHTML=`<option value="""">${placeholder}</option>`+items.map(x=>`<option value=""${esc(x.RelativePath)}"">${esc(x.RelativePath)} · ${fmtBytes(x.Bytes)}</option>`).join('');if([...el.options].some(o=>o.value===current))el.value=current}
async function stageCloud(){if(jsonDirty&&!confirm('Discard unsaved JSON edits and stage a different cloud configuration?'))return;const cpz=$('#cloudCpz').value,cfg=$('#cloudConfig').value;if(!cpz||!cfg)return alert('Select both a cloud CPZ and configuration file.');try{const p=await api('cloud/stage',{CpzRelativePath:cpz,ConfigRelativePath:cfg});renderPackage(p);await loadStagedJson(true)}catch(e){message(e)}}

function showCloudUnavailable(e){const chip=$('#cloudStatusChip');if(chip){chip.textContent='Cloud unavailable';chip.className='chip dirty'}const meta=$('#cloudMeta');if(meta)meta.textContent='Cloud is optional and could not be reached from this processor. '+(e?.message||String(e))+' · Use Offline Bundle or Local Upload without changing the regression workflow.';console.warn('Optional cloud path unavailable:',e)}

function populateOfflineSelect(el,items,placeholder){const current=el.value;el.innerHTML=`<option value="""">${placeholder}</option>`+items.map(x=>`<option value=""${esc(x.RelativePath)}"">${esc(x.RelativePath)} · ${fmtBytes(x.Bytes)}</option>`).join('');if([...el.options].some(o=>o.value===current))el.value=current}
function renderOffline(o){if(!o)return;const chip=$('#offlineStatusChip');if(o.LastError){chip.textContent='Import failed';chip.className='chip invalid';$('#offlineMeta').textContent='Last offline bundle error: '+o.LastError}else if(o.ImportedUtc){chip.textContent='Bundle ready';chip.className='chip valid';$('#offlineMeta').textContent=`${o.BundleFileName||'Offline bundle'} · imported ${new Date(o.ImportedUtc).toLocaleString()} · ${o.CpzFiles?.length||0} CPZ · ${o.ConfigFiles?.length||0} JSON · CP4N Internet not required`}else{chip.textContent='No bundle';chip.className='chip neutral';$('#offlineMeta').textContent='Upload a ZIP through this browser. The laptop or approved transfer process supplies the bundle; the processor remains offline.'}populateOfflineSelect($('#offlineCpz'),o.CpzFiles||[],'Select a CPZ…');populateOfflineSelect($('#offlineConfig'),o.ConfigFiles||[],'Select a configuration…')}
async function refreshOffline(){try{renderOffline(await api('offline/status'))}catch(e){console.warn(e)}}
async function importOffline(){const file=$('#offlineBundle').files[0];if(!file)return alert('Select an offline bundle ZIP.');if(jsonDirty&&!confirm('Discard unsaved JSON edits and import a different offline package?'))return;try{busy=true;await api('offline/upload/start',{FileName:file.name,Size:file.size});const chunkSize=256*1024;let idx=0;for(let offset=0;offset<file.size;offset+=chunkSize){const b=new Uint8Array(await file.slice(offset,offset+chunkSize).arrayBuffer());await api('offline/upload/chunk',{Index:idx++,Base64:bytesToBase64(b)});$('#phase').textContent=`Importing ${file.name}: ${Math.min(100,Math.round((offset+b.length)/file.size*100))}%`}renderOffline(await api('offline/upload/complete',{}))}catch(e){message(e);await refreshOffline()}finally{busy=false}}
async function stageOffline(){if(jsonDirty&&!confirm('Discard unsaved JSON edits and stage a different offline configuration?'))return;const cpz=$('#offlineCpz').value,cfg=$('#offlineConfig').value;if(!cpz||!cfg)return alert('Select both a CPZ and JSON from the offline bundle.');try{const p=await api('offline/stage',{CpzRelativePath:cpz,ConfigRelativePath:cfg});renderPackage(p);await loadStagedJson(true)}catch(e){message(e)}}

async function start(path){if(jsonDirty){message(new Error('Save or revert staged JSON edits before deployment/testing.'));return}if($('#mode').value==='full'&&!$('#confirmMaintenance').checked){message(new Error('Confirm an authorized maintenance window before running a Full Regression.'));return}try{await api(path,{MaintenanceAcknowledged:$('#confirmMaintenance').checked});await refresh()}catch(e){message(e)}}
async function refresh(){try{const s=await api('status');renderState(s)}catch(e){console.warn(e)}}
function renderState(s){if(!s)return;const statusNames={0:'Idle',1:'Uploading',2:'Ready',3:'Deploying',4:'WaitingForCws',5:'Testing',6:'Passed',7:'Warning',8:'Failed',9:'Aborted'};const st=statusNames[s.Status]||String(s.Status||'Idle');$('#globalState').textContent=st.toUpperCase();$('#globalState').className='state '+st;$('#bar').style.width=(s.Progress||0)+'%';$('#phase').textContent=(s.Phase||'')+(s.Detail?' · '+s.Detail:'');$('#phaseBadge').textContent=s.Phase||st;$('#summaryWorkflow').textContent=st;$('#summaryProgress').textContent=(s.Progress||0)+'% complete';if(s.Target){$('#summaryTarget').textContent=s.Target.Address||'Configured';$('#summarySlot').textContent='Program '+s.Target.ProgramSlot+(s.SelfTarget?' · self-target':'')}if(s.Package)renderPackage(s.Package);const c=s.Checks||[];$('#checks').innerHTML=c.length?c.map(x=>`<div class=""checkResult""><span class=""resultBadge ${esc(x.Status)}"">${esc(x.Status)}</span><strong>${esc(x.Name)}</strong><span class=""resultDetail"">${esc(x.Detail||'')}${x.TargetEvidence?`<br><small>Target SDK evidence: ${x.TargetEvidence.EventsObserved} event(s), observer #${x.TargetEvidence.FromLocalSequence+1}–${x.TargetEvidence.ToLocalSequence}${x.TargetEvidence.FeedConnected?'':' · feed unavailable'}</small>`:''}</span></div>`).join(''):'<div class=""empty"">No checks yet.</div>';$('#logMeta').textContent=(s.LogPath||'SDK-backed session log')+(s.SelfTarget?` · self-target · controller slot ${s.ControllerSlot}`:'')}

async function pollLog(){try{const entries=await api('logs/poll',{AfterSequence:logCursor,MaxEntries:500});if(entries?.length){const host=$('#log');for(const e of entries){logCursor=Math.max(logCursor,Number(e.Sequence||0));host.textContent+=`\n${e.TimestampUtc||''} [${e.Level}] ${e.Message}`;}host.scrollTop=host.scrollHeight}}catch{}}
async function pollConsole(){try{const entries=await api('console/poll',{AfterSequence:consoleCursor,MaxEntries:500});if(entries?.length){const host=$('#targetConsole');for(const e of entries){consoleCursor=Math.max(consoleCursor,Number(e.Sequence||0));host.textContent+=`\n${e.TimestampUtc||''} [${e.Kind||'CON'}] ${e.Text||''}`;}host.scrollTop=host.scrollHeight}}catch{}}

function renderTargetSdk(){
  const filter=$('#targetSdkFilter').value.trim().toLowerCase();
  const visible=filter?sdkLines.filter(x=>x.toLowerCase().includes(filter)):sdkLines;
  const host=$('#targetSdkLog');host.textContent=visible.slice(-1500).join('\n')||'No matching target SDK events yet.';
  if($('#targetSdkAutoScroll').checked)host.scrollTop=host.scrollHeight;
}
async function refreshTargetSdk(){
  if(sdkPollBusy)return;
  sdkPollBusy=true;
  try{
    const st=await api('target-sdk/status');
    const chip=$('#targetSdkState');
    chip.textContent=!st.Active?'Stopped':st.Connected?'SDK journal live':'Waiting for CWS';
    chip.className='chip '+(st.Connected?'valid':'neutral');
    if(st.LogPath&&st.LogPath!==sdkPath){sdkPath=st.LogPath;sdkCursor=0;sdkLines=[];renderTargetSdk()}
    $('#targetSdkMeta').textContent=st.Active?`${st.Target||'Target'} · ${st.Path||''} · epoch ${st.Epoch||0} · target cursor ${st.TargetSequence||0} · observed ${st.ObservedCount||0} · ${st.LastError?'Waiting: '+st.LastError:'Last sync '+(st.LastSyncUtc?new Date(st.LastSyncUtc).toLocaleTimeString():'pending')}`:'SDK observation stopped. A regression automatically starts it.';
    if(st.Active){
      const rows=await api('target-sdk/poll',{AfterSequence:sdkCursor,MaxEntries:650});
      if(rows?.length){for(const e of rows){sdkCursor=Math.max(sdkCursor,Number(e.LocalSequence||0));sdkLines.push(`${e.ObservedUtc||''} [E${e.Epoch}/#${e.TargetSequence}] [${e.Level||'LOG'}]${e.TestStep?' ['+e.TestStep+']':''} ${e.Message||''}`)}if(sdkLines.length>5000)sdkLines=sdkLines.slice(-4500);renderTargetSdk()}
    }
  }catch(e){$('#targetSdkState').textContent='Observer unavailable';$('#targetSdkState').className='chip invalid';$('#targetSdkMeta').textContent=e.message||String(e)}
  finally{sdkPollBusy=false}
}
async function connectTargetSdk(){try{await api('target-sdk/connect',{});await refreshTargetSdk()}catch(e){message(e)}}
async function disconnectTargetSdk(){try{await api('target-sdk/disconnect',{});await refreshTargetSdk()}catch(e){message(e)}}

async function refreshConsoleStatus(){try{const s=await api('console/status');const el=$('#consoleState');el.textContent=s.Connected?'Connected':'Disconnected';el.className='chip '+(s.Connected?'':'neutral');if(s.Connected&&s.Target)el.title='Connected to '+s.Target}catch{}}
async function connectConsole(){try{await api('console/connect',{});await refreshConsoleStatus();await pollConsole()}catch(e){message(e)}}
async function disconnectConsole(){try{await api('console/disconnect',{});await refreshConsoleStatus()}catch(e){message(e)}}

async function sendConsole(command){const cmd=(command??$('#consoleCommand').value).trim();if(!cmd)return;try{await api('console/send',{Command:cmd});$('#consoleCommand').value='';await pollConsole()}catch(e){message(e)}}
async function sendPreset(name){if(name==='kill'&&!confirm(`Stop Program ${$('#slot').value} on the configured target?`))return;if(name==='load'&&!confirm(`Issue PROGLOAD for Program ${$('#slot').value} on the configured target?`))return;try{await api('console/preset',{Preset:name});await pollConsole()}catch(e){message(e)}}
async function clearConsole(){try{await api('console/clear',{});consoleCursor=0;$('#targetConsole').textContent='Target console cleared.'}catch(e){message(e)}}

async function downloadText(route,fileName){try{const r=await fetch('/cws/regression/'+route,{cache:'no-store'}),t=await r.text(),blob=new Blob([t],{type:'text/plain'}),a=document.createElement('a');a.href=URL.createObjectURL(blob);a.download=fileName;a.click();setTimeout(()=>URL.revokeObjectURL(a.href),3000)}catch(e){message(e)}}
const stamp=()=>new Date().toISOString().replace(/[:.]/g,'-');

$('#exportReport').addEventListener('click',()=>downloadText('evidence/report','MAV-Regression-Report-'+stamp()+'.json'));$('#configure').addEventListener('click',configure);$('#scan').addEventListener('click',scan);$('#upload').addEventListener('click',upload);$('#tabCloud').addEventListener('click',()=>setSource('cloud'));$('#tabOffline').addEventListener('click',()=>setSource('offline'));$('#tabLocal').addEventListener('click',()=>setSource('local'));$('#importOffline').addEventListener('click',importOffline);$('#refreshOffline').addEventListener('click',refreshOffline);$('#stageOffline').addEventListener('click',stageOffline);$('#connectDropbox').addEventListener('click',()=>beginDropboxAuth(false));$('#disconnectDropbox').addEventListener('click',disconnectDropbox);$('#syncCloud').addEventListener('click',syncCloud);$('#refreshCloud').addEventListener('click',refreshCloud);$('#stageCloud').addEventListener('click',stageCloud);$('#submitDropboxAuth').addEventListener('click',completeDropboxAuth);$('#cancelDropboxAuth').addEventListener('click',()=>showDropboxModal(false));$('#closeDropboxAuth').addEventListener('click',()=>showDropboxModal(false));$('#dropboxAuthCode').addEventListener('keydown',e=>{if(e.key==='Enter'){e.preventDefault();completeDropboxAuth()}});
$('#deployTest').addEventListener('click',()=>{if(!$('#confirmDestructive').checked)return alert('Confirm the deployment actions before starting Deploy + Full Workflow.');start('workflow/deploy-test')});$('#testOnly').addEventListener('click',()=>start('workflow/test-only'));$('#abort').addEventListener('click',()=>start('workflow/abort'));$('#refresh').addEventListener('click',refresh);
$('#consoleForm').addEventListener('submit',e=>{e.preventDefault();sendConsole()});$('#connectConsole').addEventListener('click',connectConsole);$('#disconnectConsole').addEventListener('click',disconnectConsole);document.querySelectorAll('[data-preset]').forEach(b=>b.addEventListener('click',()=>sendPreset(b.dataset.preset)));$('#clearConsole').addEventListener('click',clearConsole);$('#exportConsole').addEventListener('click',()=>downloadText('console/export','MAV-Target-Console-'+stamp()+'.log'));$('#exportLog').addEventListener('click',()=>downloadText('logs/export','MAV-Regression-'+stamp()+'.log'));
$('#connectTargetSdk').addEventListener('click',connectTargetSdk);
$('#disconnectTargetSdk').addEventListener('click',disconnectTargetSdk);
$('#exportTargetSdk').addEventListener('click',()=>downloadText('target-sdk/export','MAV-Target-SDK-'+stamp()+'.jsonl'));
$('#exportCombined').addEventListener('click',()=>downloadText('evidence/combined','MAV-Combined-Regression-'+stamp()+'.log'));
$('#targetSdkFilter').addEventListener('input',renderTargetSdk);
setInterval(()=>{if(!busy){refresh();pollLog();pollConsole();refreshCloud();refreshOffline();refreshConsoleStatus();refreshTargetSdk()}},1300);refresh();refreshCloud();refreshOffline();refreshConsoleStatus();pollLog();pollConsole();refreshTargetSdk();


// Configuration editing lives ONLY on the Regression Controller staged copy.
let jsonOriginal='',jsonHash='',jsonDirty=false,jsonLoadedHash='',jsonPreviewTimer=0,jsonLoadRunning=false;
const editor=$('#jsonEditor'), editorStatus=$('#jsonStatus'), feedback=$('#jsonFeedback');
function editorNote(text,kind=''){feedback.textContent=text;feedback.className='editorFeedback '+kind;}
function markJson(dirty,valid){jsonDirty=dirty;editorStatus.textContent=!jsonHash?'No staged JSON':dirty?(valid?'Unsaved edits':'Invalid / unsaved'):'Staged / saved';editorStatus.className='chip '+(!jsonHash?'neutral':dirty?(valid?'dirty':'invalid'):'valid');$('#jsonSave').disabled=!dirty||!valid;$('#deployTest').disabled=dirty;}
function jsonSyntax(source){let result='',last=0;const token=/""(?:\\.|[^""\\])*""\s*(?=:)|""(?:\\.|[^""\\])*""|\b(?:true|false|null|-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)\b/g;let m;while((m=token.exec(source))){result+=esc(source.slice(last,m.index));const raw=m[0],kind=raw.startsWith('""')?(source.slice(token.lastIndex).trimStart().startsWith(':')?'key':'str'):raw==='true'||raw==='false'?'bool':raw==='null'?'null':'num';result+='<span class=""'+kind+'"">'+esc(raw)+'</span>';last=token.lastIndex}return result+esc(source.slice(last))}
function updateJsonPreview(){const text=editor.value;$('#jsonLines').textContent=Array.from({length:Math.min(10000,text.split('\n').length)},(_,i)=>i+1).join('\n');$('#editorSize').textContent=fmtBytes(new TextEncoder().encode(text).length);$('#jsonPreview').innerHTML=jsonSyntax(text||'No JSON loaded.');let ok=false;try{if(text.trim()&&!(JSON.parse(text) instanceof Array)&&typeof JSON.parse(text)==='object'){ok=true;editorNote(text!==jsonOriginal?'Valid local JSON. Save to staging before deploying.':'Valid staged JSON.','ok')}else throw Error('Root must be a JSON object.')}catch(e){if(jsonHash)editorNote(e.message,'error');else editorNote('Load a staged configuration first.');}markJson(text!==jsonOriginal&&!!jsonHash,ok);}
async function loadStagedJson(force=false){if(jsonLoadRunning)return;if(jsonDirty&&!force){if(!confirm('Discard unsaved edits and reload the staged JSON?'))return;}jsonLoadRunning=true;try{const d=await api('package/config/get');jsonOriginal=d.JsonText||'';jsonHash=d.ConfigSha256||'';jsonLoadedHash=jsonHash;editor.value=jsonOriginal;$('#editorFile').textContent=d.FileName||'SystemDefinition.json';updateJsonPreview();editorNote('Loaded staged JSON · SHA-256 '+jsonHash.slice(0,18)+'…','ok');$('#jsonStatus').textContent='Staged / saved';$('#jsonStatus').className='chip valid'}catch(e){jsonHash='';$('#jsonStatus').textContent='JSON handoff failed';$('#jsonStatus').className='chip invalid';editorNote('Package staged, but configuration workspace could not open the staged JSON: '+(e.message||e),'error');message(e)}finally{jsonLoadRunning=false}}
async function validateJson(){try{const text=editor.value;const data=JSON.parse(text);if(!data||Array.isArray(data)||typeof data!=='object')throw Error('Root must be a JSON object.');const r=await api('package/config/validate',{JsonText:text});updateJsonPreview();editorNote('Validated on controller · '+(r.Name||'Unnamed system')+' · '+r.Properties+' root fields · '+fmtBytes(r.Bytes),'ok')}catch(e){editorNote(e.message,'error');markJson(editor.value!==jsonOriginal,false)}}
async function saveJson(){try{if(!jsonHash)throw Error('Stage JSON before editing.');const data=JSON.parse(editor.value);if(!data||Array.isArray(data)||typeof data!=='object')throw Error('Root must be a JSON object.');const r=await api('package/config/save',{JsonText:editor.value,ExpectedSha256:jsonHash});jsonHash=r.ConfigSha256;jsonOriginal=r.JsonText;editor.value=jsonOriginal;jsonLoadedHash=jsonHash;updateJsonPreview();editorNote('Saved to controller staging, not to target. Previous copy preserved. SHA-256 '+jsonHash.slice(0,18)+'…','ok');await refresh();}catch(e){editorNote(e.message,'error')}}
function exportJson(){if(!editor.value)return;const blob=new Blob([editor.value],{type:'application/json'}),url=URL.createObjectURL(blob),a=document.createElement('a');a.href=url;a.download='MAV-Staged-SystemDefinition-'+stamp()+'.json';a.click();setTimeout(()=>URL.revokeObjectURL(url),3000)}
editor.addEventListener('input',()=>{clearTimeout(jsonPreviewTimer);jsonPreviewTimer=setTimeout(updateJsonPreview,120)});
editor.addEventListener('scroll',()=>{$('#jsonLines').scrollTop=editor.scrollTop});
editor.addEventListener('keydown',e=>{if(e.key==='Tab'){e.preventDefault();const i=editor.selectionStart;editor.setRangeText('  ',i,editor.selectionEnd,'end');editor.dispatchEvent(new Event('input'))}});
$('#jsonLoad').addEventListener('click',()=>loadStagedJson());$('#jsonFormat').addEventListener('click',()=>{try{const v=JSON.parse(editor.value);if(!v||Array.isArray(v)||typeof v!=='object')throw Error('Root must be a JSON object.');editor.value=JSON.stringify(v,null,2)+'\n';updateJsonPreview()}catch(e){editorNote(e.message,'error')}});
$('#jsonValidate').addEventListener('click',validateJson);$('#jsonRevert').addEventListener('click',()=>{editor.value=jsonOriginal;updateJsonPreview();editorNote('Unsaved edits reverted.','ok')});$('#jsonSave').addEventListener('click',saveJson);$('#jsonDownload').addEventListener('click',exportJson);
$('#siteTheme').addEventListener('change',e=>{document.body.dataset.siteTheme=e.target.value});$('#editorAppearance').addEventListener('change',e=>{document.body.dataset.editorAppearance=e.target.value});
// A status poll must never erase an unsaved editing session; load once when a new config SHA is staged.
const baseRenderPackage=renderPackage;
renderPackage=function(p){baseRenderPackage(p);if(p&&p.ConfigSha256&&p.ConfigSha256!==jsonLoadedHash&&!jsonDirty&&!jsonLoadRunning){loadStagedJson(true)}};
updateJsonPreview();
";
    }
}
