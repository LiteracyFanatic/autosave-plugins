using System.Threading;

namespace InventorAutosave.Core.Logic;

internal enum AutosaveTriggerSource
{
    Auto,
    Manual,
}

internal sealed class SingleFlightGate
{
    private int _isRunning;

    public bool IsRunning => Volatile.Read(ref _isRunning) == 1;

    public bool TryEnter()
    {
        return Interlocked.CompareExchange(ref _isRunning, 1, 0) == 0;
    }

    public void Exit()
    {
        Interlocked.Exchange(ref _isRunning, 0);
    }
}
