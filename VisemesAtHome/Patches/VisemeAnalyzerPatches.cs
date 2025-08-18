using System.Runtime.CompilerServices;
using HarmonyLib;
using FrooxEngine;
using OpenLipSync.Inference;
using OVRLipSyncContext = OpenLipSync.Inference.OVRCompat.OVRLipSyncContext;
using OVRLipSyncInterface = OpenLipSync.Inference.OVRCompat.OVRLipSyncInterface;

namespace VisemesAtHome.Patches;

[HarmonyPatch]
internal static class VisemeAnalyzerPatches
{
    // Optional override to choose a specific model path. Defaults to relative "model/model.onnx".
    public static string? ModelPathOverride { get; set; } = "rml_mods/model/model.onnx";

    // Per-context state (stores our compat OVR context)
    private static readonly ConditionalWeakTable<FrooxEngine.OVRLipSyncContext, AnalyzerState> State = [];

    private sealed class AnalyzerState : IDisposable
    {
        public OpenLipSyncBackend? Backend;
        public OVRLipSyncInterface? OvrInterface;
        public OVRLipSyncContext? Context;
        public bool Initialized => Context?.IsInitialized == true;

        public void Dispose()
        {
            try
            {
                Context?.Dispose();
                OvrInterface?.Dispose();
                Backend?.Dispose();
            }
            catch { /* ignore */ }
            Context = null;
            OvrInterface = null;
            Backend = null;
        }
    }

    // After Awake, ensure OVR path is disabled so it won't race our results
    [HarmonyPostfix]
    [HarmonyPatch(typeof(VisemeAnalyzer), "OnAwake")]
    public static void OnAwake_Postfix(ref FrooxEngine.OVRLipSyncContext ___analysisContext, Sync<float> ___Smoothing)
    {
        if (___analysisContext != null && !VisemesAtHomeMod.Force)
        {
            return;
        }

        // Best-effort dispose to avoid leaks
        try { ___analysisContext?.Dispose(); } catch { /* ignore */ }


        VahLog.Warn("Initializing OpenLipSync viseme analyzer");
        var st = new AnalyzerState();

        try
        {
            // Create new backend and interface (OVRLipSyncInterface handles initialization)
            string candidate = string.IsNullOrWhiteSpace(ModelPathOverride) ? "rml_mods/model/model.onnx" : ModelPathOverride;
            try
            {
                string abs = Path.GetFullPath(candidate);
                string cwd = Environment.CurrentDirectory;
                VahLog.Info($"Looking for OpenLipSync model at '{candidate}' (cwd='{cwd}', abs='{abs}')");
            }
            catch { /* path resolution/logging best-effort */ }
            Environment.SetEnvironmentVariable("OPENLIPSYNC_MODEL_PATH", candidate);

            int sampleRate = Engine.Current.AudioSystem.SampleRate; // Resonite audio sample rate, OpenLipSync will resample internally
            int bufferSamples = Engine.Current.AudioSystem.SimulationFrameSize; // Unused in practice, kept for interface signature stability

            st.Backend = new OpenLipSyncBackend();
            st.OvrInterface = new OVRLipSyncInterface(st.Backend, sampleRate, bufferSamples);

            if (!st.OvrInterface.IsInitialized)
            {
                VahLog.Error("Failed to initialize OpenLipSync backend/interface");
                st.Dispose();
                return;
            }
            st.Context = new OVRLipSyncContext(st.OvrInterface);
            if (!st.Context.IsInitialized)
            {
                VahLog.Error("Failed to create OVRLipSync context");
                st.Dispose();
                return;
            }

            // Create "fake" FrooxEngine OVRLipSyncContext to wrap ours from the VisemeAnalyzer’s perspective
            FrooxEngine.OVRLipSyncContext context = new(null);
            State.Add(context, st);
            context.Update(___Smoothing);
            ___Smoothing.OnValueChange += (smoothing) => context.Update(smoothing);
            ___analysisContext = context;

            VahLog.Info($"OpenLipSync context initialized (sr={sampleRate} Hz, bufferSize={bufferSamples})");
        }
        catch (Exception ex)
        {
            VahLog.Error("OpenLipSync analysis failed: " + ex.Message);
            st.Dispose();
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(FrooxEngine.OVRLipSyncContext), MethodType.Constructor, [typeof(FrooxEngine.OVRLipSyncInterface)])]
    public static bool Constructor_Prefix(FrooxEngine.OVRLipSyncInterface ovrLipSync, ref FrooxEngine.OVRLipSyncContext __instance)
    {
        if (ovrLipSync != null) {
            // Actually a real instanciation, let it do its job
            return true;
        }

        // Set context to non-zero value so that the VizemeAnalyzer understands it’s initialized
        AccessTools.FieldRefAccess<FrooxEngine.OVRLipSyncContext, uint>("context")(__instance) = 1;
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(FrooxEngine.OVRLipSyncContext), "Dispose")]
    public static bool Dispose_Prefix(FrooxEngine.OVRLipSyncContext __instance)
    {
        if (!State.TryGetValue(__instance, out var st))
        {
            // Actually an OVRLipSync instanciation, let it do its job
            return true;
        }

        st.Dispose();
        State.Remove(__instance);
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(FrooxEngine.OVRLipSyncContext), "Update")]
    public static bool Update_Prefix(float smoothing, FrooxEngine.OVRLipSyncContext __instance) {
        if (!State.TryGetValue(__instance, out var st))
        {
            // Actually an OVRLipSync instanciation, let it do its job
            return true;
        }

        // Use smoothing from component so user control applies; clamp to sane range
        st.Context?.Update(Math.Clamp(smoothing, 0f, 0.98f));
        return false;
    }

    // After audio update, process audio through OpenLipSync backend (handles resampling and model inference internally)
    [HarmonyPrefix]
    [HarmonyPatch(typeof(FrooxEngine.OVRLipSyncContext), "Analyze")]
    public static bool Analyze_Prefix(float[] audioData, float[] analysis, Action onDone, FrooxEngine.OVRLipSyncContext __instance)
    {
        if (!State.TryGetValue(__instance, out var st))
        {
            // Actually an OVRLipSync instanciation, let it do its job
            return true;
        }

        if (!VisemesAtHomeMod.Enabled || !st.Initialized || audioData == null || audioData.Length == 0 || analysis == null || analysis.Length < 16)
        {
            if (analysis != null && analysis.Length > 0)
            {
                // Set to silence
                Array.Clear(analysis);
                analysis[0] = 1;
            }
            onDone?.Invoke();
            return false;
        }

        try
        {
            st.Context.Analyze(audioData, analysis, onDone);

            // Model does not support Laughter; force zero for index 15.
            // if (analysis.Length > 15) analysis[15] = 0f;
        }
        catch (Exception ex)
        {
            VahLog.Error("OpenLipSync analysis failed: " + ex.Message);
            onDone?.Invoke();
        }

        return false;
    }
}


