# Put the test system online and configure SMS

## What needs your account

The Android app, backend and dashboard are separate from the old private ChatGPT Site demo. Use this .NET service URL in the Android app. Publishing requires a hosting account and a source repository (or a container registry). SMS requires your own Twilio account. Neither cloud resources nor SMS credentials are included in the download.

The supplied Render Blueprint creates a paid Docker web service with a 1 GB persistent disk. Review the displayed price in your Render account before creating it. The SQLite database must survive restarts. Do not deploy this database on an ephemeral filesystem and assume accounts will persist.

## Deploy with Render

1. Put the project contents in a new GitHub repository. Keep `render.yaml` at the repository root; never commit credentials.
2. Connect your Render account here if you want the assistant to perform deployment. Otherwise sign into Render and choose **New → Blueprint**, connect the repository and review the proposed service and disk.
3. Set the requested environment variables. Choose your own dispatcher email and a new password of at least 16 characters. Use the three Twilio values described below.
4. Deploy. The health check is `/health`. The service's HTTPS address is both the dispatcher dashboard address and the API base URL for the Android app.
5. Confirm `/health` returns `{"status":"ok"}`, then sign into the dashboard. Add two driver phone numbers and approve them.
6. On each Android phone, enter that HTTPS address, verify the driver's number and start a delivery.

Render provides TLS and redirects public HTTP to HTTPS. `Hosting__HttpsTerminatedAtEdge=true` prevents an extra application redirect at the internal HTTP hop. Use this setting ONLY behind a host that enforces HTTPS. It does not change the Android HTTPS requirement or disable secure cookies.

When using another proxy, configure `ReverseProxy__Address` only for its exact trusted address. Do not indiscriminately trust forwarded headers. A shared proxy address can make the per-IP authentication throttle apply to several drivers until trusted forwarding is configured; adjust this during hosting verification, not by removing throttling.

If the mounted database directory is not writable by the image's `app` user, fix the volume ownership for that user during host setup. Do not make it world-writable. Cloud startup and volume permissions must be verified at deployment.

## Twilio Verify setup

1. Sign into https://console.twilio.com/ and open **Verify → Services**.
2. Create a Verify service named `Dispatch Live`, with **SMS** as the verification channel. Copy the Verify Service SID (starts with `VA`).
3. From your Twilio account dashboard, copy the Account SID (starts with `AC`) and Auth Token.
4. In **Verify → Settings → Geo permissions**, enable the destination countries you intend to test, including Sri Lanka for `+94` numbers.
5. If using a trial account, verify each test recipient number with Twilio first. Trial limits and account restrictions can prevent SMS even when the code is correct.
6. Put these values in your backend hosting service's secret/environment settings:

| Key | Value |
|---|---|
| `Otp__AccountSid` | Your `AC…` Account SID |
| `Otp__AuthToken` | Your Twilio Auth Token (secret) |
| `Otp__ServiceSid` | Your `VA…` Verify Service SID |
| `Otp__DevelopmentCodes` | `false` |
| `ASPNETCORE_ENVIRONMENT` | `Production` |

Never place the Auth Token in the Android app, GitHub repository or chat. Restart/redeploy the backend after setting the secrets.

7. Add and approve the test driver's international-format number in your dispatcher dashboard, such as `+9477…`.
8. On the Android app, request a code and verify the received SMS. The backend sends the verification request to Twilio; the app never holds Twilio credentials.
9. If SMS does not arrive, check Twilio Verify logs, recipient verification, country permissions and available account balance. Use Twilio Verify logs to diagnose the provider error; the app intentionally shows a generic provider failure.

Verify manages the OTP sender; this implementation does not require you to enter an SMS 'From' number. Twilio charges and destination availability depend on your account and destination. Confirm them before testing a fleet.

## Test without SMS charges first

Run locally with `ASPNETCORE_ENVIRONMENT=Development` and `Otp__DevelopmentCodes=true`. The backend terminal prints a random one-time code for invited driver numbers. This is deliberately disabled in Production. Never expose developer OTP logs publicly.

## References

- Twilio Verify: https://www.twilio.com/docs/verify
- Verify services: https://www.twilio.com/docs/verify/api/service
- Verify country permissions: https://www.twilio.com/docs/verify/preventing-toll-fraud/verify-geo-permissions
- Verify trial restrictions: https://www.twilio.com/docs/usage/trials/try-out-verify
- Render Docker: https://render.com/docs/docker
- Persistent disks: https://render.com/docs/disks
- Render TLS: https://render.com/docs/tls
