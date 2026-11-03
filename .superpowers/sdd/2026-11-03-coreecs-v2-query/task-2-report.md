# Task 2 Report — Materialized EntityQuery

## Result

- Added public sealed `EntityQuery` with `Matcher`, `IReadOnlyList<EntityId> Entities`, `Count`,
  indexer, and `Refresh()`; it implements no interfaces and is not disposable.
- Added `World.CreateQuery(EntityMatcher, bool refreshImmediately = true)` and removed both legacy
  `World.Query(..., ICollection<...>)` overloads.
- Queries bind/freeze their matcher at creation, immediately materialize by default, start empty
  when requested, and remain stable until the next explicit refresh.
- Query snapshots retain full `EntityId` values. A destroyed ID therefore keeps its old generation
  and cannot alias a later entity that reuses the index.
- `Refresh()` clears and reuses the same result, candidate, and Any-dedup buffers. Existing
  `Entities` references are stable read-only views and observe replacement contents after refresh.
- A query is documented as invalid after World shutdown; `Refresh()` then throws an explicit
  `InvalidOperationException`.

## Planner

- All queries select the required storage with the smallest `LiveCount` and evaluate the complete
  matcher from that candidate set.
- Any-only queries union the relevant storages with full-`EntityId` deduplication.
- All+Any queries stay All-driven; Any storages never broaden the candidate set.
- None-only and empty matchers enumerate live records from `EntityTable`.
- Entity masks are checked before component membership checks.
- Storage candidates skip logical tombstones, and candidate diagnostics count live IDs rather than
  allocated slots.
- Added internal test diagnostics for candidate strategy/count, post-mask component-match count,
  and retained result-buffer capacity.

## API migration

- Deleted the six `WorldTestUnit` tests dedicated to the removed collection-output overloads.
- Mechanically migrated retained `EntityMatcherTestUnit` behavior coverage to `CreateQuery` and
  full `EntityId` assertions.
- Migrated the structural-hook assertion in `ComponentInterfaceTestUnit`; Collector-based
  integration systems remain unchanged because Collector is still a supported API.
- No matcher extension wrapper remained after Task 1, so no additional extension deletion was
  needed.

## Test coverage and TDD evidence

`Test/EntityQueryTestUnit.cs` adds 14 tests covering:

- default/immediate and deferred materialization;
- `Matcher`, `Entities`, `Count`, indexer, sealed/no-interface contract;
- stable snapshots across add/remove and reusable buffer identity/capacity;
- destroy/end-tick/index-reuse generation safety;
- invalid refresh after shutdown and factory validation;
- deletion of legacy collection Query methods;
- smallest-live All planning, including allocated tombstones;
- Any-union deduplication and All+Any candidate selection;
- none-only/empty live-table scans;
- mask-before-component filtering diagnostics;
- tombstones excluded from candidate counts.

RED was observed twice:

1. The new contract/planner suite failed compilation with `CS0246`/`CS1061` because
   `EntityQuery` and `World.CreateQuery` did not exist.
2. The legacy-API deletion test failed while reflection still found both collection Query methods.

Both focused suites were then observed GREEN after their respective implementation steps.

## Verification

```text
$ DOTNET_ROOT="$HOME/.dotnet" $HOME/.dotnet/dotnet test Test/Test.csproj --configuration Release --filter FullyQualifiedName~EntityQueryTestUnit --verbosity minimal
Passed: 14, Failed: 0, Skipped: 0, Total: 14

$ DOTNET_ROOT="$HOME/.dotnet" $HOME/.dotnet/dotnet build Kernel/Kernel.csproj --configuration Release --verbosity minimal
net8.0 + netstandard2.1: Build succeeded, 0 warnings, 0 errors

$ DOTNET_ROOT="$HOME/.dotnet" $HOME/.dotnet/dotnet test CoreECS.sln --configuration Release --no-restore --verbosity minimal
Passed: 314, Failed: 0, Skipped: 0, Total: 314

$ DOTNET_ROOT="$HOME/.dotnet" $HOME/.dotnet/dotnet test CoreECS.sln --configuration Release --no-restore -p:CoreECSUseUlongEntity=true --verbosity minimal
Passed: 314, Failed: 0, Skipped: 0, Total: 314
```

The ulong rebuild continues to emit the repository's pre-existing NUnit analyzer warnings in
unrelated tests. `git diff --check` is clean. No ledger, subagent output, or build artifacts were
added.
