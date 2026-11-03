# Task 3 Report — Convergent System Schedule Cache

## Result

- System registration tree is the final desired graph while the execution cache remains the
  immutable snapshot captured by the latest successful dirty `BeginTick`.
- Tick-outside registration constructs through DI and calls `OnCreate` immediately; tick-time
  registration activates at the next successful dirty `BeginTick`.
- Tick-time unregister/remove/readd/move/anchor operations converge to final desired state.
  Surviving registrations reuse their existing instance, removed cached systems finish the current
  tick and receive `OnDestroy` at `EndTick`, and never-activated canceled systems receive no hooks.
- Clean `BeginTick` does not rebuild or copy the execution cache. `TickGroup` keeps any-bit
  filtering semantics.

## Fix round 1/5 — activation failure atomicity

- Dirty cache rebuild now resolves the complete ordered reference list and activates all required
  new instances in temporary collections before publishing anything.
- The formal execution cache is replaced by one reference assignment only after all DI activation,
  lifecycle initialization, and live-instance publication succeeds.
- A constructor/DI exception preserves the previous complete cache, marks the schedule dirty for a
  later retry, logs the original exception through `Log.Exp`, and does not increment
  `ExecutionCacheCopyCount`.
- Any temporary instance whose `OnCreate` was attempted before the failure is removed from live
  registries if necessary and receives matching `OnDestroy`, preventing leaked or half-published
  systems.
- A subsequent successful activation retries the whole desired graph and publishes it normally.

## TDD evidence

`SystemActivationFailureTestUnit.TeardownSystems_WhenMiddleActivationFails_PreservesCacheAndRetriesAtomically`
was first observed RED because the injected DI exception escaped `TeardownSystems`. It then passed
after the atomic rebuild/rollback implementation. The test verifies old-cache execution after
failure, dirty retry state, transient cleanup, exception logging, successful retry order, and that
the copy diagnostic changes only on successful replacement.

## Verification

```text
Focused default: 19 passed, 0 failed
Focused ulong:   19 passed, 0 failed
Full default:   372 passed, 0 failed
Full ulong:     372 passed, 0 failed
Kernel build:   net8.0 + netstandard2.1, 0 warnings, 0 errors
git diff --check: clean
```

The full test builds continue to emit pre-existing nullable/NUnit analyzer warnings in unrelated
test files. No ledger changes or subagents were used.

## Whole-phase final review — one fix wave

### Desired, live, and committed state

- The registration schedule is the **desired** graph for the next successful rebuild.
- `SystemTransformer` is the **live** instance set: every member completed activation and has not
  received `OnDestroy`.
- The execution cache is the **committed** snapshot from the latest successful dirty BeginTick.
- If activation fails after a prior EndTick destroyed a removed committed system, fallback is now
  `committed ∩ live`. Destroyed references are removed without incrementing the successful-copy
  diagnostic, the desired graph remains dirty, and a later retry can commit the complete new order.

### Required system lifecycle contract

- `ISystem.OnCreate`, `OnTick`, and `OnDestroy` are all abstract interface methods and therefore
  mandatory for every implementation.
- `TickGroup` remains the sole default interface member and still returns `ulong.MaxValue`.
- All retained test system implementations that relied on default lifecycle bodies now declare
  explicit no-op hooks. Reflection coverage locks the abstract/default split.

### Execution observer guard closure

- The execution-event mutation guard now protects public `TeardownSystems`, `CleanupSystems`, and
  every public manager lifecycle callback before they can mutate manager state.
- Begin/End observers attempting to invoke `OnWorldEnded` receive a logged
  `InvalidOperationException`; the current system, later systems in the committed cache, the DAG,
  and following ticks remain intact.
- Normal World startup/shutdown runs outside observer dispatch and is unchanged.

### Final TDD and verification evidence

The combined RED run had four expected failures: stale destroyed-system execution on activation
fallback, Begin observer lifecycle bypass, End observer lifecycle bypass, and non-abstract
`OnCreate`/`OnDestroy`. After the single fix wave:

```text
DAG/event focused default: 60 passed, 0 failed
DAG/event focused ulong:   60 passed, 0 failed
Full default:             383 passed, 0 failed
Full ulong:               383 passed, 0 failed
Kernel build: net8.0 + netstandard2.1, 0 warnings, 0 errors
git diff --check: clean
```

The full test builds continue to emit only the repository's pre-existing nullable/NUnit analyzer
warnings in unrelated tests. No ledger changes or subagents were used.
