using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using MicrosoftLogger = Microsoft.Extensions.Logging.ILogger;

namespace InventorAutosave.Services;

internal static class AutosaveLogManager
{
    private const int MaxLogFilesToKeep = 14;
    private const string ProductFolderName = "InventorAutosave";

    public static ILoggerFactory CreateLoggerFactory(out string? logPath)
    {
        try
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                ProductFolderName,
                "logs");
            Directory.CreateDirectory(logDirectory);
            PruneOldLogs(logDirectory);

            var resolvedLogPath = Path.Combine(
                logDirectory,
                "inventor-autosave-.log");

            var logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.FromLogContext()
                .WriteTo.File(
                    resolvedLogPath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: MaxLogFilesToKeep,
                    shared: true,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            logPath = Path.Combine(
                logDirectory,
                $"inventor-autosave-{DateTime.Now:yyyyMMdd}.log");
            return new SerilogLoggerFactory(logger, dispose: true);
        }
        catch
        {
            logPath = null;
            return LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.None));
        }
    }

    public static void LogSessionStart(MicrosoftLogger logger, string? logPath)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        logger.LogInformation(
            "Logging initialized. Version {Version}. ProcessId {ProcessId}. Machine {MachineName}. User {UserName}. OS {OperatingSystem}. DotNet {DotNetVersion}. LogPath {LogPath}.",
            version,
            Environment.ProcessId,
            Environment.MachineName,
            Environment.UserName,
            Environment.OSVersion,
            Environment.Version,
            logPath ?? "unavailable");
    }

    private static void PruneOldLogs(string logDirectory)
    {
        try
        {
            var files = new DirectoryInfo(logDirectory)
                .GetFiles("inventor-autosave-*.log")
                .OrderByDescending(file => file.Name)
                .Skip(MaxLogFilesToKeep);

            foreach (var file in files)
            {
                file.Delete();
            }
        }
        catch
        {
        }
    }
}
