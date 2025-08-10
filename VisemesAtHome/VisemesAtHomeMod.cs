using HarmonyLib;
using ResoniteModLoader;

namespace VisemesAtHome;

public class VisemesAtHomeMod : ResoniteMod
{
    public override string Name => "VisemesAtHome";
    public override string Author => "KyuubiYoru";
    public override string Version => "0.1.0";
    public override string? Link => "https://github.com/KyuubiYoru/VisemesAtHome";


    public override void OnEngineInit()
    {
        var harmony = new Harmony("dev.KyuubiYoru.VisemesAtHome");
        harmony.PatchAll();
    }
}