# Phase 11: user-facing-diagnostics-boundaries - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md - this log preserves the alternatives considered.

**Date:** 2026-04-30
**Phase:** 11-user-facing-diagnostics-boundaries
**Areas discussed:** Error wording, Path identifiers, Result exports, Log redaction

---

## Error wording

### Unexpected failure main message

| Option | Description | Selected |
|--------|-------------|----------|
| Operation plus log | Say the operation failed, give a next action, and point to the latest AutoQAC log without raw exception text or full paths. | yes |
| Safe category | Include a category like configuration, process launch, or report generation when known, plus latest-log guidance. | |
| Minimal generic | Use very short generic text like `Cleaning failed. See the latest AutoQAC log.` with no category or operation detail. | |
| You decide | Let downstream agents choose the smallest consistent wording that satisfies `11-SPEC.md` and existing tests. | |

**User's choice:** Operation plus log
**Notes:** Applies to unexpected cleaning and preview failures currently handled in `CleaningCommandsViewModel`.

### Dialog details fields

| Option | Description | Selected |
|--------|-------------|----------|
| Safe details only | Details may repeat next action and latest-log guidance, but never include stack traces, exception messages, paths, or command fragments. | yes |
| No details pane | Leave the details argument null for unexpected failures; the main message carries all safe guidance. | |
| Details as category | Details include a safe category/reason when available, plus latest-log guidance. | |
| You decide | Let downstream agents choose per surface, as long as unsafe text never reaches dialogs. | |

**User's choice:** Safe details only
**Notes:** Replaces current `ex.Message` and `ex.StackTrace` dialog details.

### Status text after unexpected failure

| Option | Description | Selected |
|--------|-------------|----------|
| Short operation status | Use concise text like `Cleaning failed. See latest log.` or `Preview failed. See latest log.` without raw details. | yes |
| Mirror dialog message | Status text repeats the same safe main message shown in the dialog. | |
| Generic status only | Status text says only `Error` or `Failed` while the dialog contains the next action. | |
| You decide | Let downstream agents choose the smallest consistent status wording. | |

**User's choice:** Short operation status
**Notes:** Status text must not use `ex.Message`.

### Existing safe typed failures

| Option | Description | Selected |
|--------|-------------|----------|
| Preserve safe labels | Keep category-shaped safe summaries and backup reason labels because prior phases locked these as user-actionable without raw exception detail. | yes |
| Make all generic | Replace even safe categories with generic latest-log copy for maximum uniformity. | |
| Labels plus log | Keep safe category labels, but append latest-log guidance whenever technical detail might be needed. | |
| You decide | Let downstream agents decide per surface while preserving safety tests. | |

**User's choice:** Preserve safe labels
**Notes:** Carries forward Phase 10 `ConfigPersistenceFailure.SafeSummary` and Phase 7 `BackupFailureReason` labels.

---

## Path identifiers

### Missing executable or load-order identifiers

| Option | Description | Selected |
|--------|-------------|----------|
| Setting plus basename | Show the setting name and safe file name, such as `xEdit Path (SSEEdit.exe)`, but never the directory path. | yes |
| Setting only | Show only the setting/resource name, such as `xEdit Path` or `Load Order File`, with no filename. | |
| Basename only | Show the selected file name if present, but avoid repeating the setting label. | |
| You decide | Let downstream agents pick per surface while following `11-SPEC.md`. | |

**User's choice:** Setting plus basename
**Notes:** Applies to xEdit, MO2, and load-order validation/browse-path failures.

### Folder problem identifiers

| Option | Description | Selected |
|--------|-------------|----------|
| Game plus folder label | Use text like `Skyrim SE data folder` or `selected game data folder` plus the action to reselect it. | yes |
| Folder basename | Show the last folder name only, with no parent directories. | |
| Generic folder | Use only `selected folder` or `configured folder` with no game or basename. | |
| You decide | Let downstream agents choose the safest wording for each folder surface. | |

**User's choice:** Game plus folder label
**Notes:** Avoids full game-install/profile paths while retaining user context.

### Unusual basename handling

| Option | Description | Selected |
|--------|-------------|----------|
| Sanitized basename | Show a sanitized display basename with control characters removed or neutralized, preserving useful names like `Plugin.esp`. | yes |
| Suppress odd names | If the basename looks suspicious or command-like, hide it and show only the setting/resource label. | |
| Show raw basename | Show the exact basename because full paths are the only prohibited path detail. | |
| You decide | Let downstream agents define safe basename handling during planning. | |

**User's choice:** Sanitized basename
**Notes:** Must handle quotes, control characters, Unicode, and command-like text safely.

### Latest-log guidance on path failures

| Option | Description | Selected |
|--------|-------------|----------|
| Only technical failures | Simple missing-path validation gives a fix action; parse/read failures also mention latest AutoQAC log. | yes |
| Every path error | Append latest-log guidance to missing-path, invalid selection, parse, read, and folder errors for consistency. | |
| Never path errors | Keep path validation purely actionable and reserve log guidance for unexpected failures only. | |
| You decide | Let downstream agents choose based on each surface. | |

**User's choice:** Only technical failures
**Notes:** Missing-path validation should stay concise and action-oriented.

---

## Result exports

### Failed plugin line content

| Option | Description | Selected |
|--------|-------------|----------|
| Plugin plus summary | Show plugin filename plus a safe failure summary/status and latest-log guidance, with counts/duration where applicable. | yes |
| Plugin only | List only failed plugin filenames and put one generic latest-log note at the section/session level. | |
| Category per plugin | Show plugin filename plus safe category labels like timeout, launch failed, xEdit failed, or exception log detected. | |
| You decide | Let downstream agents pick the minimum useful report content while satisfying `11-SPEC.md`. | |

**User's choice:** Plugin plus summary
**Notes:** `GenerateReport()` must no longer propagate unsafe `result.Message` text.

### Sanitization boundary for failed result messages

| Option | Description | Selected |
|--------|-------------|----------|
| Sanitize at source | Service/finalizer-created `PluginCleaningResult` messages must already be safe; report/export code also has defensive checks. | yes |
| Sanitize on export | Allow internal result messages to vary, but sanitize in result windows and `GenerateReport()` before user-facing display. | |
| Defensive both ways | Require both safe source messages and a report/export sanitizer that rewrites unsafe-looking content. | |
| You decide | Let planner choose the smallest reliable boundary. | |

**User's choice:** Sanitize at source
**Notes:** Report/export code can still be defensive, but the primary contract is source-safe result messages.

### xEdit exception-log result wording

| Option | Description | Selected |
|--------|-------------|----------|
| Safe xEdit failure | Show a safe message like `xEdit reported an error for Plugin.esp. See latest AutoQAC log.` without exception log content. | yes |
| Generic failure | Treat it like any other failed plugin and do not mention xEdit exception logs. | |
| Category only | Use a label such as `xEdit exception log detected` but no details or guidance. | |
| You decide | Let downstream agents choose consistent wording. | |

**User's choice:** Safe xEdit failure
**Notes:** Exception-log content stays out of rows and reports.

### Report disclaimer

| Option | Description | Selected |
|--------|-------------|----------|
| Short disclaimer | Add one note that technical details are intentionally kept in AutoQAC logs, not repeated in exported reports. | yes |
| No disclaimer | Keep reports concise and rely on per-failure latest-log guidance. | |
| Detailed disclaimer | Explain that full paths, command fragments, stack traces, and raw exceptions are excluded by design. | |
| You decide | Let downstream agents decide whether a disclaimer is necessary. | |

**User's choice:** Short disclaimer
**Notes:** Keep disclaimer concise and non-technical.

---

## Log redaction

### Process-start and startup log replacement fields

| Option | Description | Selected |
|--------|-------------|----------|
| Structured safe fields | Log operation, launch mode, game, plugin filename, PID when available, argument count, and safe reason; omit executable paths and argv payloads. | yes |
| Redacted placeholders | Keep the same templates but replace paths/payloads with placeholders like `[redacted path]` and `[redacted argv]`. | |
| Minimal event only | Log only that a process started or startup diagnostics ran, with little contextual detail. | |
| You decide | Let downstream agents choose the smallest safe log shape. | |

**User's choice:** Structured safe fields
**Notes:** Prefer useful structured context over placeholder-heavy strings.

### Full path allowance in logs

| Option | Description | Selected |
|--------|-------------|----------|
| Direct failing resource | Keep full paths only when the path is the direct file/folder that failed and omitting it would materially hurt local troubleshooting. | yes |
| No full paths | Remove or basename-only every AutoQAC path log touched by Phase 11, even for file/config/backup failures. | |
| Existing non-launch paths | Only fix process/startup command-related logs; leave other existing path logs unchanged. | |
| You decide | Let downstream agents interpret `11-SPEC.md` case by case. | |

**User's choice:** Direct failing resource
**Notes:** Carries forward the SPEC constraint that not every full path must disappear from logs.

### Plugin identifiers in logs

| Option | Description | Selected |
|--------|-------------|----------|
| Filename only | Use plugin filenames in normal workflow logs; avoid full plugin paths unless that exact file path is the failing resource. | |
| Filename plus game | Always pair plugin filename with game/mode context to compensate for removed full paths. | yes |
| Allow full plugin paths | Full plugin paths are acceptable in logs because logs are local and useful for troubleshooting. | |
| You decide | Let downstream agents choose where plugin paths remain necessary. | |

**User's choice:** Filename plus game
**Notes:** Use game/mode context as the safe replacement for path context in normal workflow logs.

### Log boundary proof

| Option | Description | Selected |
|--------|-------------|----------|
| Captured logger tests | Add tests/source assertions that exercise startup/process-start logging and fail on full executable paths, raw argv/nested payloads, or command fragments. | yes |
| Central sanitizer tests | Focus on a shared redaction/safe-display helper and trust call sites to use it. | |
| Source guards only | Use source-level assertions against risky log templates rather than behavior tests. | |
| You decide | Let downstream agents decide the test shape from code constraints. | |

**User's choice:** Captured logger tests
**Notes:** Behavior/source assertions should cover startup and process-start diagnostics.

---

## the agent's Discretion

- Exact helper/service names, exact message strings, placeholder wording, formatter boundaries, and test file organization are left to downstream research/planning as long as the captured decisions and `11-SPEC.md` are preserved.

## Deferred Ideas

None.
