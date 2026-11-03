# Task 2 Report — Transactional Retryable World Startup

## Result

- `World.Startup()` now propagates the original exception from registration, proxy/provider
  construction, manager activation, manager hooks, or `OnSetup`.
- Completed manager phases roll back in reverse order. Rollback and disposal failures are logged
  without replacing the original startup exception.
- A failed first startup returns the World to its pre-start state; the next attempt starts again at
  `OnRegister`. `OnSetup` failure never invokes `OnCleanup`.
- Manager interface/concrete aliases map to the same instance and receive each lifecycle phase only
  once.
- Custom `IInjectionProxyFactory` integration is covered and remains supported.

## Review round 1 fixes

- A failed startup now releases the attempt-owned DI graph after manager rollback. World disposes
  the proxy when it implements `IDisposable`; otherwise it disposes the proxy's service provider.
  Exactly one ownership path is selected.
- `BuiltinInjectionProxy` implements idempotent `IDisposable` and delegates disposal only to its
  owned provider.
- Disposable provider-created services and managers are released for activation,
  `OnManagerCreated`, `OnWorldStarted`, and `OnSetup` failures. A retry creates a new graph.
- Successful normal `Shutdown` deliberately does not dispose the graph; normal cross-cycle reuse
  remains Task 3 work.
- Framework manager registrations use `TryAdd`, preserving custom manager instance/factory
  descriptors supplied by `OnRegister` without adding duplicates.
- Attempts to register a replacement descriptor for any of the four core manager concrete types
  fail with an explicit configuration error before provider construction.

## TDD evidence

- RED: focused startup/proxy tests failed for missing proxy/provider disposal, missing built-in
  `IDisposable`, duplicate custom manager descriptors, and accepted core-manager replacement.
- GREEN: the same focused suite passes in both default and ulong EntityId modes.
- Tests cover proxy-first disposal, provider fallback, proxy forwarding, disposal exceptions,
  rollback-before-disposal ordering, retry graph identity, custom instance/factory priority, core
  manager protection, and alias lifecycle de-duplication.

## Verification

```text
$ $HOME/.dotnet/dotnet test Test/Test.csproj --configuration Release --no-restore --filter "FullyQualifiedName~WorldStartupRollbackTestUnit|FullyQualifiedName~BuiltinInjectionProxyTestUnit"
Passed: 33, Failed: 0, Skipped: 0, Total: 33

$ $HOME/.dotnet/dotnet test Test/Test.csproj --configuration Release --no-restore -p:CoreECSUseUlongEntity=true --filter "FullyQualifiedName~WorldStartupRollbackTestUnit|FullyQualifiedName~BuiltinInjectionProxyTestUnit"
Passed: 33, Failed: 0, Skipped: 0, Total: 33

$ $HOME/.dotnet/dotnet test CoreECS.sln --configuration Release --no-restore
Passed: 410, Failed: 0, Skipped: 0, Total: 410

$ $HOME/.dotnet/dotnet test CoreECS.sln --configuration Release --no-restore -p:CoreECSUseUlongEntity=true
Passed: 410, Failed: 0, Skipped: 0, Total: 410

$ $HOME/.dotnet/dotnet build Kernel/Kernel.csproj --configuration Release --no-restore
net8.0 + netstandard2.1: Build succeeded, 0 warnings, 0 errors
```

The test project continues to emit pre-existing nullable/NUnit analyzer warnings in unrelated test
files. No ledger or subagent artifacts were changed.

## Scoped re-review — `baa585d..3d4a6f1`

### Verdicts

- **Finding 1 — RESOLVED.** Failed startup now rolls manager phases back before selecting exactly
  one graph-ownership cleanup path (disposable proxy first, otherwise disposable provider).
  `BuiltinInjectionProxy.Dispose()` is idempotent and forwards to its provider. Rollback, disposal,
  and logging failures cannot replace the original startup exception. Successful `Shutdown()` does
  not dispose the graph.
- **Finding 2 — RESOLVED for the reported manager cases.** An `OnRegister` instance/factory
  descriptor for a custom manager wins without a framework-added duplicate; the four core manager
  concrete service types are rejected as replacements; interface/concrete aliases resolve to one
  instance and receive each lifecycle phase once.

### New

- **[P1] Keep framework World registrations authoritative — `Kernel/World.cs:92-93`.** The patch
  changed both `IWorld` and `GetType()` registrations from unconditional `AddSingleton` calls to
  `TryAdd`. Consequently, an `OnRegister` descriptor for either service type now silently wins.
  Core managers such as `EntityManager` and `SystemManager` can then be constructed with a different
  World than the one being started. A minimal external repro registered a second World as `IWorld`;
  startup succeeded but printed `provider-is-real=False` and `entity-manager-is-real=False`. This is
  a regression from `baa585d`, where the framework registrations were appended after `OnRegister`
  and therefore remained the effective services. Limit `TryAdd` to manager implementation
  descriptors (or explicitly reject/remove conflicting World descriptors), and add coverage that
  core managers always receive the starting World.

### Out

```text
focused/default:  Passed 33, Failed 0, Skipped 0, Total 33
focused/ulong:    Passed 33, Failed 0, Skipped 0, Total 33
full/default:     Passed 410, Failed 0, Skipped 0, Total 410
full/ulong:       Passed 410, Failed 0, Skipped 0, Total 410
diff --check:     clean
```

The builds emitted only the previously noted unrelated nullable/NUnit analyzer warnings.

### Final

**CHANGES REQUESTED.** Both original findings are fixed, but the new P1 World-service override
regression prevents approval of `3d4a6f1`.

## Review round 2 fixes

- `IWorld` and the concrete runtime World type are now protected framework identities, consistent
  with the four protected core-manager services.
- Any descriptor for either identity supplied by `OnRegister` causes an explicit configuration
  exception before proxy/provider construction; the framework never silently accepts or replaces
  a foreign World.
- After validation, the framework registers exactly one `IWorld` descriptor and one concrete
  runtime-type descriptor, both bound to the current World instance.
- Tests inject foreign descriptors through both service types and assert Startup fails before
  provider construction and before any manager constructor can be wired to the foreign instance.
- Non-core custom manager descriptors retain the round-1 `TryAdd` priority behavior.

Round 2 verification:

```text
$ $HOME/.dotnet/dotnet test Test/Test.csproj --configuration Release --no-restore --filter FullyQualifiedName~WorldStartupRollbackTestUnit
Passed: 19, Failed: 0, Skipped: 0, Total: 19

$ $HOME/.dotnet/dotnet test Test/Test.csproj --configuration Release --no-restore -p:CoreECSUseUlongEntity=true --filter FullyQualifiedName~WorldStartupRollbackTestUnit
Passed: 19, Failed: 0, Skipped: 0, Total: 19

$ $HOME/.dotnet/dotnet test CoreECS.sln --configuration Release --no-restore
Passed: 412, Failed: 0, Skipped: 0, Total: 412

$ $HOME/.dotnet/dotnet test CoreECS.sln --configuration Release --no-restore -p:CoreECSUseUlongEntity=true
Passed: 412, Failed: 0, Skipped: 0, Total: 412
```
