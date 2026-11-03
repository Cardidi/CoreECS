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
