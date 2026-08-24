# Testing Strategy

## Principles

1. **No destructive mutation on developer/CI machines.** All mutation-path tests run against the
   in-memory fake layer (`tests/NeyraOptimizer.Tests/Fakes/`), never real Windows APIs.
2. **Read tests vs Mutation tests** are separated by project reference, not just naming:
   * *Read tests* (classifier, score, recommendation engine, safety engine, compatibility,
     catalog integrity, protected components) exercise pure Domain/Diagnostics code.
   * *Mutation tests* (`PipelineIntegrationTests`, `RestoreEngineTests`) drive the full
     pipeline against fakes — they validate ordering, previous-value capture, snapshot writing,
     elevation batching, abort semantics and rollback. Running them on a VM with the real
     `Windows` adapters is a manual release step (see Test Matrix).

## Coverage map (76 tests)

| Area | Tests |
|---|---|
| DeviceClassifier | RAM-alone fallacy (4 GB HDD vs 4 GB SSD), gaming class, clamping, reasons |
| PerformanceScoreCalculator | bands, determinism, component transparency |
| RecommendationEngine | service/task/debloat rules, skip-when-satisfied, build-range filter, DoNotModify gating |
| SafetyEngine | protected services blocked, SysMain-on-HDD hardware check, One-Click category/risk limits |
| CompatibilityChecker | 1809 floor, Win11 OK, VM warning, read-only mode on unsupported builds |
| RulesCatalog | unique IDs, TargetId+rollback present, **no rule targets protected components** |
| ProtectedComponents | Defender/RPC/update/audio protected; Store/VCLibs/WebView2 packages protected; task prefixes; Win32 patterns |
| ElevatedOperationValidator | regex validation, traversal rejection, registry prefix whitelist, batch recursion |
| PipelineIntegrationTests | elevated batching (single UAC), restore-point failure abort, protected skip, multi-area apply + verify + previous values, irreversible uninstall recording, cancellation |
| RestoreEngineTests | reverse-order rollback of service/startup/task/registry, delete-newly-created values |
| PersistenceTests | snapshot roundtrip, tamper rejection, list order, pending-op lifecycle incl. corrupt marker, settings migration clamp, measurement store |
| ModePlanBuilderTests | gaming needs charger, battery saver needs battery, office excludes gaming rules, low-end startup trimming respects protection |

Run:

```powershell
dotnet test tests\NeyraOptimizer.Tests\NeyraOptimizer.Tests.csproj
```

## Manual test matrix (pre-release, dedicated VMs)

Minimum: Windows 10 x64 {4 GB HDD iGPU, 8 GB SSD iGPU, 16 GB SSD dGPU} and Windows 11 x64 same set.

After applying Safe Optimization + each mode, verify: boot time, login, network, audio,
Bluetooth, printer, Windows Update, Windows Security, Microsoft Store, browser, Office,
Explorer, sleep/wake, shutdown/restart, a gaming workload. Then roll back from Restore Center
and re-verify. Record results in the release ticket. **Do not ship production without this pass.**
