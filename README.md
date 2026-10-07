# Dispatch Live — Android driver tracking

A development project for a delivery company. Drivers sign in using their own phone numbers, get approved by a dispatcher and explicitly start location sharing for a delivery.

## What is in this download

- `backend/`: ASP.NET Core 10 API, SQLite storage, SignalR notifications and a dispatcher web dashboard.
- `android/`: native Android app in Java, minimum Android 8.0, targeting Android 15/API 35.
- `tests/integration.py`: HTTP integration checks for two drivers, authentication, company boundaries and stop behavior.
- `.github/workflows/build.yml`: builds the backend and a debug Android APK in GitHub Actions.
- `docs/DEVICE-TESTS.md`: the device acceptance checklist for the conditions you requested.

The companion `Dispatch-Driver-Test.apk` is a compiled, debug-signed test build. Android compilation, lint (zero errors, four advisory warnings), APK signature verification, .NET compilation and the two-driver HTTP integration suite passed on 7 October 2026 (Asia/Colombo). Physical-device background-location tests and real SMS delivery are still pending. See `docs/VALIDATION.md` and `docs/HOSTING-AND-SMS.md`.

Install the APK on each Android phone, allowing installation from your download app when prompted. Set the app's server URL to your deployed .NET service's HTTPS address. The earlier private ChatGPT dashboard demo is not this API.

## Your requested behavior

| Driver action / device state | Implementation |
|---|---|
| Switch to another app | A location foreground service continues after Start, with a persistent notification. |
| Lock the phone screen | The same service remains active; location and upload work are independent of the Activity. Real-device battery behavior must be tested. |
| Force-stop app, power off phone, disable location or lose connectivity | Fresh updates cease; dashboard marks the last-known location unavailable after 45 seconds. No attempt to bypass force-stop. |
| Tap Stop sharing / end delivery | Location listeners are removed immediately; server stop clears the saved position. If offline, a pending stop retries when Android permits network work. |
| Dispatcher ends delivery or removes approval | Server rejects further updates; the Android service detects the change at its next successful check and stops. |

## Start the backend on your PC

Install the .NET 10 SDK and Python 3 (for the integration checks).

In PowerShell, from the project root:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://localhost:5080'
$env:Bootstrap__Email = 'your-email@example.com'
$env:Bootstrap__Password = Read-Host 'Choose an admin password (at least 16 characters)'
$env:Otp__DevelopmentCodes = 'true'
dotnet run --project backend
```

Open `http://localhost:5080` and sign in with that email and password. The bootstrap credentials create the first dispatcher only when the database is empty; changing these variables later does not reset the password.

The SQLite database is created under `data/` in the process working directory. Keep that directory on durable storage. This MVP uses `EnsureCreated` for a new database; use proper schema migrations before maintaining production data through future schema changes.

### First two-driver test

1. Add two different driver phone numbers in international format, such as `+9477…`.
2. Approve each driver.
3. Build/install the Android debug app using `android/BUILDING.md`.
4. Enter the backend address and the driver's phone number in each app.
5. Request a code. In local development, read it from the backend terminal; no SMS is sent.
6. Verify the code, enter a delivery reference and tick the consent box.
7. Grant location and notification permissions, then tap **Start** again.
8. Watch the web dashboard. Use **Locate** on each driver to center the map.
9. Test all four conditions in `docs/DEVICE-TESTS.md`.

For an Android emulator on the same computer, use `http://10.0.2.2:5080` and make the backend listen on `http://0.0.0.0:5080` for the test. Only the debug app permits cleartext to this emulator address or localhost. A physical phone needs a reachable HTTPS host with a certificate trusted by Android. Do not use the old `chatgpt.site` demo address as the API address; it runs a different prototype.

## Real SMS login

The backend includes a Twilio Verify integration. Configure these values on the server using environment variables or your host's secret settings:

```text
Otp__DevelopmentCodes=false
Otp__AccountSid=<Twilio account SID>
Otp__AuthToken=<Twilio auth token>
Otp__ServiceSid=<Twilio Verify service SID>
```

Create a Verify service in your own Twilio account and enable delivery to your drivers' countries. SMS has provider costs. No SMS credentials are included, and real SMS delivery has not been tested. Development codes work only with `ASPNETCORE_ENVIRONMENT=Development` plus the explicit development flag. They are generated randomly, valid for five minutes, limited to five attempts and consumed once.

## Hosting

Publish the backend with `dotnet publish backend -c Release -o output`. It serves both API and dispatcher UI from the same origin. You can run it on a .NET-compatible HTTPS host or build the supplied Dockerfile from the `backend/` directory.

For a TLS-terminating reverse proxy, forward the public Host and X-Forwarded-Proto headers and set `ReverseProxy__Address` to that proxy's exact trusted IP. Do not broadly trust all forwarded headers. Set `AllowedHosts` to your API hostname. Production cookies are HttpOnly, SameSite Strict and Secure. No CORS opening is needed for the Android app.

Mount durable storage for SQLite, keep one backend instance for this version, and restrict database/backups to authorized operators. A multi-instance deployment needs shared storage, distributed throttling and a SignalR scaling strategy. This project bootstraps one company; company scoping is enforced in the data/API, but public company registration and a multi-company administration UI are not included.

## Android implementation

The app starts a `location` foreground service only from the visible Activity after consent and permission. It does not request automatic background starts, does not run GPS after boot, and uses `START_NOT_STICKY` so it will not silently resume a killed sharing session. No `ACCESS_BACKGROUND_LOCATION` request is needed for this user-started foreground-service flow.

GPS/network providers deliver fixes to the service. Uploads target a ten-second cadence; this is not a guaranteed timing SLA. The dashboard polls every five seconds and also accepts SignalR refresh notifications. The original location capture time is preserved so an old fix cannot be presented as fresh merely because it was uploaded later.

An offline Stop removes location listeners immediately and saves only the pending stop ID. Android JobScheduler retries the stop on a usable connection. Force-stop can suspend that job; reopening the app resumes pending-stop handling. Expired login requires signing in again with the same phone number. Stopping offline cannot instantly notify a disconnected server, so a last-known position remains temporarily visible and becomes stale.

## Scope and retention

Only the latest coordinate per active delivery is stored. Ended sessions retain delivery/consent metadata but clear coordinates. The server expires sessions after eight hours, with cleanup checked once per minute while it is running. Driver tokens expire after 24 hours, dispatcher sessions after 12 hours.

Accounts are invited by phone number, then approved. A driver cannot read other drivers' positions or update another delivery. Approval withdrawal is checked server-side on every request. Tokens are random, hashed in the database and encrypted using Android Keystore on the phone.

This is a development MVP. Account recovery, production audit trails, push notifications, route history, customer tracking links, operational alerting and store distribution are not included. Review any deployment's access, costs and retention before using real fleet data.

## Build and checks

```sh
dotnet build backend -c Release
python3 tests/integration.py
# Gradle 8.11.1 and Android SDK 35 must be installed:
gradle -p android :app:assembleDebug :app:lintDebug
```

The GitHub Actions workflow runs these checks and uploads build artifacts. Its first run will establish compilation/test results for this reconstructed source.

Map rendering uses Leaflet, OpenStreetMap tiles and third-party script CDNs. These providers receive requests from the dashboard. Choose an appropriate map service and host audited dependencies locally for a production deployment.
