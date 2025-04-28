using MelonLoader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public static class Logger
{
    private static bool debugEnabled = true;

    public static void Log(string message)
    {
        MelonLogger.Msg($"[INFO] -> {message}");
    }

    public static void LogWarning(string message)
    {
        if (Logger.debugEnabled)
        {
            MelonLogger.Warning($"[WARNING] -> {message}");
        }
    }

    public static void LogError(string message)
    {
        if (Logger.debugEnabled)
        {
            MelonLogger.Error($"[ERROR] -> {message}");
        }
    }
}