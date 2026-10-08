# Microsoft authentication setup

This source adds Minecraft account tools to the existing Profile tab. It is not included in the published 0.7.0 installer. Do not publish an authentication release until a CodaLauncher app registration and live sign-in have been tested.

## Developer configuration

1. Register **CodaLauncher** as a Microsoft public client application supporting personal Microsoft accounts. Use your own developer account and application identity; do not borrow another launcher's client ID.
2. Enable the public client/device authorization flow. The implementation uses the consumer OAuth v2 endpoints with `XboxLive.signin offline_access`; there is no client secret in a desktop application.
3. Obtain access to the Minecraft authentication APIs for that application where required. A successful Microsoft/Xbox login does not by itself guarantee Minecraft API access. An HTTP 403 at the Minecraft exchange needs application/access investigation, not an ownership bypass.
4. Put the approved application/client ID in `auth/microsoft.json`. This identifier is public configuration, not a password. Empty or invalid configuration keeps sign-in disabled.
5. Test an owning account end to end and a nonowning account for denial. Test cancellation, expiry, account revocation, Microsoft/Xbox restrictions, and an interrupted connection.
6. Publish a new CodaLoader version containing `LaunchIdentity`, then publish a new launcher version. The launcher rejects older loaders instead of quietly launching their anonymous test identity. Bump the release versions only after this verification.

Microsoft documentation:
- https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-device-code
- https://learn.microsoft.com/en-us/entra/identity-platform/quickstart-register-app

## Behavior

The verification chain is Microsoft → Xbox Live → XSTS → Minecraft access token → Java entitlements → Minecraft profile. Only a complete successful chain saves an ownership record. Online launch refreshes and verifies again. Online verification has no automatic offline fallback.

Offline selection uses the same Minecraft name/UUID, supplies no usable online access token, skips launcher installation/repair, and uses cached Minecraft version metadata and installed files. Missing or corrupt files require an online repair. Previously verified ownership lasts 30 days for offline eligibility; subscription changes or authoritative account rejection invalidate the local record when checked online. Local settings and editable JSON never grant ownership.

Windows stores account credentials and verification timestamps using DPAPI CurrentUser with atomic replacement. Signing out deletes that credential cache but preserves worlds. Linux/macOS currently keep credentials in memory only: offline eligibility lasts for that launcher process, and the next launch needs Microsoft sign-in. Native Keychain/Secret Service integration is future work.

The loader independently checks the bearer token, Java entitlement, and profile before an online launch. Tokens are passed from launcher to loader in the child environment, never in the launcher command or WebView state; the loader removes the environment token before starting Minecraft. Minecraft itself requires its session token in its official launch arguments. Launcher output redacts that token. No refresh token is sent to CodaLoader.

`coda.offline=true` is a local mode hint for mods, not an authentication credential. Minecraft multiplayer/LAN controls are already removed by Coda menus. No shared-world/chat/bank/matchmaking authentication protocol exists in these repositories yet: every future remote service must verify a fresh online credential at its own boundary and refuse offline clients. Do not claim UI controls or environment flags enforce remote authorization.

## Verification

Run `dotnet run --project tests/accounts/AccountsTests.csproj -c Release`. Tests mock all account endpoints and cover entitlement denial, refresh revocation/rotation, expired/future ownership timestamps, network outage without fallback, stable offline UUID, sign-out, UI token redaction, and OS credential storage. CI runs them on Windows, Linux and macOS. Windows CI also tests the installer/UI. The loader has a separate identity validation test.

Mock tests and builds cannot replace live Microsoft sign-in with an approved application ID. The 0.7.0 release stays available while registration is pending.
