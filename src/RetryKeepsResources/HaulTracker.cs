using System;
using System.Collections.Generic;

namespace RetryKeepsResources;

/// <summary>Why the game is about to lay a level out (mirrors the game's LoadMode).</summary>
public enum FloorLoad
{
    /// <summary>LoadMode.kNewGame: a new run started from the base.</summary>
    NewRun,
    /// <summary>LoadMode.kLoad: a saved run is being continued.</summary>
    Continue,
    /// <summary>LoadMode.kUndo: the current run is rewound.</summary>
    Undo,
    /// <summary>LoadMode.kResetFloor: Retry / Restart - the run is thrown away and started again.</summary>
    Retry,
}

/// <summary>How a game state (GameMgr.SetState) affects the run's haul.</summary>
public enum RunScreen
{
    /// <summary>The run is being played (kPlaying, kLevelUp, kPickTreasure, ...): the haul can still change.</summary>
    Active,
    /// <summary>kPaused: Restart can be pressed from here, so the haul is frozen.</summary>
    Paused,
    /// <summary>kGameOver / kEndingGame: the run is over and Retry can be pressed, so the haul is frozen.</summary>
    RunOver,
    /// <summary>kEnteringLvl: a level is being (re)built; the haul must not be read here.</summary>
    Loading,
}

/// <summary>
/// Game-independent bookkeeping for the resources gathered in the current run ("haul").
///
/// The haul is copied from the run's save data while the run is played. As soon as the game moves to a
/// screen Retry/Restart can be pressed from, the copy is frozen, so whatever the game does to its own run data
/// after the button is pressed (resetting it to zero) can't change what we bank. When the floor is then
/// reset, <see cref="FloorLoading"/> hands the frozen haul back exactly once.
/// </summary>
public sealed class HaulTracker
{
    int[] _haul = Array.Empty<int>();
    long _runId;
    bool _live;
    bool _frozen;
    bool _runEnded;

    /// <summary>Also bank when Restart is pressed in the middle of a run (pause menu), not only after it ended.</summary>
    public bool BankMidRunRestarts { get; set; } = true;

    public bool IsLive => _live;
    public bool IsFrozen => _frozen;
    public bool RunEnded => _runEnded;
    public int[] Haul => (int[])_haul.Clone();

    /// <summary>A level has been laid out: start tracking the run identified by <paramref name="runId"/>.</summary>
    public void FloorLoaded(long runId, IReadOnlyList<int> amounts)
    {
        _live = true;
        _runId = runId;
        _frozen = false;
        _runEnded = false;
        _haul = Copy(amounts);
    }

    /// <summary>Per-frame reading of the run's resources. Ignored while frozen or for another run's data.</summary>
    public void Observe(long runId, IReadOnlyList<int> amounts)
    {
        if (!_live || _frozen || runId != _runId || amounts == null)
            return;
        _haul = Copy(amounts);
    }

    /// <summary>Called just before the game changes state, with the run's resources at that moment.</summary>
    public void StateChanging(RunScreen screen, long runId, IReadOnlyList<int> amounts)
    {
        if (!_live)
            return;

        switch (screen)
        {
            case RunScreen.Active:
                _frozen = false;
                _runEnded = false;
                break;
            case RunScreen.Paused:
            case RunScreen.RunOver:
                Observe(runId, amounts); // last reading before freezing; no-op if already frozen
                _frozen = true;
                if (screen == RunScreen.RunOver)
                    _runEnded = true;
                break;
            case RunScreen.Loading:
                _frozen = true;
                break;
        }
    }

    /// <summary>
    /// The game is about to lay a level out. Returns the amounts to bank (indexed like the game's ResourceType)
    /// if this is a Retry of a tracked run with something in it, otherwise null. Tracking stops either way until
    /// <see cref="FloorLoaded"/>.
    /// </summary>
    public int[] FloorLoading(FloorLoad load)
    {
        int[] bank = null;
        if (load == FloorLoad.Retry && _live && (_runEnded || BankMidRunRestarts))
        {
            var positive = new int[_haul.Length];
            bool any = false;
            for (int i = 0; i < _haul.Length; i++)
            {
                positive[i] = Math.Max(0, _haul[i]);
                any |= positive[i] > 0;
            }
            if (any)
                bank = positive;
        }

        _live = false;
        _frozen = false;
        _runEnded = false;
        _haul = Array.Empty<int>();
        return bank;
    }

    static int[] Copy(IReadOnlyList<int> amounts)
    {
        if (amounts == null)
            return Array.Empty<int>();
        var copy = new int[amounts.Count];
        for (int i = 0; i < copy.Length; i++)
            copy[i] = amounts[i];
        return copy;
    }
}
