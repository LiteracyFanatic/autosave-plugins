using System;
using System.Runtime.InteropServices;
using InventorAutosave.Services;
using Inventor;
using Microsoft.Extensions.Logging;

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
        _loggerFactory = AutosaveLogManager.CreateLoggerFactory(out var logPath);
        _logger = _loggerFactory.CreateLogger<StandardAddInServer>();
        AutosaveLogManager.LogSessionStart(_logger, logPath);

        try
        {
            var application = addInSiteObject.Application;
            _logger.LogInformation("Add-in activation started. FirstTime {FirstTime}.", firstTime);
            _controller = new AutosaveAddInController(application, _loggerFactory);
            _controller.Initialize();
            _logger.LogInformation("Add-in activation completed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Add-in activation failed.");
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
}
