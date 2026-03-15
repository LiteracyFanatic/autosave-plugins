using System;
using System.Runtime.InteropServices;
using Inventor;

namespace InventorAutosave;

[Guid("9DEF1247-47CE-4011-8065-FA0BC8E17110")]
[ProgId("InventorAutosave.StandardAddInServer")]
[ComVisible(true)]
public sealed class StandardAddInServer : ApplicationAddInServer
{
    private AutosaveAddInController? _controller;

    public void Activate(ApplicationAddInSite addInSiteObject, bool firstTime)
    {
        var application = addInSiteObject.Application;
        _controller = new AutosaveAddInController(application);
        _controller.Initialize();
    }

    public void Deactivate()
    {
        _controller?.Dispose();
        _controller = null;
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    public void ExecuteCommand(int commandID)
    {
    }

    public object Automation => this;
}
