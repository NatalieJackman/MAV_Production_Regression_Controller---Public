# MAV Production Regression Controller — Preview 010 Public Demo

A standalone Crestron 4-Series deployment and regression controller used to demonstrate an AI-assisted engineering workflow in which architecture, deployment safety, independent verification, and human release judgment remain explicit.

> **Public demonstration / privacy-sanitized build**  
> The production Dropbox application key, private room identifiers, and personal field-workflow labels have been removed or replaced. **Cloud CurrentBuild is intentionally disabled** in this public copy. Offline Bundle and Local Upload remain available and follow the normal staging, deployment, and regression path. See [`PUBLIC_DEMO_PRIVACY.md`](PUBLIC_DEMO_PRIVACY.md).

## What this demonstrates

The controller runs independently from the application under test and implements this workflow:

**Stage → inspect → preflight → back up → deploy → observe → stimulate → assert → collect evidence → human review**

It is designed to show both development velocity and control. AI can assist with implementation, but the target application is still evaluated through external contracts and processor state rather than trusting generated code or log text alone.

## Key capabilities

- CPZ + JSON package staging through Local Upload or Offline Bundle.
- SystemDefinition JSON inspection, editing, validation, and immutable SHA-256 identity checks.
- Selectable target processor/program slot with self-target protection.
- Backup and rollback around the destructive deployment window.
- CrestronIO-based CP4N local file handling with canonical `/user/...` paths.
- Asynchronous `KILLPROG` / `PROGLOAD` lifecycle synchronization using repeated authoritative `PROGREG` observations rather than fixed sleeps or console-string timing.
- HTML5 target-resource deployment.
- Independent CWS health/regression checks.
- Separate target SDK diagnostic observation and controller logging.
- PASS / WARN / FAIL / SKIP results and exportable evidence.
- Workflow locking that prevents package/config mutations or destructive manual console operations while a regression run is active.

## Regression coverage

Preview 010 includes tests derived from actual bench/field failures and acceptance results, including:

- controller staging-path reopen/write-read preflight;
- deployment commit preflight before target modification;
- command-handler registration;
- production resource safety;
- concurrent codec reservation/release;
- duplicate-reserve idempotence;
- idle-release idempotence;
- immediate codec reuse;
- deferred-media routing safety;
- cold-start/reconnect subscription checks when the target contract exposes them;
- configured-vs-live device evidence where available;
- explicit SKIP/WARN handling when a safe fixture or public contract is unavailable.

The suite deliberately does **not** convert unavailable coverage into a fake PASS.

## Demonstrated validation result

The private Preview 010 build was exercised on a CP4N after the lifecycle and file-I/O corrections and completed the demonstrated full regression run with:

**30 PASS · 0 FAIL · 1 WARN · 5 SKIP**

The same run demonstrated the lifecycle race protection: an incomplete/transitional `PROGREG` observation was rejected as non-authoritative, the controller continued polling, and deployment advanced only after the target program was observed registered consistently.

A privacy-sanitized summary is in [`docs/SAMPLE_REGRESSION_SUMMARY.md`](docs/SAMPLE_REGRESSION_SUMMARY.md).

## Public-demo privacy behavior

The production version supports Dropbox OAuth/PKCE for Cloud CurrentBuild. This repository intentionally sets the Dropbox App Key to an empty value. The controller/UI report the feature as **disabled for privacy** instead of silently failing. The public build also avoids reading or deleting an existing private-build Dropbox refresh token.

Bundled target HTML5 examples use generic room/operator labels. Embedded C# web manifests were regenerated from those sanitized assets so the removed identifiers are not retained inside Base64 data.

## Build environment

- .NET Framework **4.8**
- Crestron Simpl# / 4-Series SDK packages **2.22.15**
- Intended for CP4N / compatible 4-Series Simpl# Pro development environments

Restore the Crestron NuGet packages and compile in the normal Simpl# Pro toolchain. This repository does not include a compiled CPZ.

## Repository map

- `Core/` — deployment, transport, lifecycle, CWS regression, logging/evidence, package-source logic
- `Web/` — controller HTML5 UI source
- `Resources/TargetHtml5/` — privacy-sanitized target HTML5 resources embedded for deployment demonstration
- `docs/` — sample regression evidence and detailed development history
- `PUBLIC_DEMO_PRIVACY.md` — publication-specific removals and affected behavior
- `PUBLICATION_CHECKLIST.md` — sanitization checks performed before packaging

## Safety model

Full Regression can reserve resources or change AV state and is intended for an authorized maintenance window. Health Check remains read-only. The controller treats processor lifecycle state and external functional assertions as authoritative; target log output is supporting evidence, not the sole proof of correctness.

## Rights

Copyright © 2026 Natalie Jackman. Published for technical evaluation and portfolio review. No license to modify, redistribute, or incorporate this source is granted unless a separate license is added to the repository.
