# Agent task context

Human counterpart: [Background](../human/BACKGROUND.md). Repository instructions: [AGENTS.md](../../AGENTS.md).

## Required outcome

Maintain a dual-target **DDO Healer's Helper** plugin across **DH 4 (`VoK.Sdk 4.2.1.0`, `net8.0-windows`)** and **DH 5 Beta (`VoK.Sdk 5.3.0.0`, `net10.0` `x64` `ImGuiNET`)** sharing one portable core engine (`src/*.cs` linked into `src-dh5/` and `tests/HealersHelper.Tests.csproj`). Preserve DH 4 Lossless Scaling compatibility, exact pixel font settings, 5-rule target restoration, deduplicated hotkeys, 3 shared DDO × Material Design 3 themes (`UiTheme`), 3-state life/death triage, TempHP shields, DH 5 targetless `InvokeHotbar`, and strict DH 5 cryptographic trust-gate compliance.

## Verified DH 4 (`4.2.1.0`) & DH 5 Beta (`5.3.0.0`) SDK findings

- **Why dual ZIPs are mandatory:** DH 4 requires `plugins/HealersHelper/VoK.HealersHelper.{dll,deps.json,metadata}` on `net8.0-windows` (`IDdoPlugin` + `IPluginUI` WinForms); DH 5 Beta (`5.0.858`) refuses folder prefixes, runs in-process on `net10.0` (`Microsoft.NETCore.App` only, zero WinForms/WPF) via `VoK.Sdk.IPlugin` + `[PluginId]` + `ImGui_Render()`, moves `InstanceId`/`IsDeleted` to `ISimpleEntity`, replaces `IEntity.LifeState` with `IEntityExtensions.LifeState(this IEntity)` (`0x10000E0C`), removes `IPartyMember.Connected`, changes sync provider APIs to `*Async`, and enforces `PluginTrustGate`.
- **DH 4 (`4.2.1.0`):** `GetPartyMembers()` excludes self and caches getters; poll fresh each scan, merge self (`GetCurrentCharacterId()` / `GetCurrentCharacter()`), and enrich with `GetEntity(id)` for `LifeState` (`Undef=0`, `Dead=2`, `Incapacitated=3`, `Alive=4`) and `HitPoints_Temp`. `GetSelectedObjectId()` returns `(ulong?)0` when unselected.
- **DH 5 Beta (`5.3.0.0`):**
  - `DungeonHelper.dll` calls `ImGui_Render()` and `ClientFrameStep()` unconditionally every frame; `IPlugin.WindowVisible` must be explicitly overridden (default `false`).
  - `InTownAsync()`, `GetCurrentCharacterAsync()`, `GetEntityAsync()`, `GetEntitiesAsync()`, and `UpdateProperties(...)` marshal onto DDO's frame loop — never call `.Result`/`.GetAwaiter().GetResult()` inside `ClientFrameStep()`, `ImGui_Render()`, or `lock`.
  - `IEntityExtensions.LifeState()` reads `0x10000E0C` from `InstanceProperties`, which only hydrates on initial fetch unless `await entity.UpdateProperties([0x10000E0Cu, 0x10001868u, 0x10008250u])` is awaited in `TickAsync()` (hydrating `Death_LifeState`, `Death_SoulStoneID`, and `HitPoints_IncapacitatedBonus` together).
  - `eventProvider.Party` (`OnMemberConnectionChanged`, `OnMemberJoined`, `OnMemberLeft`, `OnDisbanded`) replaces removed `IPartyMember.Connected`.
  - `InvokeHotbar(uint bar, uint slot, ulong? targetId)` casts directly onto `targetId` without altering hard-target selection or mouselook.
  - `PluginTrustGate` requires Authenticode-signed DLL + `<PluginName>.pem` + `<PluginName>.entitlement(.sig)` + `<PluginName>.manifest(.sig)` at archive root (`PluginName = "Healers Helper"`, `PluginId = 54b48548-05ff-48cf-bce1-c6270e3d4a3f`, `Author = "Warned"`).

## Evidence ledger

- **Verified:** Tested on both DH 4 and DH 5 Beta up through `v1.5.1`, with later versions after `v1.5.1` tested on DH 5 only (`HealersHelper-1.5.1-dh4-plugin.zip` remains on Releases for DH 4 users). Stage 1 DH 4 IL/metadata/keymap checks (`VoK.Sdk 4.2.1.0`, `DungeonHelper.dll 4.2.0.146`); DH 5 Beta live in-game client load + security constraint validation (`VerifyDh5SecurityConstraints`, zero forbidden P/Invokes such as `GetProcAddress`) alongside empirical ALC cross-load + `TrustProbe` (`PluginTrustGate.Evaluate`, `AuthenticodeVerifier.IsSignatureValid`, `DungeonHelper.App` ZIP install against DH `5.0.858` / `VoK.Sdk 5.3.0.0`); 288 portable C# assertions (`HealersHelper.Tests`); 29 Python CI/packaging/signing/secret-hygiene/README-sync tests (`ci/test_*.py`); `WindowsSmoke` (4 suites); cross-platform `Dh5Smoke --require-signed` (11 checks); Ubuntu Linux cross-compile + `osslsigncode` + `Dh5Smoke` in GitHub Actions CI; live DH 4 DDO + Lossless Scaling gameplay on Windows 11.
- **Untested here:** Live Linux gameplay via Wine/Proton.
