# Building and testing

Use Windows x64, PowerShell 7, and the .NET 9 SDK (9.0.319 or a compatible 9.0.3xx patch). No third-party NuGet packages are downloaded by these projects.

## Core tests (no game required)

From the repository root:

```powershell
dotnet run --project tests/SpireStrategist.Tests -c Release
dotnet run --project tools/Evaluate -c Release -- artifacts/evaluation
dotnet run --project tools/LocalizationCatalog -c Release -- . --check
```

The evaluation matrix checks deterministic finite scores and immutable inputs. It is not a simulated win-rate test.

## Build the mod

You need a local game 0.111.0 installation with `sts2.dll`, `GodotSharp.dll` and `0Harmony.dll` in `data_sts2_windows_x86_64`.
Copy `local.props.example` to `local.props` and set `GameDir`, or pass it explicitly:

```powershell
./scripts/build.ps1 -GameDir 'D:/SteamLibrary/steamapps/common/Slay the Spire 2'
```

The script builds the mod, runs core and structural policy checks, and validates reflected hooks/RNG cloning against your local game assemblies. Output: `artifacts/SpireStrategist/SpireStrategist.dll` and `SpireStrategist.json`. Referenced game DLLs are not bundled.

To install into a game directory you specify:

```powershell
./scripts/install.ps1 -GameDir 'D:/SteamLibrary/steamapps/common/Slay the Spire 2'
```

Restart the game to load the new DLL. The installer backs up previous files under `.local/install-backups`.

## Native fixtures (optional, private local setup)

Prepare your own separate local game installation/copy and install this mod there. For solver-dependent fixtures install compatible CombatSolver and its dependencies; do not redistribute this test setup. The default fixture location is `.local/lab/game`; `-GameDir` overrides it.

```powershell
./scripts/test-autopilot.ps1 -GameDir 'D:/private-sts2-lab/game' -EventLab ui-0620 -Language eng -RenderUi -TimeoutSeconds 300
./scripts/test-autopilot.ps1 -GameDir 'D:/private-sts2-lab/game' -EventLab enchant-0621 -Language eng -TimeoutSeconds 300
```

The runner isolates APPDATA/LOCALAPPDATA and records the DLL hash, seed, settings and result in `artifacts/autopilot-tests`. Never point test setup at player saves. Test hooks require both the explicit lab environment variable and a marker in the isolated user directory. The tests intentionally change their own fixture state and are not gameplay-strength evidence.
