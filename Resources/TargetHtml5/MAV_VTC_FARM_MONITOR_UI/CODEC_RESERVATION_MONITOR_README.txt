MAV VTC Farm Monitor - Codec Reservation Visualization
======================================================

Adds an Overview > Codec Reservations panel driven by the existing SDK diagnostics
state endpoint. No new CWS transport or endpoint is introduced.

The four cards show:
  - NIPR VTC codec number/name
  - Assigned or Unassigned
  - Assigned room when reserved
  - Codec hardware availability
  - Primary/secondary encoder stream URLs (expandable)

The same state also remains visible in State & Changes and is available as evidence
for the VTC Farm Troubleshooter.
