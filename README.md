## VisemesAtHome

# Non-functional WIP

Resonite mod that enables mic-driven visemes on Linux (.NET 9).
Provides a workaround since OVRLipSync has no Linux build.

Install:
- Build: `dotnet build -c Release`
- Copy `./VisemesAtHome/bin/Release/net9.0/VisemesAtHome.dll` → `~/.local/share/Steam/steamapps/common/Resonite/rml_mods/`
- Launch with `-LoadAssembly Libraries/ResoniteModLoader.dll`

Requires: Resonite pre-release (.NET 9), ResoniteModLoader, Harmony (0Harmony-Net9).

Link: [ResoniteModLoader releases](https://github.com/resonite-modding-group/ResoniteModLoader/releases)