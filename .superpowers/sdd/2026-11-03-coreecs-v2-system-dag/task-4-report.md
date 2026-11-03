# Task 4 Report — System Execution Observation Events

## Result

- `SystemManager` now exposes only `OnSystemBeginExecute` and
  `OnSystemEndExecute`; the public teardown/cleanup signals and their delegate types were removed.
- Begin observers run immediately before `ISystem.OnTick`, and End observers run immediately
  after it. Observer exceptions continue to be logged independently and do not skip system
  execution.
- A private dispatch-depth guard rejects every DAG mutation entry point during Begin/End callback
  dispatch: system register/unregister, group registration, and system/group anchor changes.
  Rejected mutations leave the graph unchanged and are logged by the existing signal dispatcher.
- The guard is scoped only to observation callbacks. `OnTick` can still mutate the pending graph
  for the next `BeginTick`.
- The no-observer fast path checks `HasReceivers` before entering dispatch. A regression test
  measured zero thread allocations across 1,024 cached system executions.

## TDD evidence

The new focused fixture was first observed RED at HEAD `4c167ff`:

- Begin and End callback DAG-mutation cases expected five logged rejections each but observed zero.
- The public-surface test found the existing `OnSystemTeardown` signal.

After the private execution-observer guard and API narrowing were implemented, all focused tests
passed. The fixture also covers observer ordering and arguments, exception isolation, zero-handler
allocation, all DAG mutation entry points from both callback phases, and allowed `OnTick` pending
graph mutation.

## Verification

```text
Focused default:       7 passed, 0 failed
Focused ulong:         7 passed, 0 failed
Full default:        379 passed, 0 failed
Full ulong:          379 passed, 0 failed
Kernel default build: net8.0 + netstandard2.1, 0 warnings, 0 errors
Kernel ulong build:   net8.0 + netstandard2.1, 0 warnings, 0 errors
git diff --check: clean
```

The full test builds continue to emit pre-existing nullable/NUnit analyzer warnings in unrelated
test files. No ledger changes or subagents were used.
