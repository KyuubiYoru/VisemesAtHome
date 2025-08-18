using HarmonyLib;
using ResoniteModLoader;

namespace VisemesAtHome;

public class VisemesAtHomeMod : ResoniteMod
{
    public override string Name => "VisemesAtHome"; public override string Author => "KyuubiYoru";
    public override string Version => "0.1.2";
    public override string? Link => "https://github.com/KyuubiYoru/VisemesAtHome";

	private static ModConfiguration Config;

    public override void OnEngineInit()
    {
		Config = GetConfiguration();
		Config.Save(true);

        var harmony = new Harmony("dev.KyuubiYoru.VisemesAtHome");
        harmony.PatchAll();
    }

    [AutoRegisterConfigKey]
	public static readonly ModConfigurationKey<bool> EnabledKey = new("enabled", "Enable viseme generation with OpenLipSync if OVRLipSync is unavailable", () => true);

	[AutoRegisterConfigKey]
	public static readonly ModConfigurationKey<bool> ForceKey = new("force", "(Requires session rejoin) Force OpenLipSync viseme generation even if OVRLipSync is available, e.g. on Windows", () => false);

	public static bool Enabled => Config.GetValue(EnabledKey);

	public static bool Force => Config.GetValue(ForceKey);
}