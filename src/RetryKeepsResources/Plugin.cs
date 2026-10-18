using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace RetryKeepsResources;

// Retry Keeps Resources: pressing Retry (game over / level end) or Restart (pause menu) normally throws away
// the gold, wheat, wood and stone gathered in the run. This plugin banks them into your save instead.
//
// How:
// - The run's haul is BattleSaveData.I.NumResources (a Cost; Num[] is indexed by ResourceType). It is copied every
//   frame and frozen as soon as the game reaches a screen Retry/Restart can be pressed from (see HaulTracker).
// - Retry/Restart lays the level out again through GridMgr.InitGrid(LoadMode.kResetFloor); a new run from the base
//   uses kNewGame instead (and the game banks that run's haul itself), so only kResetFloor banks anything.
// - The haul is added on the next frame with the game's own SaveMgr.AddResources(..., saveGame: true).
//   SaveMgr's resource methods are only ever CALLED, never Harmony-patched: patching them is known to freeze the game.
[BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
public class Plugin : BasePlugin
{
    internal static ManualLogSource Logger;
    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<bool> BankMidRunRestarts;
    internal static readonly HaulTracker Tracker = new();

    static int[] _pendingBank;

    public override void Load()
    {
        Logger = Log;
        Enabled = Config.Bind("General", "Enabled", true,
            "Bank the resources gathered in a run when you press Retry/Restart instead of losing them.");
        BankMidRunRestarts = Config.Bind("General", "BankMidRunRestarts", true,
            "Also bank when you Restart from the pause menu in the middle of a run. " +
            "false = only bank when Retry is pressed after the run has ended (game over or level complete).");

        ClassInjector.RegisterTypeInIl2Cpp<RetryKeepsResourcesBehaviour>();
        AddComponent<RetryKeepsResourcesBehaviour>();

        try
        {
            new Harmony(PluginInfo.Guid).PatchAll(typeof(Plugin).Assembly);
        }
        catch (Exception ex)
        {
            Logger.LogError("Patching failed, the mod is inactive (game update?): " + ex);
            return;
        }

        Logger.LogInfo($"{PluginInfo.Name} {PluginInfo.Version} loaded.");
    }

    /// <summary>The current run's identity and resources, or (0, null) when there is no run.</summary>
    internal static (long RunId, int[] Amounts) ReadRun()
    {
        var battle = BattleSaveData.I;
        if (battle == null)
            return (0, null);
        var num = battle.NumResources?.Num;
        if (num == null)
            return (battle.Pointer.ToInt64(), null);
        var amounts = new int[num.Length];
        for (int i = 0; i < amounts.Length; i++)
            amounts[i] = num[i];
        return (battle.Pointer.ToInt64(), amounts);
    }

    internal static void QueueBank(int[] amounts)
    {
        if (_pendingBank == null)
        {
            _pendingBank = amounts;
            return;
        }
        if (_pendingBank.Length < amounts.Length)
            Array.Resize(ref _pendingBank, amounts.Length);
        for (int i = 0; i < amounts.Length; i++)
            _pendingBank[i] = (int)Math.Min(int.MaxValue, (long)_pendingBank[i] + amounts[i]);
    }

    /// <summary>Adds the queued haul to the save once SaveMgr is available. Called every frame.</summary>
    internal static void BankPending()
    {
        if (_pendingBank == null)
            return;
        var save = SaveMgr.I;
        if (save == null)
            return;

        var amounts = _pendingBank;
        _pendingBank = null;

        var parts = new System.Collections.Generic.List<string>();
        for (int i = 0; i < amounts.Length; i++)
        {
            var type = (ResourceType)i;
            if (amounts[i] <= 0 || type == ResourceType.kNum || !Enum.IsDefined(typeof(ResourceType), type))
                continue;
            try
            {
                // The game stores resources as Int32: never add past int.MaxValue (it would wrap negative).
                long current = type == ResourceType.kGold ? save.GetNumGold() : save.GetNumResources(type);
                int amount = (int)Math.Min(amounts[i], int.MaxValue - Math.Clamp(current, 0L, int.MaxValue));
                if (amount <= 0)
                    continue;
                save.AddResources(type, amount, true, false);
                parts.Add($"{amount} {type}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Could not bank {amounts[i]} {type}: {ex}");
            }
        }

        if (parts.Count > 0)
            Logger.LogInfo("Retry: banked " + string.Join(", ", parts) + " from the run you left.");
    }
}

internal static class PluginInfo
{
    public const string Guid = "hardcoregandhi.ballxpit.retrykeepsresources";
    public const string Name = "Retry Keeps Resources";
    public const string Version = "1.0.0";
}

/// <summary>Per-frame: keeps the haul copy up to date and banks queued hauls.</summary>
public class RetryKeepsResourcesBehaviour : MonoBehaviour
{
    public RetryKeepsResourcesBehaviour(IntPtr ptr) : base(ptr) { }

    void Update()
    {
        try
        {
            if (Plugin.Tracker.IsLive && !Plugin.Tracker.IsFrozen)
            {
                var (runId, amounts) = Plugin.ReadRun();
                Plugin.Tracker.Observe(runId, amounts);
            }
            Plugin.BankPending();
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError("Update failed: " + ex);
        }
    }
}

/// <summary>Freezes the haul when the game reaches a screen Retry/Restart can be pressed from.</summary>
[HarmonyPatch(typeof(GameMgr), nameof(GameMgr.SetState))]
internal static class SetStatePatch
{
    static void Prefix(GameState __0)
    {
        try
        {
            var screen = __0 switch
            {
                GameState.kPaused => RunScreen.Paused,
                GameState.kGameOver or GameState.kEndingGame => RunScreen.RunOver,
                GameState.kEnteringLvl => RunScreen.Loading,
                _ => RunScreen.Active,
            };
            var (runId, amounts) = screen is RunScreen.Paused or RunScreen.RunOver ? Plugin.ReadRun() : (0, null);
            Plugin.Tracker.StateChanging(screen, runId, amounts);
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError("SetState hook failed: " + ex);
        }
    }
}

/// <summary>Banks the frozen haul when the level is laid out again for a Retry/Restart, then tracks the new run.</summary>
[HarmonyPatch(typeof(GridMgr), "InitGrid", typeof(LoadMode))]
internal static class InitGridPatch
{
    static void Prefix(LoadMode __0)
    {
        try
        {
            var load = __0 switch
            {
                LoadMode.kResetFloor => FloorLoad.Retry,
                LoadMode.kLoad => FloorLoad.Continue,
                LoadMode.kUndo => FloorLoad.Undo,
                _ => FloorLoad.NewRun,
            };
            Plugin.Tracker.BankMidRunRestarts = Plugin.BankMidRunRestarts.Value;
            var bank = Plugin.Tracker.FloorLoading(load);
            if (bank != null && Plugin.Enabled.Value)
                Plugin.QueueBank(bank);
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError("InitGrid hook (before) failed: " + ex);
        }
    }

    static void Postfix()
    {
        try
        {
            var (runId, amounts) = Plugin.ReadRun();
            Plugin.Tracker.FloorLoaded(runId, amounts);
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError("InitGrid hook (after) failed: " + ex);
        }
    }
}
