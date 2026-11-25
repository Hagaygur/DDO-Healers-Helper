# Automatic GitHub builds and releases

Repository: [**`Hagaygur/DDO-Healers-Helper`**](https://github.com/Hagaygur/DDO-Healers-Helper) (for **DDO Healer's Helper**).

Developer entry points: [HealersHelper.sln](HealersHelper.sln), [human/agent documentation map](docs/README.md), and [AGENTS.md](AGENTS.md). Source release archives include the solution and editor configurations.

Toolchain: .NET 10 SDK builds .NET 8 targets for the pinned DH host. CI also installs .NET 8 to run compatibility tests. See [runtime/toolchain details](docs/human/DEVELOPMENT.md).

## Set up once

1. Create a GitHub repository named `DDO-Healers-Helper` and push this repository's contents to the repository root, including `.github/workflows/build.yml`.
2. Open **Actions -> Build, test and release** on GitHub.
3. The first successful run on the default branch publishes **v1.0.0** under **Releases**. Download `HealersHelper-1.0.0-plugin.zip` and install it directly through DH. Never install the `-source.zip` inside DH's plugin folder.

GitHub Actions and GitHub-hosted runners must be enabled. No personal access token, manually installed SDK, or extra secret is required; publication uses the repository's built-in token with `contents: write` in the `release` job.

## Triggers and outputs

Every branch push and pull request runs checks and produces downloadable Actions artifacts. Successful default-branch pushes and manual runs publish a tagged release only when plugin C# source (`src/**/*.cs`, including files directly under `src/`) differs from the last reachable stable release. Additions, edits, renames, and deletions count. Documentation, tests, CI/build scripts, project configuration, and metadata alone do not trigger publication or a version bump. Other branches and PRs do not publish or reserve release versions.

The comparison covers the entire unreleased range (`base..HEAD`), not just the last commit. With no previous release, tracked plugin source permits the initial `1.0.0` release.

Release assets:

- `HealersHelper-X.Y.Z-plugin.zip`: strictly `VoK.HealersHelper.dll`, `VoK.HealersHelper.deps.json`, and `VoK.HealersHelper.metadata` under `plugins/HealersHelper/`, with the DLL first.
- `HealersHelper-X.Y.Z-source.zip`: source, workflow, tests, and documentation, entirely separate.
- `contracts.json`: observed behavior used for the next comparison.
- `version.json`: chosen version, reason, baseline tag, and source commit.
- `SHA256SUMS.txt`: asset SHA-256 hashes.

## How versions are chosen

The first stable release is `1.0.0`. Later successful builds compare their observed `dist/contracts.json` with `contracts.json` from the previous reachable stable release (`v*`):

| Highest detected change | Result from 1.2.3 |
| --- | --- |
| Existing compatibility observation changed/removed, or a feature observation removed | `2.0.0` |
| New compatibility/feature observation, or changed feature observation | `1.3.0` |
| All observations unchanged | `1.2.4` |
| Any test fails | No release |

Conventional commits touching `src/**/*.cs` can raise (never lower) the bump: `fix:` -> patch, `feat:` -> minor, `feat!:` / `BREAKING CHANGE:` -> major. Documentation-only commit headers cannot trigger or escalate a release.

## Build and test stages

1. **Python unit tests** (`ci/test_version.py`, `ci/test_get_sdk_linux.py`): verifies contract classification, git release range rules, and pinned SDK hash/failure handling.
2. **Portable C# tests & contracts** (`tests/HealersHelper.Tests.csproj --contracts dist/contracts.json`): validates HP state machine, hysteresis, dead-alert timeout/recovery, single-player focus, hotkey matching/dedup, target selector guards, settings persistence, and banner presentation.
3. **Version selection** (`ci/version.py`): resolves next version and `publish` flag against the latest reachable stable release tag.
4. **Pinned SDK acquisition** (`ci/Get-Sdk.ps1` on Windows, `ci/get_sdk_linux.py` on Ubuntu): downloads `DungeonHelper.msi`, verifies its SHA-256 and `VoK.Sdk.dll` SHA-256 (`4.2.1.0`) from `ci/sdk-lock.json`, and extracts without committing SDK binaries.
5. **Debug & Release builds + output validation** (`ci/check_build_output.py`): verifies Debug keeps loose runtime files + PDB + ZIP and Release keeps only `HealersHelper-<version>-plugin.zip` (plus repeat Release build on Linux).
6. **Windows smoke harness** (`tests/WindowsSmoke/WindowsSmoke.csproj`): loads `VoK.HealersHelper.dll` directly from the Release ZIP, verifies assembly metadata/platform attributes, exercises `HealersHelperUi` and `SettingsDialog` at minimum sizes without clipping, and checks native banner rendering / extended window styles.
7. **Package & checksums** (`ci/package.py`): validates the 3-entry plugin ZIP, builds the source ZIP, and writes `dist/SHA256SUMS.txt`.
8. **Release** (`ci/release.py`): runs only when both `build` (Windows) and `linux` (Ubuntu) pass on the default branch with `publish == 'true'`.

The official MSI URL may change upstream; if so, the pinned hash check fails intentionally. Verify the new installer and SDK deliberately before updating `ci/sdk-lock.json`.

## Local commands

On Windows, with .NET 10 SDK, Python 3, and Dungeon Helper installed:

```powershell
py -3 -m unittest discover -s ci -p "test_*.py" -v
dotnet run --project tests/HealersHelper.Tests.csproj -c Release -- --contracts dist/contracts.json
.\build.ps1 -Version 1.0.0
# Nonstandard DH installation:
.\build.ps1 -DungeonHelperDir 'D:\Apps\Dungeon Helper' -Version 1.0.0
```

`build.ps1` stamps `src/AssemblyInfo.cs` and `src/VoK.HealersHelper.metadata` in the local working copy only when the target version differs; CI runs in a clean checkout.

## Verification note

GitHub-hosted workflow execution and release publication are unverified until this repository is pushed to GitHub with Actions enabled. Live DDO and Lossless Scaling behavior cannot be inferred from CI builds or synthetic smoke tests.

References: [GitHub .NET builds](https://docs.github.com/en/actions/tutorials/build-and-test-code/net), [workflow syntax and permissions](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax), [workflow triggers](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/trigger-a-workflow).
