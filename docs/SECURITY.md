# Security Model

## Threat model assumptions

* The user runs the app unelevated; malware may be able to read anything the user can read.
  Therefore: **no secrets are stored anywhere**, logs and bundles contain no credentials or
  personal files, and the only privileged surface is a strictly typed, re-validated batch.
* The elevated child is our own signed executable; its input is a JSON request that is
  validated twice (parent + child) against an allow-list.

## Hardened surfaces

| Surface | Mitigation |
|---|---|
| Command execution | `SafeCommandLineRunner` — fixed whitelist, encoded `-EncodedCommand`, timeout, exit-code capture. Only two fixed scripts exist: provisioned-package removal & Delivery-Optimization cleanup. |
| Registry writes | Typed `IRegistryManager`; elevation path restricted to prefix allow-list (`SOFTWARE\Microsoft\Windows\CurrentVersion\`, `SOFTWARE\Policies\Microsoft\Windows\`, `SYSTEM\CurrentControlSet\Services\`); no key deletion API exposed; HKCU-only changes never elevate. |
| Service/task/package ops | Service key names validated by regex; task paths by regex + traversal rejection; package full names by schema regex; protected-component checks run in BOTH parent and elevated child. |
| Elevation | One-shot child (`--elevated-op <guid>`), request/result files under ACL-restricted `%ProgramData%\NeyraOptimizer\ops\<id>`, integrity manifest on both files, parent deletes op dir afterwards. No password prompts ever; UAC handles consent. |
| File operations | Cleanup limited to enumerated safe locations with per-candidate confirmation; dry-run first; no wildcards into user folders. |
| Persistence | Atomic writes (temp+move); snapshots/history/settings carry SHA-256 manifests; corrupt/tampered data is refused for restore. |
| Logging | Structured JSONL; identifiers and error codes only; control-character sanitization; severity floor configurable. |
| Updates | None in 1.0 — the Settings entry states this honestly instead of shipping a fake checker. |

## Protected components (fail-closed)

`Security/Protection/ProtectedComponents.cs` is the backbone consulted by the Safety Engine,
the rule catalog test, and the elevated validator:

* services: WinDefend stack, wscsvc, mpssvc, BFE, RpcSs/DcomLaunch, Dhcp/Dnscache/NlaSvc,
  Lanman*, FltMgr/volmgr/stor*, AudioSrv/AudioEndpointBuilder, UsoSvc/wuauserv/DoSvc, …
* processes: csrss/winlogon/lsass/svchost/dwm/msmpeng/… (never terminable from UI)
* tasks: UpdateOrchestrator / WindowsUpdate / Windows Defender / SystemRestore prefixes
* packages: Store, SecHealthUI, VCLibs, .NET Native, UI.Xaml, WindowsAppRuntime, WebMedia/
  image codecs, shell hosts
* Win32 uninstall patterns: VC++ redistributables, Edge/WebView2, driver packages, AV products

Unknown/empty identifiers are treated as protected.

## What we deliberately do NOT do

* No permanent service disabling, task deletion, registry-key deletion or "debloat scripts".
* No memory "cleaners" that thrash the standby list.
* No background service of our own; closing the window ends all activity.
* No telemetry/network calls in any optimization flow (fully offline).
