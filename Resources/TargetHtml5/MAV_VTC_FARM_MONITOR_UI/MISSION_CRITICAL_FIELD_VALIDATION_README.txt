MAV VTC FARM MONITOR UI
Mission-Critical Guided Field Validation Update
September 30, 2026

PURPOSE
-------
This update turns the existing Legacy EISC Field Validation panel into a guided,
read-only field validation console. It is intended to let an onsite technician
collect deterministic evidence while development/analysis remains remote.

NO CONTROL-PATH CHANGE
----------------------
The guided workflow does NOT send Reserve, Release, route, dial, or other control
commands to the VTC Farm or the legacy room. The workflow buttons only create
browser-local timestamps and evidence markers.

DEPLOYMENT
----------
Replace the files in:
  /html/mav-vtc-farm-monitor-ui

with the contents of this folder. This update assumes the current VTC Farm
diagnostics build is already running and exposes the read-only room status used
by the existing Field Validation panel.

GUIDED WORKFLOW
---------------
1. Select exactly one legacy room.
2. Set Legacy room evidence to one of:
   - Program log available
   - Debugger / Test Manager only
   - No room-side logging found
   - Not checked yet
3. Click Start test session.
4. Follow NEXT ACTION.
5. When the technician physically requests VTC in the room, click:
     Mark VTC request now
   This timestamps the physical action only.
6. Allow the Farm to proceed normally.
7. The UI latches evidence for:
   - Baseline ready
   - Reserve request observed
   - Codec assigned
   - Camera join 6 received
   - Presentation join 7 received
   - Audio join 8 received
   - Desired routes match observed routes
   - Release observed
   - Returned to idle
8. When the technician physically releases VTC, click:
     Mark release now
9. End the session.
10. Export both:
    - Evidence JSON
    - Result summary TXT

STICKY RESULTS
--------------
Passed workflow steps remain latched for the duration of the browser test
session. This allows the UI to prove earlier phases after the room progresses to
later states such as Release and Idle.

STOP / NEEDS ENGINEERING REVIEW CONDITIONS
-------------------------------
The workflow explicitly tells the field technician to STOP and export evidence
when it sees a condition that should not be debugged by trial-and-error onsite,
including:
- CWS/Farm telemetry loss during an active test
- Selected room EISC registration failure
- Farm room state containing error/failed/fault/exception
- CodecAssigned reported without a codec identity
- Desired route not matching observed/applied route after assignment

A STOP condition does not modify the Farm. It changes only the browser guidance.

EVIDENCE PACKAGE
----------------
The JSON export contains:
- Test room and timestamps
- Legacy room evidence method
- Overall workflow result
- Stop reason, if any
- Every workflow step and its pass timestamp
- Technician VTC request/release markers
- Browser-local test timeline
- Current Farm status snapshot
- MAV diagnostics summary
- Current retained SDK state
- State-change journal
- Program/browser logs within the test window

The TXT result summary is designed to be attached to an email or ticket without
requiring someone to manually reconstruct the test sequence.

EXPECTED RESULT
---------------
A clean legacy-room test should progress through:
  Baseline ready
  -> Reserve request observed
  -> Codec assigned
  -> Camera received
  -> Presentation received
  -> Audio received
  -> Routes applied
  -> Release observed
  -> Returned to idle

If the sequence stops, export the evidence before making additional changes.

COLLAPSIBLE WORKFLOW UI UPDATE
------------------------------
The Guided Field Validation area is now split into two collapsible bubbles:
- Test Session: guided workflow, technician markers, escalation state, sequence, timeline, and exports.
- Room Diagnostics: room selector and live room telemetry.

When All rooms is selected, each room is independently expandable/collapsible. Expansion state is retained while live Farm status refreshes.
