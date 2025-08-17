using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace VisemesAtHome;

internal static class OnnxNative
{
    private static bool _loaded;

    public static void EnsureLoaded()
    {
        if (_loaded) return;

        try
        {
            string libName = GetNativeLibName();
            if (string.IsNullOrEmpty(libName)) return;

            string? asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (asmDir == null) return;

            string[] candidates = new[]
            {
                Path.Combine(asmDir, libName),
                Path.Combine(asmDir, "onnx", libName),
                Path.Combine(AppContext.BaseDirectory, libName),
                Path.Combine(AppContext.BaseDirectory, "runtimes", GetRidFolder(), "native", libName)
            };

            foreach (var path in candidates)
            {
                if (!File.Exists(path))
                {
                    VahLog.Debug($"ONNX native candidate not found: {path}");
                    continue;
                }
                try
                {
                    NativeLibrary.Load(path);
                    _loaded = true;
                    VahLog.Info($"Loaded ONNX Runtime native library: {path}");
                    return;
                }
                catch (Exception ex)
                {
                    VahLog.Warn($"Failed to load ONNX native '{path}': {ex.Message}");
                }
            }

            VahLog.Warn("ONNX Runtime native library was not found. Place the native library next to the mod DLL or under 'onnx/' subfolder.");
        }
        catch (Exception ex)
        {
            VahLog.Warn("Error while resolving ONNX native library: " + ex.Message);
        }
    }

    private static string GetRidFolder()
    {
        if (OperatingSystem.IsLinux()) return "linux-x64";
        if (OperatingSystem.IsWindows()) return "win-x64";
        if (OperatingSystem.IsMacOS()) return "osx-x64";
        return string.Empty;
    }

    private static string GetNativeLibName()
    {
        if (OperatingSystem.IsLinux()) return "libonnxruntime.so";
        if (OperatingSystem.IsWindows()) return "onnxruntime.dll";
        if (OperatingSystem.IsMacOS()) return "libonnxruntime.dylib";
        return string.Empty;
    }
}


