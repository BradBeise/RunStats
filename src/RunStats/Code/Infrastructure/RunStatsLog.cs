using System;
using MegaCrit.Sts2.Core.Logging;

namespace RunStats.Infrastructure;

public static class RunStatsLog
{
    private const string Prefix = "[RunStats] ";

    public static void Debug(string message)
    {
        if (Configuration.RunStatsConfig.EnableDebugLogging)
        {
            Log.Debug(Prefix + message);
        }
    }

    public static void Info(string message) => Log.Info(Prefix + message);

    public static void Warn(string message) => Log.Warn(Prefix + message);

    public static void Error(string message, Exception? exception = null)
    {
        var detail = exception is null ? message : $"{message}{Environment.NewLine}{exception}";
        Log.Error(Prefix + detail);
    }
}
