# Agent research guidance

Human counterpart and annotated source table: [References](../human/REFERENCES.md).

## Primary entry points

- Dungeon Helper 4: https://dungeonhelper.com/ | SDK docs: https://sdk.dungeonhelper.com/
- Dungeon Helper 5 Beta: https://beta.dungeonhelper.com/ | SDK 5.3.0 docs: http://sdk.dungeonhelper.com/5.3.0/
- SDK NuGet package (`VoK.Sdk` `5.3.0`): https://www.nuget.org/packages/VoK.Sdk/5.3.0
- Open-source DH 5 reference plugin (`Aligner`): https://gitlab.com/VaultOfKundarak/aligner
- Windows capture exclusion & layered HUD APIs: `SetWindowDisplayAffinity`, `UpdateLayeredWindow`, Extended Window Styles on Microsoft Learn
- Agent Markdown convention: https://agents.md/

## Evidence rules

- Always inspect pinned binaries (`VoK.Sdk.dll` `4.2.1.0` + `DungeonHelper.dll` `4.2.0.146` for DH 4; `VoK.Sdk.dll` `5.3.0.0` + `DungeonHelper.dll` `5.0.858.0` for DH 5 Beta) rather than guessing API semantics.
- Key verified DH 5 (`5.0.858` / `5.3.0.0`) semantics:
  - `VoK.Sdk.IPlugin` (`[PluginId]`, `[PluginName]`, `[PluginIcon]`, `WindowVisible`, `ClientFrameStep`, `ImGui_Render`) replaces `IDdoPlugin` + `IPluginUI`.
  - `IEntityExtensions.LifeState(entity)` reads property `0x10000E0C` from `InstanceProperties`, requiring `await entity.UpdateProperties([0x10000E0Cu])` for live death/incapacitation updates.
  - `InvokeHotbar(bar, slot, targetId)` acts without dropping the player's current hard target.
  - `PluginTrustGate` requires Authenticode + `.pem` + `.manifest(.sig)` + DH-issuer-signed `.entitlement(.sig)` at archive root.
- Keep private keys (`*.key`, `*.pfx`), user logs, character names from live captures, and downloaded SDK binaries out of source control and release archives.
