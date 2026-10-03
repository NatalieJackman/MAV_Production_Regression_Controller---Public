using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MAV.ProductionRegressionController
{
    public sealed class RegressionRunner
    {
        private readonly ControllerLogger _log;
        private readonly CwsClient _cws;
        private readonly TargetSdkLogObserver _observer;
        private int _checkSequence;

        private sealed class DspSubscriptionTarget
        {
            public string Name;
            public string Kind;
        }

        public RegressionRunner(ControllerLogger log, CwsClient cws, TargetSdkLogObserver observer)
        {
            _log = log;
            _cws = cws;
            _observer = observer;
        }

        /// <summary>
        /// Controller-side checks run before a deploy can stop or replace the target program.
        /// These tests specifically guard the CP4N staging-path failure seen during Preview 008 testing.
        /// </summary>
        public void BeginWorkflow(RunState state)
        {
            _checkSequence = 0;
            state.Phase = "Controller preflight";
            state.Progress = 2;
            CheckControllerStaging(state);
            CheckOfflinePackageIndependence(state);
        }

        public void WaitForCws(RunState state)
        {
            state.Phase = "Waiting for CWS";
            state.Progress = 70;
            string root = CwsRoot(state.Package.PackageType);
            Exception last = null;
            for (int i = 0; i < 30; i++)
            {
                if (state.AbortRequested) throw new OperationCanceledException("Abort requested.");
                try
                {
                    _cws.Get(state.Target, root + "/system/diagnostics/summary");
                    _log.Pass("CWS diagnostics endpoint online: " + root);
                    return;
                }
                catch (Exception ex) { last = ex; }
                Crestron.SimplSharpPro.CrestronThread.Thread.Sleep(2000);
            }
            throw new InvalidOperationException("CWS did not become ready within 60 seconds. Last error: " + (last == null ? "unknown" : last.Message));
        }

        public void Run(RunState state)
        {
            state.Phase = "Regression";
            state.Progress = 76;
            CheckPackageIntegrity(state);
            if (state.Package.PackageType == MavPackageType.VtcFarm) RunVtcFarm(state);
            else if (state.Package.PackageType == MavPackageType.Room) RunRoom(state);
            else throw new InvalidOperationException("Unknown package type cannot be regression tested.");

            state.Progress = 96;
            CaptureTargetDiagnostics(state);
            if (_observer != null)
            {
                _observer.CaptureNow();
                AddCheck(state, "Target SDK log feed", _observer.Connected ? "PASS" : "WARN",
                    _observer.Connected
                        ? "External target SDK diagnostic journal connected; events captured=" + _observer.ObservedCount + "."
                        : "Target SDK journal unavailable. API/state assertions remain independent; live target-log evidence is incomplete.");
            }
            WriteSummary(state);
        }

        private void CheckControllerStaging(RunState state)
        {
            PackageInfo package = state.Package;
            if (package == null || string.IsNullOrWhiteSpace(package.CpzPath) || string.IsNullOrWhiteSpace(package.ConfigPath))
            {
                AddCheck(state, "Controller staging path self-test", "SKIP", "Existing-target test mode has no local deployment artifacts to reopen.");
                return;
            }

            try
            {
                LocalFileIO.Probe(package.CpzPath);
                LocalFileIO.Probe(package.ConfigPath);
                string normalized = LocalPaths.Normalize(package.CpzPath);
                int slash = normalized.LastIndexOf('/');
                if (slash <= 0) throw new InvalidOperationException("Invalid canonical staging path.");
                string probe = normalized.Substring(0, slash) + "/.mav-regression-path-probe.tmp";
                string marker = "MAV-REGRESSION-PATH-PROBE-" + state.SessionId;
                LocalFileIO.WriteAllBytes(probe, Encoding.UTF8.GetBytes(marker));
                string reread = LocalFileIO.ReadAllText(probe, 1024);
                try { Crestron.SimplSharp.CrestronIO.File.Delete(probe); } catch { }
                if (!string.Equals(marker, reread, StringComparison.Ordinal)) throw new InvalidOperationException("CrestronIO staging write/read probe did not round-trip.");

                AddCheck(state, "Controller staging path self-test", "PASS", "CPZ/JSON reopened through CrestronIO, the exact filesystem interface used by SFTP and a staging-directory write/read probe round-tripped successfully.");
            }
            catch (Exception ex)
            {
                AddCheck(state, "Controller staging path self-test", "FAIL", ex.Message + " Deployment is blocked before the existing target program can be stopped.");
                throw;
            }
        }

        private void CheckOfflinePackageIndependence(RunState state)
        {
            PackageInfo package = state.Package;
            if (package == null || string.IsNullOrWhiteSpace(package.CpzPath) || string.IsNullOrWhiteSpace(package.ConfigPath))
            {
                AddCheck(state, "Offline package continuity", "SKIP", "Existing-target test mode does not stage a package.");
                return;
            }

            bool local = LocalFileIO.Exists(package.CpzPath) && LocalFileIO.Exists(package.ConfigPath);
            AddCheck(state, "Offline package continuity", local ? "PASS" : "FAIL",
                local
                    ? "Deployment artifacts are fully local before target modification; Dropbox/Internet is not required after staging."
                    : "One or more deployment artifacts are not locally available.");

            AddCheck(state, "Cloud cache reuse", "SKIP",
                "Cache reuse is a package-source test rather than a target regression. The cloud service already verifies Dropbox size/content_hash, but the staged PackageInfo does not retain enough source metadata to prove a cache-hit versus a download in this target run.");
        }

        private void CheckPackageIntegrity(RunState state)
        {
            PackageInfo package = state.Package;
            if (string.IsNullOrWhiteSpace(package.CpzPath) || string.IsNullOrWhiteSpace(package.ConfigPath))
            {
                AddCheck(state, "External black-box test mode", "PASS", "Existing target program; no package or configuration deployed by this test.");
                return;
            }
            string actualCpz = FileHash.Sha256(package.CpzPath);
            string actualJson = FileHash.Sha256(package.ConfigPath);
            bool cpzPass = string.Equals(actualCpz, package.CpzSha256, StringComparison.OrdinalIgnoreCase);
            bool jsonPass = string.Equals(actualJson, package.ConfigSha256, StringComparison.OrdinalIgnoreCase);
            AddCheck(state, "CPZ immutable identity", cpzPass ? "PASS" : "FAIL", "Staged CPZ digest " + (cpzPass ? "matches" : "CHANGED since analysis") + ".");
            AddCheck(state, "Configuration immutable identity", jsonPass ? "PASS" : "FAIL", "Staged JSON digest " + (jsonPass ? "matches" : "CHANGED since analysis") + ".");
            if (!cpzPass || !jsonPass) throw new InvalidOperationException("Staged package changed since analysis. Stop and restage/reanalyze before continuing.");
            JObject.Parse(LocalFileIO.ReadAllText(package.ConfigPath, 8L*1024L*1024L));
            AddCheck(state, "JSON structural unit check", "PASS", "Staged JSON parses as an object using the independent controller parser.");
        }

        private void RunVtcFarm(RunState state)
        {
            string root = "/cws/vtcfarm";
            JObject definition = CheckCall(state, "SystemDefinition readable", delegate { return _cws.Get(state.Target, root + "/system/definition/get"); });
            JObject status = CheckCall(state, "VTC Farm status", delegate { return _cws.Get(state.Target, root + "/system/vtcfarm/status"); });
            AddCheck(state, "VTC Farm command-handler registration", "PASS", "VtcFarmCommand Status dispatched successfully through the public CWS contract; the missing-handler regression is not present.");
            CheckCall(state, "SDK diagnostics summary", delegate { return _cws.Get(state.Target, root + "/system/diagnostics/summary"); });

            int configuredCodecs = ReadCodecCount(status);
            AddCheck(state, "Configured codec inventory", configuredCodecs > 0 ? "PASS" : "FAIL", configuredCodecs + " codec(s) externally reported via CWS.");
            CheckFarmCodecHardware(state, status);
            CheckFarmEiscRegistration(state, status);

            if (state.Target.RunMode == RunMode.HealthCheck)
            {
                AddCheck(state, "Health Check safety", "PASS", "Read-only CWS endpoints checked; no reservations or native target self-tests were invoked.");
                AddCheck(state, "Deferred media routing safety", "SKIP", "Requires an authorized synthetic reservation and therefore runs only in Full Regression.");
                AddCheck(state, "Late media arrival transition", "SKIP", "Requires a controllable legacy media-join/EISC injection fixture that the public CWS contract does not expose.");
                return;
            }

            int preExistingAssignments = CountAssignedRooms(status);
            if (preExistingAssignments > 0)
            {
                AddCheck(state, "Production resource safety", "FAIL", preExistingAssignments + " real room(s) are already assigned to the Farm. Full Regression will not reserve production codecs while active assignments exist.");
                return;
            }
            AddCheck(state, "Production resource safety", "PASS", "No active room assignments were present before synthetic reservation testing.");

            List<string> rooms = ReadRoomNames(status);
            int codecCount = ReadCodecCount(status);
            if (codecCount <= 0)
            {
                AddCheck(state, "Codec discovery", "FAIL", "No codecs were reported by VTC Farm status.");
                return;
            }
            if (rooms.Count < codecCount)
            {
                AddCheck(state, "Synthetic room coverage", "WARN", "Only " + rooms.Count + " configured rooms are available for " + codecCount + " codecs; concurrent allocation cannot exercise every codec.");
                codecCount = rooms.Count;
            }
            else AddCheck(state, "Synthetic room coverage", "PASS", rooms.Count + " configured rooms available; exercising " + codecCount + " codec allocations.");

            List<string> reservedRooms = new List<string>();
            try
            {
                for (int i = 0; i < codecCount; i++)
                {
                    if (state.AbortRequested) throw new OperationCanceledException("Abort requested.");
                    string room = rooms[i];
                    JObject response = CheckCall(state, "Reserve " + room, delegate { return _cws.Post(state.Target, root + "/" + Uri.EscapeDataString(room) + "/vtcfarm/reserve", new { }); });
                    reservedRooms.Add(room);
                    string assigned = ReadAssignedCodec(response);
                    AddCheck(state, "Codec assignment " + room, string.IsNullOrEmpty(assigned) ? "FAIL" : "PASS", string.IsNullOrEmpty(assigned) ? "No codec assignment was returned." : "Assigned " + assigned + ".");

                    if (i == 0)
                    {
                        JObject afterFirstReserve = _cws.Get(state.Target, root + "/system/vtcfarm/status");
                        TestDuplicateReserveIdempotence(state, root, room, assigned, afterFirstReserve);
                        TestDeferredMediaRoutingSafety(state, room, afterFirstReserve);
                    }
                }

                JObject afterReserve = _cws.Get(state.Target, root + "/system/vtcfarm/status");
                int assignedCount = CountAssignedRooms(afterReserve);
                AddCheck(state, "Concurrent reservation count", assignedCount >= reservedRooms.Count ? "PASS" : "FAIL", assignedCount + " room(s) reported assigned for " + reservedRooms.Count + " reservation request(s).");
            }
            finally
            {
                foreach (string room in reservedRooms)
                {
                    try { CheckCall(state, "Release " + room, delegate { return _cws.Post(state.Target, root + "/" + Uri.EscapeDataString(room) + "/vtcfarm/release", new { }); }); }
                    catch { }
                }
            }

            JObject idle = _cws.Get(state.Target, root + "/system/vtcfarm/status");
            AddCheck(state, "Post-release idle", CountAssignedRooms(idle) == 0 ? "PASS" : "FAIL", CountAssignedRooms(idle) + " room(s) remain assigned after cleanup.");

            if (rooms.Count > 0)
            {
                string room = rooms[0];
                TestIdleReleaseIdempotence(state, root, room);

                CheckCall(state, "Immediate re-reserve " + room, delegate { return _cws.Post(state.Target, root + "/" + Uri.EscapeDataString(room) + "/vtcfarm/reserve", new { }); });
                CheckCall(state, "Immediate re-release " + room, delegate { return _cws.Post(state.Target, root + "/" + Uri.EscapeDataString(room) + "/vtcfarm/release", new { }); });
                JObject finalState = _cws.Get(state.Target, root + "/system/vtcfarm/status");
                AddCheck(state, "Codec reuse cleanup", CountAssignedRooms(finalState) == 0 ? "PASS" : "FAIL", "Immediate reserve/release cycle completed; final assigned count=" + CountAssignedRooms(finalState) + ".");
            }

            AddCheck(state, "Late media arrival transition", "SKIP", "The controller can prove that missing media is not routed, but the public CWS contract has no safe hook that injects legacy Camera/Presentation/Audio joins after assignment. A real MAVE/EISC fixture is still required for Waiting -> Routing -> Routed proof.");
            AddCheck(state, "Duplicate dial idempotence", "SKIP", "No harmless regression dial target is declared in SystemDefinition. The controller will not generate an external call merely to exercise duplicate-dial handling.");
            AddCheck(state, "Legacy EISC transport", "WARN", "Controller contains no static IPIDs by design. CWS regression validates Farm logic and EISC registration telemetry, but an actual legacy MAVE room remains required to certify the wire-level transport boundary.");
        }

        private void RunRoom(RunState state)
        {
            string root = "/cws/api";
            JObject definition = CheckCall(state, "SystemDefinition readable", delegate { return _cws.Get(state.Target, root + "/system/definition/get"); });
            CheckCall(state, "Room state readable", delegate { return _cws.Get(state.Target, root + "/system/room/get"); });
            CheckCall(state, "SDK diagnostics summary", delegate { return _cws.Get(state.Target, root + "/system/diagnostics/summary"); });
            JObject runtimeState = CheckCall(state, "Runtime state snapshot", delegate { return _cws.Post(state.Target, root + "/system/diagnostics/state", new { }); });

            TestSubscriptionSnapshotContract(state, definition, runtimeState);
            TestNvxInventoryEvidence(state, definition);
            TestVtcFarmDependency(state, definition);

            if (state.Target.RunMode == RunMode.FullRegression)
            {
                AddCheck(state, "Security classification enforcement", "SKIP", "MAV's generic SDK does not expose a safe classification-mutation regression command. Add a room-specific regression profile with sacrificial source/destination fixtures before automating classified route changes.");
                AddCheck(state, "Security downgrade sanitization", "SKIP", "Requires an explicit room regression profile that can establish and safely restore classified video/audio state.");
                AddCheck(state, "NVX route convergence", "SKIP", "Configured inventory can be compared with runtime discovery now, but active route convergence requires an explicitly declared sacrificial source/destination pair and rollback state.");
                AddCheck(state, "Combined-room duplicate-source resolution", "SKIP", "Room Combine is state-changing and needs a declared peer/test fixture so the controller can restore the original combine and route state deterministically.");
                AddCheck(state, "Runtime IPID destroy/recreate", "SKIP", "No public CWS lifecycle hook currently creates and destroys a runtime device/IPID independently of the host application.");
                AddCheck(state, "Embedded AI target resolution", "SKIP", "No generic assistant test contract is exposed by every Room build. Add this when the embedded assistant becomes a declared package capability.");
                AddCheck(state, "Room active-control regression", "WARN", "Generic room mutation remains intentionally gated until SystemDefinition carries an explicit Regression profile. Read-only subscriptions, dependency, diagnostics and inventory checks ran independently.");
            }
            else
            {
                AddCheck(state, "Room Health Check", "PASS", "Read-only definition, room state, diagnostics, subscription and dependency checks completed without changing active AV routing.");
            }
        }

        private void TestDuplicateReserveIdempotence(RunState state, string root, string room, string assignedCodec, JObject before)
        {
            int beforeCount = CountAssignedRooms(before);
            string beforeCodec = ReadAssignedCodecForRoom(before, room);
            if (string.IsNullOrWhiteSpace(beforeCodec)) beforeCodec = assignedCodec;
            string apiNote = "accepted";
            try
            {
                _cws.Post(state.Target, root + "/" + Uri.EscapeDataString(room) + "/vtcfarm/reserve", new { });
            }
            catch (Exception ex)
            {
                apiNote = "rejected as duplicate: " + ex.Message;
            }

            try
            {
                JObject after = _cws.Get(state.Target, root + "/system/vtcfarm/status");
                int afterCount = CountAssignedRooms(after);
                string afterCodec = ReadAssignedCodecForRoom(after, room);
                bool sameCodec = string.IsNullOrWhiteSpace(beforeCodec) || string.Equals(beforeCodec, afterCodec, StringComparison.OrdinalIgnoreCase);
                bool safe = afterCount == beforeCount && IsRoomAssigned(after, room) && sameCodec;
                AddCheck(state, "Duplicate reserve idempotence " + room, safe ? "PASS" : "FAIL",
                    safe
                        ? "Repeated Reserve was " + apiNote + "; assignment remained stable at " + (string.IsNullOrWhiteSpace(afterCodec) ? "the original codec" : afterCodec) + " and assigned-room count remained " + afterCount + "."
                        : "Repeated Reserve changed allocation state. Before count=" + beforeCount + ", after count=" + afterCount + ", before codec=" + beforeCodec + ", after codec=" + afterCodec + ".");
            }
            catch (Exception ex)
            {
                AddCheck(state, "Duplicate reserve idempotence " + room, "FAIL", "Unable to verify state after duplicate Reserve: " + ex.Message);
            }
        }

        private void TestIdleReleaseIdempotence(RunState state, string root, string room)
        {
            string apiNote = "accepted";
            try
            {
                _cws.Post(state.Target, root + "/" + Uri.EscapeDataString(room) + "/vtcfarm/release", new { });
            }
            catch (Exception ex)
            {
                apiNote = "returned a no-op/rejection: " + ex.Message;
            }

            try
            {
                JObject after = _cws.Get(state.Target, root + "/system/vtcfarm/status");
                bool idle = CountAssignedRooms(after) == 0 && !IsRoomAssigned(after, room);
                AddCheck(state, "Idle release idempotence " + room, idle ? "PASS" : "FAIL",
                    idle
                        ? "Repeated Release while idle was " + apiNote + "; Farm remained unassigned with no state corruption."
                        : "Repeated Release while idle changed assignment state unexpectedly.");
            }
            catch (Exception ex)
            {
                AddCheck(state, "Idle release idempotence " + room, "FAIL", "Unable to verify state after repeated Release: " + ex.Message);
            }
        }

        private void TestDeferredMediaRoutingSafety(RunState state, string room, JObject status)
        {
            JToken roomState = FindRoom(status, room);
            if (roomState == null)
            {
                AddCheck(state, "Deferred media routing safety", "FAIL", "Reserved room disappeared from the Farm status snapshot.");
                return;
            }

            string[] names = new[] { "Camera", "Presentation", "Audio" };
            List<string> bad = new List<string>();
            int missing = 0;
            foreach (string name in names)
            {
                JToken media = GetToken(roomState, name);
                if (media == null) continue;
                string stream = TokenString(media, "Stream");
                string desired = TokenString(media, "Desired");
                if (string.IsNullOrWhiteSpace(stream))
                {
                    missing++;
                    if (!string.IsNullOrWhiteSpace(desired)) bad.Add(name + " Desired=" + desired);
                }
            }

            if (bad.Count > 0)
                AddCheck(state, "Deferred media routing safety", "FAIL", "Farm produced route desires before the corresponding legacy media URL existed: " + string.Join("; ", bad.ToArray()) + ".");
            else if (missing > 0)
                AddCheck(state, "Deferred media routing safety", "PASS", missing + " media input(s) were still absent after reservation and no route desire was emitted for the missing values. Farm correctly remained in deferred-routing behavior.");
            else
                AddCheck(state, "Deferred media routing safety", "PASS", "All media inputs were already populated at observation time; no empty/absent media value was emitted as a desired route.");
        }

        private void CheckFarmCodecHardware(RunState state, JObject status)
        {
            JToken value = UnwrapValue(status);
            JArray codecs = value == null ? null : AsArray(GetToken(value, "Codecs"));
            if (codecs == null || codecs.Count == 0)
            {
                AddCheck(state, "Codec hardware availability", "SKIP", "No codec inventory was returned.");
                return;
            }

            int known = 0, unavailable = 0;
            List<string> names = new List<string>();
            foreach (JToken codec in codecs)
            {
                JToken hw = GetToken(codec, "HardwareAvailable");
                if (hw == null) continue;
                known++;
                bool online = TokenBool(hw);
                if (!online)
                {
                    unavailable++;
                    string name = TokenString(codec, "Name");
                    if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
                }
            }
            if (known == 0)
                AddCheck(state, "Codec hardware availability", "SKIP", "This Farm build does not expose HardwareAvailable in codec status.");
            else if (unavailable == 0)
                AddCheck(state, "Codec hardware availability", "PASS", known + " codec(s) report hardware available.");
            else
                AddCheck(state, "Codec hardware availability", "WARN", unavailable + " of " + known + " codec(s) report hardware unavailable" + (names.Count > 0 ? ": " + string.Join(", ", names.ToArray()) : string.Empty) + ". This distinguishes an environmental/hardware absence from an allocation logic failure.");
        }

        private void CheckFarmEiscRegistration(RunState state, JObject status)
        {
            JToken value = UnwrapValue(status);
            JArray rooms = value == null ? null : AsArray(GetToken(value, "Rooms"));
            if (rooms == null || rooms.Count == 0)
            {
                AddCheck(state, "Legacy EISC registration inventory", "SKIP", "No Farm room inventory was returned.");
                return;
            }

            List<string> failed = new List<string>();
            int known = 0;
            foreach (JToken room in rooms)
            {
                JToken registered = GetToken(room, "EiscRegistered");
                if (registered == null) continue;
                known++;
                if (!TokenBool(registered))
                {
                    string name = TokenString(room, "Name");
                    string failure = TokenString(room, "EiscRegistrationFailure");
                    failed.Add((string.IsNullOrWhiteSpace(name) ? "Unnamed room" : name) + (string.IsNullOrWhiteSpace(failure) ? string.Empty : " (" + failure + ")"));
                }
            }

            if (known == 0)
                AddCheck(state, "Legacy EISC registration inventory", "SKIP", "This Farm build does not expose EiscRegistered per room.");
            else if (failed.Count == 0)
                AddCheck(state, "Legacy EISC registration inventory", "PASS", known + " room(s) expose EISC registration telemetry and none report registration failure.");
            else
                AddCheck(state, "Legacy EISC registration inventory", "WARN", failed.Count + " room(s) report EISC registration failure: " + string.Join("; ", failed.ToArray()) + ".");
        }

        private void TestSubscriptionSnapshotContract(RunState state, JObject definition, JObject runtimeState)
        {
            List<DspSubscriptionTarget> controls = ReadDspSubscriptionTargets(definition);
            if (controls.Count == 0)
            {
                AddCheck(state, "Subscription cold-start snapshot", "SKIP", "SystemDefinition contains no DSP fader/mute controls implementing the current subscription contract.");
                AddCheck(state, "Subscription reconnect/resync", "SKIP", "No subscription-capable DSP control is configured.");
                AddCheck(state, "Zero/default initial snapshot", "SKIP", "No subscription-capable DSP control is configured.");
                return;
            }

            DspSubscriptionTarget selected = SelectSubscriptionTarget(controls, runtimeState);
            bool expectedZero = RuntimeStateShowsZero(runtimeState, selected.Name);
            string clientId = "mav-regression-controller-" + SafeId(selected.Name);

            JToken firstSnapshot = null;
            try
            {
                // Drain any prior queue retained under this stable test client.
                try { _cws.PostQuiet(state.Target, "/cws/api/subscriptions/poll", new { ClientId = clientId }); } catch { }
                _cws.Post(state.Target, "/cws/api/subscriptions/subscribe", new { ClientId = clientId, Device = "DSP", Control = selected.Name, Property = "value" });
                firstSnapshot = PollForInitialSnapshot(state.Target, clientId, selected.Name, 25);
                if (firstSnapshot == null)
                    AddCheck(state, "Subscription cold-start snapshot", "FAIL", "Configured " + selected.Kind + " '" + selected.Name + "' subscribed successfully but no IsInitialSnapshot update arrived within 5 seconds.");
                else
                    AddCheck(state, "Subscription cold-start snapshot", "PASS", "Configured " + selected.Kind + " '" + selected.Name + "' immediately produced an authoritative initial snapshot: value=" + TokenValueText(GetToken(firstSnapshot, "Value")) + ".");
            }
            catch (Exception ex)
            {
                AddCheck(state, "Subscription cold-start snapshot", "FAIL", "Configured " + selected.Kind + " '" + selected.Name + "' could not satisfy the subscription contract: " + ex.Message);
            }

            try
            {
                // Re-subscribing the same client/control must be idempotent and replay current state.
                try { _cws.PostQuiet(state.Target, "/cws/api/subscriptions/poll", new { ClientId = clientId }); } catch { }
                _cws.Post(state.Target, "/cws/api/subscriptions/subscribe", new { ClientId = clientId, Device = "DSP", Control = selected.Name, Property = "value" });
                JToken replay = PollForInitialSnapshot(state.Target, clientId, selected.Name, 25);
                AddCheck(state, "Subscription reconnect/resync", replay != null ? "PASS" : "FAIL",
                    replay != null
                        ? "Duplicate/reconnect subscription reused the existing contract and replayed current state with IsInitialSnapshot=true."
                        : "Re-subscribe did not replay an initial snapshot within 5 seconds.");
            }
            catch (Exception ex)
            {
                AddCheck(state, "Subscription reconnect/resync", "FAIL", ex.Message);
            }

            bool snapshotZero = firstSnapshot != null && IsZeroLike(GetToken(firstSnapshot, "Value"));
            if (snapshotZero)
            {
                AddCheck(state, "Zero/default initial snapshot", "PASS", "The cold-start snapshot itself carried a zero/false value, directly exercising the historical default-value suppression regression.");
            }
            else if (expectedZero)
            {
                AddCheck(state, "Zero/default initial snapshot", "FAIL", "Runtime diagnostics showed a zero/default state before subscription, but the initial snapshot did not preserve that value.");
            }
            else
            {
                AddCheck(state, "Zero/default initial snapshot", "SKIP", "The selected live control was not at zero/false during this run, so the historical default-value edge case was not exercised. The general initial-snapshot contract was still tested.");
            }
        }

        private JToken PollForInitialSnapshot(TargetSettings target, string clientId, string controlName, int attempts)
        {
            for (int i = 0; i < attempts; i++)
            {
                JObject poll = _cws.PostQuiet(target, "/cws/api/subscriptions/poll", new { ClientId = clientId });
                JArray updates = AsArray(UnwrapValue(poll));
                if (updates != null)
                {
                    foreach (JToken update in updates)
                    {
                        string control = TokenString(update, "Control");
                        string property = TokenString(update, "Property");
                        bool initial = TokenBool(GetToken(update, "IsInitialSnapshot"));
                        if (initial && string.Equals(control, controlName, StringComparison.OrdinalIgnoreCase) && string.Equals(property, "value", StringComparison.OrdinalIgnoreCase))
                            return update;
                    }
                }
                Crestron.SimplSharpPro.CrestronThread.Thread.Sleep(200);
            }
            return null;
        }

        private void TestNvxInventoryEvidence(RunState state, JObject definition)
        {
            JToken system = UnwrapValue(definition);
            JToken matrix = system == null ? null : GetToken(system, "MatrixSwitcher");
            int configuredEncoders = ArrayCount(matrix == null ? null : GetToken(matrix, "Encoders"));
            int configuredDecoders = ArrayCount(matrix == null ? null : GetToken(matrix, "Decoders"));
            int configuredTotal = configuredEncoders + configuredDecoders;
            if (configuredTotal == 0)
            {
                AddCheck(state, "Configured-vs-live NVX inventory", "SKIP", "SystemDefinition contains no matrix-switcher encoders/decoders.");
                return;
            }

            try
            {
                JObject logs = _cws.PostQuiet(state.Target, "/cws/api/system/diagnostics/logs", new { AfterSequence = 0, MaxEntries = 1000 });
                JArray entries = AsArray(UnwrapValue(logs));
                string message = null;
                if (entries != null)
                {
                    for (int i = entries.Count - 1; i >= 0; i--)
                    {
                        string candidate = TokenString(entries[i], "Message");
                        if (!string.IsNullOrWhiteSpace(candidate) && candidate.IndexOf("NVX DEVICE DISCOVERY VALIDATION", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            message = candidate;
                            break;
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(message))
                {
                    AddCheck(state, "Configured-vs-live NVX inventory", "WARN", "SystemDefinition configures " + configuredEncoders + " encoder(s) and " + configuredDecoders + " decoder(s), but no retained NVX discovery-validation record is available. Configured inventory is not being misreported as live inventory.");
                    return;
                }

                int? expectedEncoders = ExtractLabeledInt(message, "Expected Encoders");
                int? onlineEncoders = ExtractLabeledInt(message, "Online Encoders");
                int? expectedDecoders = ExtractLabeledInt(message, "Expected Decoders");
                int? onlineDecoders = ExtractLabeledInt(message, "Online Decoders");
                int expected = (expectedEncoders ?? configuredEncoders) + (expectedDecoders ?? configuredDecoders);
                if (!onlineEncoders.HasValue || !onlineDecoders.HasValue)
                {
                    AddCheck(state, "Configured-vs-live NVX inventory", "WARN", "NVX discovery validation exists but does not expose usable online encoder/decoder counts. Configured=" + configuredTotal + ".");
                    return;
                }

                int online = onlineEncoders.Value + onlineDecoders.Value;
                string detail = "Configured/expected " + expected + " NVX endpoint(s); discovery reports " + online + " online (encoders " + onlineEncoders.Value + "/" + (expectedEncoders ?? configuredEncoders) + ", decoders " + onlineDecoders.Value + "/" + (expectedDecoders ?? configuredDecoders) + ").";
                AddCheck(state, "Configured-vs-live NVX inventory", online >= expected ? "PASS" : "WARN", detail + (online >= expected ? "" : " Missing hardware is reported as an availability condition rather than being conflated with a software routing failure."));
            }
            catch (Exception ex)
            {
                AddCheck(state, "Configured-vs-live NVX inventory", "WARN", "Unable to compare configured and live NVX evidence: " + ex.Message);
            }
        }

        private void TestVtcFarmDependency(RunState state, JObject definition)
        {
            JToken system = UnwrapValue(definition);
            JToken farm = system == null ? null : GetToken(system, "VtcFarm");
            if (farm == null)
            {
                AddCheck(state, "VTC Farm dependency readiness", "SKIP", "SystemDefinition has no VtcFarm dependency block.");
                return;
            }

            JToken enabledToken = GetToken(farm, "Enabled");
            bool enabled = enabledToken != null && TokenBool(enabledToken);
            string address = TokenString(farm, "IpAddress");
            if (!enabled)
            {
                AddCheck(state, "VTC Farm dependency readiness", "SKIP", "VTC Farm integration is disabled for this room.");
                return;
            }
            if (string.IsNullOrWhiteSpace(address))
            {
                AddCheck(state, "VTC Farm dependency readiness", "WARN", "VTC Farm integration is enabled but no Farm IP address is present in SystemDefinition.");
                return;
            }

            TargetSettings farmTarget = new TargetSettings
            {
                Address = address,
                Username = state.Target.Username,
                Password = state.Target.Password,
                ProgramSlot = state.Target.ProgramSlot,
                UseHttpsCws = state.Target.UseHttpsCws,
                AllowUntrustedHttps = state.Target.AllowUntrustedHttps,
                RunMode = RunMode.HealthCheck,
                TestProfile = MavPackageType.VtcFarm
            };
            try
            {
                _cws.GetQuiet(farmTarget, "/cws/vtcfarm/system/vtcfarm/status");
                AddCheck(state, "VTC Farm dependency readiness", "PASS", "Configured Farm at " + address + " responded to the public VTC Farm CWS status contract.");
            }
            catch (Exception ex)
            {
                AddCheck(state, "VTC Farm dependency readiness", "WARN", "Configured Farm at " + address + " did not answer the VTC Farm CWS status probe: " + ex.Message + " This is separated from Room routing results; a legacy EISC-only Phase One installation may still be intentional.");
            }
        }

        private void CaptureTargetDiagnostics(RunState state)
        {
            try
            {
                string root = CwsRoot(state.Package.PackageType);
                JObject logs = _cws.Post(state.Target, root + "/system/diagnostics/logs", new { AfterSequence = 0, MaxEntries = 1000 });
                LocalFileIO.WriteAllBytes(Path.Combine(state.EvidenceFolder, "Target-CWS-Logs.json"), Encoding.UTF8.GetBytes(logs.ToString()));
                JObject summary = _cws.Get(state.Target, root + "/system/diagnostics/summary");
                LocalFileIO.WriteAllBytes(Path.Combine(state.EvidenceFolder, "Target-Diagnostics-Summary.json"), Encoding.UTF8.GetBytes(summary.ToString()));
                _log.Pass("Target CWS diagnostics captured into evidence folder.");
            }
            catch (Exception ex) { _log.Warn("Unable to capture target diagnostics: " + ex.Message); }
        }

        private JObject CheckCall(RunState state, string name, Func<JObject> action)
        {
            long marker = _observer == null ? 0 : _observer.BeginStep(name);
            try
            {
                _log.Info("TEST BEGIN: " + name);
                JObject value = action();
                TargetLogStepEvidence evidence = _observer == null ? null : _observer.EndStep(name, marker);
                AddCheck(state, name, "PASS", Message(value), evidence);
                if (evidence != null && evidence.ErrorEvents > 0)
                    AddCheck(state, name + " target SDK errors", "WARN",
                        evidence.ErrorEvents + " Critical/Error journal entries were observed during this test window. Review the correlated target log; an API PASS alone cannot clear those errors.", evidence);
                return value;
            }
            catch (Exception ex)
            {
                TargetLogStepEvidence evidence = _observer == null ? null : _observer.EndStep(name, marker);
                AddCheck(state, name, "FAIL", ex.Message, evidence);
                throw;
            }
        }

        private void AddCheck(RunState state, string name, string status, string detail)
        {
            AddCheck(state, name, status, detail, null);
        }

        private void AddCheck(RunState state, string name, string status, string detail, TargetLogStepEvidence evidence)
        {
            RegressionCheck check = new RegressionCheck
            {
                Sequence = ++_checkSequence,
                Name = name,
                Status = status,
                Detail = detail,
                TimestampUtc = DateTime.UtcNow,
                TargetEvidence = evidence
            };
            lock (state.Checks) state.Checks.Add(check);
            if (status == "PASS") _log.Pass(name + ": " + detail);
            else if (status == "WARN") _log.Warn(name + ": " + detail);
            else if (status == "SKIP") _log.Info("SKIP " + name + ": " + detail);
            else _log.Error(name + ": " + detail);
        }

        private static List<string> ReadRoomNames(JObject status)
        {
            List<string> names = new List<string>();
            JToken value = UnwrapValue(status);
            JArray rooms = value == null ? null : AsArray(GetToken(value, "Rooms"));
            if (rooms != null)
            {
                foreach (JToken r in rooms)
                {
                    string n = TokenString(r, "Name");
                    if (string.IsNullOrWhiteSpace(n)) n = TokenString(r, "Room");
                    if (!string.IsNullOrWhiteSpace(n)) names.Add(n);
                }
            }
            return names;
        }

        private static int ReadCodecCount(JObject status)
        {
            JToken value = UnwrapValue(status);
            JArray codecs = value == null ? null : AsArray(GetToken(value, "Codecs"));
            return codecs == null ? 0 : codecs.Count;
        }

        private static int CountAssignedRooms(JObject status)
        {
            int count = 0;
            JToken value = UnwrapValue(status);
            JArray rooms = value == null ? null : AsArray(GetToken(value, "Rooms"));
            if (rooms != null)
                foreach (JToken r in rooms)
                    if (TokenBool(GetToken(r, "CodecAssigned"))) count++;
            return count;
        }

        private static bool IsRoomAssigned(JObject status, string roomName)
        {
            JToken room = FindRoom(status, roomName);
            return room != null && TokenBool(GetToken(room, "CodecAssigned"));
        }

        private static JToken FindRoom(JObject status, string roomName)
        {
            JToken value = UnwrapValue(status);
            JArray rooms = value == null ? null : AsArray(GetToken(value, "Rooms"));
            if (rooms == null) return null;
            foreach (JToken r in rooms)
            {
                string n = TokenString(r, "Name");
                if (string.IsNullOrWhiteSpace(n)) n = TokenString(r, "Room");
                if (string.Equals(n, roomName, StringComparison.OrdinalIgnoreCase)) return r;
            }
            return null;
        }

        private static string ReadAssignedCodecForRoom(JObject status, string roomName)
        {
            JToken room = FindRoom(status, roomName);
            return room == null ? string.Empty : TokenString(room, "Codec");
        }

        private static string ReadAssignedCodec(JObject response)
        {
            JToken value = UnwrapValue(response);
            return value == null ? string.Empty : TokenString(value, "Codec");
        }

        private static List<DspSubscriptionTarget> ReadDspSubscriptionTargets(JObject definition)
        {
            List<DspSubscriptionTarget> result = new List<DspSubscriptionTarget>();
            JToken system = UnwrapValue(definition);
            JToken dsp = system == null ? null : GetToken(system, "Dsp");
            if (dsp == null) return result;

            JArray faders = AsArray(GetToken(dsp, "Faders"));
            if (faders != null)
            {
                foreach (JToken fader in faders)
                {
                    // Mirrors MAV_MONITOR_UI subscription discovery: faders subscribe by Name, then ControlId fallback.
                    string name = TokenString(fader, "Name");
                    if (string.IsNullOrWhiteSpace(name)) name = TokenString(fader, "ControlId");
                    if (!string.IsNullOrWhiteSpace(name)) result.Add(new DspSubscriptionTarget { Name = name, Kind = "DSP fader" });
                }
            }

            JArray buttons = AsArray(GetToken(dsp, "Buttons"));
            if (buttons != null)
            {
                foreach (JToken button in buttons)
                {
                    string type = TokenString(button, "Type");
                    if (!string.Equals(type, "Toggle", StringComparison.OrdinalIgnoreCase)) continue;
                    // Toggle/mute subscriptions are registered against ControlId in the shipped MAV clients.
                    string name = TokenString(button, "ControlId");
                    if (string.IsNullOrWhiteSpace(name)) name = TokenString(button, "Name");
                    if (!string.IsNullOrWhiteSpace(name)) result.Add(new DspSubscriptionTarget { Name = name, Kind = "DSP mute/toggle" });
                }
            }
            return result;
        }

        private static DspSubscriptionTarget SelectSubscriptionTarget(List<DspSubscriptionTarget> controls, JObject runtimeState)
        {
            foreach (DspSubscriptionTarget control in controls)
                if (RuntimeStateShowsZero(runtimeState, control.Name)) return control;
            return controls[0];
        }

        private static bool RuntimeStateShowsZero(JObject runtimeState, string controlName)
        {
            JArray entries = AsArray(UnwrapValue(runtimeState));
            if (entries == null) return false;
            foreach (JToken entry in entries)
            {
                if (!string.Equals(TokenString(entry, "Device"), "DSP", StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(TokenString(entry, "Control"), controlName, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(TokenString(entry, "Property"), "value", StringComparison.OrdinalIgnoreCase)) continue;
                if (IsZeroLike(GetToken(entry, "Value"))) return true;
            }
            return false;
        }

        private static bool IsZeroLike(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return false;
            if (token.Type == JTokenType.Boolean) return !token.Value<bool>();
            if (token.Type == JTokenType.Integer) return token.Value<long>() == 0;
            if (token.Type == JTokenType.Float) return Math.Abs(token.Value<double>()) < 0.0000001;
            string s = Convert.ToString(((JValue)token).Value);
            return string.Equals(s, "0", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "false", StringComparison.OrdinalIgnoreCase);
        }

        private static int? ExtractLabeledInt(string text, string label)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            Match m = Regex.Match(text, Regex.Escape(label) + @"\.*\s*:\s*(\d+)", RegexOptions.IgnoreCase);
            int value;
            return m.Success && int.TryParse(m.Groups[1].Value, out value) ? (int?)value : null;
        }

        private static string SafeId(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "control";
            StringBuilder b = new StringBuilder();
            foreach (char c in text)
            {
                if (char.IsLetterOrDigit(c)) b.Append(char.ToLowerInvariant(c));
                else if (b.Length > 0 && b[b.Length - 1] != '-') b.Append('-');
            }
            string s = b.ToString().Trim('-');
            return string.IsNullOrEmpty(s) ? "control" : s;
        }

        private static int ArrayCount(JToken token)
        {
            JArray array = AsArray(token);
            return array == null ? 0 : array.Count;
        }

        private static JArray AsArray(JToken token)
        {
            return token as JArray;
        }

        private static JToken UnwrapValue(JObject response)
        {
            if (response == null) return null;
            return response["Value"] ?? response["value"];
        }

        private static JToken GetToken(JToken parent, string name)
        {
            if (parent == null || parent.Type != JTokenType.Object) return null;
            JObject obj = (JObject)parent;
            JToken token = obj[name];
            if (token != null) return token;
            foreach (JProperty p in obj.Properties())
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value;
            return null;
        }

        private static string TokenString(JToken parent, string property)
        {
            JToken token = GetToken(parent, property);
            if (token == null || token.Type == JTokenType.Null) return string.Empty;
            return token.Type == JTokenType.String ? token.Value<string>() : token.ToString();
        }

        private static string TokenValueText(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return "null";
            return token.Type == JTokenType.String ? token.Value<string>() : token.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static bool TokenBool(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return false;
            if (token.Type == JTokenType.Boolean) return token.Value<bool>();
            if (token.Type == JTokenType.Integer) return token.Value<long>() != 0;
            bool value;
            if (bool.TryParse(token.ToString(), out value)) return value;
            int numeric;
            return int.TryParse(token.ToString(), out numeric) && numeric != 0;
        }

        private static string Message(JObject o)
        {
            return Convert.ToString(o == null ? null : (o["Message"] ?? o["message"] ?? "OK"));
        }

        private static string CwsRoot(MavPackageType type)
        {
            return type == MavPackageType.VtcFarm ? "/cws/vtcfarm" : "/cws/api";
        }

        private void WriteSummary(RunState state)
        {
            int pass = 0, warn = 0, fail = 0, skip = 0;
            lock (state.Checks)
            {
                foreach (RegressionCheck c in state.Checks)
                {
                    if (c.Status == "PASS") pass++;
                    else if (c.Status == "WARN") warn++;
                    else if (c.Status == "SKIP") skip++;
                    else fail++;
                }
            }
            string summary = string.Format("Regression complete: {0} PASS / {1} FAIL / {2} WARN / {3} SKIP.", pass, fail, warn, skip);
            if (fail > 0) _log.Error(summary);
            else if (warn > 0) _log.Warn(summary);
            else _log.Pass(summary);
        }
    }
}
