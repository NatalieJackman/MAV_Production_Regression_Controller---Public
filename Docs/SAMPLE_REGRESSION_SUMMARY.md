# Sanitized Preview 010 Regression Summary

This is a privacy-sanitized summary of a successful private Preview 010 CP4N run. Room identifiers, processor addresses, and environment-specific identifiers are intentionally omitted.

## Result

**30 PASS · 0 FAIL · 1 WARN · 5 SKIP**

## Deployment / lifecycle evidence

- **PASS** — controller staging-path self-test reopened the CPZ and JSON through CrestronIO and completed a write/read probe.
- **PASS** — offline package continuity confirmed that deployment artifacts were fully local before target modification.
- **PASS** — existing target configuration was backed up.
- **PASS** — deployment commit preflight reopened staged CPZ/config/backup artifacts and verified identity before the destructive window.
- The first post-load processor-registry response was incomplete/transitional. The controller explicitly refused to infer state from it and continued polling.
- **PASS** — target program was later observed authoritatively registered and the deployment workflow continued.
- **PASS** — target CWS diagnostics became available.

## Independent regression evidence

- **PASS** — CPZ immutable identity.
- **PASS** — configuration immutable identity.
- **PASS** — JSON structural validation.
- **PASS** — SystemDefinition readable through the target public contract.
- **PASS** — application status and command-handler dispatch.
- **PASS** — diagnostics summary.
- **PASS** — configured codec inventory.
- **PASS** — no active production assignment existed before synthetic resource testing.
- **PASS** — three synthetic room reservations acquired three distinct codec resources.
- **PASS** — duplicate reservation remained idempotent and did not create an additional assignment.
- **PASS** — missing media inputs did not produce premature route desire; the Farm remained in deferred-routing behavior.
- **PASS** — concurrent reservation count matched the requested synthetic allocations.
- **PASS** — all resources released cleanly.
- **PASS** — repeated release while idle remained idempotent.
- **PASS** — immediate reserve/release reuse completed without reboot and final assignment count returned to zero.
- **PASS** — target diagnostics were captured into the evidence package.
- **PASS** — external target SDK diagnostic journal remained connected and produced evidence.

## Deliberately non-passing classifications

- **WARN** — the legacy external transport boundary still requires a real approved field fixture for wire-level certification.
- **SKIP** — cloud cache reuse is a package-source test, not a target regression in this run.
- **SKIP** — hardware-availability and per-room registration telemetry were not exposed by this target build.
- **SKIP** — late legacy media injection and duplicate dialing had no safe public/harmless test hook, so the controller did not manufacture external activity merely to claim coverage.

The important behavior is that unavailable evidence remains visible as WARN/SKIP rather than being converted into a false PASS.
