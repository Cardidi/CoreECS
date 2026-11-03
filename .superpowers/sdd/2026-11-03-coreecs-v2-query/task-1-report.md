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
