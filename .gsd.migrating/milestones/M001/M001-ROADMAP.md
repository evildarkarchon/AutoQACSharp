# M001: v1.0 Cleanup

**Vision:** AutoQAC is a Windows-only Avalonia desktop app that runs xEdit Quick Auto Clean (`-QAC`) safely, one plugin at a time, with Mutagen-based plugin analysis.

## Success Criteria


## Slices

- [x] **S01: XEdit Log Parsing Fix** `risk:medium` `depends:[]`
  > After this: unit tests prove xEdit Log Parsing Fix works
- [x] **S02: Foundation** `risk:medium` `depends:[S01]`
  > After this: unit tests prove Foundation works
- [x] **S03: Process Layer** `risk:medium` `depends:[S02]`
  > After this: unit tests prove Process Layer works
- [x] **S04: Integration** `risk:medium` `depends:[S03]`
  > After this: unit tests prove Integration works
- [x] **S05: Cleanup** `risk:medium` `depends:[S04]`
  > After this: unit tests prove Cleanup works
- [ ] **S06: Process Stop Pid Safety** `risk:medium` `depends:[S05]`
  > After this: Create the PID storage foundation for Phase 5.
- [x] **S07: Command Launch Escaping** `risk:medium` `depends:[S06]`
  > After this: Replace fragile direct xEdit and MO2 command construction with explicit argv contracts in `XEditCommandBuilder`.
- [ ] **S08: Backup Restore Retention Safety** `risk:medium` `depends:[S07]`
  > After this: Create the shared backup/restore/retention contracts and cancellable file-copy foundation that all later Phase 7 plans build on.
- [ ] **S09: Cleaning Orchestrator Decomposition** `risk:medium` `depends:[S08]`
  > After this: Add Wave 0 characterization tests that lock current `CleaningOrchestrator` behavior BEFORE any extraction begins.
- [ ] **S10: Plugin Refresh Approximation Performance** `risk:medium` `depends:[S09]`
  > After this: Optimize the QueryPlugins ITM hot path so Phase 9 approximation work can be canceled inside record/context loops while preserving exact count semantics.
- [x] **S11: Configuration Persistence Hardening** `risk:medium` `depends:[S10]`
  > After this: Replace the YAML round-trip clone in `ConfigurationService.
- [x] **S12: User Facing Diagnostics Boundaries** `risk:medium` `depends:[S11]`
  > After this: Create the shared safe diagnostics text boundary required by Phase 11.
- [x] **S13: Process Stop Verification Progress Flow Closure** `risk:medium` `depends:[S12]`
  > After this: Create the shared stop outcome text and dialog-label contract used by every Stop surface.
- [x] **S14: Command Launch Escaping Reverification Safe Mo2 Failures** `risk:medium` `depends:[S13]`
  > After this: Collect and record the targeted Phase 13 evidence proving safe MO2 missing-configuration failure, direct/MO2 command escaping, process-boundary argument preservation, and launch-failure diagnostics.
- [x] **S15: Orchestrator Decomposition Reverification** `risk:medium` `depends:[S14]`
  > After this: Produce the Phase 14 current verification artifact proving or rejecting `REF-01` after the Phase 8 session-guard and detected-load-order gap closures.
- [x] **S16: Stop Escalation Ownership Closure** `risk:medium` `depends:[S15]`
  > After this: Create the coordinator-owned retained process handle required for confirmed detached force escalation.
- [x] **S17: Milestone Evidence Validation Reconciliation** `risk:medium` `depends:[S16]`
  > After this: Create and refresh the phase-local evidence artifacts that unblock milestone audit discovery before any audit or marker reconciliation occurs.
