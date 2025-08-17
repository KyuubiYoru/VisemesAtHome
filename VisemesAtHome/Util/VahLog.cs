using System;
using Elements.Core; // UniLog

namespace VisemesAtHome;

internal static class VahLog
{
    // Toggle verbose logging via environment variable RESONITE_VAH_DEBUG=1
    public static bool DebugEnabled { get; private set; } =
        string.Equals(Environment.GetEnvironmentVariable("RESONITE_VAH_DEBUG"), "1", StringComparison.Ordinal);

    public static void Debug(string msg)
    {
        if (DebugEnabled) UniLog.Log("[VisemesAtHome][DBG] " + msg);
    }

    public static void Info(string msg)
    {
        UniLog.Log("[VisemesAtHome] " + msg);
    }

    public static void Warn(string msg)
    {
        UniLog.Warning("[VisemesAtHome][WARN] " + msg);
    }

    public static void Error(string msg)
    {
        UniLog.Error("[VisemesAtHome][ERR] " + msg);
    }
}


