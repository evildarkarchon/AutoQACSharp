# Deepen Plugin cleaning as the per-plugin Cleaning session seam

Accepted: introduce a `PluginCleaning` module for the per-plugin part of a Cleaning session, so xEdit command building, launch, retry prompts, log slicing, process attach/detach timing, and final plugin result projection sit behind one interface.

Backup policy, backup session metadata, Plugin refresh selection decisions, and user-visible Cleaning session state publication deliberately stay outside this first slice. Those remain Cleaning session concerns, and moving them now would make the new seam too wide.

`PluginCleaning` receives a small per-plugin context from the Cleaning session rather than the whole preflight plan. It is called only after preflight has decided `PreflightDecision.Clean`, owns timeout retry sequencing through the existing Cleaning session decision adapter, and uses the termination coordinator as its stop-control adapter without knowing about WinUI dialogs.
