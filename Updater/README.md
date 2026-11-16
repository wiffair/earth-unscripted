# Native update installer

The shared protocol in `Unity/Assets/EarthUnscripted/Updates` is used by the Unity
client and this native helper. The helper replaces the complete Standard/HD
package after the player exits. [Update operations](../Docs/UPDATES.md) describes
the signed catalogue and release process.

## Build

Use .NET SDK **10.0.404** and the platform's native AOT toolchain (Xcode command-line
tools on Mac; Visual Studio C++ build tools on Windows):

```sh
dotnet publish Updater/Installer/Installer.csproj -c Release -r osx-arm64 -o Updater/native/macOS-arm64
dotnet publish Updater/Tests/Tests.csproj -c Release -r osx-arm64 -o Updater/native/tests-mac
Updater/native/tests-mac/UpdaterTests
```

Use `win-x64` on Windows. The `Update installer` GitHub workflow builds the Windows
helper and chooser and runs the native tests. Its artifact is build input, not a
standalone game download. Copy the helper executable into `package_players.py`
with `--installer`; omit debug symbols and build intermediates.

The tests exercise semantic versions, signatures, tampering, replay, expiry,
artifact integrity, unsafe ZIPs, replacement and rollback after a failed probe.
Test keys and fake packages are isolated fixtures; the shipped helper always uses
the pinned production public key and has no command-line trust override.

For an exact-byte radio transfer proof:

```sh
python3 Tools/prepare_delta_proof.py /tmp/earth-radio-delta-proof
Updater/native/tests-mac/UpdaterTests --delta-proof /tmp/earth-radio-delta-proof
```

The in-game `-earthUnscriptedBetaUpdateSmoke` proof requires a fresh
`-earthUnscriptedBetaSmoke` token. It uses a disposable profile and disables live
update checks. It verifies live playlist adoption and settings persistence.

## Dependencies

The helper is a native AOT executable. Retain the .NET and Newtonsoft.Json notices
under `LICENSES/UpdateInstaller` in every packaged download. Original installer
and protocol code use MIT. Keep publisher signing keys outside the repository.
