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
| `ECS/Structures/Structure.cs` | （Task 2 修改）新增 6 个按 typeId 读写 dense/discrete version/revision 的 internal 非泛型方法，供无类型核心分发 |
| `Test/ComponentRefCoreTestUnit.cs` | `ComponentRefCore` 单元测试（dense/discrete/tag 有效性、revision 递增、generation 失效） |

测试文件统一放 `Test/`，命名 `<TypeName>TestUnit.cs`，风格与现有测试一致（classic asserts；`Test.csproj` 已通过 `<Using Include="NUnit.Framework"/>` 提供全局 using，测试无需显式 `using NUnit.Framework;`）。

## 本计划范围边界

本计划覆盖 handoff 第 3 节 Plan 1b 的**前两个内核任务**（Task 1 `EntityTable`、Task 2 `ComponentRefCore`）。以下任务将在后续会话中追加到本文件（追加时同步更新文件结构表）：

- `ComponentOrchestrator`（`ComponentManager` v2：结构迁移 + Dense 增删事件）
- matcher 求值 v2（结构级 + row 级）与 `EntityMatchManager` 接线
- `Entity` / `EntityManager` v2 与 `EntityExtension` 适配
- 删除 v1 存储、迁移内部测试、Plan 1c 切换 `World`

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

## Self-Review 记录

1. **Spec 覆盖**：本任务对应 handoff 第 3 节约束 1 中的"实体 id 单调分配 + `EntityLocation.Pool` 取用/归还 + `entityId → EntityLocation` 注册表"；generation 递增语义由 `EntityLocation.Release` 提供，并由测试 3/4 钉死。约束 2-9（Entity / ComponentRef / 编排层 / 匹配 / World / 删除 v1）不在本任务范围，已列入"本计划范围边界"待追加。
2. **占位符扫描**：无 TBD/TODO；测试与实现均为完整代码；每步命令与预期输出明确（新增 5 个测试，全量 405 passed）。
3. **类型一致性**：`Create()` 返回 `(ulong EntityId, EntityLocation Location)`、`TryGetLocation(ulong, out EntityLocation)`、`Destroy(ulong)`、`Count`——测试调用签名与实现完全一致；`EntityTable` 为 `internal sealed class`，测试项目已由 `ECS/ECS.csproj` 的 `InternalsVisibleTo("Test")` 可见。
4. **确定性**：测试 4 先 `EntityLocation.Pool.Clear()`，再依赖"唯一候选复用"断言 `AreSame`，避免共享静态池造成的顺序依赖（测试项目未启用并行，全仓库无 `[Parallelizable]`）。
5. **（Task 2）Spec 覆盖**：对应 handoff 第 3 节约束 3——内部 `(EntityLocation, generation, typeId, kind, version)`；有效性 = location generation 匹配 + 组件 version 匹配（dense/discrete），Tag 为 presence-only；`Revision` / `ChangeRevision()` 按 kind 分发且 `ChangeRevision()` 走 `Structure` 的 change 通知。`RO` / `RW` typed 包装与批量访问属后续任务，不在本任务范围。
6. **（Task 2）占位符扫描**：无 TBD/TODO；测试与实现均为完整代码；命令与预期输出明确（新增 6 个测试，全量 411 passed）。
7. **（Task 2）类型一致性**：`ComponentRefCore` 构造签名 `(EntityLocation, uint, uint, ComponentKind, uint)` 与测试调用一致；`NotNull` / `EntityId` / `Revision` / `ChangeRevision()` 与测试断言一致；新增的 `Structure` 非泛型方法（`GetDenseVersion(uint, int)` 等）与既有泛型方法靠参数个数区分重载，无歧义；`ComponentKind` 来自 `CoreECS.Defines`，测试与实现均可见（`InternalsVisibleTo("Test")`）。
8. **（Task 2）内核 API 补充（对任务文本的显式偏差）**：`Structure` 原有无类型 API 只有存在性查询（`HasDense` / `HasDiscrete` / `HasTag`），version/revision 只有泛型访问器；无类型 `ComponentRefCore` 无法调用泛型方法，因此补充 6 个 internal 非泛型访问器。该修改为纯新增，不改动任何现有泛型方法的行为与既有测试，并已同步到文件结构表与 Step 6 提交命令。若后续评审不接受该补充，替代方案是把核心改为 `ComponentRefCore<T>`（不再需要 `Structure` 改动，但偏离任务指定的无类型类声明）。
