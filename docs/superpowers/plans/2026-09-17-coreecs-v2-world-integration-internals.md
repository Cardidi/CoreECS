# CoreECS v2 World 集成内核 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 v2 archetype 内核接入 `World` 集成层：先落地实体注册表 `EntityTable`，随后在同一计划中追加 `Entity` / `ComponentRef` / 编排层 / 匹配求值等任务，最终删除 v1 存储并迁移内部测试。

**Architecture:** 新增 `CoreECS.Structures.EntityTable`，承担实体 id 单调分配与 `entityId → EntityLocation` 注册；location 从 `EntityLocation.Pool` 取用、销毁时归还，generation 在归还时递增，为后续 `Entity` / `ComponentRef` 的失效检测提供基础。内核类型保持 `internal`，测试经 `InternalsVisibleTo("Test")` 直接访问；现有 v1 `World` 集成暂不改动，由后续任务逐步切换。

**Tech Stack:** C# 9（`LangVersion 9`）、`net8.0` + `netstandard2.1`、NUnit 3.14、`dotnet test --filter`

**Spec:** `docs/superpowers/specs/2026-09-17-coreecs-v2-design.md`

**Handoff:** `docs/superpowers/plans/2026-09-17-coreecs-v2-handoff.md`（Plan 1b 范围与设计约束）

---

## File Structure

| 文件 | 职责 |
|---|---|
| `ECS/Structures/EntityTable.cs` | 实体 id 单调分配 + `entityId → EntityLocation` 注册表；`Create` / `Destroy` / `TryGetLocation` / `Count` |
| `Test/EntityTableTestUnit.cs` | `EntityTable` 单元测试（id 分配、查找、销毁归还 location、generation 失效检测、未知 id no-op） |
| `ECS/Structures/ComponentRefCore.cs` | 无类型组件引用核心：`EntityLocation` + generation + typeId + kind + version；`NotNull` / `EntityId` / `Revision` / `ChangeRevision()` |
| `ECS/Structures/Structure.cs` | （Task 2 修改）新增 6 个按 typeId 读写 dense/discrete version/revision 的 internal 非泛型方法，供无类型核心分发；（Task 3 修改）新增 `SpareSetOrNull` 只读访问器 |
| `Test/ComponentRefCoreTestUnit.cs` | `ComponentRefCore` 单元测试（dense/discrete/tag 有效性、revision 递增、generation 失效） |
| `ECS/Structures/ComponentHookDispatcher.cs` | 按 typeId 缓存 dense/discrete 的 `OnCreate` / `OnDestroy` 委托（`ComponentHookPair`），供无类型编排代码分发 |
| `ECS/Structures/ComponentOrchestrator.cs` | 实体生命周期 + 非 Dense 组件操作 + Dense 组件迁移：`CreateEntity` / `DestroyEntity` / `HasComponent` / `GetComponentRef` / discrete 与 tag 增删 / `AddDenseComponent` / `RemoveDenseComponent` |
| `ECS/Structures/SpareSetComponentContainer.cs` | （Task 3 修改）新增 `TypeIds` 枚举，供销毁时遍历存在的 discrete 存储 |
| `Test/ComponentOrchestratorTestUnit.cs` | `ComponentOrchestrator` 单元测试（生命周期、discrete/tag 增删、Dense 迁移、hook 调用、观察者事件） |
| `ECS/EntityMatcher.cs` | （Task 5 修改）新增 internal `ComponentFilter(Structure structure, int row)`：mask 交集 + Dense 结构级 + Tag/Discrete row 级求值；`OfAll` / `OfAny` / `OfNone` 在配置时把类型解析为 per-kind typeId 集合；v1 `ComponentFilter(IReadOnlyCollection<IComponentRefCore>)` 保留 |
| `Test/EntityMatcherStructureTestUnit.cs` | `EntityMatcher` 的 Structure 求值单元测试（all-of dense/tag/discrete、跨 kind 的 none/any、mask 交集、空 matcher、三集合组合、`IsRelevantComponent` 回归） |

测试文件统一放 `Test/`，命名 `<TypeName>TestUnit.cs`，风格与现有测试一致（classic asserts；`Test.csproj` 已通过 `<Using Include="NUnit.Framework"/>` 提供全局 using，测试无需显式 `using NUnit.Framework;`）。

## 本计划范围边界

本计划覆盖 handoff 第 3 节 Plan 1b 的**五个内核任务**（Task 1 `EntityTable`、Task 2 `ComponentRefCore`、Task 3 `ComponentOrchestrator`、Task 4 Dense 组件迁移、Task 5 matcher 的 Structure 求值），至此 **Plan 1b 完成**；handoff 第 3 节约束 1-5 已全部映射到 Task 1-5（见 Self-Review 记录第 18 条）。

**Plan 1c（公开 API 切换，下一计划）** 将在新计划文件中编写，覆盖 handoff 第 3 节剩余约束：

- 约束 6：`EntityMatchManager` 接线——`_changeCollector` 改用 Task 5 的 `ComponentFilter(Structure, row)`，订阅/退订信号逻辑保留
- 约束 2/3：`Entity` v2（`CreateComponent<T>` / `DestroyComponent<T>` / `GetComponent<T>` / `HasComponent<T>`）、`EntityExtension` 适配、公开 `ComponentRef` / `ComponentRef<T>` 切换到 Task 2 的 `ComponentRefCore`
- 约束 7/9：`World` / `MinimalWorld` 切换与 `CreateEntity(mask)` 选择初始结构
- 约束 8：删除 v1 存储（`ComponentStore<T>`、v1 `ComponentRefCore`、`IComponentRefLocator`、`EntityGraph` 等）、删除 Task 5 保留的 v1 `EntityMatcher.ComponentFilter(IReadOnlyCollection<IComponentRefCore>)`、迁移内部测试

**不包括**（spec 明确后置）：系统分组排序（Phase 3/4）、`IEntityQuery`（Phase 3）、World 合并与生命周期收敛（Phase 4）、CommandBuffer（Phase 5）。

---

## Task 1: EntityTable（实体注册表）

**Files:**
- Create: `ECS/Structures/EntityTable.cs`
- Test: `Test/EntityTableTestUnit.cs`

- [ ] **Step 1: 写失败测试**

创建 `Test/EntityTableTestUnit.cs`：

```csharp
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class EntityTableTestUnit
    {
        [Test]
        public void Create_AllocatesUniqueIncreasingIdsAndDistinctLocations()
        {
            var table = new EntityTable();

            var first = table.Create();
            var second = table.Create();

            Assert.AreEqual(1UL, first.EntityId);
            Assert.AreEqual(2UL, second.EntityId);
            Assert.Less(first.EntityId, second.EntityId);
            Assert.AreNotSame(first.Location, second.Location);
            Assert.AreEqual(2, table.Count);
        }

        [Test]
        public void TryGetLocation_ReturnsRegisteredLocationAndFalseForUnknownId()
        {
            var table = new EntityTable();
            var created = table.Create();

            Assert.IsTrue(table.TryGetLocation(created.EntityId, out var location));
            Assert.AreSame(created.Location, location);

            Assert.IsFalse(table.TryGetLocation(created.EntityId + 1UL, out var unknown));
            Assert.IsNull(unknown);
        }

        [Test]
        public void Destroy_RemovesEntryAndReleasesLocationToPool()
        {
            var table = new EntityTable();
            var created = table.Create();
            var generation = created.Location.Generation;

            table.Destroy(created.EntityId);

            Assert.AreEqual(0, table.Count);
            Assert.IsFalse(table.TryGetLocation(created.EntityId, out _));
            Assert.AreEqual(generation + 1U, created.Location.Generation);
        }

        [Test]
        public void Create_AfterDestroy_ReusesReleasedLocationWithNewerGeneration()
        {
            // Drain the shared pool so the released location is the only reuse candidate.
            EntityLocation.Pool.Clear();

            var table = new EntityTable();
            var first = table.Create();
            var staleLocation = first.Location;
            var staleGeneration = staleLocation.Generation;

            table.Destroy(first.EntityId);
            var second = table.Create();

            Assert.AreNotEqual(first.EntityId, second.EntityId);
            Assert.AreSame(staleLocation, second.Location);
            Assert.Greater(second.Location.Generation, staleGeneration);
            Assert.IsFalse(table.TryGetLocation(first.EntityId, out _));
            Assert.IsTrue(table.TryGetLocation(second.EntityId, out var current));
            Assert.AreSame(second.Location, current);
        }

        [Test]
        public void Destroy_UnknownId_IsNoOp()
        {
            var table = new EntityTable();
            var created = table.Create();

            table.Destroy(created.EntityId + 100UL);

            Assert.AreEqual(1, table.Count);
            Assert.IsTrue(table.TryGetLocation(created.EntityId, out var location));
            Assert.AreSame(created.Location, location);
        }
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --filter FullyQualifiedName~EntityTableTestUnit`
Expected: 编译失败，`EntityTable` 不存在

- [ ] **Step 3: 实现 EntityTable**

创建 `ECS/Structures/EntityTable.cs`：

```csharp
using System.Collections.Generic;

namespace CoreECS.Structures
{
    /// <summary>
    /// Owns entity id allocation and maps live entity ids to pooled
    /// <see cref="EntityLocation"/> instances. One table per world.
    /// Ids increase monotonically and are never reused within a table.
    /// </summary>
    internal sealed class EntityTable
    {
        private readonly Dictionary<ulong, EntityLocation> m_locations = new();
        private ulong m_nextId;

        /// <summary>Number of live entities.</summary>
        public int Count => m_locations.Count;

        /// <summary>
        /// Allocates the next entity id, takes a location from
        /// <see cref="EntityLocation.Pool"/> and registers the pair.
        /// </summary>
        /// <returns>The new entity id and its location.</returns>
        public (ulong EntityId, EntityLocation Location) Create()
        {
            var entityId = NextId();
            var location = EntityLocation.Pool.Get();
            m_locations.Add(entityId, location);
            return (entityId, location);
        }

        /// <summary>
        /// Removes the entity entry and returns its location to the pool,
        /// which advances the location's generation so stale references can be detected.
        /// Unknown ids are ignored (no-op), making destroy idempotent for callers.
        /// </summary>
        public void Destroy(ulong entityId)
        {
            if (!m_locations.TryGetValue(entityId, out var location)) return;

            m_locations.Remove(entityId);
            EntityLocation.Pool.Release(location);
        }

        /// <summary>
        /// Tries to get the current location of a live entity id.
        /// </summary>
        /// <returns>True when the id is live; otherwise false with a null location.</returns>
        public bool TryGetLocation(ulong entityId, out EntityLocation location)
        {
            return m_locations.TryGetValue(entityId, out location);
        }

        private ulong NextId() => ++m_nextId;
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --filter FullyQualifiedName~EntityTableTestUnit`
Expected: PASS（5 个测试）

- [ ] **Step 5: 运行全量测试**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj`
Expected: 405 passed（400 基线 + 新增 5），0 failed

- [ ] **Step 6: 提交**

```bash
git add ECS/Structures/EntityTable.cs Test/EntityTableTestUnit.cs
git commit -m "feat(core): add entity table over pooled entity locations"
```

---

## Task 2: ComponentRefCore v2（组件引用核心）

**Files:**
- Create: `ECS/Structures/ComponentRefCore.cs`
- Modify: `ECS/Structures/Structure.cs`（在 `ChangeDiscreteRevision<T>` 之后新增 6 个按 typeId 访问 version/revision 的 internal 非泛型方法）
- Test: `Test/ComponentRefCoreTestUnit.cs`

说明：`ComponentRefCore` 是**无类型**核心，`NotNull` / `Revision` / `ChangeRevision()` 需要按 `TypeId` 读取组件实例版本与修订号，而 `Structure` 现有的 version/revision 访问器全部是泛型的（`GetDenseVersion<T>` / `GetDiscreteVersion<T>` 等），无类型类无法调用。因此在 `Structure` 上补充 6 个 internal 非泛型访问器（纯新增，不改动现有泛型方法及其行为）。

- [ ] **Step 1: 写失败测试**

创建 `Test/ComponentRefCoreTestUnit.cs`：

```csharp
using CoreECS.Defines;
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class ComponentRefCoreTestUnit
    {
        private struct Position : IComponent<Position>
        {
            public int X;
        }

        private struct ManaComponent : IDiscreteComponent<ManaComponent>
        {
            public int Value;
        }

        private struct PlayerTag : ITagComponent<PlayerTag>
        {
        }

        private static uint IdOf<T>() where T : struct, IComponent<T>
            => ComponentTypeRegistry.GetOrRegister<T>().TypeId;

        private static Structure MakeStructure()
        {
            return new Structure(new StructureKey(new[] { IdOf<Position>() }, 0));
        }

        [Test]
        public void DenseRef_NotNull_TracksPresenceAndVersion()
        {
            var structure = MakeStructure();
            var location = EntityLocation.Pool.Get();
            var row = structure.Append(7, location);
            structure.SetDenseValue(row, new Position { X = 1 }, 5);

            var matching = new ComponentRefCore(location, location.Generation, IdOf<Position>(), ComponentKind.Dense, 5);
            var staleVersion = new ComponentRefCore(location, location.Generation, IdOf<Position>(), ComponentKind.Dense, 6);

            Assert.IsTrue(matching.NotNull);
            Assert.IsFalse(staleVersion.NotNull);
        }

        [Test]
        public void DenseRef_EntityId_Revision_And_ChangeRevision()
        {
            var structure = MakeStructure();
            var location = EntityLocation.Pool.Get();
            var row = structure.Append(11, location);
            structure.SetDenseValue(row, new Position { X = 1 }, 3);

            var core = new ComponentRefCore(location, location.Generation, IdOf<Position>(), ComponentKind.Dense, 3);

            Assert.AreEqual(11UL, core.EntityId);
            Assert.AreEqual(0u, core.Revision);
            Assert.AreEqual(1u, core.ChangeRevision());
            Assert.AreEqual(1u, core.Revision);
        }

        [Test]
        public void DiscreteRef_NotNull_TracksPresenceAndVersion()
        {
            var structure = MakeStructure();
            var location = EntityLocation.Pool.Get();
            var row = structure.Append(3, location);
            structure.SetDiscrete(row, new ManaComponent { Value = 4 }, 9);

            var matching = new ComponentRefCore(location, location.Generation, IdOf<ManaComponent>(), ComponentKind.Discrete, 9);
            var staleVersion = new ComponentRefCore(location, location.Generation, IdOf<ManaComponent>(), ComponentKind.Discrete, 10);

            Assert.IsTrue(matching.NotNull);
            Assert.IsFalse(staleVersion.NotNull);

            structure.RemoveDiscrete(IdOf<ManaComponent>(), row);

            Assert.IsFalse(matching.NotNull);
        }

        [Test]
        public void DiscreteRef_Revision_And_ChangeRevision()
        {
            var structure = MakeStructure();
            var location = EntityLocation.Pool.Get();
            var row = structure.Append(3, location);
            structure.SetDiscrete(row, new ManaComponent { Value = 4 }, 9);

            var core = new ComponentRefCore(location, location.Generation, IdOf<ManaComponent>(), ComponentKind.Discrete, 9);

            Assert.AreEqual(0u, core.Revision);
            Assert.AreEqual(1u, core.ChangeRevision());
            Assert.AreEqual(1u, core.Revision);
        }

        [Test]
        public void TagRef_NotNull_TracksTagBitOnly()
        {
            var structure = MakeStructure();
            var location = EntityLocation.Pool.Get();
            var row = structure.Append(5, location);

            var core = new ComponentRefCore(location, location.Generation, IdOf<PlayerTag>(), ComponentKind.Tag, 0);

            Assert.IsFalse(core.NotNull);

            structure.AddTag(IdOf<PlayerTag>(), row);

            Assert.IsTrue(core.NotNull);
            Assert.AreEqual(0u, core.Revision);
            Assert.AreEqual(0u, core.ChangeRevision());

            structure.RemoveTag(IdOf<PlayerTag>(), row);

            Assert.IsFalse(core.NotNull);
        }

        [Test]
        public void StaleLocation_GenerationMismatch_InvalidatesRef()
        {
            var structure = MakeStructure();
            var location = EntityLocation.Pool.Get();
            var row = structure.Append(8, location);
            structure.SetDenseValue(row, new Position { X = 2 }, 1);

            var core = new ComponentRefCore(location, location.Generation, IdOf<Position>(), ComponentKind.Dense, 1);
            Assert.IsTrue(core.NotNull);

            EntityLocation.Pool.Release(location);

            Assert.IsFalse(core.NotNull);
            Assert.AreEqual(0UL, core.EntityId);
        }

        [Test]
        public void RecycledLocation_ReboundToNewStructureWithNewerGeneration_InvalidatesRef()
        {
            // Drain the pool so the released location is the only reuse candidate.
            EntityLocation.Pool.Clear();

            var structure = MakeStructure();
            var location = EntityLocation.Pool.Get();
            var row = structure.Append(8, location);
            structure.SetDenseValue(row, new Position { X = 2 }, 1);

            var core = new ComponentRefCore(location, location.Generation, IdOf<Position>(), ComponentKind.Dense, 1);
            Assert.IsTrue(core.NotNull);

            structure.SwapRemove(row);
            EntityLocation.Pool.Release(location);

            // The pool hands the same instance back, rebound to a fresh structure whose
            // dense column carries the same type and version; only the generation differs.
            var recycled = EntityLocation.Pool.Get();
            Assert.AreSame(location, recycled);
            var reborn = MakeStructure();
            var rebornRow = reborn.Append(99, recycled);
            reborn.SetDenseValue(rebornRow, new Position { X = 2 }, 1);

            Assert.AreNotEqual(core.Generation, recycled.Generation);
            Assert.IsFalse(core.NotNull);
            Assert.AreEqual(0UL, core.EntityId);
        }

        [Test]
        public void StaleRow_AfterSwapRemoveWithoutRelease_ReportsInvalid()
        {
            var structure = MakeStructure();
            var location = EntityLocation.Pool.Get();
            var row = structure.Append(8, location);
            structure.SetDenseValue(row, new Position { X = 2 }, 1);

            var core = new ComponentRefCore(location, location.Generation, IdOf<Position>(), ComponentKind.Dense, 1);
            Assert.IsTrue(core.NotNull);

            // SwapRemove leaves the removed location untouched for the caller to release;
            // the ref must not read the vacated row in the window before that release.
            structure.SwapRemove(row);

            Assert.IsFalse(core.NotNull);
            Assert.AreEqual(0UL, core.EntityId);
        }
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --filter FullyQualifiedName~ComponentRefCoreTestUnit`
Expected: 编译失败，`ComponentRefCore` 不存在

- [ ] **Step 3: 实现 Structure 非泛型访问器与 ComponentRefCore**

在 `ECS/Structures/Structure.cs` 中 `ChangeDiscreteRevision<T>` 方法之后、`CopyDenseTo` 之前插入：

```csharp
        /// <summary>
        /// Gets the dense component instance version at the row by type id.
        /// Returns 0 when the type is absent; the row must be live.
        /// </summary>
        internal uint GetDenseVersion(uint typeId, int row)
        {
            Debug.Assert(row >= 0 && row < m_count, "Row must be live.");
            var slot = IndexOfDense(typeId);
            return slot < 0 ? 0u : m_denseVersions[slot][row];
        }

        /// <summary>
        /// Gets the dense component revision at the row by type id.
        /// Returns 0 when the type is absent; the row must be live.
        /// </summary>
        internal uint GetDenseRevision(uint typeId, int row)
        {
            Debug.Assert(row >= 0 && row < m_count, "Row must be live.");
            var slot = IndexOfDense(typeId);
            return slot < 0 ? 0u : m_denseRevisions[slot][row];
        }

        /// <summary>
        /// Bumps the dense component revision by type id and notifies the observer.
        /// Returns 0 when the type is absent; the row must be live.
        /// </summary>
        internal uint ChangeDenseRevision(uint typeId, int row)
        {
            Debug.Assert(row >= 0 && row < m_count, "Row must be live.");
            var slot = IndexOfDense(typeId);
            if (slot < 0) return 0u;

            var revision = (m_denseRevisions[slot][row] % uint.MaxValue) + 1;
            m_denseRevisions[slot][row] = revision;
            Observer?.OnComponentChanged(this, row, typeId);
            return revision;
        }

        /// <summary>
        /// Gets the discrete component instance version at the row by type id.
        /// Returns 0 when the component is absent; the row must be live.
        /// </summary>
        internal uint GetDiscreteVersion(uint typeId, int row)
        {
            Debug.Assert(row >= 0 && row < m_count, "Row must be live.");
            var store = m_spareSet?.GetStore(typeId);
            return store == null ? 0u : store.GetVersion(row);
        }

        /// <summary>
        /// Gets the discrete component revision at the row by type id.
        /// Returns 0 when the component is absent; the row must be live.
        /// </summary>
        internal uint GetDiscreteRevision(uint typeId, int row)
        {
            Debug.Assert(row >= 0 && row < m_count, "Row must be live.");
            var store = m_spareSet?.GetStore(typeId);
            return store == null ? 0u : store.GetRevision(row);
        }

        /// <summary>
        /// Bumps the discrete component revision by type id and notifies the observer.
        /// Returns 0 when the component is absent; the row must be live.
        /// </summary>
        internal uint ChangeDiscreteRevision(uint typeId, int row)
        {
            Debug.Assert(row >= 0 && row < m_count, "Row must be live.");
            var store = m_spareSet?.GetStore(typeId);
            if (store == null || !store.Has(row)) return 0u;

            var revision = store.ChangeRevision(row);
            Observer?.OnComponentChanged(this, row, typeId);
            return revision;
        }
```

创建 `ECS/Structures/ComponentRefCore.cs`：

```csharp
using CoreECS.Defines;

namespace CoreECS.Structures
{
    /// <summary>
    /// Typeless core shared by component reference handles. Holds the entity location,
    /// the location generation captured at creation, the component type id/kind and the
    /// component instance version. Every accessor resolves through the live location,
    /// so references stay valid across migrations and in-structure swap-removes.
    /// </summary>
    internal sealed class ComponentRefCore
    {
        /// <summary>Location shared with the owning entity; may be recycled after destroy.</summary>
        public EntityLocation Location { get; }

        /// <summary>Generation captured at creation; detects recycled locations.</summary>
        public uint Generation { get; }

        /// <summary>Registered component type id.</summary>
        public uint TypeId { get; }

        /// <summary>Storage kind of the referenced component.</summary>
        public ComponentKind Kind { get; }

        /// <summary>Component instance version captured at creation.</summary>
        public uint Version { get; }

        /// <summary>
        /// Creates a component reference core.
        /// </summary>
        public ComponentRefCore(EntityLocation location, uint generation, uint typeId, ComponentKind kind, uint version)
        {
            Location = location;
            Generation = generation;
            TypeId = typeId;
            Kind = kind;
            Version = version;
        }

        /// <summary>
        /// True when the location is alive (structure bound and generation matches), the
        /// row is within the structure, the component is present at the location row and,
        /// for dense/discrete components, the stored instance version matches. Tags are
        /// presence-only. A stale row left behind by an in-structure swap-remove (before
        /// the location is released) reports false instead of reading out of range.
        /// </summary>
        public bool NotNull
        {
            get
            {
                var structure = Location?.Structure;
                if (structure == null || Location.Generation != Generation) return false;

                var row = Location.Row;
                switch (Kind)
                {
                    case ComponentKind.Dense:
                        return row >= 0 && row < structure.Count &&
                               structure.HasDense(TypeId) &&
                               structure.GetDenseVersion(TypeId, row) == Version;
                    case ComponentKind.Discrete:
                        return structure.HasDiscrete(TypeId, row) &&
                               structure.GetDiscreteVersion(TypeId, row) == Version;
                    case ComponentKind.Tag:
                        return structure.HasTag(TypeId, row);
                    default:
                        return false;
                }
            }
        }

        /// <summary>Entity id owning the referenced component, or 0 when this ref is not valid.</summary>
        public ulong EntityId
        {
            get
            {
                if (!NotNull) return 0UL;
                return Location.Structure.Entities[Location.Row];
            }
        }

        /// <summary>Current component revision; 0 for tags and invalid refs.</summary>
        public uint Revision
        {
            get
            {
                if (!NotNull) return 0u;
                switch (Kind)
                {
                    case ComponentKind.Dense:
                        return Location.Structure.GetDenseRevision(TypeId, Location.Row);
                    case ComponentKind.Discrete:
                        return Location.Structure.GetDiscreteRevision(TypeId, Location.Row);
                    default:
                        return 0u;
                }
            }
        }

        /// <summary>
        /// Bumps and returns the component revision (notifying the structure observer).
        /// Returns 0 for tags and invalid refs.
        /// </summary>
        public uint ChangeRevision()
        {
            if (!NotNull) return 0u;
            switch (Kind)
            {
                case ComponentKind.Dense:
                    return Location.Structure.ChangeDenseRevision(TypeId, Location.Row);
                case ComponentKind.Discrete:
                    return Location.Structure.ChangeDiscreteRevision(TypeId, Location.Row);
                default:
                    return 0u;
            }
        }
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --filter FullyQualifiedName~ComponentRefCoreTestUnit`
Expected: PASS（8 个测试）

- [ ] **Step 5: 运行全量测试**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj`
Expected: 413 passed（Task 1 后 405 + 新增 8），0 failed

- [ ] **Step 6: 提交**

```bash
git add ECS/Structures/Structure.cs ECS/Structures/ComponentRefCore.cs Test/ComponentRefCoreTestUnit.cs
git commit -m "feat(core): add component ref core over entity locations"
```

---

## Task 3: ComponentOrchestrator（实体生命周期与非 Dense 组件操作）

**Files:**
- Create: `ECS/Structures/ComponentHookDispatcher.cs`
- Create: `ECS/Structures/ComponentOrchestrator.cs`
- Modify: `ECS/Structures/Structure.cs`（在 `SpareSet` 属性之后新增 `SpareSetOrNull`）
- Modify: `ECS/Structures/SpareSetComponentContainer.cs`（在 `Count` 属性之后新增 `TypeIds`）
- Test: `Test/ComponentOrchestratorTestUnit.cs`

前置：Task 1（`EntityTable`）与 Task 2（`ComponentRefCore` + `Structure` 非泛型访问器）已实现；按 Task 1 → 2 → 3 顺序执行。

设计说明（执行时不要改动，评审时按此核对）：
- `ComponentOrchestrator` 不持有组件数据，全部读写经 `Structure`；discrete/tag 增删直接走 `Structure.SetDiscrete` / `AddTag` / `RemoveDiscrete` / `RemoveTag`，由 structure 触发观察者事件（`CreateEntity` 时把注入的 observer 接到选中的 structure 上）。
- `OnCreate` / `OnDestroy` 经 `ComponentHookDispatcher` 按 typeId 分发：泛型调用点（`AddDiscreteComponent<T>` / `RemoveDiscreteComponent<T>`；Task 4 的 Dense 增删同理）先 `RegisterDense<T>` / `RegisterDiscrete<T>` 生成并缓存委托，`DestroyEntity` 只按 typeId 调用非泛型 `InvokeDenseDestroy` / `InvokeDiscreteDestroy`。委托由泛型静态方法创建，内核不使用反射（netstandard2.1 / IL2CPP 友好）。未注册 hook 的类型在销毁时被跳过：这只可能发生在绕过编排层直接操作内核结构的场景，经编排层添加的组件一定已注册。
- Tag 只有 presence（无数据、无生命周期 hook）：`AddTagComponent` / `RemoveTagComponent` 只翻转 tag 位并触发观察者事件；`DestroyEntity` 对 tag 不做 hook 调用。
- Hook 时序：`OnCreate` 在组件数据写入之后调用，`OnDestroy` 在移除之前调用（委托内用 `GetDenseRef<T>` / `GetDiscreteRef<T>` 读取存储实例），与 v1 `ComponentManager.Fix` / `Release` 在存储槽上调用 hook 的语义一致。
- `GetComponentRef<T>`：实体不存在或组件缺失时返回 `null`；tag 存在时返回 presence-only 核心（`Kind = Tag`、`Version = 0`）。
- 修改 `Structure` / `SpareSetComponentContainer` 的理由：`DestroyEntity` 必须遍历该 structure 上实际存在的 discrete 存储，而容器现有 API 只能按已知 typeId 查询，没有枚举；且不能用 `SpareSet` 懒加载 getter，因为销毁路径不应为没有 discrete 组件的结构分配容器。两处均为纯新增，不改变现有行为。
- **销毁重入安全（评审修订）**：`DestroyEntity` 用 `m_destroying` 守卫集合 + try/finally——同一实体的 hook 内重入销毁直接 no-op（不会二次 swap-remove 同一行）；其他实体的重入销毁由内核在 `SwapRemove` 时更新被移动实体的 `location.Row`，因此最终用 `location.Structure?.SwapRemove(location.Row)` 重读行绑定（不缓存旧 row），保证行位移后仍移除正确行；discrete 存储 id 在调用 hook 前快照为 `List<uint>`（hook 新建 discrete 存储不会在枚举 `m_stores` 时抛异常）；`finally` 中执行 `m_table.Destroy` 释放 location，异常路径不泄漏行与 location。hook 仍可访问正在销毁的实体（v1 语义），但对其做组件增删只会作用在即将移除的行上。
- **Hook 异常策略（对齐 v1）**：v1 `ComponentManager.Fix` / `Release` 对 `OnCreate` / `OnDestroy` 逐个 try/catch 并 `Log.Exp(e)`（`ECS/Managers/ComponentManager.cs:459-466`、`489-496`）；`ComponentHookDispatcher` 的四个 `Invoke*` 采用相同策略（catch + `Log.Exp`），hook 异常不中断编排流程（`Log.Logger` 未设置时 `Log.Exp` 为 no-op）。
- **委托缓存**：hook 委托对每个类型只构造一次（泛型静态持有类 `DenseHooks<T>` / `DiscreteHooks<T>` 的 `static readonly Pair`），`RegisterDense<T>` / `RegisterDiscrete<T>` 只做一次字典写入，避免每次调用分配两个委托。
- **Discrete 覆盖语义**：对已存在的 discrete 组件再次 `AddDiscreteComponent` 会用新版本覆盖值并再次触发 `OnCreate`（不触发 `OnDestroy`）；该语义写入方法文档。

- [ ] **Step 1: 写失败测试**

创建 `Test/ComponentOrchestratorTestUnit.cs`：

```csharp
using CoreECS.Defines;
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class ComponentOrchestratorTestUnit
    {
        private struct Position : IComponent<Position>
        {
            public int X;
        }

        private struct ManaComponent : IDiscreteComponent<ManaComponent>
        {
            public int Value;

            public void OnCreate(ulong entityId)
            {
                CreateCount += 1;
                CreateAction?.Invoke(entityId);
            }

            public void OnDestroy(ulong entityId)
            {
                DestroyCount += 1;
                LastDestroyedValue = Value;
                DestroyAction?.Invoke(entityId);
            }

            public static int CreateCount;
            public static int DestroyCount;
            public static int LastDestroyedValue;
            public static Action<ulong> CreateAction;
            public static Action<ulong> DestroyAction;
        }

        private struct OtherDiscrete : IDiscreteComponent<OtherDiscrete>
        {
            public int Value;
        }

        private struct DenseLifecycle : IComponent<DenseLifecycle>
        {
            public int Value;

            public void OnDestroy(ulong entityId)
            {
                DestroyCount += 1;
                LastDestroyedValue = Value;
            }

            public static int DestroyCount;
            public static int LastDestroyedValue;
        }

        private struct PlayerTag : ITagComponent<PlayerTag>
        {
        }

        private sealed class RecordingObserver : IStructureObserver
        {
            public readonly List<(uint TypeId, int Row)> Added = new();
            public readonly List<(uint TypeId, int Row)> Removed = new();
            public readonly List<(uint TypeId, int Row)> Changed = new();

            public void OnComponentAdded(Structure structure, int row, uint typeId) => Added.Add((typeId, row));

            public void OnComponentRemoved(Structure structure, int row, uint typeId) => Removed.Add((typeId, row));

            public void OnComponentChanged(Structure structure, int row, uint typeId) => Changed.Add((typeId, row));
        }

        private StructureRegistry m_registry;
        private EntityTable m_table;
        private RecordingObserver m_observer;
        private ComponentOrchestrator m_orchestrator;

        [SetUp]
        public void SetUp()
        {
            ManaComponent.CreateCount = 0;
            ManaComponent.DestroyCount = 0;
            ManaComponent.LastDestroyedValue = 0;
            ManaComponent.CreateAction = null;
            ManaComponent.DestroyAction = null;
            DenseLifecycle.DestroyCount = 0;
            DenseLifecycle.LastDestroyedValue = 0;
            m_registry = new StructureRegistry();
            m_table = new EntityTable();
            m_observer = new RecordingObserver();
            m_orchestrator = new ComponentOrchestrator(m_registry, m_table, m_observer);
        }

        private static uint IdOf<T>() where T : struct, IComponent<T>
            => ComponentTypeRegistry.GetOrRegister<T>().TypeId;

        [Test]
        public void CreateEntity_SelectsMaskStructureAndAppends()
        {
            var (entityId, location) = m_orchestrator.CreateEntity(0b101UL);

            var structure = m_registry.GetOrCreate(Array.Empty<uint>(), 0b101UL);
            Assert.AreEqual(1UL, entityId);
            Assert.AreSame(structure, location.Structure);
            Assert.AreEqual(0, location.Row);
            Assert.AreEqual(1, structure.Count);
            Assert.AreEqual(entityId, structure.Entities[0]);
            Assert.AreEqual(1, m_table.Count);

            var (secondId, secondLocation) = m_orchestrator.CreateEntity(0b010UL);
            Assert.AreEqual(2UL, secondId);
            Assert.AreNotSame(location.Structure, secondLocation.Structure);
            Assert.AreEqual(2, m_registry.Count);
        }

        [Test]
        public void DestroyEntity_ReleasesLocationAndEmptiesRow()
        {
            var (entityId, location) = m_orchestrator.CreateEntity();
            var structure = location.Structure;
            var generation = location.Generation;

            m_orchestrator.DestroyEntity(entityId);

            Assert.AreEqual(0, m_table.Count);
            Assert.IsFalse(m_table.TryGetLocation(entityId, out _));
            Assert.AreEqual(0, structure.Count);
            Assert.IsNull(location.Structure);
            Assert.AreEqual(generation + 1U, location.Generation);

            m_orchestrator.DestroyEntity(entityId + 100UL);
            Assert.AreEqual(0, m_table.Count);

            Assert.Throws<InvalidOperationException>(() => m_orchestrator.AddTagComponent<PlayerTag>(entityId));
        }

        [Test]
        public void AddDiscreteComponent_SetsValueRaisesObserverEventAndInvokesOnCreate()
        {
            var (entityId, location) = m_orchestrator.CreateEntity();
            var typeId = IdOf<ManaComponent>();

            var core = m_orchestrator.AddDiscreteComponent(entityId, new ManaComponent { Value = 7 });

            Assert.AreEqual(1, ManaComponent.CreateCount);
            Assert.IsTrue(location.Structure.HasDiscrete(typeId, location.Row));
            Assert.AreEqual(7, location.Structure.GetDiscreteRef<ManaComponent>(location.Row).Value);
            Assert.AreEqual(1, m_observer.Added.Count);
            Assert.AreEqual((typeId, location.Row), m_observer.Added[0]);
            Assert.IsTrue(core.NotNull);
            Assert.AreEqual(entityId, core.EntityId);
            Assert.AreEqual(ComponentKind.Discrete, core.Kind);
            Assert.AreEqual(location.Structure.GetDiscreteVersion(typeId, location.Row), core.Version);
        }

        [Test]
        public void RemoveDiscreteComponent_InvokesOnDestroyAndRaisesObserverEvent()
        {
            var (entityId, location) = m_orchestrator.CreateEntity();
            var typeId = IdOf<ManaComponent>();
            var core = m_orchestrator.AddDiscreteComponent(entityId, new ManaComponent { Value = 3 });

            m_orchestrator.RemoveDiscreteComponent<ManaComponent>(entityId);

            Assert.AreEqual(1, ManaComponent.DestroyCount);
            Assert.AreEqual(3, ManaComponent.LastDestroyedValue);
            Assert.IsFalse(location.Structure.HasDiscrete(typeId, location.Row));
            Assert.AreEqual(1, m_observer.Removed.Count);
            Assert.AreEqual((typeId, location.Row), m_observer.Removed[0]);
            Assert.IsFalse(core.NotNull);

            m_orchestrator.RemoveDiscreteComponent<ManaComponent>(entityId);
            Assert.AreEqual(1, ManaComponent.DestroyCount);
        }

        [Test]
        public void AddTagComponent_AndRemoveTagComponent_RoundTripWithObserverEvents()
        {
            var (entityId, location) = m_orchestrator.CreateEntity();
            var typeId = IdOf<PlayerTag>();

            var core = m_orchestrator.AddTagComponent<PlayerTag>(entityId);

            Assert.IsTrue(location.Structure.HasTag(typeId, location.Row));
            Assert.AreEqual(1, m_observer.Added.Count);
            Assert.AreEqual((typeId, location.Row), m_observer.Added[0]);
            Assert.IsTrue(core.NotNull);
            Assert.AreEqual(ComponentKind.Tag, core.Kind);

            m_orchestrator.RemoveTagComponent<PlayerTag>(entityId);

            Assert.IsFalse(location.Structure.HasTag(typeId, location.Row));
            Assert.AreEqual(1, m_observer.Removed.Count);
            Assert.AreEqual((typeId, location.Row), m_observer.Removed[0]);
            Assert.IsFalse(core.NotNull);

            Assert.DoesNotThrow(() => m_orchestrator.RemoveTagComponent<PlayerTag>(entityId));
            Assert.AreEqual(1, m_observer.Removed.Count);
        }

        [Test]
        public void HasComponent_ReportsDenseDiscreteAndTagPresence()
        {
            var denseStructure = m_registry.GetOrCreate(new[] { IdOf<Position>() }, 0b1UL);
            var (denseEntityId, denseLocation) = m_table.Create();
            denseStructure.Append(denseEntityId, denseLocation);
            denseStructure.SetDenseValue(denseLocation.Row, new Position { X = 1 }, ComponentVersion.Next());

            Assert.IsTrue(m_orchestrator.HasComponent<Position>(denseEntityId));
            Assert.IsFalse(m_orchestrator.HasComponent<ManaComponent>(denseEntityId));
            Assert.IsFalse(m_orchestrator.HasComponent<PlayerTag>(denseEntityId));

            var (entityId, _) = m_orchestrator.CreateEntity();
            Assert.IsFalse(m_orchestrator.HasComponent<Position>(entityId));

            m_orchestrator.AddDiscreteComponent(entityId, new ManaComponent { Value = 1 });
            m_orchestrator.AddTagComponent<PlayerTag>(entityId);

            Assert.IsTrue(m_orchestrator.HasComponent<ManaComponent>(entityId));
            Assert.IsTrue(m_orchestrator.HasComponent<PlayerTag>(entityId));
            Assert.IsFalse(m_orchestrator.HasComponent<Position>(entityId));
            Assert.IsFalse(m_orchestrator.HasComponent<Position>(entityId + 100UL));
        }

        [Test]
        public void GetComponentRef_ReturnsVersionedCoreForDiscreteAndPresenceCoreForTag()
        {
            var (entityId, location) = m_orchestrator.CreateEntity();

            Assert.IsNull(m_orchestrator.GetComponentRef<ManaComponent>(entityId));
            Assert.IsNull(m_orchestrator.GetComponentRef<PlayerTag>(entityId));
            Assert.IsNull(m_orchestrator.GetComponentRef<ManaComponent>(entityId + 100UL));

            var discrete = m_orchestrator.AddDiscreteComponent(entityId, new ManaComponent { Value = 5 });
            Assert.IsNotNull(discrete);
            Assert.IsTrue(discrete.NotNull);
            Assert.AreEqual(entityId, discrete.EntityId);
            Assert.AreEqual(ComponentKind.Discrete, discrete.Kind);
            Assert.AreEqual(location.Structure.GetDiscreteVersion(IdOf<ManaComponent>(), location.Row), discrete.Version);
            Assert.AreEqual(0u, discrete.Revision);

            var tag = m_orchestrator.AddTagComponent<PlayerTag>(entityId);
            Assert.IsNotNull(tag);
            Assert.IsTrue(tag.NotNull);
            Assert.AreEqual(entityId, tag.EntityId);
            Assert.AreEqual(ComponentKind.Tag, tag.Kind);
            Assert.AreEqual(0u, tag.Version);

            m_orchestrator.RemoveTagComponent<PlayerTag>(entityId);
            Assert.IsFalse(tag.NotNull);

            m_orchestrator.RemoveDiscreteComponent<ManaComponent>(entityId);
            Assert.IsFalse(discrete.NotNull);
        }

        [Test]
        public void DestroyEntity_WithDiscreteComponent_InvokesOnDestroyAndPreservesOtherEntity()
        {
            var (firstId, firstLocation) = m_orchestrator.CreateEntity();
            var (secondId, _) = m_orchestrator.CreateEntity();
            m_orchestrator.AddDiscreteComponent(firstId, new ManaComponent { Value = 1 });
            m_orchestrator.AddDiscreteComponent(secondId, new ManaComponent { Value = 2 });
            var generation = firstLocation.Generation;

            m_orchestrator.DestroyEntity(firstId);

            Assert.AreEqual(1, m_table.Count);
            Assert.IsFalse(m_table.TryGetLocation(firstId, out _));
            Assert.AreEqual(1, ManaComponent.DestroyCount);
            Assert.AreEqual(1, ManaComponent.LastDestroyedValue);
            Assert.AreEqual(generation + 1U, firstLocation.Generation);
            Assert.IsNull(firstLocation.Structure);

            Assert.IsTrue(m_table.TryGetLocation(secondId, out var moved));
            Assert.AreEqual(0, moved.Row);
            Assert.IsTrue(m_orchestrator.HasComponent<ManaComponent>(secondId));
            Assert.AreEqual(2, moved.Structure.GetDiscreteRef<ManaComponent>(moved.Row).Value);
        }

        [Test]
        public void GetComponentRef_Dense_ReturnsVersionedCore()
        {
            var structure = m_registry.GetOrCreate(new[] { IdOf<Position>() }, 0UL);
            var (entityId, location) = m_table.Create();
            structure.Append(entityId, location);
            structure.SetDenseValue(location.Row, new Position { X = 3 }, 7);

            var core = m_orchestrator.GetComponentRef<Position>(entityId);

            Assert.IsNotNull(core);
            Assert.IsTrue(core.NotNull);
            Assert.AreEqual(ComponentKind.Dense, core.Kind);
            Assert.AreEqual(7u, core.Version);
            Assert.AreEqual(entityId, core.EntityId);
        }

        [Test]
        public void DestroyEntity_WithDenseComponent_InvokesOnDestroyBeforeRemoval()
        {
            ComponentHookDispatcher.RegisterDense<DenseLifecycle>();
            var structure = m_registry.GetOrCreate(new[] { IdOf<DenseLifecycle>() }, 0UL);
            var (entityId, location) = m_table.Create();
            structure.Append(entityId, location);
            structure.SetDenseValue(location.Row, new DenseLifecycle { Value = 42 }, ComponentVersion.Next());

            m_orchestrator.DestroyEntity(entityId);

            Assert.AreEqual(1, DenseLifecycle.DestroyCount);
            Assert.AreEqual(42, DenseLifecycle.LastDestroyedValue);
            Assert.AreEqual(0, structure.Count);
            Assert.AreEqual(0, m_table.Count);
        }

        [Test]
        public void DestroyEntity_HookDestroysSameEntity_IsNoOpAndCompletesOnce()
        {
            var (entityId, location) = m_orchestrator.CreateEntity();
            m_orchestrator.AddDiscreteComponent(entityId, new ManaComponent { Value = 4 });
            var reentrantCalls = 0;
            ManaComponent.DestroyAction = id =>
            {
                reentrantCalls += 1;
                m_orchestrator.DestroyEntity(id);
            };

            Assert.DoesNotThrow(() => m_orchestrator.DestroyEntity(entityId));

            Assert.AreEqual(1, reentrantCalls);
            Assert.AreEqual(1, ManaComponent.DestroyCount);
            Assert.AreEqual(0, m_table.Count);
            Assert.IsNull(location.Structure);
        }

        [Test]
        public void DestroyEntity_HookDestroysEarlierEntityInSameStructure_CompletesBoth()
        {
            var (firstId, firstLocation) = m_orchestrator.CreateEntity();
            var (secondId, secondLocation) = m_orchestrator.CreateEntity();
            var structure = firstLocation.Structure;
            Assert.AreSame(structure, secondLocation.Structure);
            m_orchestrator.AddDiscreteComponent(firstId, new ManaComponent { Value = 1 });
            m_orchestrator.AddDiscreteComponent(secondId, new ManaComponent { Value = 2 });
            ManaComponent.DestroyAction = id =>
            {
                if (id == secondId) m_orchestrator.DestroyEntity(firstId);
            };

            Assert.DoesNotThrow(() => m_orchestrator.DestroyEntity(secondId));

            Assert.AreEqual(2, ManaComponent.DestroyCount);
            Assert.AreEqual(0, m_table.Count);
            Assert.AreEqual(0, structure.Count);
            Assert.IsNull(firstLocation.Structure);
            Assert.IsNull(secondLocation.Structure);
        }

        [Test]
        public void DestroyEntity_HookAddsNewDiscreteStore_CompletesWithoutEnumerationError()
        {
            var (firstId, firstLocation) = m_orchestrator.CreateEntity();
            var (secondId, _) = m_orchestrator.CreateEntity();
            var structure = firstLocation.Structure;
            m_orchestrator.AddDiscreteComponent(firstId, new ManaComponent { Value = 1 });
            ManaComponent.DestroyAction = id =>
            {
                if (id == firstId)
                {
                    m_orchestrator.AddDiscreteComponent(secondId, new OtherDiscrete { Value = 9 });
                }
            };

            Assert.DoesNotThrow(() => m_orchestrator.DestroyEntity(firstId));

            Assert.AreEqual(1, m_table.Count);
            Assert.IsTrue(m_table.TryGetLocation(secondId, out var moved));
            Assert.AreEqual(0, moved.Row);
            Assert.IsTrue(structure.HasDiscrete(IdOf<OtherDiscrete>(), moved.Row));
            Assert.AreEqual(9, structure.GetDiscreteRef<OtherDiscrete>(moved.Row).Value);
        }

        [Test]
        public void DestroyEntity_HookThrows_LogsAndCompletesDestroy()
        {
            var (entityId, location) = m_orchestrator.CreateEntity();
            m_orchestrator.AddDiscreteComponent(entityId, new ManaComponent { Value = 1 });
            ManaComponent.DestroyAction = _ => throw new InvalidOperationException("boom");

            Assert.DoesNotThrow(() => m_orchestrator.DestroyEntity(entityId));

            Assert.AreEqual(1, ManaComponent.DestroyCount);
            Assert.AreEqual(0, m_table.Count);
            Assert.IsNull(location.Structure);
        }

        [Test]
        public void AddDiscreteComponent_OnCreateThrows_LogsAndKeepsComponent()
        {
            var (entityId, location) = m_orchestrator.CreateEntity();
            ManaComponent.CreateAction = _ => throw new InvalidOperationException("boom");

            var core = m_orchestrator.AddDiscreteComponent(entityId, new ManaComponent { Value = 2 });

            Assert.IsTrue(core.NotNull);
            Assert.AreEqual(1, ManaComponent.CreateCount);
            Assert.IsTrue(location.Structure.HasDiscrete(IdOf<ManaComponent>(), location.Row));
        }
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --filter FullyQualifiedName~ComponentOrchestratorTestUnit`
Expected: 编译失败，`ComponentOrchestrator` 不存在

- [ ] **Step 3: 实现 ComponentHookDispatcher、内核访问器与 ComponentOrchestrator**

创建 `ECS/Structures/ComponentHookDispatcher.cs`：

```csharp
using System;
using System.Collections.Concurrent;
using CoreECS.Defines;
using CoreECS.Utils;

namespace CoreECS.Structures
{
    /// <summary>
    /// Non-generic dispatch of component lifecycle hooks (<c>OnCreate</c> / <c>OnDestroy</c>).
    /// Generic callers register the per-type hook pair; non-generic orchestration code that
    /// only knows type ids invokes the cached delegates. Hook exceptions are logged through
    /// <see cref="Log.Exp"/> and do not interrupt the caller, matching v1 store semantics.
    /// </summary>
    internal static class ComponentHookDispatcher
    {
        private static readonly ConcurrentDictionary<uint, ComponentHookPair> s_dense = new();
        private static readonly ConcurrentDictionary<uint, ComponentHookPair> s_discrete = new();

        /// <summary>Holds the dense hook pair for one type, built once per type.</summary>
        private static class DenseHooks<T> where T : struct, IComponent<T>
        {
            public static readonly ComponentHookPair Pair = new ComponentHookPair(
                (structure, row, entityId) => structure.GetDenseRef<T>(row).OnCreate(entityId),
                (structure, row, entityId) => structure.GetDenseRef<T>(row).OnDestroy(entityId));
        }

        /// <summary>Holds the discrete hook pair for one type, built once per type.</summary>
        private static class DiscreteHooks<T> where T : struct, IDiscreteComponent<T>
        {
            public static readonly ComponentHookPair Pair = new ComponentHookPair(
                (structure, row, entityId) => structure.GetDiscreteRef<T>(row).OnCreate(entityId),
                (structure, row, entityId) => structure.GetDiscreteRef<T>(row).OnDestroy(entityId));
        }

        /// <summary>Registers the dense component hooks for <typeparamref name="T"/>.</summary>
        public static void RegisterDense<T>() where T : struct, IComponent<T>
        {
            var typeId = ComponentTypeRegistry.GetOrRegister<T>().TypeId;
            s_dense[typeId] = DenseHooks<T>.Pair;
        }

        /// <summary>Registers the discrete component hooks for <typeparamref name="T"/>.</summary>
        public static void RegisterDiscrete<T>() where T : struct, IDiscreteComponent<T>
        {
            var typeId = ComponentTypeRegistry.GetOrRegister<T>().TypeId;
            s_discrete[typeId] = DiscreteHooks<T>.Pair;
        }

        /// <summary>Invokes <c>OnCreate</c> on the dense component at the row; no-op when unregistered.</summary>
        public static void InvokeDenseCreate(Structure structure, int row, uint typeId, ulong entityId)
        {
            if (!s_dense.TryGetValue(typeId, out var hooks)) return;

            try
            {
                hooks.Create(structure, row, entityId);
            }
            catch (Exception e)
            {
                Log.Exp(e);
            }
        }

        /// <summary>Invokes <c>OnDestroy</c> on the dense component at the row; no-op when unregistered.</summary>
        public static void InvokeDenseDestroy(Structure structure, int row, uint typeId, ulong entityId)
        {
            if (!s_dense.TryGetValue(typeId, out var hooks)) return;

            try
            {
                hooks.Destroy(structure, row, entityId);
            }
            catch (Exception e)
            {
                Log.Exp(e);
            }
        }

        /// <summary>Invokes <c>OnCreate</c> on the discrete component at the row; no-op when unregistered.</summary>
        public static void InvokeDiscreteCreate(Structure structure, int row, uint typeId, ulong entityId)
        {
            if (!s_discrete.TryGetValue(typeId, out var hooks)) return;

            try
            {
                hooks.Create(structure, row, entityId);
            }
            catch (Exception e)
            {
                Log.Exp(e);
            }
        }

        /// <summary>Invokes <c>OnDestroy</c> on the discrete component at the row; no-op when unregistered.</summary>
        public static void InvokeDiscreteDestroy(Structure structure, int row, uint typeId, ulong entityId)
        {
            if (!s_discrete.TryGetValue(typeId, out var hooks)) return;

            try
            {
                hooks.Destroy(structure, row, entityId);
            }
            catch (Exception e)
            {
                Log.Exp(e);
            }
        }
    }

    /// <summary>
    /// Create/destroy hook delegates for one component type. Delegates receive the structure,
    /// the live row and the owning entity id so callers need no generic type information.
    /// </summary>
    internal readonly struct ComponentHookPair
    {
        /// <summary>Invokes <c>OnCreate(entityId)</c> on the component instance at the row.</summary>
        public readonly Action<Structure, int, ulong> Create;

        /// <summary>Invokes <c>OnDestroy(entityId)</c> on the component instance at the row.</summary>
        public readonly Action<Structure, int, ulong> Destroy;

        /// <summary>Creates a hook pair from create/destroy delegates.</summary>
        public ComponentHookPair(Action<Structure, int, ulong> create, Action<Structure, int, ulong> destroy)
        {
            Create = create;
            Destroy = destroy;
        }
    }
}
```

在 `ECS/Structures/Structure.cs` 的 `internal SpareSetComponentContainer SpareSet => m_spareSet ??= CreateSpareSet();` 之后插入：

```csharp
        /// <summary>
        /// Discrete component store container, or null when no store was ever created.
        /// Unlike <see cref="SpareSet"/> this getter never allocates.
        /// </summary>
        internal SpareSetComponentContainer SpareSetOrNull => m_spareSet;
```

在 `ECS/Structures/SpareSetComponentContainer.cs` 的 `public int Count => m_count;` 之后插入：

```csharp
        /// <summary>Type ids of the discrete component stores present in this container.</summary>
        public IEnumerable<uint> TypeIds => m_stores.Keys;
```

创建 `ECS/Structures/ComponentOrchestrator.cs`：

```csharp
using System;
using System.Collections.Generic;
using CoreECS.Defines;

namespace CoreECS.Structures
{
    /// <summary>
    /// Coordinates entity lifecycle and non-dense component operations over the kernel:
    /// allocates entities through the structure registry and entity table, dispatches
    /// component lifecycle hooks and forwards structure observer events to the injected sink.
    /// </summary>
    internal sealed class ComponentOrchestrator
    {
        private static readonly uint[] s_noDenseTypes = Array.Empty<uint>();

        private readonly StructureRegistry m_registry;
        private readonly EntityTable m_table;
        private readonly IStructureObserver m_observer;
        private readonly HashSet<ulong> m_destroying = new();

        /// <summary>
        /// Creates an orchestrator over the given kernel registry and entity table.
        /// <paramref name="observer"/> may be null; when set it is attached to every
        /// structure the orchestrator selects.
        /// </summary>
        public ComponentOrchestrator(StructureRegistry registry, EntityTable table, IStructureObserver observer = null)
        {
            m_registry = registry ?? throw new ArgumentNullException(nameof(registry));
            m_table = table ?? throw new ArgumentNullException(nameof(table));
            m_observer = observer;
        }

        /// <summary>
        /// Creates an entity with the given component mask in the (empty dense) structure
        /// selected by that mask, appends its row and returns the id/location pair.
        /// </summary>
        public (ulong EntityId, EntityLocation Location) CreateEntity(ulong mask = ulong.MaxValue)
        {
            var structure = m_registry.GetOrCreate(s_noDenseTypes, mask);
            if (m_observer != null) structure.Observer = m_observer;

            var (entityId, location) = m_table.Create();
            structure.Append(entityId, location);
            return (entityId, location);
        }

        /// <summary>
        /// Destroys a live entity: invokes <c>OnDestroy</c> on every dense and discrete
        /// component instance at its row (tags carry no lifecycle hooks), swap-removes the
        /// row and releases the entity location back to the pool. Unknown ids are ignored.
        /// Re-entrant destroys of the same entity from a hook are no-ops; hooks that destroy
        /// other entities or create discrete stores are tolerated (store ids are snapshotted
        /// and the final row is re-read from the location binding).
        /// </summary>
        public void DestroyEntity(ulong entityId)
        {
            if (!m_table.TryGetLocation(entityId, out var location)) return;
            if (!m_destroying.Add(entityId)) return;

            try
            {
                var structure = location.Structure;
                if (structure != null)
                {
                    var row = location.Row;
                    var denseTypeIds = structure.DenseTypeIds;
                    for (var i = 0; i < denseTypeIds.Count; i++)
                    {
                        ComponentHookDispatcher.InvokeDenseDestroy(structure, row, denseTypeIds[i], entityId);
                    }

                    var spareSet = structure.SpareSetOrNull;
                    if (spareSet != null)
                    {
                        // Snapshot the store ids: a hook may create new stores on this structure.
                        var discreteTypeIds = new List<uint>(spareSet.TypeIds);
                        for (var i = 0; i < discreteTypeIds.Count; i++)
                        {
                            var typeId = discreteTypeIds[i];
                            if (!structure.HasDiscrete(typeId, row)) continue;
                            ComponentHookDispatcher.InvokeDiscreteDestroy(structure, row, typeId, entityId);
                        }
                    }
                }

                // Re-read the binding: re-entrant destroys keep the location's row current.
                location.Structure?.SwapRemove(location.Row);
            }
            finally
            {
                m_destroying.Remove(entityId);
                m_table.Destroy(entityId);
            }
        }

        /// <summary>Checks whether a live entity carries the component, by storage kind.</summary>
        public bool HasComponent<T>(ulong entityId) where T : struct, IComponent<T>
        {
            var info = ComponentTypeRegistry.GetOrRegister<T>();
            if (!m_table.TryGetLocation(entityId, out var location)) return false;

            var structure = location.Structure;
            if (structure == null) return false;

            switch (info.Kind)
            {
                case ComponentKind.Dense:
                    return structure.HasDense(info.TypeId);
                case ComponentKind.Discrete:
                    return structure.HasDiscrete(info.TypeId, location.Row);
                case ComponentKind.Tag:
                    return structure.HasTag(info.TypeId, location.Row);
                default:
                    return false;
            }
        }

        /// <summary>
        /// Gets a reference core for the component when present. Dense and discrete refs carry
        /// the instance version; tags return a presence-only core (version 0). Returns null
        /// when the entity is unknown or the component is absent.
        /// </summary>
        public ComponentRefCore GetComponentRef<T>(ulong entityId) where T : struct, IComponent<T>
        {
            var info = ComponentTypeRegistry.GetOrRegister<T>();
            if (!m_table.TryGetLocation(entityId, out var location)) return null;

            var structure = location.Structure;
            if (structure == null) return null;

            switch (info.Kind)
            {
                case ComponentKind.Dense:
                    if (!structure.HasDense(info.TypeId)) return null;
                    return new ComponentRefCore(location, location.Generation, info.TypeId,
                        ComponentKind.Dense, structure.GetDenseVersion(info.TypeId, location.Row));
                case ComponentKind.Discrete:
                    if (!structure.HasDiscrete(info.TypeId, location.Row)) return null;
                    return new ComponentRefCore(location, location.Generation, info.TypeId,
                        ComponentKind.Discrete, structure.GetDiscreteVersion(info.TypeId, location.Row));
                case ComponentKind.Tag:
                    if (!structure.HasTag(info.TypeId, location.Row)) return null;
                    return new ComponentRefCore(location, location.Generation, info.TypeId, ComponentKind.Tag, 0u);
                default:
                    return null;
            }
        }

        /// <summary>
        /// Writes a discrete component at the entity row with a fresh version, notifies the
        /// observer through the structure and invokes <c>OnCreate</c> on the stored instance.
        /// Adding over an existing instance overwrites the value with a fresh version and
        /// fires <c>OnCreate</c> again; no implicit <c>OnDestroy</c> is raised.
        /// </summary>
        public ComponentRefCore AddDiscreteComponent<T>(ulong entityId, in T value)
            where T : struct, IDiscreteComponent<T>
        {
            var location = RequireLocation(entityId);
            var structure = location.Structure;
            var info = ComponentTypeRegistry.GetOrRegister<T>();
            var version = ComponentVersion.Next();

            ComponentHookDispatcher.RegisterDiscrete<T>();
            structure.SetDiscrete(location.Row, value, version);
            ComponentHookDispatcher.InvokeDiscreteCreate(structure, location.Row, info.TypeId, entityId);
            return new ComponentRefCore(location, location.Generation, info.TypeId, ComponentKind.Discrete, version);
        }

        /// <summary>
        /// Adds a tag to the entity row, notifying the observer through the structure.
        /// Tags carry no data and no lifecycle hooks.
        /// </summary>
        public ComponentRefCore AddTagComponent<T>(ulong entityId) where T : struct, ITagComponent<T>
        {
            var location = RequireLocation(entityId);
            var structure = location.Structure;
            var info = ComponentTypeRegistry.GetOrRegister<T>();

            structure.AddTag(info.TypeId, location.Row);
            return new ComponentRefCore(location, location.Generation, info.TypeId, ComponentKind.Tag, 0u);
        }

        /// <summary>
        /// Invokes <c>OnDestroy</c> on the discrete component instance when present, then
        /// removes it from the entity row and notifies the observer through the structure.
        /// </summary>
        public void RemoveDiscreteComponent<T>(ulong entityId) where T : struct, IDiscreteComponent<T>
        {
            var location = RequireLocation(entityId);
            var structure = location.Structure;
            var info = ComponentTypeRegistry.GetOrRegister<T>();
            if (!structure.HasDiscrete(info.TypeId, location.Row)) return;

            ComponentHookDispatcher.RegisterDiscrete<T>();
            ComponentHookDispatcher.InvokeDiscreteDestroy(structure, location.Row, info.TypeId, entityId);
            structure.RemoveDiscrete(info.TypeId, location.Row);
        }

        /// <summary>Removes a tag from the entity row, notifying the observer through the structure.</summary>
        public void RemoveTagComponent<T>(ulong entityId) where T : struct, ITagComponent<T>
        {
            var location = RequireLocation(entityId);
            var structure = location.Structure;
            var info = ComponentTypeRegistry.GetOrRegister<T>();

            structure.RemoveTag(info.TypeId, location.Row);
        }

        private EntityLocation RequireLocation(ulong entityId)
        {
            if (!m_table.TryGetLocation(entityId, out var location) || location.Structure == null)
            {
                throw new InvalidOperationException($"Entity {entityId} is not alive.");
            }

            return location;
        }
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --filter FullyQualifiedName~ComponentOrchestratorTestUnit`
Expected: PASS（15 个测试）

- [ ] **Step 5: 运行全量测试**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj`
Expected: 428 passed（Task 2 后 413 + 新增 15），0 failed

- [ ] **Step 6: 提交**

```bash
git add ECS/Structures/ComponentHookDispatcher.cs ECS/Structures/ComponentOrchestrator.cs ECS/Structures/Structure.cs ECS/Structures/SpareSetComponentContainer.cs Test/ComponentOrchestratorTestUnit.cs
git commit -m "feat(core): add component orchestrator entity lifecycle"
```

---

## Task 4: ComponentOrchestrator — Dense 组件迁移

**Files:**
- Modify: `ECS/Structures/ComponentOrchestrator.cs`（替换 `RequireLocation` 拒绝正在销毁的实体；在 `RemoveTagComponent<T>` 方法之后、`RequireLocation` 方法之前新增 `AddDenseComponent<T>` / `RemoveDenseComponent<T>`）
- Test: `Test/ComponentOrchestratorTestUnit.cs`（追加 `Health` 组件类型、扩展 `RecordingObserver` / `SetUp`、追加 8 个测试）

前置：Task 1–3 已实现。本任务不新增内核 API：`ComponentHookDispatcher.RegisterDense<T>` / `InvokeDenseCreate` / `InvokeDenseDestroy`（Task 3）、`Structure.Append` / `CopyDenseTo` / `CopyTagsTo` / `MoveDiscreteTo` / `SetDenseValue<T>` / `SwapRemove` / `HasDense` / `Key` / `Mask`、`StructureKey.AddType` / `RemoveType` / `ToArray`、`StructureRegistry.GetOrCreate(in StructureKey)`、`ComponentVersion.Next()` 全部已就位。

设计说明（执行时不要改动，评审时按此核对）：
- **重复添加抛异常**：`AddDenseComponent<T>` 在 `current.HasDense(typeId)` 时抛 `InvalidOperationException`，且在任何迁移/写入之前抛出。理由：archetype 模型每个结构每个类型只有一个 dense 列，无法表示两个实例；v1 原始 `ComponentStore.Fix` 会静默分配第二个槽位，而 v1 的公开 create-or-get 路径（`EntityExtension.GetOrCreateComponent`）先查 `HasComponent` 再创建。v2 把这一约束显式化为错误。
- **移除缺失抛异常**：`RemoveDenseComponent<T>` 在 `!current.HasDense(typeId)` 时抛 `InvalidOperationException`，与 v1 `Entity.DestroyComponent<T>()`（`Assertion.IsTrue(component.NotNull, ...)`）的存在性断言一致；同样在任何迁移/销毁 hook 之前抛出。
- **事件由编排层发出**：`CopyDenseTo` / `CopyTagsTo` / `MoveDiscreteTo`（及其底层的 `SpareSetComponentContainer.CopyRowTo` / `TagContainer.CopyRowTo`）不调用 `IStructureObserver`，迁移本身不产生事件。dense 增删各由编排层显式发出恰好一次事件，参数为 `(target, targetRow, typeId)`——`target` 是迁移后的结构，`targetRow` 是迁移后的行（不是旧行）。`target.Observer` 同时被设为编排层的 sink，保证迁移后在该结构上的 discrete/tag 操作继续上报。
- **Hook 时序**：`OnCreate` 在目标行写入、旧行 `SwapRemove` 之后调用（`InvokeDenseCreate(target, targetRow, typeId, entityId)`）；`OnDestroy` 在迁移之前、旧行仍可读时调用（`InvokeDenseDestroy(current, oldRow, typeId, entityId)`），委托内通过 `GetDenseRef<T>` 读到的就是被移除前的值。
- **引用跨迁移存活**：`ComponentRefCore` 持有共享的 `EntityLocation`，`target.Append` 会把 `location.Structure` / `location.Row` 重绑定到目标结构，而 `CopyDenseTo` / `MoveDiscreteTo` 保留 version/revision、`CopyTagsTo` 保留 tag 位，因此迁移前捕获的 dense/discrete/tag 引用在迁移后仍 `NotNull` 且 `EntityId` 不变。
- **版本新鲜**：新增的 dense 组件用 `ComponentVersion.Next()` 盖新版本（`SetDenseValue` 同时把 revision 置 0）；移除后再添加会得到不同版本，旧引用不会误匹配新实例。
- **结构去重复用**：`RemoveDenseComponent` 的目标 key 若已存在于 registry（例如该实体此前从该结构迁出），`GetOrCreate` 直接返回原结构实例，实体迁回原结构。
- 返回值：`AddDenseComponent` 返回新组件的 `ComponentRefCore`（`Kind = Dense`、`Version` 为新版本）；`RemoveDenseComponent` 返回 `void`（与 `RemoveDiscreteComponent<T>` 一致）。
- **销毁期间禁止组件变更（Task 3 复审修订）**：`RequireLocation` 增加 `m_destroying.Contains(entityId)` 检查并抛 `InvalidOperationException`——实体进入销毁流程后仍可读（`HasComponent` / `GetComponentRef`），但任何组件增删（dense 迁移、discrete/tag 变更）都被拒绝。这关闭了"hook 对正在销毁的实体做 dense 迁移会把 location 重绑到新结构、最终 `SwapRemove` 移除新结构行且迁移组件静默丢失"的缺口；hook 内触发的异常由 `ComponentHookDispatcher` catch + `Log.Exp`，销毁流程照常完成。

- [ ] **Step 1: 写失败测试（追加到 `Test/ComponentOrchestratorTestUnit.cs`）**

先做三处小改动，再追加测试方法。

1a. 在 `PlayerTag` 结构体之后插入 `Health` 组件类型（带生命周期 hook，`OnDestroy` 记录被移除前的值）：

```csharp
        private struct Health : IComponent<Health>
        {
            public int Value;

            public void OnCreate(ulong entityId)
            {
                CreateCount += 1;
                LastCreatedEntity = entityId;
            }

            public void OnDestroy(ulong entityId)
            {
                DestroyCount += 1;
                LastDestroyedValue = Value;
            }

            public static int CreateCount;
            public static int DestroyCount;
            public static ulong LastCreatedEntity;
            public static int LastDestroyedValue;
        }
```

1b. 用以下版本替换 `RecordingObserver` 类（新增 `LastAddedStructure` / `LastRemovedStructure`，其余保持不变）：

```csharp
        private sealed class RecordingObserver : IStructureObserver
        {
            public readonly List<(uint TypeId, int Row)> Added = new();
            public readonly List<(uint TypeId, int Row)> Removed = new();
            public readonly List<(uint TypeId, int Row)> Changed = new();

            public Structure LastAddedStructure;
            public Structure LastRemovedStructure;

            public void OnComponentAdded(Structure structure, int row, uint typeId)
            {
                Added.Add((typeId, row));
                LastAddedStructure = structure;
            }

            public void OnComponentRemoved(Structure structure, int row, uint typeId)
            {
                Removed.Add((typeId, row));
                LastRemovedStructure = structure;
            }

            public void OnComponentChanged(Structure structure, int row, uint typeId) => Changed.Add((typeId, row));
        }
```

1c. 用以下版本替换 `SetUp`（保留 Task 3 修订新增的 ManaComponent / DenseLifecycle 重置，并新增 `Health` 静态状态重置）：

```csharp
        [SetUp]
        public void SetUp()
        {
            ManaComponent.CreateCount = 0;
            ManaComponent.DestroyCount = 0;
            ManaComponent.LastDestroyedValue = 0;
            ManaComponent.CreateAction = null;
            ManaComponent.DestroyAction = null;
            DenseLifecycle.DestroyCount = 0;
            DenseLifecycle.LastDestroyedValue = 0;
            Health.CreateCount = 0;
            Health.DestroyCount = 0;
            Health.LastCreatedEntity = 0UL;
            Health.LastDestroyedValue = 0;
            m_registry = new StructureRegistry();
            m_table = new EntityTable();
            m_observer = new RecordingObserver();
            m_orchestrator = new ComponentOrchestrator(m_registry, m_table, m_observer);
        }
```

1d. 在 `ComponentOrchestratorTestUnit` 类结尾的 `}` 之前追加以下 8 个测试：

```csharp
        [Test]
        public void AddDenseComponent_MigratesEntityAndPreservesDiscreteAndTagState()
        {
            var (entityId, location) = m_orchestrator.CreateEntity(0b10UL);
            var source = location.Structure;
            var mana = m_orchestrator.AddDiscreteComponent(entityId, new ManaComponent { Value = 9 });
            var tag = m_orchestrator.AddTagComponent<PlayerTag>(entityId);

            var core = m_orchestrator.AddDenseComponent(entityId, new Health { Value = 55 });

            var target = location.Structure;
            Assert.AreNotSame(source, target);
            Assert.AreEqual(0b10UL, target.Mask);
            Assert.AreEqual(0b10UL, target.Key.Mask);
            Assert.AreEqual(1, target.Count);
            Assert.AreEqual(0, source.Count);
            Assert.AreEqual(0, location.Row);
            Assert.IsTrue(target.HasDense(IdOf<Health>()));
            Assert.AreEqual(55, target.GetDenseRef<Health>(location.Row).Value);
            Assert.AreEqual(core.Version, target.GetDenseVersion(IdOf<Health>(), location.Row));
            Assert.AreNotEqual(0u, core.Version);

            Assert.IsTrue(target.HasDiscrete(IdOf<ManaComponent>(), location.Row));
            Assert.AreEqual(9, target.GetDiscreteRef<ManaComponent>(location.Row).Value);
            Assert.AreEqual(mana.Version, target.GetDiscreteVersion(IdOf<ManaComponent>(), location.Row));
            Assert.IsTrue(target.HasTag(IdOf<PlayerTag>(), location.Row));

            Assert.IsTrue(core.NotNull);
            Assert.AreEqual(entityId, core.EntityId);
            Assert.AreEqual(ComponentKind.Dense, core.Kind);
            Assert.IsTrue(mana.NotNull);
            Assert.IsTrue(tag.NotNull);
        }

        [Test]
        public void AddDenseComponent_EmitsAddEventWithTargetRowAndInvokesOnCreate()
        {
            var (entityId, location) = m_orchestrator.CreateEntity();
            m_observer.Added.Clear();

            var core = m_orchestrator.AddDenseComponent(entityId, new Health { Value = 7 });

            Assert.AreEqual(1, m_observer.Added.Count);
            Assert.AreEqual((IdOf<Health>(), location.Row), m_observer.Added[0]);
            Assert.AreSame(location.Structure, m_observer.LastAddedStructure);
            Assert.AreEqual(1, Health.CreateCount);
            Assert.AreEqual(entityId, Health.LastCreatedEntity);
            Assert.IsTrue(core.NotNull);
        }

        [Test]
        public void ComponentRefCore_CapturedBeforeAddDense_RemainsNotNullAfterMigration()
        {
            var (entityId, location) = m_orchestrator.CreateEntity();
            var position = m_orchestrator.AddDenseComponent(entityId, new Position { X = 3 });
            var mana = m_orchestrator.AddDiscreteComponent(entityId, new ManaComponent { Value = 5 });
            var tag = m_orchestrator.AddTagComponent<PlayerTag>(entityId);
            var source = location.Structure;

            m_orchestrator.AddDenseComponent(entityId, new Health { Value = 1 });

            Assert.AreNotSame(source, location.Structure);
            Assert.AreSame(location, position.Location);

            Assert.IsTrue(position.NotNull);
            Assert.AreEqual(entityId, position.EntityId);
            Assert.AreEqual(3, location.Structure.GetDenseRef<Position>(location.Row).X);
            Assert.AreEqual(position.Version, location.Structure.GetDenseVersion(IdOf<Position>(), location.Row));

            Assert.IsTrue(mana.NotNull);
            Assert.AreEqual(entityId, mana.EntityId);
            Assert.AreEqual(5, location.Structure.GetDiscreteRef<ManaComponent>(location.Row).Value);

            Assert.IsTrue(tag.NotNull);
            Assert.AreEqual(entityId, tag.EntityId);
        }

        [Test]
        public void RemoveDenseComponent_DropsTypeAndPreservesOtherDenseDiscreteAndTag()
        {
            var (entityId, location) = m_orchestrator.CreateEntity();
            var position = m_orchestrator.AddDenseComponent(entityId, new Position { X = 8 });
            var positionStructure = location.Structure;
            var mana = m_orchestrator.AddDiscreteComponent(entityId, new ManaComponent { Value = 5 });
            var tag = m_orchestrator.AddTagComponent<PlayerTag>(entityId);
            m_orchestrator.AddDenseComponent(entityId, new Health { Value = 3 });
            var healthStructure = location.Structure;
            m_observer.Removed.Clear();

            m_orchestrator.RemoveDenseComponent<Health>(entityId);

            var target = location.Structure;
            Assert.AreSame(positionStructure, target);
            Assert.AreNotSame(healthStructure, target);
            Assert.AreEqual(0, healthStructure.Count);
            Assert.AreEqual(1, target.Count);
            Assert.IsFalse(target.HasDense(IdOf<Health>()));
            Assert.IsTrue(target.HasDense(IdOf<Position>()));
            Assert.AreEqual(8, target.GetDenseRef<Position>(location.Row).X);
            Assert.IsTrue(position.NotNull);
            Assert.AreEqual(position.Version, target.GetDenseVersion(IdOf<Position>(), location.Row));
            Assert.IsTrue(mana.NotNull);
            Assert.IsTrue(target.HasDiscrete(IdOf<ManaComponent>(), location.Row));
            Assert.AreEqual(5, target.GetDiscreteRef<ManaComponent>(location.Row).Value);
            Assert.IsTrue(tag.NotNull);
            Assert.IsTrue(target.HasTag(IdOf<PlayerTag>(), location.Row));

            Assert.AreEqual(1, m_observer.Removed.Count);
            Assert.AreEqual((IdOf<Health>(), location.Row), m_observer.Removed[0]);
            Assert.AreSame(target, m_observer.LastRemovedStructure);
        }

        [Test]
        public void RemoveDenseComponent_InvokesOnDestroyWhileOldValueStillReadable()
        {
            var (entityId, location) = m_orchestrator.CreateEntity();
            m_orchestrator.AddDenseComponent(entityId, new Health { Value = 42 });

            m_orchestrator.RemoveDenseComponent<Health>(entityId);

            Assert.AreEqual(1, Health.DestroyCount);
            Assert.AreEqual(42, Health.LastDestroyedValue);
            Assert.IsFalse(location.Structure.HasDense(IdOf<Health>()));
            Assert.IsFalse(m_orchestrator.HasComponent<Health>(entityId));
        }

        [Test]
        public void AddDenseComponent_AlreadyPresentAndRemoveDenseComponent_Absent_Throw()
        {
            var structure = m_registry.GetOrCreate(new[] { IdOf<Position>() }, 0UL);
            var (entityId, location) = m_table.Create();
            structure.Append(entityId, location);
            structure.SetDenseValue(location.Row, new Position { X = 1 }, ComponentVersion.Next());

            Assert.Throws<InvalidOperationException>(() =>
            {
                m_orchestrator.AddDenseComponent(entityId, new Position { X = 2 });
            });
            Assert.IsTrue(structure.HasDense(IdOf<Position>()));
            Assert.AreEqual(1, structure.Count);

            var (otherId, otherLocation) = m_orchestrator.CreateEntity();
            Assert.Throws<InvalidOperationException>(() =>
            {
                m_orchestrator.RemoveDenseComponent<Position>(otherId);
            });
            Assert.IsFalse(otherLocation.Structure.HasDense(IdOf<Position>()));
        }

        [Test]
        public void SequentialAddDenseComponent_BuildsSortedCompositionAndPreservesMask()
        {
            var positionId = IdOf<Position>();
            var healthId = IdOf<Health>();
            var lowId = Math.Min(positionId, healthId);
            var highId = Math.Max(positionId, healthId);

            var (entityId, location) = m_orchestrator.CreateEntity(0b100UL);

            if (positionId == highId)
            {
                m_orchestrator.AddDenseComponent(entityId, new Position { X = 1 });
                m_orchestrator.AddDenseComponent(entityId, new Health { Value = 2 });
            }
            else
            {
                m_orchestrator.AddDenseComponent(entityId, new Health { Value = 2 });
                m_orchestrator.AddDenseComponent(entityId, new Position { X = 1 });
            }

            var target = location.Structure;
            Assert.AreEqual(2, target.Key.DenseCount);
            Assert.AreEqual(lowId, target.Key.DenseTypeIds[0]);
            Assert.AreEqual(highId, target.Key.DenseTypeIds[1]);
            Assert.AreEqual(0b100UL, target.Key.Mask);
            Assert.AreSame(target, m_registry.GetOrCreate(new[] { lowId, highId }, 0b100UL));
        }

        [Test]
        public void DestroyEntity_HookMutatesDyingEntity_ThrowsAndDestroyCompletes()
        {
            var (entityId, location) = m_orchestrator.CreateEntity();
            var structure = location.Structure;
            m_orchestrator.AddDiscreteComponent(entityId, new ManaComponent { Value = 1 });
            var mutationRejected = false;
            ManaComponent.DestroyAction = id =>
            {
                try
                {
                    m_orchestrator.AddDenseComponent(id, new Health { Value = 7 });
                }
                catch (InvalidOperationException)
                {
                    mutationRejected = true;
                }
            };

            Assert.DoesNotThrow(() => m_orchestrator.DestroyEntity(entityId));

            Assert.IsTrue(mutationRejected);
            Assert.AreEqual(1, ManaComponent.DestroyCount);
            Assert.AreEqual(0, m_table.Count);
            Assert.AreEqual(0, structure.Count);
            Assert.AreEqual(1, m_registry.Count);
        }
```

- [ ] **Step 2: 运行测试确认失败**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --filter FullyQualifiedName~ComponentOrchestratorTestUnit`
Expected: 编译失败，`ComponentOrchestrator` 不含 `AddDenseComponent` / `RemoveDenseComponent`

- [ ] **Step 3: 实现 AddDenseComponent / RemoveDenseComponent**

3a. 用以下版本替换 `RequireLocation`（新增正在销毁检查）：

```csharp
        private EntityLocation RequireLocation(ulong entityId)
        {
            if (m_destroying.Contains(entityId)
                || !m_table.TryGetLocation(entityId, out var location)
                || location.Structure == null)
            {
                throw new InvalidOperationException($"Entity {entityId} is not alive.");
            }

            return location;
        }
```

3b. 在 `ECS/Structures/ComponentOrchestrator.cs` 的 `RemoveTagComponent<T>` 方法之后、`RequireLocation` 方法之前插入：

```csharp
        /// <summary>
        /// Adds a dense component to a live entity by migrating its row into the structure
        /// whose key gains <typeparamref name="T"/>: copies dense data shared with the target,
        /// tags and discrete components, writes the value with a fresh version, swap-removes
        /// the source row, reports the addition to the observer sink with the target row and
        /// invokes <c>OnCreate</c> on the stored instance.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the entity is not alive, or already carries the dense component.
        /// v1's create-or-get path checked presence before creating; the archetype model
        /// cannot represent two instances of the same dense type, so a duplicate add is
        /// an explicit error rather than a silent second instance.
        /// </exception>
        public ComponentRefCore AddDenseComponent<T>(ulong entityId, in T value)
            where T : struct, IComponent<T>
        {
            var location = RequireLocation(entityId);
            var current = location.Structure;
            var info = ComponentTypeRegistry.GetOrRegister<T>();
            if (current.HasDense(info.TypeId))
            {
                throw new InvalidOperationException(
                    $"Entity {entityId} already has dense component {typeof(T).Name}.");
            }

            var targetKey = new StructureKey(
                StructureKey.AddType(current.Key.ToArray(), info.TypeId), current.Mask);
            var target = m_registry.GetOrCreate(targetKey);
            if (m_observer != null) target.Observer = m_observer;

            var sourceRow = location.Row;
            var targetRow = target.Append(entityId, location);
            current.CopyDenseTo(target, sourceRow, targetRow);
            current.CopyTagsTo(target, sourceRow, targetRow);
            current.MoveDiscreteTo(target, sourceRow, targetRow);

            var version = ComponentVersion.Next();
            target.SetDenseValue(targetRow, value, version);
            current.SwapRemove(sourceRow);

            ComponentHookDispatcher.RegisterDense<T>();
            m_observer?.OnComponentAdded(target, targetRow, info.TypeId);
            ComponentHookDispatcher.InvokeDenseCreate(target, targetRow, info.TypeId, entityId);

            return new ComponentRefCore(location, location.Generation, info.TypeId, ComponentKind.Dense, version);
        }

        /// <summary>
        /// Removes a dense component from a live entity: invokes <c>OnDestroy</c> while the
        /// old value is still stored, migrates the row into the structure without
        /// <typeparamref name="T"/>, swap-removes the source row and reports the removal to
        /// the observer sink with the target row.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the entity is not alive, or does not carry the dense component;
        /// v1 <c>DestroyComponent&lt;T&gt;()</c> asserted presence the same way.
        /// </exception>
        public void RemoveDenseComponent<T>(ulong entityId) where T : struct, IComponent<T>
        {
            var location = RequireLocation(entityId);
            var current = location.Structure;
            var info = ComponentTypeRegistry.GetOrRegister<T>();
            if (!current.HasDense(info.TypeId))
            {
                throw new InvalidOperationException(
                    $"Entity {entityId} does not have dense component {typeof(T).Name}.");
            }

            ComponentHookDispatcher.RegisterDense<T>();
            ComponentHookDispatcher.InvokeDenseDestroy(current, location.Row, info.TypeId, entityId);

            var targetKey = new StructureKey(
                StructureKey.RemoveType(current.Key.ToArray(), info.TypeId), current.Mask);
            var target = m_registry.GetOrCreate(targetKey);
            if (m_observer != null) target.Observer = m_observer;

            var sourceRow = location.Row;
            var targetRow = target.Append(entityId, location);
            current.CopyDenseTo(target, sourceRow, targetRow);
            current.CopyTagsTo(target, sourceRow, targetRow);
            current.MoveDiscreteTo(target, sourceRow, targetRow);
            current.SwapRemove(sourceRow);

            m_observer?.OnComponentRemoved(target, targetRow, info.TypeId);
        }
```

- [ ] **Step 4: 运行测试确认通过**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --filter FullyQualifiedName~ComponentOrchestratorTestUnit`
Expected: PASS（23 个测试 = Task 3 的 15 个 + 新增 8 个）

- [ ] **Step 5: 运行全量测试**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj`
Expected: 436 passed（Task 3 后 428 + 新增 8），0 failed

- [ ] **Step 6: 提交**

```bash
git add ECS/Structures/ComponentOrchestrator.cs Test/ComponentOrchestratorTestUnit.cs
git commit -m "feat(core): add dense component migration to orchestrator"
```

---

## Task 5: EntityMatcher 的 Structure 求值

**Files:**
- Modify: `ECS/EntityMatcher.cs`（新增 `using CoreECS.Structures;`、三组 per-kind 解析集合、`ComponentFilter(Structure, int)` 及私有辅助；v1 `ComponentFilter(IReadOnlyCollection<IComponentRefCore>)` / `IsRelevantComponent` / `m_all` / `m_any` / `m_none` / `m_changing` 全部保留不变）
- Test: `Test/EntityMatcherStructureTestUnit.cs`

前置：Task 1–4 已实现。本任务不依赖编排层：测试直接用 `new Structure(new StructureKey(...))` + `Append` + `AddTag` / `SetDiscrete` 手工搭建结构（`Structure` 构造为 internal，测试经 `InternalsVisibleTo("Test")` 访问）。

设计说明（执行时不要改动，评审时按此核对）：
- **为什么在配置时解析**：v1 `ComponentFilter` 每次调用都要经 `RefLocator.GetT()` 取类型；v2 要在结构/row 上按 typeId 直接查询，必须在 `OfAll` / `OfAny` / `OfNone` 调用时一次性把 `typeof(T)` 解析为 `(Kind, TypeId)` 并按 kind 分桶缓存，求值路径不再触碰 `Type` 或注册表。
- **Dense 结构级 / Tag·Discrete row 级**：dense 参与 archetype 身份，结构内每一行都带该 dense 列（`structure.HasDense(typeId)`）；tag / discrete 不参与结构身份，按行查询（`HasTag` / `HasDiscrete`）。
- **三集合语义与 v1 一致**：先做 mask 交集，`(EntityMask & structure.Mask) == 0` 直接拒绝；`none` 任一存在即拒绝；`all` 必须全部存在；`any` 为空视为满足，否则至少一个存在。求值顺序 none → all → any。
- **v1 方法保留**：`ComponentFilter(IReadOnlyCollection<IComponentRefCore>)`、`IsRelevantComponent` 与三个 `HashSet<Type>` 完全不动（`m_changing` 仍只服务 v1 方法）；Plan 1c 在最后一个 v1 调用点消失后再删除。
- **测试如何调用 internal 方法**：链式方法返回接口类型（`IAllOfEntityMatcher` 等），而 `ComponentFilter(Structure, int)` 是 `EntityMatcher` 上的 internal 重载；测试在链式构建后用 `(EntityMatcher)` 还原具体类型（fluent 方法返回 `this`，转换恒成功），或直接用具体类型变量分步配置。

- [ ] **Step 1: 写失败测试**

创建 `Test/EntityMatcherStructureTestUnit.cs`：

```csharp
using System;
using CoreECS.Defines;
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class EntityMatcherStructureTestUnit
    {
        private struct Position : IComponent<Position>
        {
            public int X;
        }

        private struct OtherDense : IComponent<OtherDense>
        {
            public int X;
        }

        private struct Mana : IDiscreteComponent<Mana>
        {
            public int Value;
        }

        private struct PlayerTag : ITagComponent<PlayerTag>
        {
        }

        private static uint IdOf<T>() where T : struct, IComponent<T>
            => ComponentTypeRegistry.GetOrRegister<T>().TypeId;

        private static Structure MakeStructure(ulong mask, params uint[] denseTypeIds)
        {
            Array.Sort(denseTypeIds);
            return new Structure(new StructureKey(denseTypeIds, mask));
        }

        private static int AppendRow(Structure structure, ulong entityId)
        {
            var location = EntityLocation.Pool.Get();
            return structure.Append(entityId, location);
        }

        [Test]
        public void ComponentFilter_AllOfDense_MatchesOnlyStructuresCarryingTheType()
        {
            var matching = MakeStructure(ulong.MaxValue, IdOf<Position>());
            var matchingRow = AppendRow(matching, 1UL);
            var missing = MakeStructure(ulong.MaxValue);
            var missingRow = AppendRow(missing, 2UL);

            var matcher = (EntityMatcher)EntityMatcher.With.OfAll<Position>();

            Assert.IsTrue(matcher.ComponentFilter(matching, matchingRow));
            Assert.IsFalse(matcher.ComponentFilter(missing, missingRow));
        }

        [Test]
        public void ComponentFilter_AllOfTag_MatchesOnlyRowsCarryingTheTag()
        {
            var structure = MakeStructure(ulong.MaxValue);
            var taggedRow = AppendRow(structure, 1UL);
            var plainRow = AppendRow(structure, 2UL);
            structure.AddTag(IdOf<PlayerTag>(), taggedRow);

            var matcher = (EntityMatcher)EntityMatcher.With.OfAll<PlayerTag>();

            Assert.IsTrue(matcher.ComponentFilter(structure, taggedRow));
            Assert.IsFalse(matcher.ComponentFilter(structure, plainRow));
        }

        [Test]
        public void ComponentFilter_AllOfDiscrete_MatchesOnlyRowsCarryingTheComponent()
        {
            var structure = MakeStructure(ulong.MaxValue);
            var withManaRow = AppendRow(structure, 1UL);
            var withoutManaRow = AppendRow(structure, 2UL);
            structure.SetDiscrete(withManaRow, new Mana { Value = 3 }, ComponentVersion.Next());

            var matcher = (EntityMatcher)EntityMatcher.With.OfAll<Mana>();

            Assert.IsTrue(matcher.ComponentFilter(structure, withManaRow));
            Assert.IsFalse(matcher.ComponentFilter(structure, withoutManaRow));
        }

        [Test]
        public void ComponentFilter_NoneOf_RejectsPresenceAcrossDenseTagAndDiscrete()
        {
            var denseStructure = MakeStructure(ulong.MaxValue, IdOf<Position>());
            var denseRow = AppendRow(denseStructure, 1UL);

            var tagStructure = MakeStructure(ulong.MaxValue);
            var taggedRow = AppendRow(tagStructure, 2UL);
            var untaggedRow = AppendRow(tagStructure, 3UL);
            tagStructure.AddTag(IdOf<PlayerTag>(), taggedRow);

            var discreteStructure = MakeStructure(ulong.MaxValue);
            var withManaRow = AppendRow(discreteStructure, 4UL);
            var plainRow = AppendRow(discreteStructure, 5UL);
            discreteStructure.SetDiscrete(withManaRow, new Mana { Value = 1 }, ComponentVersion.Next());

            var matcher = (EntityMatcher)EntityMatcher.With
                .OfNone<Position>()
                .OfNone<PlayerTag>()
                .OfNone<Mana>();

            Assert.IsFalse(matcher.ComponentFilter(denseStructure, denseRow));
            Assert.IsFalse(matcher.ComponentFilter(tagStructure, taggedRow));
            Assert.IsFalse(matcher.ComponentFilter(discreteStructure, withManaRow));
            Assert.IsTrue(matcher.ComponentFilter(tagStructure, untaggedRow));
            Assert.IsTrue(matcher.ComponentFilter(discreteStructure, plainRow));
        }

        [Test]
        public void ComponentFilter_AnyOf_MixedKinds_SatisfiedByAnyPresentCondition()
        {
            var denseStructure = MakeStructure(ulong.MaxValue, IdOf<Position>());
            var denseRow = AppendRow(denseStructure, 1UL);

            var rowStructure = MakeStructure(ulong.MaxValue);
            var row = AppendRow(rowStructure, 2UL);

            var matcher = (EntityMatcher)EntityMatcher.With
                .OfAny<Position>()
                .OfAny<PlayerTag>()
                .OfAny<Mana>();

            Assert.IsTrue(matcher.ComponentFilter(denseStructure, denseRow));
            Assert.IsFalse(matcher.ComponentFilter(rowStructure, row));

            rowStructure.AddTag(IdOf<PlayerTag>(), row);
            Assert.IsTrue(matcher.ComponentFilter(rowStructure, row));
            rowStructure.RemoveTag(IdOf<PlayerTag>(), row);

            rowStructure.SetDiscrete(row, new Mana { Value = 2 }, ComponentVersion.Next());
            Assert.IsTrue(matcher.ComponentFilter(rowStructure, row));
        }

        [Test]
        public void ComponentFilter_MaskMismatch_RejectsOtherwiseMatchingRow()
        {
            var structure = MakeStructure(0b0010UL, IdOf<Position>());
            var row = AppendRow(structure, 1UL);

            var rejected = EntityMatcher.WithMask(0b0001UL);
            var matching = EntityMatcher.WithMask(0b0010UL);
            var overlapping = EntityMatcher.WithMask(0b0011UL);

            Assert.IsFalse(rejected.ComponentFilter(structure, row));
            Assert.IsTrue(matching.ComponentFilter(structure, row));
            Assert.IsTrue(overlapping.ComponentFilter(structure, row));
        }

        [Test]
        public void ComponentFilter_EmptyMatcher_MatchesAnyRowWithIntersectingMask()
        {
            var structure = MakeStructure(0b100UL);
            var row = AppendRow(structure, 1UL);

            Assert.IsTrue(EntityMatcher.With.ComponentFilter(structure, row));
            Assert.IsTrue(EntityMatcher.WithMask(0b100UL).ComponentFilter(structure, row));
            Assert.IsFalse(EntityMatcher.WithMask(0b011UL).ComponentFilter(structure, row));
        }

        [Test]
        public void ComponentFilter_AllAnyNone_CombinedSemantics()
        {
            var matching = MakeStructure(ulong.MaxValue, IdOf<Position>());
            var matchingRow = AppendRow(matching, 1UL);
            matching.AddTag(IdOf<PlayerTag>(), matchingRow);

            var anyMissing = MakeStructure(ulong.MaxValue, IdOf<Position>());
            var anyMissingRow = AppendRow(anyMissing, 2UL);

            var nonePresent = MakeStructure(ulong.MaxValue, IdOf<Position>(), IdOf<OtherDense>());
            var nonePresentRow = AppendRow(nonePresent, 3UL);
            nonePresent.AddTag(IdOf<PlayerTag>(), nonePresentRow);

            var matcher = (EntityMatcher)EntityMatcher.With
                .OfAll<Position>()
                .OfAny<PlayerTag>()
                .OfAny<Mana>()
                .OfNone<OtherDense>();

            Assert.IsTrue(matcher.ComponentFilter(matching, matchingRow));
            Assert.IsFalse(matcher.ComponentFilter(anyMissing, anyMissingRow));
            Assert.IsFalse(matcher.ComponentFilter(nonePresent, nonePresentRow));
        }

        [Test]
        public void IsRelevantComponent_UnchangedForTagAndDiscreteTypes()
        {
            var matcher = EntityMatcher.With
                .OfAll<Position>()
                .OfAny<PlayerTag>()
                .OfNone<Mana>();

            Assert.IsTrue(matcher.IsRelevantComponent(typeof(Position)));
            Assert.IsTrue(matcher.IsRelevantComponent(typeof(PlayerTag)));
            Assert.IsTrue(matcher.IsRelevantComponent(typeof(Mana)));
            Assert.IsFalse(matcher.IsRelevantComponent(typeof(OtherDense)));
        }
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --filter FullyQualifiedName~EntityMatcherStructureTestUnit`
Expected: 编译失败，`EntityMatcher` 不含 `ComponentFilter(Structure, int)`

- [ ] **Step 3: 实现 EntityMatcher 的 Structure 求值**

3a. 在 `ECS/EntityMatcher.cs` 顶部把 `using CoreECS.Defines;` 替换为：

```csharp
using System;
using System.Collections.Generic;
using CoreECS.Defines;
using CoreECS.Structures;
```

3b. 把 `OfNone<T>` / `OfAny<T>` / `OfAll<T>` 三个方法替换为（仅新增 per-kind 解析写入；`HashSet<Type>` 写入保持原样）：

```csharp
        /// <summary>
        /// Excludes entities that have the specified component type.
        /// </summary>
        /// <typeparam name="T">Component type to exclude, must be a struct implementing IComponent&lt;T&gt;</typeparam>
        /// <returns>This matcher instance for method chaining</returns>
        public INoneOfEntityMatcher OfNone<T>() where T : struct, IComponent<T>
        {
            var type = typeof(T);
            m_none.Add(type);
            m_noneResolved.Add(type);
            return this;
        }

        /// <summary>
        /// Includes entities that have at least one of the specified component types.
        /// </summary>
        /// <typeparam name="T">Component type to include, must be a struct implementing IComponent&lt;T&gt;</typeparam>
        /// <returns>This matcher instance for method chaining</returns>
        public IAnyOfEntityMatcher OfAny<T>() where T : struct, IComponent<T>
        {
            var type = typeof(T);
            m_any.Add(type);
            m_anyResolved.Add(type);
            return this;
        }

        /// <summary>
        /// Requires entities to have all of the specified component types.
        /// </summary>
        /// <typeparam name="T">Component type to require, must be a struct implementing IComponent&lt;T&gt;</typeparam>
        /// <returns>This matcher instance for method chaining</returns>
        public IAllOfEntityMatcher OfAll<T>() where T : struct, IComponent<T>
        {
            var type = typeof(T);
            m_all.Add(type);
            m_allResolved.Add(type);
            return this;
        }
```

3c. 在 `private readonly HashSet<Type> m_none = new();` 之后新增三个解析集合字段：

```csharp
        /// <summary>All-of conditions resolved to per-kind type ids at configuration time.</summary>
        private readonly ResolvedSet m_allResolved = new();

        /// <summary>Any-of conditions resolved to per-kind type ids at configuration time.</summary>
        private readonly ResolvedSet m_anyResolved = new();

        /// <summary>None-of conditions resolved to per-kind type ids at configuration time.</summary>
        private readonly ResolvedSet m_noneResolved = new();
```

3d. 在 v1 `ComponentFilter(IReadOnlyCollection<IComponentRefCore>)` 方法之后、`IsRelevantComponent` 方法之前插入：

```csharp
        /// <summary>
        /// Evaluates this matcher against a structure row without materializing component
        /// references. Dense conditions resolve at structure level; tag and discrete
        /// conditions resolve at row level; the entity mask must intersect the structure mask.
        /// </summary>
        /// <param name="structure">Structure owning the row.</param>
        /// <param name="row">Live row inside the structure.</param>
        /// <returns>True when the mask, all, none and any criteria are satisfied.</returns>
        internal bool ComponentFilter(Structure structure, int row)
        {
            if ((EntityMask & structure.Mask) == 0UL) return false;
            if (HasAny(structure, row, m_noneResolved)) return false;
            if (!HasAll(structure, row, m_allResolved)) return false;

            return m_anyResolved.IsEmpty || HasAny(structure, row, m_anyResolved);
        }

        /// <summary>True when every condition in the set is present at the structure/row.</summary>
        private static bool HasAll(Structure structure, int row, ResolvedSet set)
        {
            for (var i = 0; i < set.Dense.Count; i++)
            {
                if (!structure.HasDense(set.Dense[i])) return false;
            }

            for (var i = 0; i < set.Tags.Count; i++)
            {
                if (!structure.HasTag(set.Tags[i], row)) return false;
            }

            for (var i = 0; i < set.Discretes.Count; i++)
            {
                if (!structure.HasDiscrete(set.Discretes[i], row)) return false;
            }

            return true;
        }

        /// <summary>True when at least one condition in the set is present at the structure/row.</summary>
        private static bool HasAny(Structure structure, int row, ResolvedSet set)
        {
            for (var i = 0; i < set.Dense.Count; i++)
            {
                if (structure.HasDense(set.Dense[i])) return true;
            }

            for (var i = 0; i < set.Tags.Count; i++)
            {
                if (structure.HasTag(set.Tags[i], row)) return true;
            }

            for (var i = 0; i < set.Discretes.Count; i++)
            {
                if (structure.HasDiscrete(set.Discretes[i], row)) return true;
            }

            return false;
        }

        /// <summary>
        /// Matcher conditions resolved once at configuration time into per-kind type id
        /// lists, so structure evaluation never inspects <see cref="Type"/> or the registry.
        /// </summary>
        private sealed class ResolvedSet
        {
            public readonly List<uint> Dense = new();
            public readonly List<uint> Tags = new();
            public readonly List<uint> Discretes = new();

            public bool IsEmpty => Dense.Count == 0 && Tags.Count == 0 && Discretes.Count == 0;

            /// <summary>Resolves the component type and appends its id to the kind bucket.</summary>
            public void Add(Type type)
            {
                var info = ComponentTypeRegistry.GetOrRegister(type);
                switch (info.Kind)
                {
                    case ComponentKind.Dense:
                        Dense.Add(info.TypeId);
                        break;
                    case ComponentKind.Discrete:
                        Discretes.Add(info.TypeId);
                        break;
                    case ComponentKind.Tag:
                        Tags.Add(info.TypeId);
                        break;
                }
            }
        }
```

- [ ] **Step 4: 运行测试确认通过**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --filter FullyQualifiedName~EntityMatcherStructureTestUnit`
Expected: PASS（9 个测试）

- [ ] **Step 5: 运行全量测试**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj`
Expected: 445 passed（Task 4 后 436 + 新增 9），0 failed

- [ ] **Step 6: 提交**

```bash
git add ECS/EntityMatcher.cs Test/EntityMatcherStructureTestUnit.cs
git commit -m "feat(core): add structure based matcher evaluation"
```

---

## Self-Review 记录

1. **Spec 覆盖**：本任务对应 handoff 第 3 节约束 1 中的"实体 id 单调分配 + `EntityLocation.Pool` 取用/归还 + `entityId → EntityLocation` 注册表"；generation 递增语义由 `EntityLocation.Release` 提供，并由测试 3/4 钉死。约束 2-9（Entity / ComponentRef / 编排层 / 匹配 / World / 删除 v1）不在本任务范围，已列入"本计划范围边界"待追加。
2. **占位符扫描**：无 TBD/TODO；测试与实现均为完整代码；每步命令与预期输出明确（新增 5 个测试，全量 405 passed）。
3. **类型一致性**：`Create()` 返回 `(ulong EntityId, EntityLocation Location)`、`TryGetLocation(ulong, out EntityLocation)`、`Destroy(ulong)`、`Count`——测试调用签名与实现完全一致；`EntityTable` 为 `internal sealed class`，测试项目已由 `ECS/ECS.csproj` 的 `InternalsVisibleTo("Test")` 可见。
4. **确定性**：测试 4 先 `EntityLocation.Pool.Clear()`，再依赖"唯一候选复用"断言 `AreSame`，避免共享静态池造成的顺序依赖（测试项目未启用并行，全仓库无 `[Parallelizable]`）。
5. **（Task 2）Spec 覆盖**：对应 handoff 第 3 节约束 3——内部 `(EntityLocation, generation, typeId, kind, version)`；有效性 = location generation 匹配 + 组件 version 匹配（dense/discrete），Tag 为 presence-only；`Revision` / `ChangeRevision()` 按 kind 分发且 `ChangeRevision()` 走 `Structure` 的 change 通知。`RO` / `RW` typed 包装与批量访问属后续任务，不在本任务范围。
6. **（Task 2）占位符扫描**：无 TBD/TODO；测试与实现均为完整代码；命令与预期输出明确（新增 8 个测试，全量 413 passed）。
7. **（Task 2）类型一致性**：`ComponentRefCore` 构造签名 `(EntityLocation, uint, uint, ComponentKind, uint)` 与测试调用一致；`NotNull` / `EntityId` / `Revision` / `ChangeRevision()` 与测试断言一致；新增的 `Structure` 非泛型方法（`GetDenseVersion(uint, int)` 等）与既有泛型方法靠参数个数区分重载，无歧义；`ComponentKind` 来自 `CoreECS.Defines`，测试与实现均可见（`InternalsVisibleTo("Test")`）。
8. **（Task 2）内核 API 补充（对任务文本的显式偏差）**：`Structure` 原有无类型 API 只有存在性查询（`HasDense` / `HasDiscrete` / `HasTag`），version/revision 只有泛型访问器；无类型 `ComponentRefCore` 无法调用泛型方法，因此补充 6 个 internal 非泛型访问器。该修改为纯新增，不改动任何现有泛型方法的行为与既有测试，并已同步到文件结构表与 Step 6 提交命令。若后续评审不接受该补充，替代方案是把核心改为 `ComponentRefCore<T>`（不再需要 `Structure` 改动，但偏离任务指定的无类型类声明）。
9. **（Task 3）Spec 覆盖**：对应 handoff 第 3 节约束 4/5——`CreateEntity` 走 `StructureRegistry.GetOrCreate(empty, mask)` + `EntityTable.Create` + `Append`；`DestroyEntity` 先按 dense（`DenseTypeIds`）/ discrete（`SpareSetOrNull.TypeIds` + `HasDiscrete`）调用 `OnDestroy`，再 `SwapRemove` + `EntityTable.Destroy`（tag 无 hook，已文档化）；`HasComponent` / `GetComponentRef` 按 kind 分发（缺失返回 null，tag 为 presence-only 核心）；discrete/tag 增删经 structure 并触发观察者事件；`ComponentHookDispatcher` 为 Task 4 的 Dense 迁移复用而设计（`RegisterDense<T>` + `InvokeDenseCreate` / `InvokeDenseDestroy`）。
10. **（Task 3）占位符扫描**：无 TBD/TODO；测试与实现均为完整代码；命令与预期输出明确（新增 15 个测试，全量 428 passed）。
11. **（Task 3）类型一致性**：`EntityTable.Create()` 返回 `(ulong EntityId, EntityLocation Location)`（Task 1）；`ComponentRefCore(EntityLocation, uint, uint, ComponentKind, uint)` 与 `NotNull` / `EntityId` / `Revision` 来自 Task 2；`Structure` 的 `SetDiscrete` / `AddTag` / `RemoveDiscrete` / `RemoveTag` / `HasDense` / `HasDiscrete` / `HasTag` / `DenseTypeIds` / `GetDenseVersion(uint,int)` / `GetDiscreteVersion(uint,int)` 均来自现有实现或 Task 2 计划；`ComponentKind` 取值为 `Dense` / `Discrete` / `Tag`；`SpareSetOrNull` / `TypeIds` 与 Step 3 插入代码一致。
12. **（Task 3）内核 API 补充（对任务文本的显式偏差）**：`Structure.SpareSetOrNull` 与 `SpareSetComponentContainer.TypeIds` 为纯新增。理由：`DestroyEntity` 必须枚举该结构上实际存在的 discrete 存储，容器现有 API 只能按已知 typeId 查询；且不能用 `SpareSet` 懒加载 getter，因为销毁路径不应为没有 discrete 组件的结构分配容器。两处新增不改变现有行为与既有测试，已同步文件结构表与 Step 6 提交列表。
13. **（Task 3）Hook 分发设计**：`ComponentHookDispatcher` 按 typeId 缓存 `ComponentHookPair`（`Action<Structure,int,ulong>` 的 Create/Destroy）；委托由泛型 `RegisterDense<T>` / `RegisterDiscrete<T>` 生成，内核不使用反射（netstandard2.1 / IL2CPP 友好）；`DestroyEntity` 对未注册类型静默跳过（仅发生在绕过编排层直接操作内核的场景，经编排层添加的组件一定已注册）。`OnCreate` 在写入后调用、`OnDestroy` 在移除前调用，hook 通过 `GetDenseRef<T>` / `GetDiscreteRef<T>` 读取存储实例，与 v1 `ComponentManager.Fix` / `Release` 语义一致。
14. **（Task 4）Spec 覆盖**：对应"本计划范围边界"中的 Task 4——结构间迁移（`AddType` / `RemoveType` 计算目标 key + registry 去重）、dense 增删事件（编排层以 `(target, targetRow, typeId)` 显式上报）、复用 Task 3 的 `ComponentHookDispatcher` 与 `Structure.CopyDenseTo` / `CopyTagsTo` / `MoveDiscreteTo`。测试覆盖：迁移后 discrete/tag/其他 dense 保留（含 version 保留）、新类型新版本、观察者目标结构与目标行、OnCreate/OnDestroy 时序、迁移前引用存活（location 共享）、重复添加/缺失移除抛异常、多次 AddDense 的排序组合与 mask 身份。
15. **（Task 4）占位符扫描**：无 TBD/TODO；测试与实现均为完整代码；命令与预期输出明确（新增 8 个测试，过滤运行 23 个，全量 436 passed）。
16. **（Task 4）类型一致性**：`AddDenseComponent<T>` 返回 `ComponentRefCore`（Task 2 构造签名 `(EntityLocation, uint, uint, ComponentKind, uint)`）；`RemoveDenseComponent<T>` 为 `void`；事件参数与 Task 3 的 `RecordingObserver` 扩展字段一致；`StructureKey.AddType` / `RemoveType` / `ToArray`、`StructureRegistry.GetOrCreate(in StructureKey)`、`Structure.Append` / `CopyDenseTo` / `CopyTagsTo` / `MoveDiscreteTo` / `SetDenseValue<T>` / `SwapRemove` / `HasDense` / `Key` / `Mask`、`ComponentVersion.Next()`、`ComponentHookDispatcher.RegisterDense` / `InvokeDenseCreate` / `InvokeDenseDestroy` 全部来自现有实现或 Task 2/3 计划。
17. **（Task 4）行为决策**：重复 AddDense 与缺失 RemoveDense 均抛 `InvalidOperationException` 且发生在任何迁移/写入之前（测试 6 同时断言结构未被改动）；迁移拷贝不触发观察者（底层 `CopyRowTo` 无 observer 调用，已核对 `SpareSetComponentContainer` / `TagContainer`），事件恰好一条且带目标行；`OnDestroy` 在旧行可读时调用（`Health.LastDestroyedValue == 42` 钉死）；`RemoveDense` 的目标结构经 registry 去重返回既有实例（测试 4 断言 `AreSame(positionStructure, target)`），`AddDense` 则断言源结构清空、目标结构独立。
18. **（Task 5）Spec 覆盖与 handoff 映射（最终检查）**：handoff 第 3 节约束 1-5 已全部由本计划 Task 1-5 覆盖——约束 1（实体 id 单调分配 + `EntityLocation.Pool` 取用/归还 + `entityId → EntityLocation` 注册表 + 销毁归还）→ Task 1 + Task 3 `DestroyEntity`；约束 2（Entity v2）的内核部分（location 共享、generation 失效检测、组件统一三种 kind）→ Task 1/2/3，公开 `Entity` 切换留 Plan 1c；约束 3（ComponentRef v2 内核：`(EntityLocation, generation, typeId, kind, version)`、`Revision` / `NotNull`、迁移/swap-remove 后自动有效、Tag 为 presence-only）→ Task 2（公开 `RO` / `RW` 包装留 Plan 1c）；约束 4（编排层：目标 key 计算 → `GetOrCreate` → `Append` → `CopyDenseTo` / `CopyTagsTo` / `MoveDiscreteTo` → `SwapRemove` → got 事件 + `OnCreate`，Dense 事件由编排层发出）→ Task 3/4；约束 5（匹配求值 v2：Dense + Mask 结构级、tag/discrete row 级、`IsRelevantComponent` 语义保留）→ Task 5。约束 6-9 留给 Plan 1c，已列在"本计划范围边界"。
19. **（Task 5）占位符扫描**：无 TBD/TODO；测试与实现均为完整代码；命令与预期输出明确（新增 9 个测试 = 任务列举的 8 个场景 + 1 个 all/any/none 三集合组合语义，全量 445 passed）。
20. **（Task 5）类型一致性**：`ComponentTypeRegistry.GetOrRegister(Type)` 返回 `ComponentTypeInfo`（`TypeId` / `Kind`），`ComponentKind` 为 `Dense` / `Discrete` / `Tag`；`Structure.HasDense(uint)` / `HasTag(uint, int)` / `HasDiscrete(uint, int)` / `Mask` 均为现有 public API；`Structure` 构造为 internal、`StructureKey(uint[], ulong)` 为 public 且要求 id 有序（测试辅助 `MakeStructure` 先 `Array.Sort`）；测试经 `InternalsVisibleTo("Test")` 调用 internal 重载。
21. **（Task 5）行为决策**：v1 `ComponentFilter(IReadOnlyCollection<IComponentRefCore>)`、`IsRelevantComponent` 与 `m_all` / `m_any` / `m_none` / `m_changing` 全部保留（Plan 1c 删除）；类型解析在 `OfAll` / `OfAny` / `OfNone` 配置时完成并缓存（`GetOrRegister` 只增不减，重复解析幂等）；求值顺序 none → all → any，空 `any` 视为满足，mask 交集先于所有条件；测试链式构建后以 `(EntityMatcher)` 还原具体类型调用 internal 重载（fluent 方法返回 `this`，转换恒成功）。
22. **（Task 2 评审修订）**：质量审查发现两处 Important 问题并已修订计划——(a) 原 `StaleLocation_GenerationMismatch_InvalidatesRef` 经 `Pool.Release` 失效（同时清空 Structure 并递增 generation），只覆盖 null-structure 分支，未隔离 generation 不匹配分支（Tag 引用的唯一失效防线）；新增测试 `RecycledLocation_ReboundToNewStructureWithNewerGeneration_InvalidatesRef`（drain 池 → 释放 → 复用同一实例并重绑到同 typeId/version 的新结构，断言仅 generation 差异即失效）。(b) Dense `NotNull` 在 `SwapRemove` 后、`Release` 前的窗口内会越界读取（Debug 触发 `Debug.Assert`，Release 下 stale 版本可能误报 true 且 `EntityId` 越界抛异常）；`NotNull` 的 Dense 分支新增 `row >= 0 && row < structure.Count` 行存活护栏并同步 XML 文档（Discrete/Tag 经 `DiscreteStore.Has` / `TagContainer.Has` 已天然有界），并新增回归测试 `StaleRow_AfterSwapRemoveWithoutRelease_ReportsInvalid` 钉死该窗口（无护栏时 Debug 断言失败、Release 误报 true）。Task 2 测试数 6 → 8，全量 411 → 413，Task 3/4/5 与 Plan 1c Task 3 的预期总数同步 +2（当时的预期：421 / 428 / 437；1c 收口 405，随后由第 23 条修订为 428 / 435 / 444；1c 收口 412）。两处修订仅影响内部类与测试，不改变公开 API。
23. **（Task 3 评审修订）**：质量审查用探针复现了 `DestroyEntity` 的三类重入问题并判定 1 处 Critical + 3 处 Important，已修订计划——(a) **Critical 销毁重入**：hook 内新建 discrete 存储会在 `foreach (spareSet.TypeIds)` 枚举 `m_stores` 时抛 `InvalidOperationException`；hook 内销毁同结构其他实体可能位移被销毁实体的行，随后 `SwapRemove` 旧行抛 `ArgumentOutOfRangeException` 或移除错误实体。修订为 `m_destroying` 守卫（同一实体重入 no-op）+ discrete 存储 id 快照 + `location.Structure?.SwapRemove(location.Row)` 重读行绑定 + try/finally 释放 location；hook 期间实体仍可访问（v1 语义）。(b) **Important hook 异常策略**：恢复 v1 `Fix`/`Release` 的 catch + `Log.Exp`（`ECS/Managers/ComponentManager.cs:459-466`、`489-496`），四个 `Invoke*` 统一 try/catch，异常不中断编排。(c) **Important 委托缓存**：`RegisterDense<T>` / `RegisterDiscrete<T>` 改为写入泛型静态持有类 `DenseHooks<T>` / `DiscreteHooks<T>` 的 `static readonly Pair`，消除每次调用的委托分配。(d) **Important 覆盖缺口**：新增 7 个测试（`GetComponentRef` Dense 分支、`DestroyEntity` dense hook 时序、同实体重入 no-op、销毁更早实体触发行位移、hook 新建 discrete 存储、destroy hook 抛异常仍完成、create hook 抛异常仍保留组件），并在既有测试中补 `LastDestroyedValue`（discrete OnDestroy 在移除前可读）、死实体 `RequireLocation` 抛异常、absent tag 重复移除 no-op 断言。Task 3 测试数 8 → 15，全量 413 → 428，Task 4/5 与 Plan 1c Task 3 的预期总数同步 +7（435 / 444；1c 收口 412）。所有修订仅影响内部类与测试，不改变公开 API。
24. **（Task 3 复审遗留 → Task 4 关闭）**：复审确认修订后 `DestroyEntity` 对 discrete/tag 安全，但指出 latent 缺口——一旦 Task 4 引入 dense 迁移，hook 对正在销毁的实体调用 `AddDenseComponent` / `RemoveDenseComponent` 会把 location 重绑到新结构，最终 `SwapRemove` 移除的是新结构的行，迁移的 dense 组件静默丢失（OnCreate 已发、OnDestroy 不发）。Task 4 因此把 `RequireLocation` 扩展为拒绝 `m_destroying` 中的实体（读操作不受影响），并新增回归测试 `DestroyEntity_HookMutatesDyingEntity_ThrowsAndDestroyCompletes`（hook 内 dense 迁移被 `InvalidOperationException` 拒绝、异常被 dispatcher 记录、销毁照常完成、registry 不新增结构）。Task 4 测试数 7 → 8，全量 428 → 436，Task 5 与 Plan 1c Task 3 的预期总数同步 +1（445；1c 收口 413）。
