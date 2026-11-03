# Task 1 Report — Frozen EntityMatcher

## Result

- Removed the public `IEntityMatcher` and staged fluent matcher interfaces.
- Removed `EntityMatcherExtension`, including its multi-type and Query/Build wrappers.
- Replaced them with a sealed concrete `EntityMatcher`; `OfAll<T>`, `OfAny<T>`, and
  `OfNone<T>` return the same concrete matcher.
- Added first-bind freezing for both temporary `World.Query` overloads and the old Collector.
  Every mutator checks the frozen state before duplicate-term deduplication.
- Added an internal immutable plan containing sorted TypeId arrays, mask, relevance lookup, and
  candidate strategy (`SmallestAllStorage`, `AnyStorageUnion`, or `LiveEntityTable`).
- Mechanically changed the old Collector and temporary World Query signatures to consume
  `EntityMatcher`; Collector phase and buffer algorithms were not changed.

## Test coverage

`Test/EntityMatcherTestUnit.cs` now covers:

- combined All/Any/None truth table;
- empty matcher over live entities with mask filtering;
- any-intersecting-bit mask semantics;
- duplicate term idempotence before bind;
- Query and Collector first-bind freeze;
- all three post-freeze mutators, for both duplicate and new terms;
- relevant component behavior for All/Any/None and an empty matcher.

The two `WorldTestUnit` cases dedicated to deleted matcher extension Query wrappers were removed.
Other World Query contract tests remain and were mechanically changed to the concrete matcher.

## TDD evidence

- RED: focused matcher test compilation failed because existing mutators returned staged matcher
  interfaces instead of concrete `EntityMatcher` (`CS0266` at the new contract assignments).
- GREEN: focused matcher suite passed 8/8 after the concrete frozen implementation.

## Verification

- Default full suite: 289 passed, 0 failed.
- `CoreECSUseUlongEntity=true` full suite: 289 passed, 0 failed.
- Kernel Release dual-target build: `net8.0` and `netstandard2.1`, 0 warnings, 0 errors.
- `git diff --check`: clean.

## Scope

Task 2 `EntityQuery` was not implemented. No ledger or generated artifacts were modified.

## Fix round 1/5 — restore matcher regression coverage

Restored and mechanically adapted all 17 pre-Task1 `EntityMatcherTestUnit` tests from `b28585e`
to the concrete `EntityMatcher` API. This restores the previously covered standalone All, Any,
None, complex filtering, empty component list, multiple Any, multiple None, mask/property,
combined filter, relevance, and Collector revision behaviors. In particular:

- `EntityMatcher_MixedFilters_CombinedLogic` uses two distinct All terms and proves conjunction;
- `EntityMatcher_CanHandleMultipleOfNone` uses two distinct None terms;
- `EntityMatcher_IsRelevantComponent_WithMaskNoConditions_TreatsAllAsRelevant` covers an empty
  `WithMask` matcher;
- `EntityMatcher_PropertyAccess_Getter`, `EntityMatcher_WithProperty_Access`, and
  `EntityMatcher_WithMask_CreatesCorrectMatcher` cover `EntityMask` values;
- `EntityMatcher_Change_Existing_Component` restores the matcher/Collector revision behavior.

No production bug was exposed, so this round changes tests and this report only.

### Test-count reconciliation

- Before Task1 (`b28585e`): 300 full-suite tests, including 17 matcher tests and 2 tests dedicated
  to the now-deleted `EntityMatcherExtension.Query` wrappers.
- Initial Task1 commit (`b788d09`): 289 tests. It added 8 focused frozen-matcher tests, but replaced
  rather than retained the 17 existing matcher tests, and correctly removed the 2 extension-only
  tests: `300 - 17 - 2 + 8 = 289`.
- After this fix: 306 tests. All 17 existing matcher tests are restored, the 8 Task1 tests remain,
  and only the 2 extension-only tests stay deleted: `300 - 2 + 8 = 306`.

### Verification commands and outputs

```text
$ DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --configuration Release --filter "FullyQualifiedName~EntityMatcherTestUnit|FullyQualifiedName~EntityCollectorTestUnit" --verbosity minimal
Passed!  Failed: 0, Passed: 90, Skipped: 0, Total: 90

$ DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --configuration Release -p:CoreECSUseUlongEntity=true --filter "FullyQualifiedName~EntityMatcherTestUnit|FullyQualifiedName~EntityCollectorTestUnit" --verbosity minimal
Passed!  Failed: 0, Passed: 90, Skipped: 0, Total: 90

$ DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH" dotnet test CoreECS.sln --configuration Release --verbosity minimal
Passed!  Failed: 0, Passed: 306, Skipped: 0, Total: 306

$ DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH" dotnet test CoreECS.sln --configuration Release -p:CoreECSUseUlongEntity=true --verbosity minimal
Passed!  Failed: 0, Passed: 306, Skipped: 0, Total: 306
```

The rebuilds continue to emit the repository's pre-existing NUnit analyzer warnings in unrelated
test files; all four commands exited successfully.
