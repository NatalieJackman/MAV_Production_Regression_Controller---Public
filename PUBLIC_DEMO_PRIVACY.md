# Public Demonstration Privacy Notes

This repository is a privacy-sanitized demonstration copy of MAV Production Regression Controller Preview 010. The private/working build is maintained separately and is not modified by this sanitization.

## Intentionally removed or replaced

- The production Dropbox OAuth application key is omitted.
- Room identifiers used in bundled target-UI examples are replaced with generic names such as `ROOM-A`.
- Personal/operator names in bundled field-workflow examples are replaced with role labels such as `Field Technician` and `Engineering Lead`.
- The controller still demonstrates cloud-source architecture, but Cloud CurrentBuild is disabled and explicitly labeled as privacy-disabled. Use **Offline Bundle** or **Local Upload** in this public build.

## Behavior affected by sanitization

### Cloud CurrentBuild
The Dropbox App Key is intentionally blank. OAuth start, inventory synchronization, and cloud staging return an explicit privacy message instead of attempting authorization. This is deliberate and is not a controller failure.

### Bundled target HTML5 examples
The HTML5 resources are functionally representative but contain sanitized example labels. They should not be used as authoritative production room/facility data.

## Behavior not intentionally changed

Target configuration, CPZ staging, offline/local package workflows, deployment preflight, backup/rollback, processor lifecycle synchronization, CWS regression checks, PASS/WARN/FAIL/SKIP reporting, target console, target SDK log observation, and evidence export retain the Preview 010 implementation.
