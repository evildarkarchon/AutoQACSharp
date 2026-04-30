# Requirements: AutoQAC

**Defined:** 2026-04-28
**Core Value:** Accurate, automated xEdit Quick Auto Clean with reliable result reporting.

## v1.0 Cleanup Requirements

Requirements for the cleanup milestone. Each maps to roadmap phases.

### Safety

- [x] **SAF-01**: User can stop cleaning without AutoQAC force-killing xEdit before the confirmation path is shown.
- [x] **SAF-02**: User can see an accurate failure outcome when force-killing xEdit fails.
- [x] **SAF-03**: User can clean plugins whose paths or names contain quotes, Unicode, spaces, or shell-sensitive characters.
- [x] **SAF-04**: User can restore backups with clear failure reporting when directories are missing, permissions fail, or a session partially restores.

### Refactoring

- [x] **REF-01**: Maintainer can change cleaning preflight, backup, execution, result finalization, or termination logic without editing one monolithic cleaning orchestrator.
- [x] **REF-02**: Maintainer can change plugin loading or issue approximation refresh behavior outside `ConfigurationViewModel`.
- [ ] **REF-03**: Maintainer can reason about configuration saves, reloads, deferrals, and failures through one serialized persistence flow.
- [x] **REF-04**: Maintainer can test PID tracking through injected storage/path abstractions with process-safe update behavior.

### Tests

- [x] **TEST-01**: Maintainer can verify real child-process timeout, graceful stop, force kill, and PID cleanup behavior through controlled integration tests.
- [x] **TEST-02**: Maintainer can verify xEdit and MO2 command argument escaping across quotes, Unicode, shell-sensitive characters, and nested arguments.
- [ ] **TEST-03**: Maintainer can verify configuration watcher race cases deterministically.
- [x] **TEST-04**: Maintainer can verify backup restore safety across missing target directories, permission failures, partial failures, and cleanup deletion failures.

### Security

- [ ] **SEC-01**: User sees concise error dialogs with log-file references instead of stack traces or excessive internal path detail.
- [ ] **SEC-02**: User diagnostic logs avoid unnecessary full command-line/path exposure while preserving enough information for local troubleshooting.

### Performance

- [x] **PERF-01**: User can refresh plugin issue approximations with better cancellation, reduced redundant load-order work, or narrower target scope.
- [x] **PERF-02**: User can run ITM approximation on large plugins without materializing every override context for each record.
- [ ] **PERF-03**: User configuration changes avoid YAML serialization round-trips for in-memory cloning.
- [x] **PERF-04**: User backup and retention operations remain cancellable and visible without parallelizing xEdit cleaning.

## Future Requirements

Deferred to a later milestone or follow-up cleanup pass.

### Security

- **SEC-03**: User receives an explicit warning when a configured xEdit or MO2 executable name does not match expected tool names for the selected game.

### QueryPlugins

- **QP-01**: Maintainer can see a documented, tested support matrix aligning app-side issue approximation support with registered `QueryPlugins` detectors.
- **QP-02**: Maintainer can validate detector counts against a fixture-backed plugin corpus outside the read-only `Mutagen/` submodule.

### UI Testing

- **UI-01**: Maintainer can verify Avalonia dialog ownership and visual/control behavior through a headless UI test project.

## Out of Scope

Explicitly excluded from this milestone to keep cleanup bounded.

| Feature | Reason |
|---------|--------|
| Parallel xEdit cleaning | Sequential cleaning is a hard runtime requirement. |
| Mutagen submodule changes | `Mutagen/` is read-only in this repository. |
| New user-facing cleaning features | This milestone hardens existing behavior rather than expanding product scope. |
| Full replacement of Rx service internals | Rx isolation can be improved only where needed for selected config watcher requirements. |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| SAF-01 | Phase 5 | Complete |
| SAF-02 | Phase 5 | Complete |
| SAF-03 | Phase 6 | Complete |
| SAF-04 | Phase 7 | Complete |
| REF-01 | Phase 8 | Complete |
| REF-02 | Phase 9 | Complete |
| REF-03 | Phase 10 | Pending |
| REF-04 | Phase 5 | Complete |
| TEST-01 | Phase 5 | Complete |
| TEST-02 | Phase 6 | Complete |
| TEST-03 | Phase 10 | Pending |
| TEST-04 | Phase 7 | Complete |
| SEC-01 | Phase 11 | Pending |
| SEC-02 | Phase 11 | Pending |
| PERF-01 | Phase 9 | Complete |
| PERF-02 | Phase 9 | Complete |
| PERF-03 | Phase 10 | Pending |
| PERF-04 | Phase 7 | Complete |

**Coverage:**
- v1.0 Cleanup requirements: 18 total
- Mapped to phases: 18
- Unmapped: 0

---
*Requirements defined: 2026-04-28*
*Last updated: 2026-04-28 after roadmap creation*
