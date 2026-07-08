# Phase 6: Command Launch Escaping - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md - this log preserves the alternatives considered.

**Date:** 2026-04-28
**Phase:** 06-command-launch-escaping
**Areas discussed:** Coverage matrix, MO2 contract, Failure behavior, Verification depth

---

## Coverage Matrix

### Quote cases

| Option | Description | Selected |
|--------|-------------|----------|
| Synthetic parser tests (Recommended) | Use command/argv-level tests for quote cases, and real file/path tests only for characters Windows actually permits. | Yes |
| Real files only | Only test inputs that can exist on disk; impossible quote cases are documented but not exercised. | |
| Reject quotes early | Explicitly fail validation if quotes appear in configured launch-bound values. | |

**User's choice:** Synthetic parser tests (Recommended)
**Notes:** Quote cases remain required for command-parser regression coverage even when Windows cannot create a matching real file name.

### Launch-bound values

| Option | Description | Selected |
|--------|-------------|----------|
| All launch inputs (Recommended) | Cover xEdit path, MO2 path, plugin file name, and nested xEdit arguments because each is part of the process launch boundary. | Yes |
| Plugin names only | Focus on the target plugin because that is what could be corrupted or split. | |
| Executable paths plus plugin | Cover xEdit/MO2 paths and plugin names, but keep partial-form/game flags at simple smoke-test coverage. | |

**User's choice:** All launch inputs (Recommended)
**Notes:** Coverage should not stop at plugin names because executable paths and nested arguments also cross parser boundaries.

### Unicode breadth

| Option | Description | Selected |
|--------|-------------|----------|
| Representative Unicode (Recommended) | Include accented, CJK/Cyrillic or similar non-Latin names, plus one supplementary/emoji-style case if the test harness can represent it reliably. | Yes |
| Basic non-ASCII only | One accented or CJK plugin/path is enough to prove the command path is Unicode-safe. | |
| Broad corpus | Exercise many Unicode categories, including combining marks and right-to-left text, even if it expands test scope. | |

**User's choice:** Representative Unicode (Recommended)
**Notes:** Use meaningful representative cases without broad corpus/fuzz expansion.

### Shell-sensitive characters

| Option | Description | Selected |
|--------|-------------|----------|
| Literal argv proof (Recommended) | Use cases like `&`, `|`, `;`, `(`, `)`, `^`, and spaces to prove they remain literal argument text under `UseShellExecute=false`. | Yes |
| Minimal shell set | Cover only `&`, `|`, and spaces because those are the most likely real-world failure cases. | |
| Document only | Do not build a broad shell-character matrix because no shell should be involved after the fix. | |

**User's choice:** Literal argv proof (Recommended)
**Notes:** Tests should prove no splitting, redirection, or shell interpretation occurs.

---

## MO2 Contract

### Launch shape

| Option | Description | Selected |
|--------|-------------|----------|
| Preserve shape (Recommended) | Keep the existing MO2 command contract, but replace fragile escaping with a tested helper so behavior changes stay minimal. | Yes |
| Allow safer MO2 form | Planner may change to another MO2-supported invocation if research shows it avoids nested escaping problems. | |
| Direct xEdit fallback | If MO2 escaping is difficult, fall back to direct xEdit launch for difficult names. | |

**User's choice:** Preserve shape (Recommended)
**Notes:** Phase 6 should be a targeted escaping fix, not a broad MO2 integration redesign.

### Nested argument shape

| Option | Description | Selected |
|--------|-------------|----------|
| Single xEdit argument string (Recommended) | Keep one MO2 `-a` payload containing the xEdit flags, because that matches the current code and common MO2 argument-field behavior. | Yes |
| Research alternate splitting | Let the planner investigate whether MO2 can accept repeated or separately-tokenized target arguments safely. | |
| You decide | Downstream agents may choose the smallest safe shape after researching MO2's actual parser behavior. | |

**User's choice:** Single xEdit argument string (Recommended)
**Notes:** Planner/researcher should still verify exact escaping, but the intended contract is one nested payload.

### Autoload target

| Option | Description | Selected |
|--------|-------------|----------|
| File name only (Recommended) | Preserve current behavior; MO2's VFS and xEdit load order resolve the plugin name, while full host paths may not exist inside the VFS. | Yes |
| Full path when available | Use the full plugin path to reduce ambiguity outside MO2, accepting more VFS compatibility risk. | |
| Mode-specific | Use file name in MO2 mode and allow direct mode to use a fuller path if testing proves xEdit accepts it. | |

**User's choice:** File name only (Recommended)
**Notes:** Keep `-autoload` aligned with current behavior and MO2 VFS assumptions.

### MO2 verification

| Option | Description | Selected |
|--------|-------------|----------|
| Parse contract tests (Recommended) | Verify the final MO2 argv is `run`, xEdit path, `-a`, and one intact xEdit argument string; do not require real MO2 in tests. | Yes |
| Mocked MO2 executable | Use a local helper executable named like MO2 to capture argv and simulate the wrapper receiving difficult inputs. | |
| Documented only | Test direct xEdit thoroughly and document that real MO2 behavior cannot be verified without MO2 installed. | |

**User's choice:** Parse contract tests (Recommended)
**Notes:** Tests should not require real MO2 installation.

---

## Failure Behavior

### Unsafe command build

| Option | Description | Selected |
|--------|-------------|----------|
| Fail before start (Recommended) | Do not launch anything; return a clear command-build failure for the plugin and log technical detail. | Yes |
| Let Process.Start fail | Build the closest command and rely on the process layer to report start failure. | |
| Best-effort launch | Try to launch with best-effort escaping unless the input is definitely impossible. | |

**User's choice:** Fail before start (Recommended)
**Notes:** Starting the wrong or ambiguously represented command is worse than failing early.

### Input mutation

| Option | Description | Selected |
|--------|-------------|----------|
| Never mutate inputs (Recommended) | Treat configured paths and plugin file names as authoritative; either pass them exactly as argv text or fail. | Yes |
| Normalize safe whitespace | Trim accidental leading/trailing whitespace from configured executable paths, but never change plugin names. | |
| Sanitize and continue | Remove or replace risky characters where possible so cleaning can proceed. | |

**User's choice:** Never mutate inputs (Recommended)
**Notes:** No sanitization or rewriting of user-configured paths or plugin identities.

### Session behavior

| Option | Description | Selected |
|--------|-------------|----------|
| Use existing failure flow (Recommended) | Mark that plugin failed and let existing orchestrator retry/continue/session-finalization behavior decide the rest; do not invent a new flow. | Yes |
| Stop the session | Abort remaining plugins because a launch construction failure means configuration may be unsafe. | |
| Skip and continue silently | Skip the failed plugin and keep cleaning the rest without extra prompt behavior. | |

**User's choice:** Use existing failure flow (Recommended)
**Notes:** Phase 6 should not create a new cleaning session policy.

### User-facing message

| Option | Description | Selected |
|--------|-------------|----------|
| Concise plugin/mode (Recommended) | Name the plugin and launch mode (direct xEdit or MO2), tell the user no process was started, and point to logs for details. | Yes |
| Full command line | Show the executable path and generated arguments so advanced users can debug immediately. | |
| Generic failure | Keep the same generic `Failed to build xEdit command` style message and rely on logs. | |

**User's choice:** Concise plugin/mode (Recommended)
**Notes:** Avoid showing full command lines in user-facing messages.

---

## Verification Depth

### Process layer preservation

| Option | Description | Selected |
|--------|-------------|----------|
| Yes, required (Recommended) | The fix is incomplete if the command builder uses `ArgumentList` but the process layer drops it when cloning `ProcessStartInfo`. | Yes |
| Builder tests only | Focus on `XEditCommandBuilder`; trust process execution once the start info is built. | |
| You decide | Planner may add the process-layer assertion if it falls out naturally during implementation. | |

**User's choice:** Yes, required (Recommended)
**Notes:** Existing `ProcessExecutionService` copies `Arguments`, so this is a real integration risk.

### Helper approach

| Option | Description | Selected |
|--------|-------------|----------|
| Extend Phase 5 helper (Recommended) | Reuse `AutoQAC.TestProcessHelper` with an argv-echo mode so tests stay in the existing process integration pattern. | Yes |
| New argv helper | Add a separate tiny helper dedicated to command-line parsing tests. | |
| No helper process | Use only command-line parser/unit tests and avoid launching local helper processes for this phase. | |

**User's choice:** Extend Phase 5 helper (Recommended)
**Notes:** Reuse the known bounded helper-process pattern.

### Matrix size

| Option | Description | Selected |
|--------|-------------|----------|
| Curated matrix (Recommended) | Cover each locked category plus one combined worst-case; avoid broad fuzzing in this phase. | Yes |
| Broad fuzz-style matrix | Generate many combinations of punctuation, whitespace, and Unicode to catch parser surprises. | |
| Minimal smoke tests | One direct xEdit case and one MO2 case are enough if they include spaces and Unicode. | |

**User's choice:** Curated matrix (Recommended)
**Notes:** Keep coverage strong but bounded.

### Assertion target

| Option | Description | Selected |
|--------|-------------|----------|
| Assert argv semantics (Recommended) | Prefer parsed argv assertions; only assert raw strings where MO2's single `-a` payload contract requires it. | Yes |
| Assert raw strings | Lock the exact command text so future changes to quoting format are visible. | |
| Assert both everywhere | Check raw command string and parsed argv for all cases, accepting more brittle tests. | |

**User's choice:** Assert argv semantics (Recommended)
**Notes:** Raw strings should not be over-specified except for MO2 nested payload shape.

---

## the agent's Discretion

- Exact implementation names, helper APIs, and test method names are left to downstream research and planning.

## Deferred Ideas

None.
