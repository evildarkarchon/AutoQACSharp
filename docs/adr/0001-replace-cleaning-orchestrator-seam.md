# Replace the cleaning orchestrator seam with CleaningSession

Accepted: replace `ICleaningOrchestrator` directly with a `CleaningSession` seam instead of keeping a compatibility facade. The facade option would lower migration risk, but it would preserve the shallow interface and keep tests coupled to orchestration plumbing; replacing the seam in one vertical slice gives the new module locality over start, dry-run, stop, backup cancellation, user decisions, and final state publication.

The slice deliberately does not redesign the xEdit process lifecycle, plugin discovery, progress publication, or QueryPlugins adapter seams at the same time.

The replacement seam uses a ports/adapters hybrid: `CleaningSession` exposes `StartAsync`, `PreviewAsync`, `ControlAsync`, and `HangDetected`, while user decisions, state publication, preflight, backup I/O, process/termination, runner, and finalizer stay behind the seam as adapters or internal implementation seams.
