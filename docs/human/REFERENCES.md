# External references, SDK facts & HCI bibliography

External pages can change; the pinned binaries (`VoK.Sdk.dll` `4.2.1.0` + `DungeonHelper.dll` `4.2.0.146` for DH 4; `VoK.Sdk.dll` `5.3.0.0` + `DungeonHelper.dll` `5.0.858.0` for DH 5 Beta) and current code determine actual behavior.

## Platform & SDK references

| Resource | Where to read | How to use it |
| --- | --- | --- |
| DH 4 installation & FAQ | [Dungeon Helper](https://dungeonhelper.com/) | Host requirements and DH 4 plugin installation |
| DH 5 Beta portal | [Dungeon Helper Beta](https://beta.dungeonhelper.com/) | DH 5 release notes, DX10/11 rules, and trust-gate docs |
| DH 5 SDK 5.3.0 docs | [SDK 5.3.0 Reference](http://sdk.dungeonhelper.com/5.3.0/) | `IPlugin`, `IDdoGameDataProvider`, `InvokeHotbar`, `ImGuiNET` |
| SDK NuGet package | [VoK.Sdk 5.3.0](https://www.nuget.org/packages/VoK.Sdk/5.3.0) | Pinned DH 5 NuGet archive verified by `ci/sdk-lock.json` |
| DH 5 reference plugin | [Aligner on GitLab](https://gitlab.com/VaultOfKundarak/aligner) | Official DH 5 `net10.0` `x64` packaging and manifest reference |
| Capture exclusion & layered HUD | [SetWindowDisplayAffinity](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity) / [UpdateLayeredWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-updatelayeredwindow) | DH 4 `WDA_EXCLUDEFROMCAPTURE` (`0x11`) and 32bpp DIB rendering |
| Material Design 3 dark elevation | [M3 Elevation](https://m3.material.io/styles/elevation/overview) | 4-layer dark tonal surface hierarchy shared by all 3 `UiTheme` palettes |
| W3C WCAG 2.2 accessibility | [Three Flashes (2.3.1)](https://www.w3.org/WAI/WCAG22/Understanding/three-flashes-or-below-threshold.html) / [Target Size (2.5.8)](https://www.w3.org/WAI/WCAG22/Understanding/target-size-minimum.html) | `<= 2 Hz` bounded flash cadence, `>= 3:1` non-text contrast, and `>= 28 px` raid rows |

## Peer-reviewed Game HCI & UX citations

1. **Treisman, A. M., & Gelade, G. (1980).** *A feature-integration theory of attention.* Cognitive Psychology. https://doi.org/10.1016/0010-0285(80)90005-5
2. **Wickens, C. D., & Carswell, C. M. (1995).** *The Proximity Compatibility Principle: Its Psychological Foundation and Relevance to Display Design.* Human Factors. https://doi.org/10.1518/001872095779049408
3. **Edworthy, J., Loxley, S., & Dennis, I. (1991).** *Improving Auditory Warning Design: Relationship between Warning Sound Parameters and Perceived Urgency.* Human Factors, 33(2), 205–231. https://doi.org/10.1177/001872089103300206
4. **Edworthy, J., & Hellier, E. (2006).** *Alarms and human behaviour: implications for medical alarms.* British Journal of Anaesthesia, 97(1), 12–17. https://doi.org/10.1093/bja/ael114
5. **Bartram, L., Ware, C., & Calvert, T. W. (2003).** *Moticons: detection, distraction and task.* International Journal of Human-Computer Studies, 58(5), 515–545. https://doi.org/10.1016/S1071-5819(03)00021-1
6. **MacKenzie, I. S. (1992).** *Fitts' Law as a Research and Design Tool in Human-Computer Interaction.* Human–Computer Interaction. https://doi.org/10.1207/s15327051hci0701_3
7. **Seow, S. C. (2005).** *Information Theoretic Models of HCI: A Comparison of the Hick-Hyman Law and Fitts' Law.* Human–Computer Interaction. https://doi.org/10.1207/s15327051hci2003_3
8. **Fagerholt, E., & Lorentzon, M. (2009).** *Beyond the HUD – User Interfaces for Increased Player Immersion in FPS Games.* Chalmers University of Technology. https://odr.chalmers.se/items/d5fe6889-4cc6-49c2-ba56-0d759e2f37eb
9. **Peacocke, M., Teather, R. J., Carette, J., MacKenzie, I. S., & McArthur, V. (2018).** *An empirical comparison of first-person shooter information displays: HUDs, diegetic displays, and spatial representations.* Entertainment Computing, 26, 41–58. https://doi.org/10.1016/j.entcom.2018.01.003
10. **Okabe, M., & Ito, K. (2008).** *Color Universal Design (CUD): How to make figures and presentations that are friendly to colorblind people.* https://jfly.uni-koeln.de/color/

Agent counterpart: [Research guidance](../agents/REFERENCES.md).
