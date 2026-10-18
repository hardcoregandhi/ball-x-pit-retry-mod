using RetryKeepsResources;

namespace RetryKeepsResources.Tests;

public class HaulTrackerTests
{
    // Indexed like the game's ResourceType: kGold, kWheat, kWood, kStone.
    static readonly int[] Haul = { 120, 5, 7, 0 };
    static readonly int[] Zero = { 0, 0, 0, 0 };
    const long Run = 1;

    static HaulTracker StartedRun(long runId = Run)
    {
        var t = new HaulTracker();
        t.FloorLoaded(runId, Zero);
        t.StateChanging(RunScreen.Active, runId, null);
        return t;
    }

    [Fact]
    public void RetryAfterGameOver_BanksTheHaul()
    {
        var t = StartedRun();
        t.Observe(Run, Haul);
        t.StateChanging(RunScreen.RunOver, Run, Haul);

        Assert.Equal(Haul, t.FloorLoading(FloorLoad.Retry));
    }

    [Fact]
    public void RunDataResetAfterRetryIsPressed_StillBanksTheFrozenHaul()
    {
        var t = StartedRun();
        t.StateChanging(RunScreen.RunOver, Run, Haul);

        // Retry pressed: the game wipes its run data and enters the level before laying it out.
        t.Observe(Run, Zero);
        t.StateChanging(RunScreen.Loading, Run, Zero);

        Assert.Equal(Haul, t.FloorLoading(FloorLoad.Retry));
    }

    [Fact]
    public void StateChangeToRunOver_TakesTheLatestAmounts()
    {
        var t = StartedRun();
        t.Observe(Run, new[] { 10, 0, 0, 0 });
        t.StateChanging(RunScreen.RunOver, Run, Haul);

        Assert.Equal(Haul, t.FloorLoading(FloorLoad.Retry));
    }

    [Fact]
    public void ReadingsFromAnotherRunObject_AreIgnored()
    {
        var t = StartedRun();
        t.Observe(Run, Haul);
        t.Observe(Run + 1, Zero);
        t.StateChanging(RunScreen.RunOver, Run + 1, Zero);

        Assert.Equal(Haul, t.FloorLoading(FloorLoad.Retry));
    }

    [Theory]
    [InlineData(FloorLoad.NewRun)]
    [InlineData(FloorLoad.Continue)]
    [InlineData(FloorLoad.Undo)]
    public void OtherLoads_NeverBank(FloorLoad load)
    {
        var t = StartedRun();
        t.StateChanging(RunScreen.RunOver, Run, Haul);

        Assert.Null(t.FloorLoading(load));
    }

    [Fact]
    public void HaulIsBankedOnlyOnce()
    {
        var t = StartedRun();
        t.StateChanging(RunScreen.RunOver, Run, Haul);

        Assert.NotNull(t.FloorLoading(FloorLoad.Retry));
        Assert.Null(t.FloorLoading(FloorLoad.Retry));
    }

    [Fact]
    public void NewRunAfterRetry_StartsFromItsOwnAmounts()
    {
        var t = StartedRun();
        t.StateChanging(RunScreen.RunOver, Run, Haul);
        t.FloorLoading(FloorLoad.Retry);

        t.FloorLoaded(Run + 1, Zero);
        t.StateChanging(RunScreen.Active, Run + 1, null);
        t.Observe(Run + 1, new[] { 3, 0, 0, 0 });
        t.StateChanging(RunScreen.RunOver, Run + 1, new[] { 3, 0, 0, 0 });

        Assert.Equal(new[] { 3, 0, 0, 0 }, t.FloorLoading(FloorLoad.Retry));
    }

    [Fact]
    public void EmptyHaul_BanksNothing()
    {
        var t = StartedRun();
        t.StateChanging(RunScreen.RunOver, Run, Zero);

        Assert.Null(t.FloorLoading(FloorLoad.Retry));
    }

    [Fact]
    public void NegativeAmounts_AreNotBanked()
    {
        var t = StartedRun();
        t.StateChanging(RunScreen.RunOver, Run, new[] { 50, -3, 0, 0 });

        Assert.Equal(new[] { 50, 0, 0, 0 }, t.FloorLoading(FloorLoad.Retry));
    }

    [Fact]
    public void RetryWithoutATrackedRun_BanksNothing()
    {
        var t = new HaulTracker();
        t.Observe(Run, Haul);
        t.StateChanging(RunScreen.RunOver, Run, Haul);

        Assert.Null(t.FloorLoading(FloorLoad.Retry));
    }

    [Fact]
    public void MidRunRestart_BanksByDefault()
    {
        var t = StartedRun();
        t.Observe(Run, Haul);
        t.StateChanging(RunScreen.Paused, Run, Haul);

        Assert.Equal(Haul, t.FloorLoading(FloorLoad.Retry));
    }

    [Fact]
    public void MidRunRestart_DoesNotBankWhenDisabled()
    {
        var t = StartedRun();
        t.BankMidRunRestarts = false;
        t.StateChanging(RunScreen.Paused, Run, Haul);

        Assert.Null(t.FloorLoading(FloorLoad.Retry));
    }

    [Fact]
    public void RetryAfterRunEnded_BanksEvenWhenMidRunRestartsDisabled()
    {
        var t = StartedRun();
        t.BankMidRunRestarts = false;
        t.StateChanging(RunScreen.RunOver, Run, Haul);
        t.StateChanging(RunScreen.Paused, Run, Zero);

        Assert.Equal(Haul, t.FloorLoading(FloorLoad.Retry));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RestartStraightIntoLoading_BanksOnlyWhenMidRunRestartsEnabled(bool bankMidRun)
    {
        var t = StartedRun();
        t.BankMidRunRestarts = bankMidRun;
        t.Observe(Run, Haul);

        // Restart without a pause/game-over state change first: Loading freezes without reading the run data.
        t.StateChanging(RunScreen.Loading, Run, Zero);
        Assert.True(t.IsFrozen);
        t.Observe(Run, Zero);

        var bank = t.FloorLoading(FloorLoad.Retry);
        if (bankMidRun)
            Assert.Equal(Haul, bank);
        else
            Assert.Null(bank);
    }

    [Fact]
    public void ResumingPlay_UnfreezesAndClearsRunEnded()
    {
        var t = StartedRun();
        t.StateChanging(RunScreen.RunOver, Run, Haul);
        t.StateChanging(RunScreen.Active, Run, null);

        Assert.False(t.IsFrozen);
        Assert.False(t.RunEnded);

        t.Observe(Run, new[] { 200, 5, 7, 0 });
        Assert.Equal(new[] { 200, 5, 7, 0 }, t.Haul);
    }
}
