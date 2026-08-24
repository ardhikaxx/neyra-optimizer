# Known Limitations (honest list)

1. **No guaranteed RAM numbers.** Windows baseline footprint varies by edition/driver set;
   Neyra reports measured deltas only and may truthfully report "no significant change".
2. **Temperature** is not shown unless a safe API exposes it; most consumer hardware doesn't.
3. **ARM64 is untested**; x64 builds only.
4. **Startup impact values** are heuristic estimates from entry type, never invented numbers.
5. **Win32 app uninstall** uses the standard uninstaller strings; silent removal depends on the
   vendor's uninstaller. AppX removal is current-user only; provisioned-package removal is an
   explicit advanced action requiring elevation.
6. **Power-mode overlay** requires Windows 10 1709+ hardware support; on desktops without it,
   only classic plans are offered (the UI states this).
7. **Update checker**: intentionally absent in 1.0 until a signed update channel exists.
8. **Language switch** applies to all newly rendered text immediately; some in-place labels
   refresh after reopening the page.
9. **System Restore availability** depends on the user's Windows configuration; when it cannot
   create a point, batches are aborted rather than proceeding silently.
10. **Per-app gaming profiles** are not included in 1.0 — per-game tuning would require writing
    game config files, which we avoid by policy.
