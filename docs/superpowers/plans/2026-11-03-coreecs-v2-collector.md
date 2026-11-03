# CoreECS v2 Collector Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the old per-Collector matching/journal logic with the approved central pump, TypeId inverted indexes, pending net-membership state, and public sealed EntityCollector.

**Architecture:** RW writes append at most one `(EntityId,TypeId)` reason per global pump epoch. Any Collector.Flush pumps the global journal once and distributes reasons to all relevant Collector pending sets; only the calling Collector evaluates final matcher state and publishes buffers.

**Tech Stack:** C# 9, EntityMatcher frozen plans, sparse sets, reusable buffers, NUnit internal diagnostics.

**Spec:** `docs/superpowers/specs/2026-11-03-coreecs-v2-simplified-design.md`

## Global Constraints

- Collector is public sealed IDisposable; delete IEntityCollector and Change().
- Do not pump at EndTick.
- Structural events mark pending through TypeId subscriber indexes; they do not run the complete matcher synchronously.
- Only Flush publishes Collected/Matching/Clashing/Changed.
- Membership output is net start/end state; relevant structural bounce can still produce Changed.
- Revision eligibility uses final matcher state at the calling Collector's Flush.

## Review Focus

- A late Collector must not receive pre-creation writes still sitting in the journal; Task 2 tests two distinct entities around CreatedSequence.
- Flushing C1 must pump reasons for C2 without publishing C2; Task 3 tests interleaved flush order.
- Index reuse must never apply an old journal reason to a new generation; Task 2 tests full EntityId keys.
- Dispose during/after shutdown must be idempotent and remove every inverted-index registration; Task 4 tests it.
- Empty/none-only matchers must receive entity lifecycle pending reasons despite having no positive storage term; Task 3 tests it.

## Standard Verification

- Default full suite: `dotnet test CoreECS.sln --configuration Release`
- Ulong full suite: `dotnet test CoreECS.sln --configuration Release -p:CoreECSUseUlongEntity=true`
- Kernel dual-target build: `dotnet build Kernel/Kernel.csproj --configuration Release`

---

### Task 1: Add central change journal and pump epochs

**Files:**
- Create: `Kernel/Structures/ComponentChangeJournal.cs`
- Modify: `Kernel/Structures/ComponentHandler.cs`
- Modify: `Kernel/Managers/ComponentManager.cs`
- Create: `Test/ComponentChangeJournalTestUnit.cs`

**Interfaces:**
- Produces: append-only value-type entries `(Sequence,EntityId,TypeId)`; `CurrentEpoch`, `NextSequence`, `AdvanceEpoch()`, `Pump(Action<Entry>)`; per-handler LastJournalEpoch; change-interest toggle.

- [ ] **Step 1: Write failing journal tests**

Assert N RW acquisitions increment Revision N but append one reason before pump; pump clears/advances epoch; explicit AdvanceEpoch without pump makes the next RW append while retaining old entries; different TypeIds/entities append separately; no-interest path appends nothing; removed/reused index keeps full generation.

- [ ] **Step 2: Verify red**

- [ ] **Step 3: Implement journal and connect RW sink**

Capture the journal tail before pumping so internal work cannot unboundedly chase newly appended entries. There are no public component change events.

- [ ] **Step 4: Verify full tests and commit**

```bash
git add Kernel/Structures/ComponentChangeJournal.cs Kernel/Structures/ComponentHandler.cs Kernel/Managers/ComponentManager.cs Test/ComponentChangeJournalTestUnit.cs
git commit -m "feat(core): add central component change journal"
```

### Task 2: Add Collector indexes and pending sparse state

**Files:**
- Rewrite: `Kernel/Managers/EntityMatchManager.cs`
- Create: `Kernel/Structures/CollectorSubscriptions.cs`
- Create: `Kernel/Structures/CollectorPendingSet.cs`
- Create: `Test/CollectorSubscriptionTestUnit.cs`

**Interfaces:**
- Produces structural TypeId subscribers, revision TypeId subscribers, wildcard revision list, lifecycle-sensitive list; pending EntityId→reason flags; CreatedSequence gate.

- [ ] **Step 1: Write failing inverted-index tests**

Register Position, Velocity, wildcard, empty, and none-only Collectors. Assert Position structural/revision reasons touch only eligible collectors, wildcard receives all revision types, lifecycle reasons reach empty/none-only, and Dispose unlinks every list.

- [ ] **Step 2: Write failing CreatedSequence/generation tests**

Use Epre written before Collector creation and Epost after creation; after another Collector triggers pump, late Collector receives only Epost. Also write the same entity/component before and after late Collector creation and prove AdvanceEpoch forces a second entry so the late Collector observes the post-create write. Destroy/reuse an index and prove old full EntityId reason never marks the new entity.

- [ ] **Step 3: Implement subscriptions and pending set**

Creating a Collector calls `journal.AdvanceEpoch()` without pumping, then captures CreatedSequence and registers subscriptions. Pending set stores one slot per full EntityId and ORs reason flags/type relevance. Keep diagnostics for delivered/unrelated counts under InternalsVisibleTo.

- [ ] **Step 4: Verify and commit**

```bash
git add Kernel/Managers/EntityMatchManager.cs Kernel/Structures/CollectorSubscriptions.cs Kernel/Structures/CollectorPendingSet.cs Test/CollectorSubscriptionTestUnit.cs
git commit -m "feat(core): index collector subscriptions by component type"
```

### Task 3: Implement public EntityCollector and net membership Flush

**Files:**
- Create: `Kernel/EntityCollector.cs`
- Create: `Kernel/EntityCollectorFlag.cs`
- Modify: `Kernel/World.cs`
- Delete: `Kernel/Defines/IEntityCollector.cs`
- Rewrite: `Test/EntityCollectorTestUnit.cs`
- Add: `Test/CollectorNetStateTestUnit.cs`
- Add: `Test/CollectorPumpOrderingTestUnit.cs`

**Interfaces:**
- Produces final public EntityCollector API and World.CreateCollector(EntityMatcher,EntityCollectorFlag flag=Default).

- [ ] **Step 1: Port unchanged master Collector behavior tests to EntityCollector**

Convert IDs to EntityId and interfaces to concrete class. Preserve flags and initial first-Flush publication.

- [ ] **Step 2: Change the four audited structural-bounce expectations**

For the four spec-listed test names, assert Matching/Clashing empty, Collected contains E, Changed contains E once.

- [ ] **Step 3: Add failing net-state property tests**

Cover initial match then exit before first Flush, unmatched add/remove, matched remove/add, net enter, net exit, relevant bounce Changed, write→exit, write→enter, and one matcher evaluation per pending EntityId.

- [ ] **Step 4: Add failing multi-Collector pump tests**

Cover reverse flush order and exact sequence `W1 → C1.Flush → W2 → C2.Flush → C1.Flush → C2.Flush`; assert pump count, per-collector publication timing, and no lost/duplicate Changed.

- [ ] **Step 5: Implement EntityCollector.Flush**

Order: manager PumpChanges once; swap/clear published phase buffers; evaluate each pending full EntityId exactly once against final world state; compute previous Collected membership vs final match; apply flags to Changed; update packed membership; clear pending while retaining capacity.

- [ ] **Step 6: Verify full suite and commit**

```bash
git add Kernel/EntityCollector.cs Kernel/EntityCollectorFlag.cs Kernel/World.cs Kernel/Defines/IEntityCollector.cs Test/EntityCollectorTestUnit.cs Test/CollectorNetStateTestUnit.cs Test/CollectorPumpOrderingTestUnit.cs
git commit -m "feat(core): add net-state entity collectors"
```

### Task 4: Remove migration bridges and harden lifecycle

**Files:**
- Delete: `Kernel/EntityGraph.cs`
- Remove compatibility members from: `Kernel/Defines/ComponentRef.cs`
- Modify: `Kernel/Managers/EntityManager.cs`
- Modify: `Kernel/Managers/ComponentManager.cs`
- Modify: `Kernel/Managers/EntityMatchManager.cs`
- Add: `Test/CollectorLifecycleTestUnit.cs`
- Remove/replace: old structure-cache tests if copied from v2.

**Interfaces:**
- Produces final bridge-free handler/entity/collector integration and idempotent shutdown/dispose.

- [ ] **Step 1: Add failing lifecycle tests**

Dispose three times; dispose one of two collectors without affecting the other; shutdown with live collector; dispose twice after shutdown; all other member access after dispose throws ObjectDisposedException; EndTick diagnostic confirms it does not pump.

- [ ] **Step 2: Remove EntityGraph/list/event compatibility path**

Entity destruction uses only handler intrusive list. ComponentManager talks to EntityMatchManager through internal structural/revision methods, not public signals.

- [ ] **Step 3: Remove old implementation-detail tests**

Delete exact StructureEvaluationCount/per-Structure cache and old cursor performance constants. Replace only with diagnostics for pump count, delivered subscriber count, and matcher-once invariants.

- [ ] **Step 4: Run Collector, Integration, Stress, full default/ulong tests**

- [ ] **Step 5: Commit**

```bash
git add Kernel Test
git commit -m "refactor(core): remove legacy collector bridges"
```
