using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace UnityPluginLoader;

internal static class CrashTrace
{
    private static readonly object Sync = new object();
    private static readonly string LogFilePath = BuildPath();

    public static string PathForDebug => LogFilePath;

    public static void Log(string source, string message)
    {
        WriteLine("INFO", source, message);
    }

    public static void LogException(string source, Exception ex)
    {
        WriteLine("ERROR", source, ex?.ToString() ?? "null exception");
    }

    private static string BuildPath()
    {
        try
        {
            string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OutbreakClientLoader");
            Directory.CreateDirectory(root);
            return System.IO.Path.Combine(root, "diagnostics.log");
        }
        catch
        {
            return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OutbreakClientLoader.Diagnostics.log");
        }
    }

    private static void WriteLine(string level, string source, string message)
    {
        try
        {
            string line = string.Format(
                "{0:O} [{1}] [PID:{2}] [TID:{3}] [Domain:{4}] [{5}] {6}{7}",
                DateTime.UtcNow,
                level,
                Process.GetCurrentProcess().Id,
                Thread.CurrentThread.ManagedThreadId,
                AppDomain.CurrentDomain.FriendlyName,
                source,
                message,
                Environment.NewLine);

            lock (Sync)
            {
                File.AppendAllText(LogFilePath, line);
            }
        }
        catch
        {
            // Best effort diagnostics only.
        }
    }
}
