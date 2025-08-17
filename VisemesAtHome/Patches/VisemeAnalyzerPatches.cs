using System;
using System.IO;
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

    // Per-instance state (stores our compat OVR context)
    private static readonly ConditionalWeakTable<VisemeAnalyzer, AnalyzerState> State = new();

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

    // Private fields on FrooxEngine.VisemeAnalyzer
    private static readonly AccessTools.FieldRef<VisemeAnalyzer, object?> f_analysisContext = AccessTools.FieldRefAccess<VisemeAnalyzer, object?>("analysisContext");
    private static readonly AccessTools.FieldRef<VisemeAnalyzer, float[]?> f_buffer          = AccessTools.FieldRefAccess<VisemeAnalyzer, float[]>("buffer");
    private static readonly AccessTools.FieldRef<VisemeAnalyzer, float[]?>  f_analysis        = AccessTools.FieldRefAccess<VisemeAnalyzer, float[]>("analysis");
    private static readonly AccessTools.FieldRef<VisemeAnalyzer, bool>     f_hasRemoteSource = AccessTools.FieldRefAccess<VisemeAnalyzer, bool>("_hasRemoteSource");
    private static readonly AccessTools.FieldRef<VisemeAnalyzer, Sync<float>> f_smoothing     = AccessTools.FieldRefAccess<VisemeAnalyzer, Sync<float>>("Smoothing");

    // After Awake, ensure OVR path is disabled so it won't race our results
    [HarmonyPostfix]
    [HarmonyPatch(typeof(VisemeAnalyzer), "OnAwake")]
    private static void OnAwake_Postfix(VisemeAnalyzer __instance)
    {
        try
        {
            var ctx = f_analysisContext(__instance);
            if (ctx != null)
            {
                // Best-effort dispose to avoid leaks
                try { (ctx as IDisposable)?.Dispose(); } catch { /* ignore */ }
                f_analysisContext(__instance) = null;
                VahLog.Info("Disabled OVRLipSyncContext for VisemeAnalyzer instance.");
            }
        }
        catch (Exception ex)
        {
            VahLog.Warn("Failed to disable OVRLipSyncContext: " + ex.Message);
        }

        // State created lazily on first audio update
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(VisemeAnalyzer), "OnDispose")]
    private static void OnDispose_Postfix(VisemeAnalyzer __instance)
    {
        if (State.TryGetValue(__instance, out var st))
        {
            st.Dispose();
            State.Remove(__instance);
        }
    }

    // After audio update, process audio through OpenLipSync backend (handles resampling and model inference internally)
    [HarmonyPostfix]
    [HarmonyPatch(typeof(VisemeAnalyzer), "OnAudioUpdate")]
    private static void OnAudioUpdate_Postfix(VisemeAnalyzer __instance)
    {
        try
        {
            // Respect remote source: do not compute when driven by someone else
            if (f_hasRemoteSource(__instance))
                return;

            var buf = f_buffer(__instance);
            var ana = f_analysis(__instance);
            if (buf == null || buf.Length == 0 || ana == null || ana.Length < 16)
                return;

            var st = State.GetValue(__instance, _ => new AnalyzerState());

            
            int sampleRate = 48000; // Resonite audio sample rate, OpenLipSync will resample internally
            int bufferSamples = buf.Length;

            // Read smoothing from component so user control applies; clamp to sane range
            float smooth = 0.65f;
            try { smooth = Math.Clamp(f_smoothing(__instance).Value, 0f, 0.98f); } catch { }

            // Create context on first use
            if (!st.Initialized)
            {
                // Dispose existing context
                st.Context?.Dispose();
                st.OvrInterface?.Dispose();
                st.Backend?.Dispose();

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
                st.Backend = new OpenLipSyncBackend();
                st.OvrInterface = new OVRLipSyncInterface(st.Backend, sampleRate, bufferSamples);
                
                if (!st.OvrInterface.IsInitialized)
                {
                    VahLog.Error("Failed to initialize OpenLipSync backend/interface");
                    return;
                }
                st.Context = new OVRLipSyncContext(st.OvrInterface);
                if (!st.Context.IsInitialized)
                {
                    VahLog.Error("Failed to create OVRLipSync context");
                    return;
                }
                VahLog.Info($"OpenLipSync context initialized (sr={sampleRate} Hz, bufferSize={bufferSamples})");
            }

            // Update context smoothing and process audio
            st.Context.Update(smooth);
            
            st.Context.Analyze(buf, ana, null);

            // Model does not support Laughter; force zero for index 15.
            // if (ana.Length > 15) ana[15] = 0f;

        }
        catch (Exception ex)
        {
            VahLog.Error("OpenLipSync analysis failed: " + ex.Message);
        }
    }

}


