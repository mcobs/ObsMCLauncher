using System;
using System.Diagnostics;

namespace ObsMCLauncher.Core.Utils;

public static class DebugLogger
{
    private static readonly object _lock = new();

    public static void Info(string serviceName, string message)
    {
        Log("INFO", serviceName, message);
    }

    public static void Warn(string serviceName, string message)
    {
        Log("WARN", serviceName, message);
    }

    public static void Error(string serviceName, string message)
    {
        Log("ERROR", serviceName, message);
    }

    public static void Info(string serviceName, string operation, string message)
    {
        Log("INFO", serviceName, $"{operation} - {message}");
    }

    public static void Warn(string serviceName, string operation, string message)
    {
        Log("WARN", serviceName, $"{operation} - {message}");
    }

    public static void Error(string serviceName, string operation, string message)
    {
        Log("ERROR", serviceName, $"{operation} - {message}");
    }

    private static void Log(string level, string serviceName, string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var logMessage = $"[{timestamp}] [{level}] [{serviceName}] {message}";
        
        lock (_lock)
        {
            Debug.WriteLine(logMessage);
        }
    }
}
