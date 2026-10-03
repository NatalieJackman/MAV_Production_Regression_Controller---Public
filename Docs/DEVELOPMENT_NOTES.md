# MAV Production Regression Controller — Preview 010 Public Demo

> **PUBLIC DEMONSTRATION / PRIVACY-SANITIZED BUILD**  
> This copy intentionally removes private room/person identifiers and the production Dropbox application key. Cloud CurrentBuild is therefore disabled in this public copy; Offline Bundle and Local Upload remain available. See `PUBLIC_DEMO_PRIVACY.md`.


**Validation status:** The private Preview 010 build was successfully exercised on a CP4N after the lifecycle/file-I/O fixes, completing the demonstrated full regression run with **30 PASS / 0 FAIL / 1 WARN / 5 SKIP**. This public copy changes privacy-sensitive identifiers and intentionally disables Dropbox Cloud CurrentBuild by removing the application key; those publication-only changes have not been separately hardware-tested. Offline Bundle and Local Upload preserve the normal staging/deployment/regression path.


## Preview 010 - defensive program lifecycle and unified CP4N file I/O

Preview 010 addresses the production deployment timing hazard observed when a 4-Series processor returned **Waiting for program to stop** after `KILLPROG`. `KILLPROG` and `PROGLOAD` are asynchronous lifecycle operations; the controller no longer treats console wording or a fixed sleep as authoritative. Preflight first requires a valid `PROGREG` response, and every stop/load transition must then be observed in **two consecutive authoritative `PROGREG` polls** before deployment advances. Blank or partial console output is never interpreted as "slot absent."

The workflow is transactional around the destructive window. Rollback material and the exact staged CPZ/JSON are reopened through CrestronIO immediately before commit; the candidate hashes must still match analysis. The deployment is marked modified **before** a potentially destructive command/write is issued so a transport exception cannot hide the fact that the processor may already be changing. Rollback will not overwrite program storage until an empty slot has itself been confirmed.

Package/config mutations and arbitrary target-console writes are locked while a workflow is active; read-only console presets remain available. A workflow-busy latch is set under the workflow lock before the running CrestronThread is constructed, removing a second start/worker-assignment timing window.

Controller-local deployment, cache, evidence, log, JSON-edit, and HTML5 materialization paths are being standardized on canonical `/user/MAVRegression/...` paths and CrestronIO streams. Do not substitute `System.IO.FileStream` for these CP4N-local paths. The SFTP client requires CrestronIO-compatible streams, and mixing the two filesystem views previously caused staged files to appear missing.



## Preview 009 - evidence-driven regression expansion

Preview 009 turns the field and bench failures discovered during MAV development into permanent automated guardrails. The controller now distinguishes **PASS**, **FAIL**, **WARN**, and **SKIP**. SKIP means the behavior is intentionally not exercised because the current package/environment does not expose a safe fixture; it never masquerades as a PASS and it does not make the overall run yellow.

New automated coverage includes:

- CP4N staging-path reopen/write-read preflight **before** any existing target program can be stopped.
- Local/offline package continuity after staging.
- VTC Farm command-handler registration, EISC registration inventory, codec hardware availability, duplicate-reserve idempotence, idle-release idempotence, deferred-media route safety, concurrent codec allocation, cleanup, and immediate codec reuse.
- MAV Room DSP cold-start subscription snapshots, duplicate/reconnect snapshot replay, and explicit zero/default-value coverage when a live zero/false control is available.
- Configured-vs-live NVX evidence using the target's retained `NVX DEVICE DISCOVERY VALIDATION` record rather than treating configured devices as automatically online.
- Configured VTC Farm dependency readiness when a Room declares an enabled Farm address.
- Explicit SKIP records for tests that still need a safe fixture: late legacy media injection, duplicate dialing, classified route mutation/downgrade, active NVX route convergence, Room Combine, runtime IPID recreation, and embedded-assistant targeting.

This keeps the production suite evidence-driven: observed bugs become executable tests when the public contract can exercise them; unsafe or unavailable boundaries are documented as SKIP/WARN rather than being guessed.

## Preview 008 CP4N deployment-path fix

Preview 008 was the point at which the deploy-first SFTP bridge failure was isolated. An attempted `System.IO.FileStream` workaround from that preview is **superseded and must not be reused**. The corrected contract used by Preview 009/010 is CrestronIO end-to-end for CP4N-local files that cross into Crestron APIs/SFTP, with canonical `/user/...` paths.

The transport also validates that the local file exists and logs its byte length before upload.


Preview 008 originally isolated the staged-configuration handoff seen on CP4N when Local Upload or Offline Bundle successfully classified a package but the Configuration Workspace still reported **No staged JSON**. Any System.IO-based CP4N-local file workaround from that preview is historical and superseded. Preview 010 uses the corrected CrestronIO `/user/...` contract end-to-end for controller-local artifacts that cross into Crestron APIs/SFTP.

Expected smoke test:
1. Local Upload -> select CPZ + JSON -> Upload + analyze.
2. Package card should identify Room/VTC Farm.
3. Configuration workspace should immediately populate the JSON editor without pressing Load staged JSON.
4. Offline Bundle -> import ZIP -> select CPZ + JSON -> Stage selected package should behave identically.

# MAV Production Regression Controller — Preview 006

**Source-ready only.** Targets **.NET Framework 4.8** and Crestron packages **2.22.15**. Compile/test in your configured Simpl# Pro / CP4N development environment; the build environment used to package this source cannot compile or hardware-test a CPZ.

## New: independent target MAV SDK log observer

The controller now consumes the same documented MAV diagnostic CWS contract already used by the Farm Monitor and Room Monitor:

- `GET /cws/vtcfarm/system/diagnostics/summary` or `/cws/api/system/diagnostics/summary`: `Value.LogSequence`
- `POST .../system/diagnostics/logs` with `{ "AfterSequence": 123, "MaxEntries": 500 }`: `Value[]` containing `Sequence`, `Timestamp`, `Level`, `Message`, `IsMultiline`

It runs as a separate CP4N thread and uses an independent client, without static IPIDs or any direct attachment to the target program internals. It **starts automatically before Deploy & Test or Test Loaded Program**, so it observes recovery/startup once the target's CWS becomes reachable. Alternatively, configure the target plus Farm/Room test profile and click **Observe loaded program** in the Target SDK Log panel to watch a running program without deploying.

### Target SDK Log panel

- CWS connection/waiting status, journal epoch, current cursor, total observed entries and last error
- Live cursor-based polling (850 ms controller polling; page refreshes roughly 1.3 s), with filter, follow-latest, reconnect/stop and export
- Separate, unchanged **Target SSH Console** and **Controller SDK Log** panels
- Export original SDK-event JSONL and a combined chronological text transcript
- Persists **every acquired event** to `Target-SDK-Logs.jsonl` in the regression evidence directory; UI memory is bounded. Large raw logs (>16 MB) must be retrieved from the evidence directory instead of browser export

### Correlation / regression semantics

- Each independently executed CWS test step is bracketed by `BeginStep` and `EndStep`, which record a target-observer *local sequence window*, start/end target sequence, number of observed events, feed connection state and warning/error counts in `RegressionReport.json` under each check's `TargetEvidence`.
- New `WARN` checks flag Critical/Error-level target SDK entries arriving during a step. This does **not** blindly FAIL or PASS a behavior based on an English log string: correctness still requires independent CWS response/state assertions. Log silence/temporary feed loss never creates a false PASS.
- Target journal sequence rollback (e.g. process restart) creates a new journal epoch and resets only the source cursor. Target timestamps are preserved separately from observer timestamps.
- The evidence directory receives `RegressionReport.json`, `Target-SDK-Logs.jsonl`, `Combined-Transcript.log`, the existing controller SDK log and existing diagnostic summary/snapshot exports where accessible.

## Operator flow

1. Load the controller in a protected program slot; configure the target processor and selected target slot. For manual observation, select **Existing VTC Farm** or **Existing MAV Room** under Test Profile.
2. To attach to an already-running program, click **Observe loaded program**, then watch the **Target application · live SDK log** panel.
3. For a test run, stage a CPZ + SystemDefinition from CurrentBuild or local upload, review/edit JSON in the MAV-themed editor, and run Health Check or explicitly authorized Full Regression.
4. The observer follows the target across deployment and startup; open Regression Checks for per-step target evidence ranges. Export target log, combined transcript, controller SDK log and JSON report.

## Restrictions / known boundaries

- Requires the target build to expose the MAV SDK diagnostic CWS endpoints. No SSH console parsing is substituted if it doesn't. Observe the explicit Waiting/unavailable status and WARN in the report.
- Polling isn't an atomic event subscriber: rapidly overwritten or journal-evicted events can be missed; `ObservedUtc` represents **when the controller received** each event, while `TargetTimestamp` preserves the source timestamp. Clock synchronization is not assumed.
- The external regression still exercises CWS contracts and does not certify the legacy MAVE↔EISC wire protocol or physical two-way A/V. Those need the separate field fixture/human acceptance test.
- Logs may contain system information. Keep the controller, browser, evidence location and any exported files in an appropriately approved environment; this change never automatically uploads logs to cloud storage.
- Self-target slot protection, zero static IPIDs, target-slot selection, verified program backup, and the staged-JSON safety workflow remain unchanged.


## Preview 005 - Dropbox OAuth / PKCE

- Replaced anonymous Dropbox shared-folder ZIP sync with the same Dropbox App Key and PKCE authorization flow used by MAV Deployment Utility Preview 145.
- Clicking Connect Dropbox opens Dropbox in a browser tab, while the Regression Controller displays an authorization-code dialog. After the operator clicks Allow, paste Dropbox's single-use code into the dialog.
- The controller exchanges the code for a short-lived access token plus offline refresh token. Access tokens are renewed automatically.
- Sync CurrentBuild now enumerates the Dropbox app-root inventory through the Dropbox API. It does not download the entire repository.
- Selected CPZ and JSON files download only when staged and are verified against Dropbox size and content_hash before use.
- Tokens are never returned through CWS APIs and are never written to the regression, target-console, or SDK logs.
- Unlike the Windows PowerShell utility, a CP4N does not provide Windows DPAPI. The refresh token is stored locally under `/user/MAVRegression/DropboxAuth/refresh-token.dat`. Treat access to the controller filesystem as privileged.


## Preview 006 - resilient online/offline package transport

Cloud access is now optional. If the processor cannot resolve or reach Dropbox, the controller remains usable and reports the cloud path as unavailable rather than treating it as a controller failure.

Package sources:

1. **Cloud CurrentBuild** - Dropbox API when processor Internet access is permitted.
2. **Offline Bundle** - upload a ZIP through the browser. The operator laptop may download the bundle from an approved cloud source when it has both Internet and processor-LAN access, or receive the same ZIP through any approved offline transfer process. The CP4N itself requires no Internet access.
3. **Local Upload** - select an individual CPZ and JSON directly.

The Offline Bundle importer expands each import into a fresh `/user/MAVRegression/OfflineBundles/Active-<timestamp>` root, rejects unsafe ZIP paths, limits entry count and expanded size, inventories all CPZ/JSON files, and lets the operator stage the desired pair before JSON editing/deployment/regression.

## Preview 009 corrected — CP4N local I/O contract

The corrected Preview 009 source restores the required CrestronIO filesystem contract for SFTP-bound processor-local files. `ProcessorTransport` uses `Crestron.SimplSharp.CrestronIO.FileStream` and related CrestronIO types, never `System.IO.FileStream`, and controller-local roots are canonical `/user/MAVRegression/...` paths. This is a platform constraint, not a stylistic preference.
