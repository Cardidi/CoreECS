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
| `ECS/Structures/ComponentOrchestrator.cs` | 实体生命周期 + 非 Dense 组件操作：`CreateEntity` / `DestroyEntity` / `HasComponent` / `GetComponentRef` / discrete 与 tag 增删 |
| `ECS/Structures/SpareSetComponentContainer.cs` | （Task 3 修改）新增 `TypeIds` 枚举，供销毁时遍历存在的 discrete 存储 |
| `Test/ComponentOrchestratorTestUnit.cs` | `ComponentOrchestrator` 单元测试（生命周期、discrete/tag 增删、hook 调用、观察者事件） |

测试文件统一放 `Test/`，命名 `<TypeName>TestUnit.cs`，风格与现有测试一致（classic asserts；`Test.csproj` 已通过 `<Using Include="NUnit.Framework"/>` 提供全局 using，测试无需显式 `using NUnit.Framework;`）。

## 本计划范围边界

本计划覆盖 handoff 第 3 节 Plan 1b 的**前三个内核任务**（Task 1 `EntityTable`、Task 2 `ComponentRefCore`、Task 3 `ComponentOrchestrator`）。以下任务将在后续会话中追加到本文件（追加时同步更新文件结构表）：

- **Task 4**：Dense 组件迁移（结构间迁移 + Dense 增删事件；复用 Task 3 的 `ComponentHookDispatcher`）
- **Task 5**：matcher 求值 v2（结构级 + row 级）与 `EntityMatchManager` 接线
- **Plan 1c**：`Entity` / `EntityManager` v2、`EntityExtension` 适配、删除 v1 存储、迁移内部测试、切换 `World`

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
        /// component is present at the location row and, for dense/discrete components,
        /// the stored instance version matches. Tags are presence-only.
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
                        return structure.HasDense(TypeId) &&
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
Expected: PASS（6 个测试）

- [ ] **Step 5: 运行全量测试**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj`
Expected: 411 passed（Task 1 后 405 + 新增 6），0 failed

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

            public void OnCreate(ulong entityId) => CreateCount += 1;

            public void OnDestroy(ulong entityId) => DestroyCount += 1;

            public static int CreateCount;
            public static int DestroyCount;
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
            Assert.AreEqual(generation + 1U, firstLocation.Generation);
            Assert.IsNull(firstLocation.Structure);

            Assert.IsTrue(m_table.TryGetLocation(secondId, out var moved));
            Assert.AreEqual(0, moved.Row);
            Assert.IsTrue(m_orchestrator.HasComponent<ManaComponent>(secondId));
            Assert.AreEqual(2, moved.Structure.GetDiscreteRef<ManaComponent>(moved.Row).Value);
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

namespace CoreECS.Structures
{
    /// <summary>
    /// Non-generic dispatch of component lifecycle hooks (<c>OnCreate</c> / <c>OnDestroy</c>).
    /// Generic callers register the per-type hook pair; non-generic orchestration code that
    /// only knows type ids invokes the cached delegates.
    /// </summary>
    internal static class ComponentHookDispatcher
    {
        private static readonly ConcurrentDictionary<uint, ComponentHookPair> s_dense = new();
        private static readonly ConcurrentDictionary<uint, ComponentHookPair> s_discrete = new();

        /// <summary>Registers the dense component hooks for <typeparamref name="T"/>.</summary>
        public static void RegisterDense<T>() where T : struct, IComponent<T>
        {
            var typeId = ComponentTypeRegistry.GetOrRegister<T>().TypeId;
            s_dense[typeId] = new ComponentHookPair(
                (structure, row, entityId) => structure.GetDenseRef<T>(row).OnCreate(entityId),
                (structure, row, entityId) => structure.GetDenseRef<T>(row).OnDestroy(entityId));
        }

        /// <summary>Registers the discrete component hooks for <typeparamref name="T"/>.</summary>
        public static void RegisterDiscrete<T>() where T : struct, IDiscreteComponent<T>
        {
            var typeId = ComponentTypeRegistry.GetOrRegister<T>().TypeId;
            s_discrete[typeId] = new ComponentHookPair(
                (structure, row, entityId) => structure.GetDiscreteRef<T>(row).OnCreate(entityId),
                (structure, row, entityId) => structure.GetDiscreteRef<T>(row).OnDestroy(entityId));
        }

        /// <summary>Invokes <c>OnCreate</c> on the dense component at the row; no-op when unregistered.</summary>
        public static void InvokeDenseCreate(Structure structure, int row, uint typeId, ulong entityId)
        {
            if (s_dense.TryGetValue(typeId, out var hooks)) hooks.Create(structure, row, entityId);
        }

        /// <summary>Invokes <c>OnDestroy</c> on the dense component at the row; no-op when unregistered.</summary>
        public static void InvokeDenseDestroy(Structure structure, int row, uint typeId, ulong entityId)
        {
            if (s_dense.TryGetValue(typeId, out var hooks)) hooks.Destroy(structure, row, entityId);
        }

        /// <summary>Invokes <c>OnCreate</c> on the discrete component at the row; no-op when unregistered.</summary>
        public static void InvokeDiscreteCreate(Structure structure, int row, uint typeId, ulong entityId)
        {
            if (s_discrete.TryGetValue(typeId, out var hooks)) hooks.Create(structure, row, entityId);
        }

        /// <summary>Invokes <c>OnDestroy</c> on the discrete component at the row; no-op when unregistered.</summary>
        public static void InvokeDiscreteDestroy(Structure structure, int row, uint typeId, ulong entityId)
        {
            if (s_discrete.TryGetValue(typeId, out var hooks)) hooks.Destroy(structure, row, entityId);
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
        /// </summary>
        public void DestroyEntity(ulong entityId)
        {
            if (!m_table.TryGetLocation(entityId, out var location)) return;

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
                    foreach (var typeId in spareSet.TypeIds)
                    {
                        if (!structure.HasDiscrete(typeId, row)) continue;
                        ComponentHookDispatcher.InvokeDiscreteDestroy(structure, row, typeId, entityId);
                    }
                }

                structure.SwapRemove(row);
            }

            m_table.Destroy(entityId);
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
Expected: PASS（8 个测试）

- [ ] **Step 5: 运行全量测试**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj`
Expected: 419 passed（Task 2 后 411 + 新增 8），0 failed

- [ ] **Step 6: 提交**

```bash
git add ECS/Structures/ComponentHookDispatcher.cs ECS/Structures/ComponentOrchestrator.cs ECS/Structures/Structure.cs ECS/Structures/SpareSetComponentContainer.cs Test/ComponentOrchestratorTestUnit.cs
git commit -m "feat(core): add component orchestrator entity lifecycle"
```

---

## Self-Review 记录

1. **Spec 覆盖**：本任务对应 handoff 第 3 节约束 1 中的"实体 id 单调分配 + `EntityLocation.Pool` 取用/归还 + `entityId → EntityLocation` 注册表"；generation 递增语义由 `EntityLocation.Release` 提供，并由测试 3/4 钉死。约束 2-9（Entity / ComponentRef / 编排层 / 匹配 / World / 删除 v1）不在本任务范围，已列入"本计划范围边界"待追加。
2. **占位符扫描**：无 TBD/TODO；测试与实现均为完整代码；每步命令与预期输出明确（新增 5 个测试，全量 405 passed）。
3. **类型一致性**：`Create()` 返回 `(ulong EntityId, EntityLocation Location)`、`TryGetLocation(ulong, out EntityLocation)`、`Destroy(ulong)`、`Count`——测试调用签名与实现完全一致；`EntityTable` 为 `internal sealed class`，测试项目已由 `ECS/ECS.csproj` 的 `InternalsVisibleTo("Test")` 可见。
4. **确定性**：测试 4 先 `EntityLocation.Pool.Clear()`，再依赖"唯一候选复用"断言 `AreSame`，避免共享静态池造成的顺序依赖（测试项目未启用并行，全仓库无 `[Parallelizable]`）。
5. **（Task 2）Spec 覆盖**：对应 handoff 第 3 节约束 3——内部 `(EntityLocation, generation, typeId, kind, version)`；有效性 = location generation 匹配 + 组件 version 匹配（dense/discrete），Tag 为 presence-only；`Revision` / `ChangeRevision()` 按 kind 分发且 `ChangeRevision()` 走 `Structure` 的 change 通知。`RO` / `RW` typed 包装与批量访问属后续任务，不在本任务范围。
6. **（Task 2）占位符扫描**：无 TBD/TODO；测试与实现均为完整代码；命令与预期输出明确（新增 6 个测试，全量 411 passed）。
7. **（Task 2）类型一致性**：`ComponentRefCore` 构造签名 `(EntityLocation, uint, uint, ComponentKind, uint)` 与测试调用一致；`NotNull` / `EntityId` / `Revision` / `ChangeRevision()` 与测试断言一致；新增的 `Structure` 非泛型方法（`GetDenseVersion(uint, int)` 等）与既有泛型方法靠参数个数区分重载，无歧义；`ComponentKind` 来自 `CoreECS.Defines`，测试与实现均可见（`InternalsVisibleTo("Test")`）。
8. **（Task 2）内核 API 补充（对任务文本的显式偏差）**：`Structure` 原有无类型 API 只有存在性查询（`HasDense` / `HasDiscrete` / `HasTag`），version/revision 只有泛型访问器；无类型 `ComponentRefCore` 无法调用泛型方法，因此补充 6 个 internal 非泛型访问器。该修改为纯新增，不改动任何现有泛型方法的行为与既有测试，并已同步到文件结构表与 Step 6 提交命令。若后续评审不接受该补充，替代方案是把核心改为 `ComponentRefCore<T>`（不再需要 `Structure` 改动，但偏离任务指定的无类型类声明）。
9. **（Task 3）Spec 覆盖**：对应 handoff 第 3 节约束 4/5——`CreateEntity` 走 `StructureRegistry.GetOrCreate(empty, mask)` + `EntityTable.Create` + `Append`；`DestroyEntity` 先按 dense（`DenseTypeIds`）/ discrete（`SpareSetOrNull.TypeIds` + `HasDiscrete`）调用 `OnDestroy`，再 `SwapRemove` + `EntityTable.Destroy`（tag 无 hook，已文档化）；`HasComponent` / `GetComponentRef` 按 kind 分发（缺失返回 null，tag 为 presence-only 核心）；discrete/tag 增删经 structure 并触发观察者事件；`ComponentHookDispatcher` 为 Task 4 的 Dense 迁移复用而设计（`RegisterDense<T>` + `InvokeDenseCreate` / `InvokeDenseDestroy`）。
10. **（Task 3）占位符扫描**：无 TBD/TODO；测试与实现均为完整代码；命令与预期输出明确（新增 8 个测试，全量 419 passed）。
11. **（Task 3）类型一致性**：`EntityTable.Create()` 返回 `(ulong EntityId, EntityLocation Location)`（Task 1）；`ComponentRefCore(EntityLocation, uint, uint, ComponentKind, uint)` 与 `NotNull` / `EntityId` / `Revision` 来自 Task 2；`Structure` 的 `SetDiscrete` / `AddTag` / `RemoveDiscrete` / `RemoveTag` / `HasDense` / `HasDiscrete` / `HasTag` / `DenseTypeIds` / `GetDenseVersion(uint,int)` / `GetDiscreteVersion(uint,int)` 均来自现有实现或 Task 2 计划；`ComponentKind` 取值为 `Dense` / `Discrete` / `Tag`；`SpareSetOrNull` / `TypeIds` 与 Step 3 插入代码一致。
12. **（Task 3）内核 API 补充（对任务文本的显式偏差）**：`Structure.SpareSetOrNull` 与 `SpareSetComponentContainer.TypeIds` 为纯新增。理由：`DestroyEntity` 必须枚举该结构上实际存在的 discrete 存储，容器现有 API 只能按已知 typeId 查询；且不能用 `SpareSet` 懒加载 getter，因为销毁路径不应为没有 discrete 组件的结构分配容器。两处新增不改变现有行为与既有测试，已同步文件结构表与 Step 6 提交列表。
13. **（Task 3）Hook 分发设计**：`ComponentHookDispatcher` 按 typeId 缓存 `ComponentHookPair`（`Action<Structure,int,ulong>` 的 Create/Destroy）；委托由泛型 `RegisterDense<T>` / `RegisterDiscrete<T>` 生成，内核不使用反射（netstandard2.1 / IL2CPP 友好）；`DestroyEntity` 对未注册类型静默跳过（仅发生在绕过编排层直接操作内核的场景，经编排层添加的组件一定已注册）。`OnCreate` 在写入后调用、`OnDestroy` 在移除前调用，hook 通过 `GetDenseRef<T>` / `GetDiscreteRef<T>` 读取存储实例，与 v1 `ComponentManager.Fix` / `Release` 语义一致。
