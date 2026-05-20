# T01: 06-command-launch-escaping 01

**Slice:** S07 — **Milestone:** M001

## Description

Replace fragile direct xEdit and MO2 command construction with explicit argv contracts in `XEditCommandBuilder`.

Purpose: Users must be able to clean plugins whose launch-bound paths and names contain quotes, Unicode, spaces, and shell-sensitive punctuation without AutoQAC corrupting the target plugin or MO2 wrapper arguments.
Output: Command-builder tests and implementation proving direct xEdit argv tokens plus the locked MO2 `run <xEdit> -a <payload>` contract.

## Must-Haves

- [ ] "D-01/D-15: Curated direct and MO2 command-builder tests cover quote/parser, Unicode, shell-sensitive, and combined worst-case inputs."
- [ ] "D-02: Tests cover every launch-bound input: xEdit executable path, MO2 executable path, plugin file name, and nested xEdit arguments."
- [ ] "D-05/D-06/D-07/D-08: MO2 command shape remains `run`, xEdit path, `-a`, one nested xEdit payload, and file-name-only `-autoload`."
- [ ] "Reviews: Direct xEdit `-autoload` argv shape is explicitly locked as parsed tokens `-autoload`, exact plugin filename, preserving the parsed intent of the old `-autoload \"Plugin.esp\"` command string."
- [ ] "Reviews: MO2 nested `-a` payload uses a documented quote/backslash formatter with direct formatter tests for quotes and trailing backslashes."
- [ ] "D-10/D-16: Builder passes authoritative inputs as argv data and asserts parsed ArgumentList entries rather than direct raw command strings."

## Files

- `AutoQAC/Services/Cleaning/XEditCommandBuilder.cs`
- `AutoQAC.Tests/Services/XEditCommandBuilderTests.cs`
