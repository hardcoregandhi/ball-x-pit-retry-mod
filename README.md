# BALL x PIT – Retry Keeps Resources

A [BepInEx 6 (Bleeding Edge, IL2CPP)](https://builds.bepinex.dev/projects/bepinex_be) mod for **BALL x PIT**.

In the unmodded game, pressing **Retry** (or **Restart** in the pause menu) throws away everything you gathered in the
run: the gold, wheat, wood and stone never reach your save. To keep them you have to go back to the base and set out
again. With this mod, the resources from the run you are leaving are **added to your save when you press Retry**, so
you can retry straight away without losing anything.

## Install

1. Install BepInEx 6 BE for IL2CPP (build 788 or newer, the same one the other BALL x PIT mods use) into the game
   folder (the one with `BALLxPIT.exe`), then start the game once so BepInEx can finish setting up.
2. Copy `RetryKeepsResources.dll` into `BALLxPIT\BepInEx\plugins\`.
3. Play. Every time you retry, the BepInEx log shows a line like
   `Retry: banked 340 kGold, 12 kWheat from the run you left.`

## Settings

`BepInEx\config\hardcoregandhi.ballxpit.retrykeepsresources.cfg` (created on first start):

| Setting | Default | Meaning |
|---|---|---|
| `Enabled` | `true` | Bank the run's resources when you press Retry/Restart. |
| `BankMidRunRestarts` | `true` | Also bank when you Restart from the pause menu in the middle of a run. Set to `false` to only bank when you Retry after the run has ended (game over or level complete). |

## How it works

* While a run is played, the mod keeps a copy of the run's resources (`BattleSaveData.NumResources`).
* When the game reaches a screen you can Retry/Restart from (game over, level end, pause), the copy is frozen, so
  it is not affected when the game resets the run.
* Retry/Restart rebuilds the level with `GridMgr.InitGrid(LoadMode.kResetFloor)`. Only then is the frozen copy added
  to your save with the game's own `SaveMgr.AddResources`, on the next frame. Starting a new run from the base
  (`kNewGame`), continuing a run or undoing never adds anything. In those cases the game handles resources itself,
  so nothing is counted twice.
* The mod never patches the game's resource methods; it only calls them.

## Build

You need the [.NET SDK](https://dotnet.microsoft.com/download) 8 or newer (the plugin itself targets net6.0 like
BepInEx 6; the tests target net8.0), and the game with BepInEx 6 installed and
started once (that generates `BepInEx\interop`). No game files are included in this repository.

```powershell
dotnet build src/RetryKeepsResources -c Release -p:GameDir="C:\Program Files (x86)\Steam\steamapps\common\BALLxPIT"
```

`GameDir` defaults to the standard Steam location shown above. The DLL is written to `src/RetryKeepsResources/bin/Release/`.

The game-independent logic has unit tests that don't need the game:

```
dotnet test tests/RetryKeepsResources.Tests
```

BALL x PIT is a trademark of its owner (Kenny Sun / Devolver Digital). This project is not affiliated with them.
