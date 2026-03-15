using InventorAutosave.Core.Logic;
using Xunit;

namespace InventorAutosave.Core.Tests;

public class SingleFlightGateTests
{
    [Fact]
    public void TryEnter_ReturnsFalseWhenAlreadyRunning()
    {
        var gate = new SingleFlightGate();

        Assert.True(gate.TryEnter());
        Assert.False(gate.TryEnter());
        Assert.True(gate.IsRunning);
    }

    [Fact]
    public void Exit_AllowsAnotherRun()
    {
        var gate = new SingleFlightGate();

        Assert.True(gate.TryEnter());
        gate.Exit();

        Assert.False(gate.IsRunning);
        Assert.True(gate.TryEnter());
    }
}
