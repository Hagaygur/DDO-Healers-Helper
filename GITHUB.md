# Automatic GitHub builds and releases

Repository: [**`Hagaygur/DDO-Healers-Helper`**](https://github.com/Hagaygur/DDO-Healers-Helper) (for **DDO Healer's Helper**).

Developer entry points: [HealersHelper.sln](HealersHelper.sln), [human/agent documentation map](docs/README.md), and [AGENTS.md](AGENTS.md). Source release archives include all five solution projects (`src/`, `src-dh5/`, `tests/`, `tests/WindowsSmoke/`, `tests/Dh5Smoke/`) and editor configurations.

Toolchain: .NET 10 SDK builds both the DH 4 `.NET 8 Windows` target (`VoK.Sdk 4.2.1.0`) and the DH 5 Beta `.NET 10 x64` target (`VoK.Sdk 5.3.0.0`). CI also installs .NET 8 to run DH 4 compatibility and `WindowsSmoke` tests. See [runtime/toolchain details](docs/human/DEVELOPMENT.md).

## Set up once

1. Push this repository to `Hagaygur/DDO-Healers-Helper` including `.github/workflows/build.yml` and the public community entitlement pair (`ci/Healers Helper.entitlement`, `ci/Healers Helper.entitlement.sig`).
2. Add the repository Actions secret **`DH_AUTHOR_KEY_B64`** containing the base64-encoded PKCS#8 author private key matching `authorKeyFingerprint` in `ci/Healers Helper.entitlement` (never commit `*.key` or `*.pfx` files).
3. Open **Actions -> Build, test and release** on GitHub. Successful default-branch runs publish tagged releases under **Releases**.

## Triggers and outputs

Every branch push and pull request runs checks and produces downloadable Actions artifacts. Successful default-branch pushes and manual runs publish a tagged release only when plugin C# source (`src/**/*.cs` or `src-dh5/**/*.cs`) differs from the last reachable stable release. Additions, edits, renames, and deletions count. Documentation, tests, CI/build scripts, project configuration, and metadata alone do not trigger publication or a version bump. Other branches and PRs do not publish or reserve release versions.

The comparison covers the entire unreleased range (`base..HEAD`), not just the last commit. With no previous release, tracked plugin source permits the initial `1.0.0` release. Untrusted fork PRs lacking `DH_AUTHOR_KEY_B64` validate unsigned DH 5 builds (`--allow-unsigned`), whereas publish/release builds fail unconditionally if DH 5 signing is missing.

Release assets:

- `HealersHelper-X.Y.Z-dh4-plugin.zip`: DH 4 runtime-only archive containing strictly `VoK.HealersHelper.dll`, `VoK.HealersHelper.deps.json`, and `VoK.HealersHelper.metadata` under `plugins/HealersHelper/`, DLL first.
- `HealersHelper-X.Y.Z-dh5-plugin.zip`: DH 5 Beta archive-root bundle containing the exact 8 signed files (`Healers Helper.entitlement`, `Healers Helper.entitlement.sig`, `Healers Helper.manifest`, `Healers Helper.manifest.sig`, `Healers Helper.pem`, `VoK.HealersHelper.deps.json`, `VoK.HealersHelper.dll`, `plugin.metadata`).
- `HealersHelper-X.Y.Z-source.zip`: source, workflow, tests, CI scripts, and documentation, entirely separate (never containing `*.key`, `*.pfx`, or downloaded SDK binaries).
- `contracts.json`: observed behavior used for the next comparison.
- `version.json`: chosen version, reason, baseline tag, and source commit.
- `SHA256SUMS.txt`: asset SHA-256 hashes covering both plugin ZIPs, source ZIP, `contracts.json`, and `version.json`.

## How versions are chosen

The first stable release is `1.0.0`. Later successful builds compare their observed `dist/contracts.json` with `contracts.json` from the previous reachable stable release (`v*`):

| Highest detected change | Result from 1.3.0 |
| --- | --- |
| Existing compatibility observation changed/removed, or a feature observation removed | `2.0.0` |
| New compatibility/feature observation, or changed feature observation | `1.4.0` |
| All observations unchanged | `1.3.1` |
| Any test fails | No release |

Conventional commits touching `src/**/*.cs` or `src-dh5/**/*.cs` can raise (never lower) the bump: `fix:` -> patch, `feat:` -> minor, `feat!:` / `BREAKING CHANGE:` -> major. Documentation-only commit headers cannot trigger or escalate a release.

## Build and test stages

1. **Python unit tests, secret hygiene & README sync guard** (`ci/test_version.py`, `ci/test_get_sdk_linux.py`): verifies contract classification, git release range rules across `src/` + `src-dh5/`, source package allowlist coverage, zero tracked/packaged `.key`/`.pfx`/private PEM files, dual pinned SDK hash/failure handling, and that `README.md` documents 100% of `src/Settings.cs` public properties, all 8 prestige ranks, `### Gamification options explainer`, and `## Release history`.
2. **Portable C# tests & contracts** (`tests/HealersHelper.Tests.csproj --contracts dist/contracts.json`): validates 3-state triage (Incapacitated > Living Low HP > Dead Soulstone), TempHP shields, session mute, raid keymap readiness, 3 `UiTheme` palettes, hysteresis, `PeopleSavedTracker`, `TargetRestoreTracker`, hotkey matching/dedup, settings persistence, `OfflineSettingsEngineView`, and banner + status HUD presentation.
3. **Version selection** (`ci/version.py`): resolves next version and `publish` flag against the latest reachable stable release tag.
4. **Dual pinned SDK acquisition** (`ci/Get-Sdk.ps1` on Windows, `ci/get_sdk_linux.py` on Ubuntu): verifies and extracts both DH 4 `DungeonHelper.msi` (`VoK.Sdk.dll` `4.2.1.0`) and DH 5 `vok.sdk.5.3.0.nupkg` (`VoK.Sdk.dll` `5.3.0.0`) from `ci/sdk-lock.json` without committing SDK binaries.
5. **Debug & Release builds + output validation** (`ci/check_build_output.py`): validates DH 4 (`src/bin/{Debug,Release}/net8.0-windows`) and DH 5 (`src-dh5/bin/x64/{Debug,Release}/net10.0`) build outputs on both Windows and Linux (plus repeat Release build on Linux).
6. **Ephemeral DH 5 Authenticode + Manifest signing** (`ci/sign_dh5.py`): decodes `DH_AUTHOR_KEY_B64` into an ephemeral temp directory (cleaned up in `finally:`), verifies SPKI fingerprint match against `ci/Healers Helper.entitlement`, Authenticode-signs `VoK.HealersHelper.dll` via Windows `signtool.exe` (ephemeral legacy-compatible PFX) or Linux `osslsigncode`, writes `Healers Helper.pem`, `plugin.metadata`, `.manifest`, and `.manifest.sig`, and produces `HealersHelper-<version>-dh5-plugin.zip`.
7. **Dual smoke harnesses**:
   - **Windows DH 4 smoke (`tests/WindowsSmoke/WindowsSmoke.csproj`)**: loads `VoK.HealersHelper.dll` from `*-dh4-plugin.zip`, exercises `HealersHelperUi` (`380px` and `720px`), `SettingsDialog` (3 `UiTheme` combos, `FallbackDeathHpNumeric`, `IncludeTempHpCheckBox`, `ShowBannerHpBarCheckBox`, `RaidKeymapReadinessLabel`, 6 color pickers, 8 hotkeys, dirty-tracking), and native `BannerHudWindow` + `StatusHudWindow` rendering and `WS_EX_*` styles.
   - **Cross-platform DH 5 smoke (`tests/Dh5Smoke/Dh5Smoke.csproj`, Windows + Linux)**: inspects `*-dh5-plugin.zip` (`--require-signed` verifying DH 5 `IssuerKeys`, SPKI fingerprint, `.manifest(.sig)` bidirectional SHA-256, and Windows `WinVerifyTrust`), scans all bundle assemblies with `VerifyDh5SecurityConstraints` (no `UnverifiableCodeAttribute`, 0/15 forbidden P/Invokes incl. `GetProcAddress`, 0/7 forbidden `TypeReference`s, 0/5 forbidden `MemberReference`s), verifies 0 WinForms/`System.Media` refs and 0 unresolved PE tokens in an isolated `AssemblyLoadContext` against `VoK.Sdk 5.3.0.0`, and exercises async `TickAsync` (`UpdateProperties([0x10000E0Cu, 0x10001868u, 0x10008250u])` live `LifeState`, Soulstone, and `HitPoints_IncapacitatedBonus`), `Party.OnMemberConnectionChanged`, targetless `InvokeHotbar`, `/hh` commands (incl. `/hh deathhp`), all 3 `UiTheme` switches, and `Dispose()`.
8. **Package & checksums** (`ci/package.py`): validates both plugin ZIPs, builds the source ZIP, and writes `dist/SHA256SUMS.txt`.
9. **Release** (`ci/release.py`): runs only when both `build` (Windows) and `linux` (Ubuntu) pass on the default branch with `publish == 'true'`.

## Local commands

On Windows, with .NET 10 SDK, Python 3, OpenSSL, Windows SDK `signtool.exe`, and Dungeon Helper 4 installed:

```powershell
py -3 -m unittest discover -s ci -p "test_*.py" -v
dotnet run --project tests/HealersHelper.Tests.csproj -c Release -- --contracts dist/contracts.json
.\build.ps1 -Version 1.5.1 -Dh5KeyFile C:\path\to\author.key
# Or unsigned local dev build for DH 5:
.\build.ps1 -Version 1.5.1 -AllowUnsignedDh5
```

`build.ps1` stamps `src/HealersHelper.csproj`, `src/AssemblyInfo.cs`, `src/VoK.HealersHelper.metadata`, and `src-dh5/HealersHelper.Dh5.csproj` in the local working copy only when the target version differs; CI runs in a clean checkout.

## Verification note

DDO Healer's Helper is tested on both DH 4 and DH 5 Beta up through `v1.5.1`, with later versions after `v1.5.1` tested on DH 5 only (`HealersHelper-1.5.1-dh4-plugin.zip` remains available on Releases for DH 4 users). GitHub-hosted workflow execution (`linux`, `build`, and `release`) is verified on `Hagaygur/DDO-Healers-Helper`, the DH 4 plugin is confirmed working on live DDO + Dungeon Helper + Lossless Scaling on Windows 11 (not guaranteed to work on any or all OS/GPU/display/capture configurations), and DH 5 Beta live in-game client load + security constraint validation is verified alongside `PluginTrustGate.Evaluate`, `AuthenticodeVerifier.IsSignatureValid`, `DungeonHelper.App` ZIP installation, and `Dh5Smoke` against DH `5.0.858` / `VoK.Sdk 5.3.0.0`, while live Linux/Wine gameplay remains untested.

References: [GitHub .NET builds](https://docs.github.com/en/actions/tutorials/build-and-test-code/net), [workflow syntax and permissions](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax), [workflow triggers](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/trigger-a-workflow).
