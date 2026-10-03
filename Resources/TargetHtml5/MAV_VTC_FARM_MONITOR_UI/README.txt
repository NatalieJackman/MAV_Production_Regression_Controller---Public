MAV_VTC_FARM_MONITOR_UI - Plain HTML5 Conversion

Source baseline:
  MAV 4-Series Program Archive and Load Utility Preview 135(1)
  embedded MAV_VTC_FARM_MONITOR_UI CH5 project

Scope of this conversion:
  - Removed the CH5 shell/template/import layer.
  - Removed npm/webpack/CrComLib runtime requirements.
  - Preserved the original page markup.
  - Preserved the original page styling.
  - Preserved page1.js byte-for-byte as app.js.

CWS behavior is unchanged. The application still uses:
  API root: /cws/vtcfarm
  GET  /system/definition/get
  POST /subscriptions/subscribe
  POST /subscriptions/poll

No VTC Farm program, SDK, CWS route, request body, subscription, or polling behavior was changed for this conversion.

Palette revision:
- Day theme now mirrors the MAV_MONITOR_UI light palette and contrast treatment.
- Visual-only change; app.js/CWS behavior is unchanged.
