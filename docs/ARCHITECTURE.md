# Architecture

## Layers (dependency direction: top → bottom)

```
NeyraOptimizer.App (WPF, MVVM, DI composition root)
        │
NeyraOptimizer.Application      ← use-case orchestration
   ├── NeyraOptimizer.Diagnostics   (analyzer, compatibility, measurement, reporting)
   ├── NeyraOptimizer.Optimization  (safety engine, pipeline, catalog, modes, restore)
   ├── NeyraOptimizer.Infrastructure (persistence, logging, crash recovery)
   └── NeyraOptimizer.Security      (protected components, elevation gateway, integrity)
        │
NeyraOptimizer.Domain           ← models, enums, PORTS (interfaces), pure engines
        ▲
NeyraOptimizer.Windows          ← Windows-specific implementations of Domain ports
```

Key rule: **only** `NeyraOptimizer.Windows` and the WPF shell touch Win32/COM/WMI/registry.
Every other layer compiles against `Domain.Abstractions` interfaces, which is what makes the
engine fully unit-testable with in-memory fakes.

## Ports (Domain) → Adapters (Windows)

| Port | Adapter | Notes |
|---|---|---|
| `IWindowsServiceManager` | `WindowsServiceManager` | OpenSCManager/ChangeServiceConfig; service **key names**, never localized display names |
| `IStartupManager` | `WindowsStartupManager` | HKCU/HKLM Run + WOW64 + startup folders; disable = state flag, never delete |
| `ITaskSchedulerManager` | `WindowsTaskSchedulerManager` | Task Scheduler COM; full `\path\name` identity; protected prefix list |
| `IRegistryManager` | `WindowsRegistryManager` | typed values; no key deletion exposed |
| `IPowerManager` | `WindowsPowerManager` | powercfg APIs + overlay (`PowerReadACValueIndex`-family) |
| `IVisualEffectsManager` | `WindowsVisualEffectsManager` | documented registry values + SystemParametersInfo |
| `IAppPackageManager` | `AppxPackageManager` | PackageManager (current-user uninstall only); Win32 uninstall registry enumeration |
| `IProcessAnalyzer` / `IBackgroundActivityManager` | `ProcessAnalyzer`, `WindowsBackgroundActivityManager` | classification (user/system/service-hosted/security…), official background toggles |
| `IPerformanceMonitor` | `WindowsPerformanceMonitor` | perf counters + GlobalMemoryStatusEx; disposable |
| `ISystemInformationProvider` | `WmiSystemInformationProvider` | CIM queries; locale-independent identifiers |
| `IRestorePointManager` | `RestorePointManager` | srclient.dll; runs inside elevated child |
| `ICleanupScanner` | `CleanupScanner` | known temp/cache locations only |

## Operation pipeline (the single mutation path)

```
UI (Preview dialog)
  └─> OptimizationCoordinator (operation lock, read-only gate)
        └─> OptimizationPipeline.ExecuteAsync()
              ├─ SafetyEngine.ValidateBatch            (preconditions)
              ├─ RestorePointManager                   (optional, abort-on-failure)
              ├─ BuildPlan: capture previous values    (backup semantics per change)
              ├─ Direct items applied in-process       (non-privileged)
              ├─ Privileged items -> ONE ElevatedOperationRequest{ApplyBatch}
              │     └─ ElevationGateway -> child process (--elevated-op <id>)
              │           └─ ElevatedOperationExecutor (+ validator re-run inside child)
              ├─ Verify postconditions                 (service/task state re-read)
              ├─ SnapshotRepository.Save               (previous values + SHA-256 manifest)
              ├─ HistoryRepository.Save                (per-item details)
              └─ PendingOperationTracker.Clear         (crash marker)
```

Failure semantics:
* restore point failure ⇒ `RestorePointFailedException` ⇒ UI asks explicit consent to continue without one;
* 4 consecutive failures abort the batch;
* cancellation persists a partial snapshot and records an honest history entry;
* any unexpected exception leaves the pending marker so next launch offers rollback.

## Snapshot & restore design

Snapshots are standalone JSON files under `%ProgramData%\NeyraOptimizer\Snapshots`
(`snapshot-<guid>.json` + `.sha256` sidecar). They are intentionally outside any database so the
Emergency Restore window can enumerate and integrity-check them even when other app state is
corrupt. `RestoreEngine` replays changes in reverse order using stored previous values.
Uninstalls are recorded honestly as irreversible (package family name stored to help reinstall).

## Elevation design

The main app always runs *asInvoker*. Privileged work is serialized into one typed batch
request (no free-form commands), written under `%ProgramData%\NeyraOptimizer\ops\<id>\`,
and executed by the same executable relaunched with `--elevated-op <id>` (one UAC prompt).
The child re-validates the request independently (`ElevatedOperationValidator`: regexes,
protected-component checks, registry path whitelist) before doing anything, writes a signed
result file, and exits. No credentials are ever collected or stored.

## Data locations

| Data | Path |
|---|---|
| Snapshots | `%ProgramData%\NeyraOptimizer\Snapshots` |
| History | `%ProgramData%\NeyraOptimizer\History` |
| Measurements | `%ProgramData%\NeyraOptimizer\Measurements` |
| Logs | `%LocalAppData%\NeyraOptimizer\Logs` |
| Settings | `%AppData%\NeyraOptimizer\settings.json` |

All paths derive from `Environment.SpecialFolder` — no hardcoded users/drives.
