MAV VTC Farm Monitor - SDK Full Logging
=======================================

This build uses the existing VTC Farm CWS root /cws/vtcfarm and adds the same
read-only MAV SDK diagnostics polling model used by MAV_MONITOR_UI:

  GET  /system/diagnostics/summary
  POST /system/diagnostics/logs
  POST /system/diagnostics/state
  POST /system/diagnostics/changes

The Logs view merges MAV program Logger records with browser monitoring records.
The State & Changes view displays the SDK runtime state journal.

The VTC Farm must be compiled against the diagnostics-capable MAV_SDK(9) DLL.
No new CWS server or alternate transport is introduced by this UI.
