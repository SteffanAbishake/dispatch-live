# Validation status — 7 October 2026 (Asia/Colombo)

Completed against the source in this package:
- .NET SDK 10.0.401: Release compilation, zero warnings and errors.
- `tests/integration.py`: PASS for OTP single-use, approval, two independent drivers, role and company isolation, explicit consent, GPS validation, stale detection, sequence ordering, stop, coordinate clearing, suspension and logout.
- JDK 17 / Gradle 8.11.1 / AGP 8.9.1 / SDK 35: `:app:assembleDebug :app:lintDebug` passed.
- Android lint: zero errors; four advisory warnings (synchronous preference persistence, backup configuration, and untranslated UI text).
- Android build-tools 35.0.0 `apksigner verify --verbose`: signature verified with APK Signature Scheme v2, one signer.
- Dashboard JavaScript syntax and Android XML/component reference checks.

Still pending:
- APK installation and physical-device app-switch, lock-screen, force-stop, offline-stop and battery tests. Compilation alone does not verify these behaviors.
- Real Twilio SMS delivery. Integration tests use local development OTPs.
- Durable public cloud deployment, including mounted-volume permissions and HTTPS smoke tests.

The APK is debug-signed for testing, not a Play Store release. Use the device checklist before using this with a delivery fleet. Render deployment configuration and SMS setup instructions are supplied in `docs/HOSTING-AND-SMS.md`.
