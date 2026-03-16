using System;
using System.Runtime.InteropServices;
using InventorAutosave.Services;
using Inventor;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace InventorAutosave;

[Guid("9DEF1247-47CE-4011-8065-FA0BC8E17110")]
[ProgId("InventorAutosave.StandardAddInServer")]
[ComVisible(true)]
public sealed class StandardAddInServer : ApplicationAddInServer
{
    private AutosaveAddInController? _controller;
    private ILoggerFactory? _loggerFactory;
    private ILogger<StandardAddInServer>? _logger;

    public void Activate(ApplicationAddInSite addInSiteObject, bool firstTime)
    {
        WriteStartupTrace($"Activate entered. FirstTime={firstTime}");

        try
        {
            try
            {
                _loggerFactory = AutosaveLogManager.CreateLoggerFactory(out var logPath);
                _logger = _loggerFactory.CreateLogger<StandardAddInServer>();
                WriteStartupTrace($"Logger factory created. LogPath={logPath ?? "unavailable"}");
                AutosaveLogManager.LogSessionStart(_logger, logPath);
            }
            catch (Exception ex)
            {
                WriteStartupTrace($"Logger initialization failed: {ex}");
                _loggerFactory = NullLoggerFactory.Instance;
                _logger = _loggerFactory.CreateLogger<StandardAddInServer>();
            }

            var application = addInSiteObject.Application;
            _logger.LogInformation("Add-in activation started. FirstTime {FirstTime}.", firstTime);
            _controller = new AutosaveAddInController(application, _loggerFactory);
            WriteStartupTrace("Controller constructed.");
            _controller.Initialize();
            WriteStartupTrace("Controller initialized successfully.");
            _logger.LogInformation("Add-in activation completed.");
        }
        catch (Exception ex)
        {
            WriteStartupTrace($"Activation failed: {ex}");
            _logger?.LogError(ex, "Add-in activation failed.");
            throw;
        }
    }

    public void Deactivate()
    {
        _logger?.LogInformation("Add-in deactivation started.");

        try
        {
            _controller?.Dispose();
            _controller = null;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            _logger?.LogInformation("Add-in deactivation completed.");
        }
        finally
        {
            _loggerFactory?.Dispose();
            _loggerFactory = null;
            _logger = null;
        }
    }

    public void ExecuteCommand(int commandID)
    {
    }

    public object Automation => this;

    private static void WriteStartupTrace(string message)
    {
        try
        {
            var logDirectory = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "InventorAutosave",
                "logs");
            System.IO.Directory.CreateDirectory(logDirectory);

            var tracePath = System.IO.Path.Combine(logDirectory, "startup-trace.log");
            System.IO.File.AppendAllText(
                tracePath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {message}{System.Environment.NewLine}");
        }
        catch
        {
        }
    }
}
