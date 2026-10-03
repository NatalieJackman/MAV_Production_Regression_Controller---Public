MAV VTC FARM MONITOR UI - Resident Field Advisor
=================================================

Purpose
-------
Provide a deterministic, read-only field-engineering workflow so onsite staff can
perform legacy-room validation with much less remote hand-holding.

Added capabilities
------------------
- Resident Engineer Guidance panel: What I am seeing / What to do next / Why.
- Explicit Do Not Troubleshoot Yet guidance to prevent chasing downstream layers.
- STOP / NEEDS ENGINEERING REVIEW escalation retains existing safety behavior.
- Technician Capture Baseline and live Changed Since Baseline comparison.
- Known-good test pattern storage in browser localStorage after a PASS.
- Known-good timing comparison for future tests of the same room.
- Timeout intelligence when a technician marks a VTC request but Reserve does not arrive.
- Export Complete Evidence: one JSON evidence package containing human-readable summary,
  field workflow, baseline, known-good reference, Farm status, diagnostics state/change
  journal, and test-window logs.

Safety / control-path note
--------------------------
These features remain read-only. Browser controls mark technician actions and manage
local evidence only. They do not Reserve, Release, route, dial, or otherwise command
the VTC Farm or legacy room.

Deployment
----------
Replace the contents of:
  /html/mav-vtc-farm-monitor-ui
with the contents of this folder.
