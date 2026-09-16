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

测试文件统一放 `Test/`，命名 `<TypeName>TestUnit.cs`，风格与现有测试一致（classic asserts；`Test.csproj` 已通过 `<Using Include="NUnit.Framework"/>` 提供全局 using，测试无需显式 `using NUnit.Framework;`）。

## 本计划范围边界

本计划覆盖 handoff 第 3 节 Plan 1b 的**第一个内核任务**。以下任务将在后续会话中追加到本文件（追加时同步更新文件结构表）：

- `ComponentRefCore` v2（`EntityLocation` + generation + typeId + kind + version）
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

## Self-Review 记录

1. **Spec 覆盖**：本任务对应 handoff 第 3 节约束 1 中的"实体 id 单调分配 + `EntityLocation.Pool` 取用/归还 + `entityId → EntityLocation` 注册表"；generation 递增语义由 `EntityLocation.Release` 提供，并由测试 3/4 钉死。约束 2-9（Entity / ComponentRef / 编排层 / 匹配 / World / 删除 v1）不在本任务范围，已列入"本计划范围边界"待追加。
2. **占位符扫描**：无 TBD/TODO；测试与实现均为完整代码；每步命令与预期输出明确（新增 5 个测试，全量 405 passed）。
3. **类型一致性**：`Create()` 返回 `(ulong EntityId, EntityLocation Location)`、`TryGetLocation(ulong, out EntityLocation)`、`Destroy(ulong)`、`Count`——测试调用签名与实现完全一致；`EntityTable` 为 `internal sealed class`，测试项目已由 `ECS/ECS.csproj` 的 `InternalsVisibleTo("Test")` 可见。
4. **确定性**：测试 4 先 `EntityLocation.Pool.Clear()`，再依赖"唯一候选复用"断言 `AreSame`，避免共享静态池造成的顺序依赖（测试项目未启用并行，全仓库无 `[Parallelizable]`）。
