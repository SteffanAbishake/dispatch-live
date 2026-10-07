# Android acceptance tests

Record phone model, Android version, battery settings, app build, server version and results. Test on at least two physical phones with separate driver accounts. No phone tests have been completed for this source package.

| Test | Steps | Required result |
|---|---|---|
| Permissions | Deny location, then tap Start | No location collection; clear permission guidance. |
| Consent | Leave consent unchecked | No foreground service or delivery start. |
| Separate drivers | Sign in on two phones using different approved numbers | Dashboard displays both; each marker belongs to its own delivery. |
| Unapproved driver | Sign in before approval | Cannot start sharing. Approve in dashboard, refresh app, then retry. |
| App switching | Start sharing, press Home, walk outdoors for 5 minutes | Notification stays visible; GPS capture times and positions continue changing. |
| Screen locked | Start sharing, lock phone, walk for at least 15 minutes | Recent GPS fixes continue. Repeat under the intended battery configuration. |
| Remove from Recents | Swipe the Activity away during delivery | Service is designed to remain active; document manufacturer-specific behavior. |
| Force-stop | Force-stop via Android Settings | Updates stop; dashboard reports Location unavailable within 45 seconds plus polling delay. No automatic GPS restart. |
| Reopen after force-stop | Launch app again | It does not silently resume tracking; previous session can be ended. |
| Power off | Turn phone off during delivery | Old position stays as last known; it becomes unavailable. |
| Disable Location | Disable the phone's global Location setting | No old fix is relabeled live; app explains GPS unavailable; dashboard becomes stale. |
| Restore Location | Enable location while service remains running | Fresh fixes may resume; verify behavior of the phone's providers. |
| Stop in app | Tap Stop sharing online | Listener removed immediately, server session ends, marker disappears. |
| Stop in notification | Use notification Stop action | Same outcome as the in-app Stop button. |
| Offline Stop | Disconnect network, tap Stop, then reconnect | GPS stops locally immediately; server ends once stop retry succeeds. No queued coordinates are uploaded after Stop. |
| Dispatcher end | End session from dashboard | Server immediately rejects updates; phone stops on next successful check. |
| Withdraw approval | Suspend active driver | Driver cannot send more coordinates; current server session ends. |
| Re-login | Let token expire or revoke session | App stops sharing; driver signs in again before another session. |
| Provider accuracy | Try approximate permission and poor GPS | Accuracy is shown; unavailable positions are not reported as live. |
| Dashboard offline | Disconnect dispatcher computer | Existing map data is marked unavailable, not continuously live. |

Force-stop and switching apps are different states. Never promise tracking through force-stop, shutdown, revoked permissions or a manufacturer's forced process termination.

An emulator can help with API/permission flows but cannot establish reliable screen-lock behavior, battery consumption or GPS quality on your drivers' phones.
