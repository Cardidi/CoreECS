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

## Fix round 1/5 — queue convergence and compatibility coverage

Fixed the legacy immutable-tick queue sequence for an already active system:

1. unregister queues removal;
2. re-register queues an add and creates a replacement schedule node;
3. final unregister now removes that queued add as well as the replacement node.

Previously the queued add survived cleanup and instantiated the system at the following teardown
without a schedule node. The fix deliberately keeps the existing add/remove queues and only
cancels the matching pending add; Task 3 remains responsible for the full final-state convergence
rewrite.

Restored binary API shape by adding a real zero-parameter `World.RegisterSystem<T>()` method that
delegates to the grouped generic overload. Reflection tests also lock the existing one-parameter
`Type` overloads on both `World` and `SystemManager`, their new grouped overloads, and the grouped
generic overload as a non-optional one-parameter method.

Added regression coverage for:

- Early/Later mixed group/system children under a nested parent;
- stale group handles across lifecycle generations while `SystemManager` is not shutdown;
- stale system node handles after unregister and same-type re-registration;
- unregister → register → unregister during the immutable tick phase, including the next teardown;
- exact public `RegisterSystem` method signatures.

### TDD evidence

- RED: focused registration tests failed 2/15: the exact zero-parameter generic overload was absent,
  and `InputSystem` resurrected at the next teardown.
- GREEN: focused registration tests passed 15/15 in both default and ulong modes.

### Verification

- Default focused registration suite: 15 passed, 0 failed.
- Ulong focused registration suite: 15 passed, 0 failed.
- Default Release full suite: 347 passed, 0 failed.
- `CoreECSUseUlongEntity=true` Release full suite: 347 passed, 0 failed.
- Kernel Release dual-target build: `net8.0` and `netstandard2.1`, 0 warnings, 0 errors.

The full test builds continue to emit the repository's pre-existing nullable and NUnit analyzer
warnings in unrelated test files.
