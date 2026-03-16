using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Events;
using MicrosoftLogger = Microsoft.Extensions.Logging.ILogger;

namespace InventorAutosave.Services;

internal static class AutosaveLogManager
{
    private const int MaxLogFilesToKeep = 14;
    private const string ProductFolderName = "InventorAutosave";

    public static string GetBuildVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        var gitTag = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, "GitTag", StringComparison.OrdinalIgnoreCase))?
            .Value;
        if (!string.IsNullOrWhiteSpace(gitTag))
        {
            return gitTag;
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }

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
            return new SerilogLoggerFactoryAdapter(logger);
        }
        catch
        {
            logPath = null;
            return NullLoggerFactory.Instance;
        }
    }

    public static void LogSessionStart(MicrosoftLogger logger, string? logPath)
    {
        var version = GetBuildVersion();
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

internal sealed class SerilogLoggerFactoryAdapter : ILoggerFactory
{
    private readonly Serilog.ILogger _logger;
    private bool _disposed;

    public SerilogLoggerFactoryAdapter(Serilog.ILogger logger)
    {
        _logger = logger;
    }

    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new SerilogLoggerAdapter(_logger.ForContext("SourceContext", categoryName));
    }

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        (_logger as IDisposable)?.Dispose();
        _disposed = true;
    }
}

internal sealed class SerilogLoggerAdapter : Microsoft.Extensions.Logging.ILogger
{
    private readonly Serilog.ILogger _logger;

    public SerilogLoggerAdapter(Serilog.ILogger logger)
    {
        _logger = logger;
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
        return NoopScope.Instance;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel != LogLevel.None && _logger.IsEnabled(MapLogLevel(logLevel));
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        if (string.IsNullOrWhiteSpace(message) && exception == null)
        {
            return;
        }

        var logger = _logger;
        if (eventId.Id != 0)
        {
            logger = logger.ForContext("EventId", eventId.Id);
        }

        if (!string.IsNullOrWhiteSpace(eventId.Name))
        {
            logger = logger.ForContext("EventName", eventId.Name);
        }

        logger.Write(MapLogLevel(logLevel), exception, "{Message}", message);
    }

    private static LogEventLevel MapLogLevel(LogLevel logLevel)
    {
        return logLevel switch
        {
            LogLevel.Trace => LogEventLevel.Verbose,
            LogLevel.Debug => LogEventLevel.Debug,
            LogLevel.Information => LogEventLevel.Information,
            LogLevel.Warning => LogEventLevel.Warning,
            LogLevel.Error => LogEventLevel.Error,
            LogLevel.Critical => LogEventLevel.Fatal,
            _ => LogEventLevel.Information,
        };
    }

    private sealed class NoopScope : IDisposable
    {
        public static NoopScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
