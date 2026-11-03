# Task 1 Report — System registration tree and handles

## Result

- Added `GroupInsertMode.Early` / `Later` and an internal `SystemSchedule` registration tree.
- Added an implicit root, nested named groups, mixed group/system children, global group lookup,
  and global concrete-system-type lookup.
- Added public `SystemRegistration` and `GroupRegistration` fluent handles with `Before` / `After`
  overloads for a system `Type`, generic system type, and group name.
- Anchors are retained as unresolved declarations, including forward references; no flattening or
  topological ordering from Task 2 was implemented.
- Added generation and node ownership checks so handles fail after shutdown/lifecycle generation
  changes and stale system handles fail after unregistration.
- Changed `World.RegisterSystem` to return a handle and added grouped system and root/nested group
  registration overloads. Added the corresponding minimal `SystemManager` API.
- Kept `UnregisterSystem` and intentionally did not add `UnregisterGroup`.

## Test coverage

`Test/SystemGroupRegistrationTestUnit.cs` covers:

- root-level mixed system/group registration;
- three-level nested groups and systems in nested groups;
- mixed-child ordering under repeated Early and Later insertion;
- globally duplicate group names across different parents;
- globally duplicate concrete system types across different groups;
- unknown group parents for both group and system registration;
- unresolved Type, generic, and group-name anchors on systems and groups;
- forward-reference retention after targets register;
- handle invalidation after World shutdown;
- absence of a public `UnregisterGroup` API.

## TDD evidence

- RED: the focused test build failed with `CS0246` for the absent `SystemSchedule`,
  `SystemGroupNode`, `SystemAnchor`, and `SystemAnchorKind` contracts.
- GREEN: the focused registration suite passed 10/10 after the tree, handles, and registration API
  were implemented.

## Verification

Commands used the repository-provided .NET 8 installation at `$HOME/.dotnet`.

- Default Release full suite: 342 passed, 0 failed.
- `CoreECSUseUlongEntity=true` Release full suite: 342 passed, 0 failed.
- Kernel Release dual-target build: `net8.0` and `netstandard2.1`, 0 warnings, 0 errors.
- `git diff --check`: clean.

The test project continues to emit pre-existing nullable and NUnit analyzer warnings in unrelated
test files. The Kernel dual-target build is warning-free.

## Scope

Task 2 stable flattening/topological sorting and Task 3 cache/convergence behavior were not
implemented. No ledger was updated and no subagent was used.
