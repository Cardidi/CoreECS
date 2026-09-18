# CoreECS v2 Phase 3 查询 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 交付 spec 第 3 阶段（查询）：先以专用契约测试钉死 `Structure` 批量访问（`RO<T>()` / `RW<T>()`），随后追加 `IEntityQuery` + `World.Query(matcher)`（删除 v1 重载），最后把 collector 切到结构级粗筛。

**Architecture:** 批量访问直接读/标记 `Structure` 的行对齐 SoA 列（`RO` 只读不标记；`RW` 获取即整结构标记该类型全部 live row）；查询走 `IEntityMatcher` 的结构级粗筛（Dense 集合 + Mask）加行级精筛（Tag 位图 / Discrete 存在性），由 `IEntityQuery.Refresh()` 生成快照；collector 复用同一粗筛缓存。全部建立在 Phase 1 内核（`Structure` / `StructureKey` / `StructureRegistry` / `EntityLocation` / `EntityTable`）之上，不新增存储层。

**Tech Stack:** C# 9（`LangVersion 9`）、`net8.0` + `netstandard2.1`、NUnit 3.14、`dotnet test --filter`

**Spec:** `docs/superpowers/specs/2026-09-17-coreecs-v2-design.md`（5.2 / 5.3 / 5.4、已决事项 6/7）

**Handoff:** `docs/superpowers/plans/2026-09-17-coreecs-v2-handoff.md`（Phase 3 范围与"计划编写子代理单次只写 1-2 个任务"约定）

---

## File Structure

| 文件 | 职责 |
|---|---|
| `Test/StructureBatchAccessTestUnit.cs` | Task 1 新增：`Structure.RO<T>()` / `RW<T>()` 批量访问契约测试（8 个） |
| `ECS/Structures/Structure.cs` | Task 1 核对对象：`RO<T>()` / `RW<T>()` 已在 Plan 1a Task 7 落地（当前 `Structure.cs:192-219`），本任务预期不改动；仅当契约测试失败时按参考实现修正 |
| `ECS/Defines/IEntityQuery.cs` | Task 2 新增：`IEntityQuery : IDisposable` 接口（`Matcher` / `Structures` / `Entities` / `Refresh()`，spec 5.2） |
| `ECS/EntityQuery.cs` | Task 2 新增：`internal sealed EntityQuery` 非池化实现；`Refresh()` 重建快照（遍历 `Table.EntityIds` + `ComponentFilter` 精筛 + 结构去重） |
| `ECS/World.cs` | Task 2 修改：删除 v1 `Query(IEntityMatcher, ICollection<ulong>)` / `Query(IEntityMatcher, ICollection<Entity>)`，新增 `Query(IEntityMatcher)` → `IEntityQuery` |
| `ECS/EntityMatcherExtension.cs` | Task 2 修改：两个 `Query(this IEntityMatcher, World, ICollection<...>)` 扩展保留签名，方法体迁移到 `IEntityQuery` |
| `Test/EntityQueryTestUnit.cs` | Task 2 新增：`IEntityQuery` 契约测试（10 个） |
| `Test/WorldTestUnit.cs` | Task 2 修改：查询测试迁移到新 API（11 → 8：删 2、并 2→1；全类 26 → 23） |
| `Test/EntityMatcherTestUnit.cs` | Task 2 修改：12 处 `_world.Query(matcher, collection)` 调用点迁移到 `IEntityQuery`（17 个测试数不变） |
| `ECS/EntityMatcher.cs` | Task 3 修改：`ComponentFilter` 拆为结构级 `EvaluateStructure` + 行级 `RowFilter`；新增 `internal readonly struct StructureMatch` 与 `internal int StructureEvaluationCount` 测试钩子 |
| `ECS/Managers/EntityMatchManager.cs` | Task 3 修改：`Collector` 新增每 collector 的 `StructureMatches` 缓存与 `Matches` 方法；`_changeCollector` 调用点切换到缓存路径；`Dispose` / `OnManagerDestroyed` 清理缓存 |
| `Test/CollectorAccelerationTestUnit.cs` | Task 3 新增：collector 结构级加速契约测试（9 个） |

测试文件统一放 `Test/`，命名 `<TypeName>TestUnit.cs`，风格与现有测试一致（classic asserts；`Test.csproj` 已通过 `<Using Include="NUnit.Framework"/>` 提供全局 using，测试无需显式 `using NUnit.Framework;`）。

## 本计划范围边界

本计划覆盖 spec 5.2 / 5.3 / 5.4（Phase 3 查询），按 handoff 第 2 节的建议拆分：

- **Task 1（本次 dispatch）**：`Structure` 批量访问契约——`RO<T>()` / `RW<T>()` 的行对齐、`RW` 整结构标记、缺失/非 Dense 类型抛异常、容量不外露等语义由专用契约测试钉死
- **Task 2（后续 dispatch 追加）**：`IEntityQuery`（不池化、`IDisposable`、`Matcher` / `Structures` / `Entities` / `Refresh()`）+ `world.Query(matcher)`；删除 v1 `Query(IEntityMatcher, ICollection<ulong>)` / `Query(IEntityMatcher, ICollection<Entity>)` 重载并迁移 `EntityMatcherTestUnit` / `WorldTestUnit` 等调用点
- **Task 3（后续 dispatch 追加）**：collector 结构级加速——`EntityMatchManager` 的 `_changeCollector` 用结构级粗筛（Dense + Mask）缓存，行级只查 Tag / Discrete；用户 API（`Collected` / `Matching` / `Clashing` / `Changed` / `Flush` / `EntityCollectorFlag`）不变

**不包括**（spec 明确后置）：系统分组排序（Phase 4）、World 合并与生命周期收敛（Phase 5）、CommandBuffer 与文档更新（Phase 6）。

---

## Task 1: Structure 批量访问契约（RO/RW）

**Files:**
- Test: `Test/StructureBatchAccessTestUnit.cs`（新增，8 个测试）
- 核对（预期不改动）: `ECS/Structures/Structure.cs`（`RO<T>()` / `RW<T>()`，当前 `Structure.cs:192-219`）

**前置:** 无。基线 430 passed（handoff 记录）。测试直接用 `new Structure(new StructureKey(...))` + `Append` / `SetDenseValue` 手工搭建结构（`Structure` 构造为 internal，测试经 `InternalsVisibleTo("Test")` 访问）。

**设计说明（执行时不要改动，评审时按此核对）：**

- **绑定语义（spec 5.3 + 已决事项 7）**：`RO<T>()` 返回 `ReadOnlySpan<T>`，只读、不标记（不改 revision、不发 observer 事件）；`RW<T>()` 返回 `Span<T>`，**获取即**标记该结构该类型全部 live row（每 row revision `(revision % uint.MaxValue) + 1`，且每 row 恰好一次 `Observer?.OnComponentChanged`）；两者与 `Entities` / `Count` 行对齐，span 长度为 `m_count`（不暴露容量行）；`Count == 0` 时返回空 span（除类型存在性检查外不抛异常）。
- **类型检查复用 `SlotOf<T>()`**：与 `GetDenseRef<T>` 同一查找；`T` 不是本结构 Dense 类型（缺失、Discrete、Tag）时抛 `InvalidOperationException`。`RO` / `RW` 都是 `where T : struct, IComponent<T>`，Discrete / Tag 类型也满足约束，因此必须由 `SlotOf<T>()` 的 dense 查找拦截。
- **实勘偏差（重要）**：任务文本假设 `RO<T>()` / `RW<T>()` 尚未实现，但当前 HEAD 已包含二者——Plan 1a Task 7（commit `0d75744`，`Structure.cs:192-219`），且 `StructureTestUnit` 已有 4 个基础用例（`RO_ReturnsRowAlignedSpan` / `RW_BumpsRevisionAndNotifiesObserver` / `RW_OnEmptyStructure_ReturnsEmptySpanAndDoesNotNotify` / `SlotOf_ThrowsForMissingDenseType`）。逐条核对后，实现与 spec 5.3 / 已决事项 7 完全一致，因此本任务重定义为**契约测试补齐**：生产代码预期零改动；参考实现保留在下方，仅作为 Step 2 失败时的修正路径。handoff 第 2 节 Phase 3 范围第 1 条同样滞后，建议后续修订该条。
- **参考实现（与当前 `ECS/Structures/Structure.cs:192-219` 逐字一致；预期不使用）**：

```csharp
        /// <summary>
        /// Gets a read-only span over a dense component column.
        /// The span is invalidated by structural changes (Append/SwapRemove/Grow).
        /// </summary>
        public ReadOnlySpan<T> RO<T>() where T : struct, IComponent<T>
        {
            return ((T[])m_denseData[SlotOf<T>()]).AsSpan(0, m_count);
        }

        /// <summary>
        /// Gets a writable span over a dense component column.
        /// Acquiring the span marks every row as changed (revision bump + observer notification),
        /// so per-row acquisition is O(n²); acquire once per structure.
        /// The span is invalidated by structural changes (Append/SwapRemove/Grow).
        /// </summary>
        public Span<T> RW<T>() where T : struct, IComponent<T>
        {
            var slot = SlotOf<T>();
            var revisions = m_denseRevisions[slot];
            var typeId = m_denseTypeIds[slot];
            for (var row = 0; row < m_count; row++)
            {
                revisions[row] = (revisions[row] % uint.MaxValue) + 1;
                Observer?.OnComponentChanged(this, row, typeId);
            }

            return ((T[])m_denseData[slot]).AsSpan(0, m_count);
        }
```

- **为什么独立成文件**：现有 4 个用例只覆盖单行 RO 读值、单行 RW 标记、空结构 RW、RO 缺失类型抛异常；未覆盖：RO 无副作用（revision / observer）、RO 反映前一次 RW 的写入、RW 整结构逐行标记与事件计数、按列隔离（其他 Dense 列不受影响）、RW 对缺失类型抛异常、Discrete / Tag 类型抛异常、容量大于 Count 时只暴露 live 行。本任务用 8 个测试钉死上述契约。
- **TDD 说明**：因为生产代码已在 Plan 1a 落地，Step 2 预期直接 PASS（不是经典的"先红"）。任何失败都表示实现与 spec 5.3 存在偏差，执行 Step 3 修正后重跑；Step 2 全绿则跳过 Step 3。

- [ ] **Step 1: 写契约测试**

创建 `Test/StructureBatchAccessTestUnit.cs`：

```csharp
using CoreECS.Defines;
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class StructureBatchAccessTestUnit
    {
        private struct Position : IComponent<Position>
        {
            public int X;
        }

        private struct Velocity : IComponent<Velocity>
        {
            public int Y;
        }

        private struct ManaComponent : IDiscreteComponent<ManaComponent>
        {
        }

        private struct PlayerTag : ITagComponent<PlayerTag>
        {
        }

        private sealed class RecordingObserver : IStructureObserver
        {
            public readonly List<(uint TypeId, int Row)> Changed = new();

            public void OnComponentAdded(Structure structure, int row, uint typeId)
            {
            }

            public void OnComponentRemoved(Structure structure, int row, uint typeId)
            {
            }

            public void OnComponentChanged(Structure structure, int row, uint typeId)
                => Changed.Add((typeId, row));
        }

        private static uint IdOf<T>() where T : struct, IComponent<T>
            => ComponentTypeRegistry.GetOrRegister<T>().TypeId;

        private static Structure MakeStructure(params uint[] typeIds)
        {
            Array.Sort(typeIds);
            return new Structure(new StructureKey(typeIds, 0));
        }

        private static int AppendPosition(Structure structure, ulong entityId, int value)
        {
            var row = structure.Append(entityId, EntityLocation.Pool.Get());
            structure.SetDenseValue(row, new Position { X = value }, ComponentVersion.Next());
            return row;
        }

        [Test]
        public void RO_ReturnsLiveRowsWithoutMarkingOrNotifying()
        {
            var structure = MakeStructure(IdOf<Position>());
            var observer = new RecordingObserver();
            structure.Observer = observer;
            AppendPosition(structure, 11, 1);
            AppendPosition(structure, 22, 2);
            AppendPosition(structure, 33, 3);

            var span = structure.RO<Position>();

            Assert.AreEqual(structure.Count, span.Length);
            Assert.AreEqual(1, span[0].X);
            Assert.AreEqual(2, span[1].X);
            Assert.AreEqual(3, span[2].X);
            Assert.AreEqual(11UL, structure.Entities[0]);
            Assert.AreEqual(22UL, structure.Entities[1]);
            Assert.AreEqual(33UL, structure.Entities[2]);
            for (var row = 0; row < structure.Count; row++)
            {
                Assert.AreEqual(0u, structure.GetDenseRevision<Position>(row));
            }

            Assert.AreEqual(0, observer.Changed.Count);
        }

        [Test]
        public void RO_ReflectsWritesMadeThroughPreviousRWSpan()
        {
            var structure = MakeStructure(IdOf<Position>());
            AppendPosition(structure, 1, 1);
            AppendPosition(structure, 2, 2);

            var rw = structure.RW<Position>();
            rw[0] = new Position { X = 10 };
            rw[1] = new Position { X = 20 };

            var ro = structure.RO<Position>();

            Assert.AreEqual(2, ro.Length);
            Assert.AreEqual(10, ro[0].X);
            Assert.AreEqual(20, ro[1].X);
        }

        [Test]
        public void RW_WritesPersistAndAreVisibleThroughGetDenseRef()
        {
            var structure = MakeStructure(IdOf<Position>());
            var first = AppendPosition(structure, 1, 1);
            var second = AppendPosition(structure, 2, 2);

            var span = structure.RW<Position>();
            span[0] = new Position { X = 7 };
            span[1] = new Position { X = 8 };

            Assert.AreEqual(7, structure.GetDenseRef<Position>(first).X);
            Assert.AreEqual(8, structure.GetDenseRef<Position>(second).X);
        }

        [Test]
        public void RW_MarksEveryLiveRowOnceAndOnlyItsOwnColumn()
        {
            var structure = MakeStructure(IdOf<Position>(), IdOf<Velocity>());
            var observer = new RecordingObserver();
            structure.Observer = observer;

            for (var i = 0; i < 3; i++)
            {
                var row = structure.Append((ulong)(i + 1), EntityLocation.Pool.Get());
                structure.SetDenseValue(row, new Position { X = i }, ComponentVersion.Next());
                structure.SetDenseValue(row, new Velocity { Y = i * 10 }, ComponentVersion.Next());
            }

            var span = structure.RW<Position>();

            Assert.AreEqual(3, span.Length);
            for (var row = 0; row < structure.Count; row++)
            {
                Assert.AreEqual(1u, structure.GetDenseRevision<Position>(row));
                Assert.AreEqual(0u, structure.GetDenseRevision<Velocity>(row));
            }

            Assert.AreEqual(3, observer.Changed.Count);
            Assert.AreEqual((IdOf<Position>(), 0), observer.Changed[0]);
            Assert.AreEqual((IdOf<Position>(), 1), observer.Changed[1]);
            Assert.AreEqual((IdOf<Position>(), 2), observer.Changed[2]);
        }

        [Test]
        public void RO_And_RW_ThrowForDenseTypeAbsentFromStructure()
        {
            var structure = MakeStructure(IdOf<Position>());
            structure.Append(1, EntityLocation.Pool.Get());

            Assert.Throws<InvalidOperationException>(() => structure.RO<Velocity>());
            Assert.Throws<InvalidOperationException>(() => structure.RW<Velocity>());
        }

        [Test]
        public void RO_And_RW_ThrowForDiscreteAndTagTypes()
        {
            var structure = MakeStructure(IdOf<Position>());
            structure.Append(1, EntityLocation.Pool.Get());

            Assert.Throws<InvalidOperationException>(() => structure.RO<ManaComponent>());
            Assert.Throws<InvalidOperationException>(() => structure.RW<ManaComponent>());
            Assert.Throws<InvalidOperationException>(() => structure.RO<PlayerTag>());
            Assert.Throws<InvalidOperationException>(() => structure.RW<PlayerTag>());
        }

        [Test]
        public void RO_And_RW_OnEmptyStructure_ReturnEmptySpansWithoutNotifying()
        {
            var structure = MakeStructure(IdOf<Position>());
            var observer = new RecordingObserver();
            structure.Observer = observer;

            Assert.AreEqual(0, structure.RO<Position>().Length);
            Assert.AreEqual(0, structure.RW<Position>().Length);
            Assert.AreEqual(0, observer.Changed.Count);
        }

        [Test]
        public void Spans_ExposeLiveRowsOnlyWhenCapacityExceedsCount()
        {
            var structure = MakeStructure(IdOf<Position>());
            for (var i = 0; i < 20; i++)
            {
                AppendPosition(structure, (ulong)(i + 1), i);
            }

            for (var row = 19; row >= 3; row--)
            {
                structure.SwapRemove(row);
            }

            Assert.AreEqual(3, structure.Count);

            var ro = structure.RO<Position>();
            var rw = structure.RW<Position>();

            Assert.AreEqual(3, ro.Length);
            Assert.AreEqual(3, rw.Length);

            for (var row = 0; row < structure.Count; row++)
            {
                rw[row] = new Position { X = (int)structure.Entities[row] };
            }

            for (var row = 0; row < structure.Count; row++)
            {
                Assert.AreEqual((int)structure.Entities[row], ro[row].X);
                Assert.AreEqual((int)structure.Entities[row], structure.GetDenseRef<Position>(row).X);
                Assert.AreEqual(1u, structure.GetDenseRevision<Position>(row));
            }
        }
    }
}
```

- [ ] **Step 2: 运行过滤测试，确认现有实现满足契约**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --filter FullyQualifiedName~StructureBatchAccessTestUnit`
Expected: PASS（8 个测试）。实现已在 Plan 1a 落地，本步预期全绿；若失败，执行 Step 3 后重跑。

- [ ] **Step 3:（条件步骤）仅当 Step 2 失败时，用参考实现修正 `Structure.RO/RW`**

用"设计说明"中的参考实现替换 `ECS/Structures/Structure.cs` 的 `RO<T>()` / `RW<T>()`（其余不动）。Step 2 全绿则跳过本步、不改任何生产文件。

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --filter FullyQualifiedName~StructureBatchAccessTestUnit`
Expected: PASS（8 个测试）

- [ ] **Step 4: 运行全量测试**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj`
Expected: 438 passed（基线 430 + 新增 8），0 failed

- [ ] **Step 5: 提交**

```bash
git add Test/StructureBatchAccessTestUnit.cs
git commit -m "feat(test): lock structure RO/RW batch access contract"
```

若 Step 3 被触发，把 `ECS/Structures/Structure.cs` 一并加入 `git add`。

---

## Task 2: IEntityQuery + World.Query(matcher)（删除 v1 集合式重载并迁移调用点）

**Files:**
- Create: `ECS/Defines/IEntityQuery.cs`
- Create: `ECS/EntityQuery.cs`
- Modify: `ECS/World.cs`（`World.cs:197-257`：删除两个 v1 重载，替换为 `Query(IEntityMatcher)`）
- Modify: `ECS/EntityMatcherExtension.cs`（`EntityMatcherExtension.cs:457-487`：两个扩展方法体迁移到新 API）
- Create: `Test/EntityQueryTestUnit.cs`（11 个测试）
- Modify: `Test/WorldTestUnit.cs`（查询测试段 `WorldTestUnit.cs:277-482`；11 → 9 个查询测试：删 2、并 2→1、新增 1 个扩展追加覆盖；全类 26 → 24）
- Modify: `Test/EntityMatcherTestUnit.cs`（12 处调用点）

**前置:** Task 1 已提交（`8390317`）；本次 dispatch 前实测全量 438 passed / 0 failed。

**设计说明（执行时不要改动，评审时按此核对）：**

- **接口（spec 5.2）**：`IEntityQuery : IDisposable` 放 `ECS/Defines/IEntityQuery.cs`，命名空间 `CoreECS.Defines`（与 `IEntityMatcher` / `IEntityCollector` 同目录同命名空间）；成员与 spec 逐字一致：`IEntityMatcher Matcher { get; }`、`IReadOnlyList<Structure> Structures { get; }`、`IEnumerable<ulong> Entities { get; }`、`void Refresh();`。
- **实现（绑定决定）**：`internal sealed class EntityQuery : IEntityQuery` 放 `ECS/EntityQuery.cs`，命名空间 `CoreECS`；公开接口 + 内部实现，与 `EntityLocation` / `EntityTable` / `StructureRegistry` 等内核类型可见性一致。构造参数 `(IEntityMatcher matcher, EntityManager entityManager)`，经 `EntityManager.Table`（internal）访问 `EntityTable`。
- **快照生命周期（绑定决定）**：构造函数只保存引用，快照初始为空；`Refresh()` 才重建；`World.Query(matcher)` 是纯工厂，**不自动 Refresh**。依据：spec 5.2 接口注释"快照：Refresh() 后有效"，且 5.2 标题"与 EntityCollector 一致"（collector 也必须显式 `Flush()` 后才可读 `Collected`）。因此所有调用点在读取 `Entities` / `Structures` 前必须显式 `Refresh()`。任务文本中的示例（`using var query = world.Query(matcher); foreach ...`）省略了该调用，按 spec 执行并在此记录。
- **`Refresh()` 语义**：清空内部列表 → 遍历 `EntityManager.Table.EntityIds`（`EntityTable.EntityIds`，枚举顺序未定义）→ `TryGetLocation` 且 `location.Structure != null` → `matcher.ComponentFilter(location.Structure, location.Row)` → 命中则把 entityId 加入 `m_entities`；`m_structures` 用 `HashSet<Structure>` 去重，按**首次出现顺序**加入。`Structures` 只含"至少有一个匹配实体"的结构、无重复、顺序不保证（测试只用计数 / `AreEquivalent` / 唯一性断言，不依赖顺序）。
- **快照稳定性**：`Entities` / `Structures` 直接返回内部 `List<ulong>` / `List<Structure>`（分别以 `IEnumerable<ulong>` / `IReadOnlyList<Structure>` 暴露）。除 `Refresh()` 外的世界变更（创建 / 销毁 / 组件增删）不触碰这两个列表；遍历期间禁止调用 `Refresh()`。
- **`Dispose()`（spec 已决事项 6）**：非池化，当前显式 no-op；文档注明"Dispose 后快照仍可读"是当前语义，后续若引入池化需重新评审 `Query_Dispose_IsNoOp`。
- **`World.Query(matcher)`**：保留 v1 校验顺序——`Assertion.IsTrue(Ready, "World is not ready")` → `Assertion.ArgumentNotNull(matcher, nameof(matcher))` → `Entity == null` 抛 `InvalidOperationException("Core ECS managers are not available")` → `new EntityQuery(matcher, Entity)`。v1 `Query(IEntityMatcher, ICollection<ulong>)` / `Query(IEntityMatcher, ICollection<Entity>)` 整体删除（spec 5.2 / §11 破坏性变更）。
- **扩展方法（实勘决定，任务文本未点名）**：`ECS/EntityMatcherExtension.cs:457-487` 的两个 `Query(this IEntityMatcher, World, ICollection<...>)` 是 v1 集合式便捷 API，也是被删 World 重载的调用点。spec §11 未点名删除它们，且设计原则要求"用户可见 API 尽量与 v1 保持一致"，故**保留签名**，把方法体迁移到新 API；行为保持（追加到调用方集合、返回追加数量、`world == null` / `result == null` 抛 `ArgumentNullException`，校验顺序不变）。其两个测试（`EntityMatcherExtension_Query_*`）原样保留。
- **测试迁移**：`EntityMatcherTestUnit` 12 处调用点全部改写为 `using var query = _world.Query(matcher); query.Refresh(); var ... = query.Entities.ToList();`，断言不变。`WorldTestUnit` 11 个查询测试 → 9 个：6 个迁移（其中 2 个 not-ready 合并为 1），2 个删除（`World_Query_Ulong_AppendsToExistingCollection` 的"追加 / 返回数量"语义随 v1 World API 删除，改由扩展方法测试恢复覆盖，见 Step 8(j)；`World_Query_ThrowsWhenResultIsNull` 的 result 参数已不存在），2 个扩展测试保留，新增 1 个扩展追加覆盖。
- **测试计数（绑定）**：基线 438 + 新增 11 − 删除 / 合并净 3 + 扩展追加 1 = **447 passed**；过滤预期：`EntityQueryTestUnit` 11、`WorldTestUnit` 24、`EntityMatcherTestUnit` 17。
- **执行顺序说明**：生产 API（Step 1-4）与所有调用点迁移（Step 6-8）完成前 Test 项目无法编译（引用了被删重载），因此 Step 5 只构建 `ECS.csproj` 验证库代码；Step 8 之前不要运行 `dotnet test`。

- [ ] **Step 1: 新增 `IEntityQuery` 接口**

创建 `ECS/Defines/IEntityQuery.cs`：

```csharp
using System;
using System.Collections.Generic;
using CoreECS.Structures;

namespace CoreECS.Defines
{
    /// <summary>
    /// A non-pooled query over the entities matching an <see cref="IEntityMatcher"/>.
    /// The exposed snapshot is rebuilt by <see cref="Refresh"/> and stays stable while
    /// enumerated; call <see cref="Refresh"/> again to recompute it. Dispose when done.
    /// </summary>
    /// <remarks>
    /// <see cref="IDisposable.Dispose"/> is currently a no-op: the snapshot stays readable
    /// after disposal, but callers should not rely on that once query pooling lands.
    /// </remarks>
    public interface IEntityQuery : IDisposable
    {
        /// <summary>
        /// Gets the matcher this query was created with.
        /// </summary>
        public IEntityMatcher Matcher { get; }

        /// <summary>
        /// Gets the distinct structures containing at least one matching entity in the last
        /// snapshot. Empty until <see cref="Refresh"/> is called. Order is unspecified.
        /// </summary>
        public IReadOnlyList<Structure> Structures { get; }

        /// <summary>
        /// Gets the matching entity ids of the last snapshot. Empty until <see cref="Refresh"/>
        /// is called. The enumeration is stable until the next <see cref="Refresh"/>.
        /// </summary>
        public IEnumerable<ulong> Entities { get; }

        /// <summary>
        /// Recomputes the snapshot from the live entity table.
        /// </summary>
        public void Refresh();
    }
}
```

- [ ] **Step 2: 新增 `EntityQuery` 实现**

创建 `ECS/EntityQuery.cs`：

```csharp
using System.Collections.Generic;
using CoreECS.Defines;
using CoreECS.Managers;
using CoreECS.Structures;

namespace CoreECS
{
    /// <summary>
    /// Non-pooled <see cref="IEntityQuery"/> over the entity table. Every <see cref="Refresh"/>
    /// rebuilds the snapshot by iterating live entity ids and evaluating the matcher per row.
    /// </summary>
    internal sealed class EntityQuery : IEntityQuery
    {
        private readonly IEntityMatcher m_matcher;
        private readonly EntityManager m_entityManager;
        private readonly List<ulong> m_entities = new();
        private readonly List<Structure> m_structures = new();
        private readonly HashSet<Structure> m_structureSet = new();

        /// <inheritdoc />
        public IEntityMatcher Matcher => m_matcher;

        /// <inheritdoc />
        public IReadOnlyList<Structure> Structures => m_structures;

        /// <inheritdoc />
        public IEnumerable<ulong> Entities => m_entities;

        /// <summary>
        /// Creates a query bound to the entity manager. The snapshot starts empty; call
        /// <see cref="Refresh"/> before reading <see cref="Entities"/> or <see cref="Structures"/>.
        /// </summary>
        /// <param name="matcher">Matcher that defines the query conditions.</param>
        /// <param name="entityManager">Entity manager owning the queried entity table.</param>
        public EntityQuery(IEntityMatcher matcher, EntityManager entityManager)
        {
            m_matcher = matcher;
            m_entityManager = entityManager;
        }

        /// <inheritdoc />
        public void Refresh()
        {
            m_entities.Clear();
            m_structures.Clear();
            m_structureSet.Clear();

            foreach (var entityId in m_entityManager.Table.EntityIds)
            {
                if (!m_entityManager.Table.TryGetLocation(entityId, out var location) || location.Structure == null) continue;
                if (!m_matcher.ComponentFilter(location.Structure, location.Row)) continue;

                m_entities.Add(entityId);
                if (m_structureSet.Add(location.Structure))
                    m_structures.Add(location.Structure);
            }
        }

        /// <summary>
        /// No-op today: the query owns no pooled resources. Kept so pooling can be introduced
        /// later without an interface change.
        /// </summary>
        public void Dispose()
        {
        }
    }
}
```

- [ ] **Step 3: 替换 `World.Query`（删除 v1 重载）**

用下面代码整体替换 `ECS/World.cs` 中 `Query(IEntityMatcher, ICollection<ulong>)` 与 `Query(IEntityMatcher, ICollection<Entity>)`（含 XML 文档，`World.cs:197-257`）：

```csharp
        /// <summary>
        /// Creates a non-pooled query over the entities matching the specified matcher.
        /// The returned query owns an empty snapshot until <see cref="IEntityQuery.Refresh"/> is called.
        /// </summary>
        /// <param name="matcher">Matcher that defines the query conditions.</param>
        /// <returns>A new query bound to this world.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the world is not ready or the entity manager is unavailable.</exception>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="matcher"/> is null.</exception>
        public IEntityQuery Query(IEntityMatcher matcher)
        {
            Assertion.IsTrue(Ready, "World is not ready");
            Assertion.ArgumentNotNull(matcher, nameof(matcher));

            if (Entity == null)
                throw new InvalidOperationException("Core ECS managers are not available");

            return new EntityQuery(matcher, Entity);
        }
```

替换后删除 `ECS/World.cs` 顶部已不再使用的 `using System.Collections.Generic;`（v1 重载删除后文件内不再出现 `List<>` / `ICollection` / `IEnumerable`；评审修订）。

- [ ] **Step 4: 迁移 `EntityMatcherExtension` 两个扩展方法体**

保留 `EntityMatcherExtension.cs:457-487` 的签名与 XML 文档（`result` 描述可保持"Target non-alloc output collection."），仅替换方法体；`world.Query(matcher, result)` 调用点迁移如下（校验顺序与 v1 一致：world → Ready/matcher → result）：

`Query(this IEntityMatcher, World, ICollection<ulong>)` 方法体：

```csharp
        public static int Query(this IEntityMatcher matcher, World world, ICollection<ulong> result)
        {
            CoreECS.Utils.Assertion.ArgumentNotNull(world, nameof(world));

            using var query = world.Query(matcher);
            CoreECS.Utils.Assertion.ArgumentNotNull(result, nameof(result));
            query.Refresh();

            var added = 0;
            foreach (var entityId in query.Entities)
            {
                result.Add(entityId);
                added += 1;
            }

            return added;
        }
```

`Query(this IEntityMatcher, World, ICollection<Entity>)` 方法体：

```csharp
        public static int Query(this IEntityMatcher matcher, World world, ICollection<Entity> result)
        {
            CoreECS.Utils.Assertion.ArgumentNotNull(world, nameof(world));

            using var query = world.Query(matcher);
            CoreECS.Utils.Assertion.ArgumentNotNull(result, nameof(result));
            query.Refresh();

            var added = 0;
            foreach (var entityId in query.Entities)
            {
                result.Add(world.GetEntity(entityId));
                added += 1;
            }

            return added;
        }
```

- [ ] **Step 5: 构建 ECS 库（两个 TFM）**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet build ECS/ECS.csproj`
Expected: Build succeeded（net8.0 + netstandard2.1，0 Error）。此时 Test 项目尚未迁移，不要运行 `dotnet test`。

- [ ] **Step 6: 新增 `Test/EntityQueryTestUnit.cs`（11 个测试）**

创建 `Test/EntityQueryTestUnit.cs`：

```csharp
using CoreECS.Defines;
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class EntityQueryTestUnit
    {
        private struct Position : IComponent<Position>
        {
            public int X;
        }

        private struct Velocity : IComponent<Velocity>
        {
            public int Y;
        }

        private struct Health : IComponent<Health>
        {
            public int Value;
        }

        private struct Mana : IDiscreteComponent<Mana>
        {
        }

        private struct PlayerTag : ITagComponent<PlayerTag>
        {
        }

        private World _world;

        [SetUp]
        public void Setup()
        {
            _world = new World();
            _world.Startup();
        }

        [TearDown]
        public void TearDown()
        {
            _world?.Shutdown();
        }

        [Test]
        public void Query_OnEmptyWorld_RefreshYieldsEmptySnapshot()
        {
            using var query = _world.Query(EntityMatcher.With.OfAll<Position>());
            query.Refresh();

            Assert.AreEqual(0, query.Entities.Count());
            Assert.AreEqual(0, query.Structures.Count);
        }

        [Test]
        public void Query_MatchesEntitiesAcrossMultipleStructures()
        {
            var onlyPosition = _world.CreateEntity();
            var positionAndVelocity = _world.CreateEntity();
            var positionAndHealth = _world.CreateEntity();
            var unrelated = _world.CreateEntity();

            onlyPosition.CreateComponent<Position>();
            positionAndVelocity.CreateComponent<Position>();
            positionAndVelocity.CreateComponent<Velocity>();
            positionAndHealth.CreateComponent<Position>();
            positionAndHealth.CreateComponent<Health>();
            unrelated.CreateComponent<Velocity>();

            using var query = _world.Query(EntityMatcher.With.OfAll<Position>());
            query.Refresh();

            var ids = query.Entities.ToList();
            Assert.AreEqual(3, ids.Count);
            CollectionAssert.Contains(ids, onlyPosition.EntityId);
            CollectionAssert.Contains(ids, positionAndVelocity.EntityId);
            CollectionAssert.Contains(ids, positionAndHealth.EntityId);
            CollectionAssert.DoesNotContain(ids, unrelated.EntityId);
            Assert.AreEqual(3, query.Structures.Count);
        }

        [Test]
        public void Query_Refresh_PicksUpNewEntitiesAndNewComponents()
        {
            using var query = _world.Query(EntityMatcher.With.OfAll<Position>());
            query.Refresh();
            Assert.AreEqual(0, query.Entities.Count());

            var late = _world.CreateEntity();
            query.Refresh();
            Assert.AreEqual(0, query.Entities.Count());

            late.CreateComponent<Position>();
            query.Refresh();
            Assert.AreEqual(1, query.Entities.Count());
            CollectionAssert.Contains(query.Entities.ToList(), late.EntityId);

            var createdAfter = _world.CreateEntity();
            createdAfter.CreateComponent<Position>();
            query.Refresh();
            Assert.AreEqual(2, query.Entities.Count());
        }

        [Test]
        public void Query_Refresh_DropsDestroyedAndNoLongerMatchingEntities()
        {
            var stays = _world.CreateEntity();
            var destroyed = _world.CreateEntity();
            var losesComponent = _world.CreateEntity();

            stays.CreateComponent<Position>();
            destroyed.CreateComponent<Position>();
            losesComponent.CreateComponent<Position>();
            losesComponent.CreateComponent<Velocity>();

            using var query = _world.Query(EntityMatcher.With.OfAll<Position>());
            query.Refresh();
            Assert.AreEqual(3, query.Entities.Count());

            _world.DestroyEntity(destroyed);
            losesComponent.DestroyComponent<Position>();
            query.Refresh();

            var ids = query.Entities.ToList();
            Assert.AreEqual(1, ids.Count);
            CollectionAssert.Contains(ids, stays.EntityId);
            CollectionAssert.DoesNotContain(ids, destroyed.EntityId);
            CollectionAssert.DoesNotContain(ids, losesComponent.EntityId);
        }

        [Test]
        public void Query_MaskFiltering_ExcludesNonIntersectingMasks()
        {
            var expected = _world.CreateEntity(0b0001);
            var wrongMask = _world.CreateEntity(0b0010);
            var overlapping = _world.CreateEntity(0b0011);

            expected.CreateComponent<Position>();
            wrongMask.CreateComponent<Position>();
            overlapping.CreateComponent<Position>();

            using var query = _world.Query(EntityMatcher.WithMask(0b0001).OfAll<Position>());
            query.Refresh();

            var ids = query.Entities.ToList();
            Assert.AreEqual(2, ids.Count);
            CollectionAssert.Contains(ids, expected.EntityId);
            CollectionAssert.Contains(ids, overlapping.EntityId);
            CollectionAssert.DoesNotContain(ids, wrongMask.EntityId);
        }

        [Test]
        public void Query_TagAndDiscreteFiltering_AreRowLevel()
        {
            var tagged = _world.CreateEntity();
            var plain = _world.CreateEntity();
            var withMana = _world.CreateEntity();
            var withoutMana = _world.CreateEntity();

            tagged.CreateComponent<PlayerTag>();
            withMana.CreateComponent<Mana>();

            using (var tagQuery = _world.Query(EntityMatcher.With.OfAll<PlayerTag>()))
            {
                tagQuery.Refresh();
                var ids = tagQuery.Entities.ToList();
                Assert.AreEqual(1, ids.Count);
                CollectionAssert.Contains(ids, tagged.EntityId);
                CollectionAssert.DoesNotContain(ids, plain.EntityId);
            }

            using (var manaQuery = _world.Query(EntityMatcher.With.OfAll<Mana>()))
            {
                manaQuery.Refresh();
                var ids = manaQuery.Entities.ToList();
                Assert.AreEqual(1, ids.Count);
                CollectionAssert.Contains(ids, withMana.EntityId);
                CollectionAssert.DoesNotContain(ids, withoutMana.EntityId);
            }
        }

        [Test]
        public void Query_Structures_AreDistinctAndOnlyContainMatchingEntities()
        {
            var first = _world.CreateEntity();
            var second = _world.CreateEntity();
            var third = _world.CreateEntity();
            var excluded = _world.CreateEntity();

            first.CreateComponent<Position>();
            second.CreateComponent<Position>();
            third.CreateComponent<Position>();
            third.CreateComponent<Velocity>();
            excluded.CreateComponent<Velocity>();

            using var query = _world.Query(EntityMatcher.With.OfAll<Position>());
            query.Refresh();

            Assert.AreEqual(2, query.Structures.Count);
            CollectionAssert.AllItemsAreUnique(query.Structures);

            var positionTypeId = ComponentTypeRegistry.GetOrRegister<Position>().TypeId;
            var matchedIds = query.Entities.ToList();
            foreach (var structure in query.Structures)
            {
                Assert.IsTrue(structure.HasDense(positionTypeId));

                var containsMatch = false;
                foreach (var entityId in structure.Entities)
                {
                    if (matchedIds.Contains(entityId))
                    {
                        containsMatch = true;
                        break;
                    }
                }

                Assert.IsTrue(containsMatch, "Structure must contain at least one matching entity.");
            }
        }

        [Test]
        public void Query_SnapshotIsStableUntilRefresh()
        {
            var first = _world.CreateEntity();
            first.CreateComponent<Position>();

            using var query = _world.Query(EntityMatcher.With.OfAll<Position>());
            query.Refresh();
            var snapshot = query.Entities.ToList();
            Assert.AreEqual(1, snapshot.Count);

            var second = _world.CreateEntity();
            second.CreateComponent<Position>();
            _world.DestroyEntity(first);

            CollectionAssert.AreEquivalent(snapshot, query.Entities.ToList());
            CollectionAssert.Contains(query.Entities.ToList(), first.EntityId);
            CollectionAssert.DoesNotContain(query.Entities.ToList(), second.EntityId);

            query.Refresh();
            Assert.AreEqual(1, query.Entities.Count());
            CollectionAssert.DoesNotContain(query.Entities.ToList(), first.EntityId);
            CollectionAssert.Contains(query.Entities.ToList(), second.EntityId);
        }

        [Test]
        public void Query_Dispose_IsNoOp()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<Position>();

            var query = _world.Query(EntityMatcher.With.OfAll<Position>());
            query.Refresh();
            query.Dispose();
            query.Dispose();

            var ids = query.Entities.ToList();
            Assert.AreEqual(1, ids.Count);
            CollectionAssert.Contains(ids, entity.EntityId);
        }

        [Test]
        public void Query_Matcher_ReturnsSameInstance()
        {
            var matcher = EntityMatcher.With.OfAll<Position>().OfNone<Velocity>();

            using var query = _world.Query(matcher);

            Assert.AreSame(matcher, query.Matcher);
        }

        [Test]
        public void Query_BeforeRefresh_SnapshotIsEmpty()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<Position>();

            using var query = _world.Query(EntityMatcher.With.OfAll<Position>());

            Assert.AreEqual(0, query.Structures.Count);
            Assert.AreEqual(0, query.Entities.ToList().Count);

            query.Refresh();

            CollectionAssert.Contains(query.Entities.ToList(), entity.EntityId);
        }
    }
}
```

- [ ] **Step 7: 迁移 `Test/EntityMatcherTestUnit.cs`（12 处调用点）**

以下 8 个测试的 Act 段形状相同（行号为迁移前），逐处替换：
`EntityMatcher_OfAll_CanMatchEntitiesWithAllComponents`（L38-40）、
`EntityMatcher_OfAny_CanMatchEntitiesWithAnyComponent`（L63-65）、
`EntityMatcher_OfNone_CanExcludeEntitiesWithComponent`（L89-91）、
`EntityMask_CanFilterEntitiesByMask`（L110-112）、
`EntityMatcher_CanHandleMultipleOfAny`（L201-203）、
`EntityMatcher_CanHandleMultipleOfNone`（L226-228）、
`EntityMatcher_ChainFilters_CorrectlyMatches`（L271-273）、
`EntityMatcher_MixedFilters_CombinedLogic`（L314-316）。

旧：

```csharp
            var matchedEntities = new List<ulong>();
            _world.Query(matcher, matchedEntities);
```

新：

```csharp
            using var query = _world.Query(matcher);
            query.Refresh();
            var matchedEntities = query.Entities.ToList();
```

`EntityMatcher_CanHandleEmptyComponentList`（L176-178）局部变量名为 `matched`，同样迁移：

旧：

```csharp
            var matched = new List<ulong>();
            _world.Query(matcher, matched);
```

新：

```csharp
            using var query = _world.Query(matcher);
            query.Refresh();
            var matched = query.Entities.ToList();
```

`EntityMatcher_ComplexFiltering`（L149-155）一次建 3 个查询，整段替换：

旧：

```csharp
            // Act - v2 evaluates matchers against live structures through World.Query
            var positionEntities = new List<ulong>();
            var positionOrVelocityEntities = new List<ulong>();
            var positionWithoutHealthEntities = new List<ulong>();
            _world.Query(positionMatcher, positionEntities);
            _world.Query(positionOrVelocityMatcher, positionOrVelocityEntities);
            _world.Query(positionWithoutHealthMatcher, positionWithoutHealthEntities);
```

新：

```csharp
            // Act - v2 evaluates matchers against live structures through IEntityQuery snapshots
            using var positionQuery = _world.Query(positionMatcher);
            positionQuery.Refresh();
            var positionEntities = positionQuery.Entities.ToList();

            using var positionOrVelocityQuery = _world.Query(positionOrVelocityMatcher);
            positionOrVelocityQuery.Refresh();
            var positionOrVelocityEntities = positionOrVelocityQuery.Entities.ToList();

            using var positionWithoutHealthQuery = _world.Query(positionWithoutHealthMatcher);
            positionWithoutHealthQuery.Refresh();
            var positionWithoutHealthEntities = positionWithoutHealthQuery.Entities.ToList();
```

其余 Arrange / Assert 行不变（`EntityMatcherTestUnit` 测试总数保持 17）。

- [ ] **Step 8: 迁移 `Test/WorldTestUnit.cs` 查询测试段**

按下述逐个替换（行号为迁移前）。所有其余测试（生命周期 / 系统 / manager / 两个 `EntityMatcherExtension_Query_*` 测试）不动。

(a) `World_Query_Ulong_ReturnsIdsForEntitiesMatchingMatcher`（L277-298）→ 重命名并整方法替换：

```csharp
        [Test]
        public void World_Query_ReturnsIdsForEntitiesMatchingMatcher()
        {
            var world = new World();
            world.Startup();

            var withPositionA = world.CreateEntity();
            var withPositionB = world.CreateEntity();
            _ = world.CreateEntity();

            withPositionA.CreateComponent<PositionComponent>();
            withPositionB.CreateComponent<PositionComponent>();

            using var query = world.Query(EntityMatcher.With.OfAll<PositionComponent>());
            query.Refresh();
            var ids = query.Entities.ToList();

            Assert.AreEqual(2, ids.Count);
            CollectionAssert.AreEquivalent(new[] { withPositionA.EntityId, withPositionB.EntityId }, ids);

            world.Shutdown();
        }
```

(b) `World_Query_Ulong_AppendsToExistingCollection`（L300-319）→ **删除整个方法**（新 API 不再接受调用方集合，也没有"返回追加数"语义；理由见 Self-Review）。

(c) `World_Query_Ulong_HonorsMaskAndComponentRules`（L321-346）→ 重命名为 `World_Query_HonorsMaskAndComponentRules`，把 L339-343 替换为：

```csharp
            using var query = world.Query(matcher);
            query.Refresh();
            var ids = query.Entities.ToList();

            Assert.AreEqual(1, ids.Count);
            CollectionAssert.AreEqual(new[] { expected.EntityId }, ids);
```

(d) `World_Query_Entity_ReturnsValidHandlesMatchingIds`（L348-380）→ 重命名为 `World_Query_ReturnsValidHandlesForMatchingIds`，把 L359-377 替换为：

```csharp
            var matcher = EntityMatcher.With.OfAll<PositionComponent>();
            using var query = world.Query(matcher);
            query.Refresh();

            var entities = query.Entities.Select(world.GetEntity).ToList();

            Assert.AreEqual(2, entities.Count);
            CollectionAssert.AreEquivalent(
                new[] { e1.EntityId, e2.EntityId },
                entities.Select(e => e.EntityId).ToList());

            foreach (var e in entities)
            {
                Assert.IsTrue(e.IsValid);
                Assert.AreSame(world, e.World);
            }
```

(e) `World_Query_Ulong_DoesNotReturnDestroyedEntities`（L382-402）→ 重命名为 `World_Query_DoesNotReturnDestroyedEntities`，把 L394-399 替换为：

```csharp
            using var query = world.Query(EntityMatcher.With.OfAll<PositionComponent>());
            query.Refresh();
            var ids = query.Entities.ToList();

            Assert.AreEqual(1, ids.Count);
            CollectionAssert.AreEqual(new[] { alive.EntityId }, ids);
            CollectionAssert.DoesNotContain(ids, destroyed.EntityId);
```

(f) `World_Query_ThrowsWhenWorldNotReady_UlongCollection`（L404-411）与 (g) `World_Query_ThrowsWhenWorldNotReady_EntityCollection`（L413-420）→ 合并为一个方法（新 API 只有一个重载）：

```csharp
        [Test]
        public void World_Query_ThrowsWhenWorldNotReady()
        {
            var world = new World();

            Assert.Throws<InvalidOperationException>(() =>
                world.Query(EntityMatcher.With.OfAll<PositionComponent>()));
        }
```

(h) `World_Query_ThrowsWhenMatcherIsNull`（L422-432）→ 把 L428-429 两行断言替换为一行：

```csharp
            Assert.Throws<ArgumentNullException>(() => world.Query((IEntityMatcher)null));
```

(i) `World_Query_ThrowsWhenResultIsNull`（L434-445）→ **删除整个方法**（result 参数已不存在；理由见 Self-Review）。

(j) 新增 `World_Query_Extension_AppendsToExistingCollection`（恢复被删除的 `World_Query_Ulong_AppendsToExistingCollection` 的"追加 + 返回数量"覆盖——扩展方法签名保留，语义仍在）：

```csharp
        [Test]
        public void World_Query_Extension_AppendsToExistingCollection()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();

            var ids = new List<ulong> { 999UL };
            var added = EntityMatcher.With.OfAll<PositionComponent>().Query(_world, ids);

            Assert.AreEqual(1, added);
            CollectionAssert.AreEqual(new[] { 999UL, entity.EntityId }, ids);
        }
```

迁移后 `WorldTestUnit` 测试数 26 → 24，查询测试 11 → 9。

- [ ] **Step 9: 运行过滤测试**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --filter FullyQualifiedName~EntityQueryTestUnit`
Expected: PASS（11 个测试，失败 0）

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --filter FullyQualifiedName~WorldTestUnit`
Expected: PASS（24 个测试，失败 0）

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --filter FullyQualifiedName~EntityMatcherTestUnit`
Expected: PASS（17 个测试，失败 0）

- [ ] **Step 10: 运行全量测试**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj`
Expected: 447 passed（基线 438 + 新增 11 − 删除 / 合并净 3 + 扩展追加 1），0 failed

- [ ] **Step 11: 提交**

```bash
git add ECS/Defines/IEntityQuery.cs ECS/EntityQuery.cs ECS/World.cs ECS/EntityMatcherExtension.cs Test/EntityQueryTestUnit.cs Test/WorldTestUnit.cs Test/EntityMatcherTestUnit.cs
git commit -m "feat(core): add entity query over structure snapshots

world.Query(matcher) now returns a non-pooled IEntityQuery whose
Refresh() rebuilds a stable snapshot of matching entity ids and the
distinct structures they live in. The v1 collection-based World
overloads are removed; the matcher extension overloads keep their
signatures and delegate to the new API."
```

---

## Task 3: Collector 结构级加速（结构级过滤缓存 + 行级过滤）

**Files:**
- Modify: `ECS/EntityMatcher.cs`（`EntityMatcher.cs:151-208`：`ComponentFilter` 拆分为结构级 `EvaluateStructure` + 行级 `RowFilter`，新增 `internal readonly struct StructureMatch` 与 `internal int StructureEvaluationCount` 测试钩子）
- Modify: `ECS/Managers/EntityMatchManager.cs`（5 处：`Collector.StructureMatches` 缓存、`Collector.Matches`、`_changeCollector` 调用点 `EntityMatchManager.cs:480`、`Dispose` 清理、`OnManagerDestroyed` 清理）
- Create: `Test/CollectorAccelerationTestUnit.cs`（9 个测试）

**前置:** Task 2 已提交（`a08237f`）；本次 dispatch 前实测全量 447 passed / 0 failed。

**设计说明（执行时不要改动，评审时按此核对）：**

- **目标（spec 5.4 + 5.1）**：`_changeCollector` 与 `MakeCollector` 初始化循环不得对同一结构的每个实体重复求值结构级条件（Mask + Dense）。用户 API（`Collected` / `Matching` / `Clashing` / `Changed` / `Flush` / `EntityCollectorFlag`）不变；`RelevantGate` 不变；Tag / Discrete 增删与 revision 变化仍按原语义进入 `Changed`（受 `RelatedComponentOnly` 约束）。
- **求值拆分（绑定决定）**：`ComponentFilter(structure, row)` 的公开语义不变，内部改为 `EvaluateStructure(structure)` 的结构级结果 + `RowFilter(structure, row, anySatisfied)` 的行级判断：
  - 结构级：Mask 相交；`OfNone` 的 Dense 桶不得命中；`OfAll` 的 Dense 桶必须全部命中；`OfAny` 的 Dense 桶命中即 `AnySatisfied = true`。
  - 行级：`OfNone` / `OfAll` 的 Tag + Discrete 桶；`AnySatisfied == false` 时再查 `OfAny` 的 Tag + Discrete 桶。
  - `OfAny` 的 Dense 桶只有在 Any 桶**不含任何行级条件**时才是结构级必要条件；否则结构级只记录 `AnySatisfied`，与行级结果做或运算（保证 `OfAny<Dense>().OfAny<Tag>()` 语义正确）。
- **缓存值不是单 bool（对任务建议的修订）**：若缓存 `Dictionary<Structure, bool>` 并把 Dense any 当作结构级必要条件，会错误拒绝"结构无 Dense any、但行有 Tag any"的实体；若把 Dense any 留给行级每次求值，则混合 any 场景仍逐实体求值 Dense，违背目标。因此缓存值为 `EntityMatcher.StructureMatch`（`Passes` + `AnySatisfied` 两个 bool，均为结构不变量）。`RowFilter` 仅依赖这两个缓存位，行级只访问 Tag / Discrete。
- **缓存归属（绑定决定）**：缓存放在 `EntityMatchManager.Collector`（`Dictionary<Structure, EntityMatcher.StructureMatch>`），每 collector 一份，key 为结构引用（默认引用相等）。依据：`ComponentManager.Structures` 是 `StructureRegistry`，只增不删（`StructureRegistry.cs:8-37`），结构创建后 Dense 组成与 Mask 不再变化（行增删 / 迁移不影响结构级条件），因此条目**永不需要失效**；结构生命周期覆盖 collector 生命周期，无悬垂 key。不放在 `EntityMatcher` 上：同一 matcher 可被多个 collector / query 共享，缓存会跨使用方串味且无归属释放点。
- **第三方 `IEntityMatcher` 安全回退（绑定决定）**：加速入口 `Collector.Matches` 用 `Matcher is EntityMatcher fastMatcher` 判定；非内置实现直接走 `Matcher.ComponentFilter(structure, row)`，不缓存、行为不变。选 `is` 模式匹配而非新增内部接口：`EntityMatcher` 是 `public class`（`EntityMatcher.cs:55`），`EvaluateStructure` / `RowFilter` / `StructureMatch` 均为 `internal`，同程序集可见；公开接口 `IEntityMatcher`（`IEntityMatcher.cs:9-32`）零改动，第三方实现继续工作。`EntityQuery`（Task 2）继续按行调用 `ComponentFilter`，本任务不改 `EntityQuery`。
- **`_changeCollector` 最小改动（绑定决定）**：保留 `EntityMatchManager.cs:470` 的 Mask 快速预筛（mask 不相交提前返回，同时保持"不更新 pending 缓冲"的既有行为）；仅把 `EntityMatchManager.cs:480` 换成 `collector.Matches(structure, row)`。`alreadyCollected` / `isAdd` 分支 / `RelevantGate` / `MarkChanged` 全部不动——拆分前后 `isMatched` 真值表逐项一致（Dense 组成对结构内所有行统一）。
- **生命周期收尾**：`Collector.Dispose()` 与 `OnManagerDestroyed` 的逐 collector 清理循环清空 `StructureMatches`（与既有 buffer 清理一致）。
- **测试钩子（绑定决定）**：`EntityMatcher` 新增 `internal int StructureEvaluationCount { get; private set; }`，在 `EvaluateStructure` 开头自增，XML 注释注明"内部测试钩子、非公开 API"。选它而不是计数子类：`ComponentFilter` 非 virtual，子类无法拦截；自定义 `IEntityMatcher` 只走回退路径，测不到缓存。测试直接读 matcher 实例的计数，不新增任何公开成员。
- **测试文件独立（绑定决定）**：新增 `Test/CollectorAccelerationTestUnit.cs` 而非追加到 `EntityCollectorTestUnit.cs`（1757 行、无 Tag / Discrete 组件、无计数断言）。新 fixture 自带 `Position` / `Velocity` / `Mana`(Discrete) / `PlayerTag`(Tag) 与 `CountingPositionMatcher`；原 fixture 一字不改、继续全绿，是"语义不变"的最强证据。
- **行为不变性**：447 个既有测试全部不得修改、必须保持通过。7 个新测试覆盖：结构级结果跨行复用（测试 1）、行级 Tag（测试 2）/ Discrete（测试 3）过滤、混合 kind `OfAny` 语义（测试 4）、新结构出现后的缓存正确性（测试 5）、进入 / 离开 / revision 语义（测试 6）、第三方 matcher 回退（测试 7）。
- **测试计数（绑定）**：基线 447 + 新增 9 = **456 passed**；过滤预期：`CollectorAccelerationTestUnit` 9、`EntityCollectorTestUnit` 65 不变、全量 456。

- [ ] **Step 1: 写失败测试**

创建 `Test/CollectorAccelerationTestUnit.cs`：

```csharp
using CoreECS.Defines;
using CoreECS.Structures;

namespace CoreECS.Test
{
    /// <summary>
    /// Collector structure-level acceleration contract: the structure-level matcher result
    /// (mask + dense conditions) is evaluated once per structure and reused across its rows,
    /// while tag/discrete conditions stay row-level and collector semantics are unchanged.
    /// Kept separate from <see cref="EntityCollectorTestUnit"/> so the untouched fixture
    /// remains the parity baseline for the accelerated path.
    /// </summary>
    [TestFixture]
    public class CollectorAccelerationTestUnit
    {
        private World _world = null!;

        [SetUp]
        public void Setup()
        {
            _world = new World();
            _world.Startup();
        }

        [TearDown]
        public void TearDown()
        {
            _world?.Shutdown();
        }

        private struct Position : IComponent<Position>
        {
            public int X;
        }

        private struct Velocity : IComponent<Velocity>
        {
            public int Y;
        }

        private struct Mana : IDiscreteComponent<Mana>
        {
            public int Value;
        }

        private struct PlayerTag : ITagComponent<PlayerTag>
        {
        }

        /// <summary>
        /// Third-party matcher that counts <see cref="IEntityMatcher.ComponentFilter"/> calls,
        /// proving the manager's non-<see cref="EntityMatcher"/> fallback stays uncached.
        /// </summary>
        private sealed class CountingPositionMatcher : IEntityMatcher
        {
            public int ComponentFilterCalls;

            public ulong EntityMask => ulong.MaxValue;

            public bool ComponentFilter(Structure structure, int row)
            {
                ComponentFilterCalls += 1;
                return structure.HasDense(ComponentTypeRegistry.GetOrRegister<Position>().TypeId);
            }

            public bool IsRelevantComponent(Type componentType) => true;
        }

        [Test]
        public void Collector_ReusesStructureLevelResultAcrossRowsOfSameStructure()
        {
            var first = _world.CreateEntity();
            var second = _world.CreateEntity();
            var third = _world.CreateEntity();
            first.CreateComponent<Position>();
            second.CreateComponent<Position>();
            third.CreateComponent<Position>();

            var matcher = (EntityMatcher)EntityMatcher.With.OfAll<Position>();
            var collector = _world.CreateCollector(matcher);

            Assert.AreEqual(1, matcher.StructureEvaluationCount,
                "the init loop must evaluate the shared structure once");

            var fourth = _world.CreateEntity();
            fourth.CreateComponent<Position>();
            collector.Flush();

            Assert.AreEqual(1, matcher.StructureEvaluationCount,
                "a new row in a known structure must reuse the cached structure result");
            AssertOnly(collector.Matching, first.EntityId, second.EntityId, third.EntityId, fourth.EntityId);
            AssertOnly(collector.Collected, first.EntityId, second.EntityId, third.EntityId, fourth.EntityId);
        }

        [Test]
        public void Collector_RowLevelTagConditions_StillFilterPerRow()
        {
            var tagged = _world.CreateEntity();
            var plain = _world.CreateEntity();
            tagged.CreateComponent<Position>();
            tagged.CreateComponent<PlayerTag>();
            plain.CreateComponent<Position>();

            var matcher = (EntityMatcher)EntityMatcher.With.OfAll<Position>().OfAll<PlayerTag>();
            var collector = _world.CreateCollector(matcher);
            collector.Flush();

            Assert.AreEqual(1, matcher.StructureEvaluationCount);
            AssertOnly(collector.Matching, tagged.EntityId);
            AssertOnly(collector.Collected, tagged.EntityId);

            collector.Flush();
            plain.CreateComponent<PlayerTag>();
            collector.Flush();

            Assert.AreEqual(1, matcher.StructureEvaluationCount,
                "tag changes must not re-evaluate the structure-level filter");
            AssertOnly(collector.Matching, plain.EntityId);
            AssertOnly(collector.Collected, tagged.EntityId, plain.EntityId);

            collector.Flush();
            tagged.DestroyComponent<PlayerTag>();
            collector.Flush();

            Assert.AreEqual(1, matcher.StructureEvaluationCount);
            AssertOnly(collector.Clashing, tagged.EntityId);
            AssertOnly(collector.Collected, plain.EntityId);
        }

        [Test]
        public void Collector_RowLevelDiscreteConditions_StillFilterPerRow()
        {
            var withMana = _world.CreateEntity();
            var withoutMana = _world.CreateEntity();
            withMana.CreateComponent<Position>();
            withMana.CreateComponent<Mana>();
            withoutMana.CreateComponent<Position>();

            var matcher = (EntityMatcher)EntityMatcher.With.OfAll<Position>().OfAll<Mana>();
            var collector = _world.CreateCollector(matcher);
            collector.Flush();

            Assert.AreEqual(1, matcher.StructureEvaluationCount);
            AssertOnly(collector.Matching, withMana.EntityId);
            AssertOnly(collector.Collected, withMana.EntityId);

            collector.Flush();
            withoutMana.CreateComponent<Mana>();
            collector.Flush();

            Assert.AreEqual(1, matcher.StructureEvaluationCount,
                "discrete changes must not re-evaluate the structure-level filter");
            AssertOnly(collector.Matching, withoutMana.EntityId);
            AssertOnly(collector.Collected, withMana.EntityId, withoutMana.EntityId);
        }

        [Test]
        public void Collector_MixedKindAny_MatchesDenseOrRowLevelConditions()
        {
            var denseOnly = _world.CreateEntity();
            var tagOnly = _world.CreateEntity();
            var both = _world.CreateEntity();
            var neither = _world.CreateEntity();

            denseOnly.CreateComponent<Position>();
            tagOnly.CreateComponent<PlayerTag>();
            both.CreateComponent<Position>();
            both.CreateComponent<PlayerTag>();

            var matcher = (EntityMatcher)EntityMatcher.With.OfAny<Position>().OfAny<PlayerTag>();
            var collector = _world.CreateCollector(matcher);
            collector.Flush();

            Assert.AreEqual(2, matcher.StructureEvaluationCount,
                "the dense-only and dense-free structures are each evaluated once");
            AssertOnly(collector.Matching, denseOnly.EntityId, tagOnly.EntityId, both.EntityId);
            AssertOnly(collector.Collected, denseOnly.EntityId, tagOnly.EntityId, both.EntityId);
            Assert.IsFalse(collector.Collected.Contains(neither.EntityId));
        }

        [Test]
        public void Collector_NewStructure_IsEvaluatedOnceAndThenCached()
        {
            var first = _world.CreateEntity();
            first.CreateComponent<Position>();

            var matcher = (EntityMatcher)EntityMatcher.With.OfAll<Position>();
            var collector = _world.CreateCollector(matcher);
            Assert.AreEqual(1, matcher.StructureEvaluationCount);

            var second = _world.CreateEntity();
            second.CreateComponent<Position>();
            second.CreateComponent<Velocity>();
            collector.Flush();

            Assert.AreEqual(2, matcher.StructureEvaluationCount,
                "a structure seen for the first time must be evaluated exactly once");
            AssertOnly(collector.Matching, first.EntityId, second.EntityId);

            var third = _world.CreateEntity();
            third.CreateComponent<Position>();
            third.CreateComponent<Velocity>();
            collector.Flush();

            Assert.AreEqual(2, matcher.StructureEvaluationCount,
                "rows of the new structure must reuse its cached result");
            AssertOnly(collector.Matching, third.EntityId);
            AssertOnly(collector.Collected, first.EntityId, second.EntityId, third.EntityId);
        }

        [Test]
        public void Collector_AcceleratedPath_PreservesMatchClashAndRevisionSemantics()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<Position>();

            var matcher = (EntityMatcher)EntityMatcher.With.OfAll<Position>();
            var collector = _world.CreateCollector(matcher, EntityCollectorFlag.Default);
            collector.Flush();

            AssertOnly(collector.Matching, entity.EntityId);
            AssertOnly(collector.Collected, entity.EntityId);
            AssertOnly(collector.Changed, entity.EntityId);

            collector.Flush();
            ref var position = ref entity.GetComponent<Position>().RW;
            position.X = 1;
            collector.Flush();

            AssertOnly(collector.Changed, entity.EntityId);

            collector.Flush();
            _world.DestroyEntity(entity);
            collector.Flush();

            AssertEmpty(collector.Collected);
            AssertOnly(collector.Clashing, entity.EntityId);
        }

        [Test]
        public void Collector_ThirdPartyMatcher_UsesComponentFilterFallback()
        {
            var first = _world.CreateEntity();
            var second = _world.CreateEntity();
            first.CreateComponent<Position>();
            second.CreateComponent<Position>();

            var matcher = new CountingPositionMatcher();
            var collector = _world.CreateCollector(matcher);
            collector.Flush();

            Assert.AreEqual(2, matcher.ComponentFilterCalls,
                "a third-party matcher is evaluated once per entity without caching");
            AssertOnly(collector.Matching, first.EntityId, second.EntityId);

            collector.Flush();
            var third = _world.CreateEntity();
            third.CreateComponent<Position>();
            collector.Flush();

            Assert.AreEqual(3, matcher.ComponentFilterCalls);
            AssertOnly(collector.Collected, first.EntityId, second.EntityId, third.EntityId);
        }

        [Test]
        public void Collector_NonMatchingStructure_CachesTheFailureResult()
        {
            var matcher = (EntityMatcher)EntityMatcher.With.OfAny<Position>();
            var collector = _world.CreateCollector(matcher);
            var first = _world.CreateEntity();
            first.CreateComponent<Velocity>();
            collector.Flush();

            AssertEmpty(collector.Matching);
            Assert.AreEqual(1, matcher.StructureEvaluationCount);

            var second = _world.CreateEntity();
            second.CreateComponent<Velocity>();
            collector.Flush();

            AssertEmpty(collector.Matching);
            Assert.AreEqual(1, matcher.StructureEvaluationCount,
                "a cached failing structure-level result must not be re-evaluated");
        }

        [Test]
        public void Collector_CacheIsPerCollector_NotSharedAcrossCollectors()
        {
            var matcher = (EntityMatcher)EntityMatcher.With.OfAll<Position>();
            var firstCollector = _world.CreateCollector(matcher);
            var secondCollector = _world.CreateCollector(matcher);

            var entity = _world.CreateEntity();
            entity.CreateComponent<Position>();
            firstCollector.Flush();
            secondCollector.Flush();

            AssertOnly(firstCollector.Matching, entity.EntityId);
            AssertOnly(secondCollector.Matching, entity.EntityId);
            Assert.AreEqual(2, matcher.StructureEvaluationCount,
                "each collector owns its structure-level cache");

            var second = _world.CreateEntity();
            second.CreateComponent<Position>();
            firstCollector.Flush();
            secondCollector.Flush();

            Assert.AreEqual(2, matcher.StructureEvaluationCount,
                "both collectors reuse their own cached structure-level result");
        }

        private static void AssertEmpty(IReadOnlyList<ulong> actual)
        {
            Assert.AreEqual(0, actual.Count);
        }

        private static void AssertOnly(IReadOnlyList<ulong> actual, params ulong[] expectedIds)
        {
            Assert.AreEqual(expectedIds.Length, actual.Count);
            for (var i = 0; i < expectedIds.Length; i++)
            {
                var occurrences = 0;
                for (var j = 0; j < actual.Count; j++)
                {
                    if (actual[j] == expectedIds[i]) occurrences += 1;
                }

                Assert.AreEqual(1, occurrences, $"entity {expectedIds[i]} must appear exactly once");
            }
        }
    }
}
```

- [ ] **Step 2: 运行过滤测试，确认失败（红灯）**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --filter FullyQualifiedName~CollectorAccelerationTestUnit`
Expected: **FAIL（编译错误）**——`error CS1061: 'EntityMatcher' does not contain a definition for 'StructureEvaluationCount'`（测试先行引用了 Step 3 才加入的内部钩子），构建失败、0 个测试执行。

- [ ] **Step 3: 拆分 `EntityMatcher` 求值并加测试钩子**

用下面代码整体替换 `ECS/EntityMatcher.cs:151-208`（从 `ComponentFilter` 的 XML 注释到 `HasAny` 方法结束；`ResolvedSet` 及前后内容不动，旧 `HasAll` / `HasAny` 一并移除）：

```csharp
        /// <summary>
        /// Number of times <see cref="EvaluateStructure"/> has run. Internal test hook used
        /// to prove the collector evaluates a structure once and reuses the result across
        /// its rows; not part of the public API.
        /// </summary>
        internal int StructureEvaluationCount { get; private set; }

        /// <summary>
        /// Evaluates this matcher against a structure row without materializing component
        /// references. Dense conditions resolve at structure level; tag and discrete
        /// conditions resolve at row level; the entity mask must intersect the structure mask.
        /// </summary>
        /// <param name="structure">Structure owning the row.</param>
        /// <param name="row">Live row inside the structure.</param>
        /// <returns>True when the mask, all, none and any criteria are satisfied.</returns>
        public bool ComponentFilter(Structure structure, int row)
        {
            var match = EvaluateStructure(structure);
            return match.Passes && RowFilter(structure, row, match.AnySatisfied);
        }

        /// <summary>
        /// Structure-level part of the matcher: mask intersection, dense none/all conditions
        /// and the dense any conditions. A structure's dense composition and mask never
        /// change, so the collector implementation caches this result per structure.
        /// </summary>
        /// <param name="structure">Structure to evaluate.</param>
        /// <returns>
        /// A result whose <see cref="StructureMatch.Passes"/> is false when no row can match
        /// and whose <see cref="StructureMatch.AnySatisfied"/> tells whether the any-condition
        /// already holds for every row through the dense composition.
        /// </returns>
        internal StructureMatch EvaluateStructure(Structure structure)
        {
            StructureEvaluationCount += 1;

            if ((EntityMask & structure.Mask) == 0UL) return default;
            if (HasAnyDense(structure, m_noneResolved)) return default;
            if (!HasAllDense(structure, m_allResolved)) return default;

            var anySatisfied = m_anyResolved.IsEmpty || HasAnyDense(structure, m_anyResolved);

            // The any-condition is the only disjunction: when the dense composition cannot
            // satisfy it and there are no row-level any conditions, no row can match.
            if (!anySatisfied && m_anyResolved.Tags.Count == 0 && m_anyResolved.Discretes.Count == 0)
                return default;

            return new StructureMatch(true, anySatisfied);
        }

        /// <summary>
        /// Row-level part of the matcher: tag and discrete none/all conditions plus the
        /// row-level any conditions when the dense any did not already satisfy the matcher.
        /// Only call after <see cref="EvaluateStructure"/> passed.
        /// </summary>
        /// <param name="structure">Structure owning the row.</param>
        /// <param name="row">Live row inside the structure.</param>
        /// <param name="anySatisfied">True when the any-condition already holds for every row.</param>
        /// <returns>True when the row-level criteria are satisfied.</returns>
        internal bool RowFilter(Structure structure, int row, bool anySatisfied)
        {
            if (HasAnyRow(structure, row, m_noneResolved)) return false;
            if (!HasAllRow(structure, row, m_allResolved)) return false;

            return anySatisfied || HasAnyRow(structure, row, m_anyResolved);
        }

        /// <summary>True when every dense condition in the set is present at the structure.</summary>
        private static bool HasAllDense(Structure structure, ResolvedSet set)
        {
            for (var i = 0; i < set.Dense.Count; i++)
            {
                if (!structure.HasDense(set.Dense[i])) return false;
            }

            return true;
        }

        /// <summary>True when at least one dense condition in the set is present at the structure.</summary>
        private static bool HasAnyDense(Structure structure, ResolvedSet set)
        {
            for (var i = 0; i < set.Dense.Count; i++)
            {
                if (structure.HasDense(set.Dense[i])) return true;
            }

            return false;
        }

        /// <summary>True when every tag/discrete condition in the set is present at the row.</summary>
        private static bool HasAllRow(Structure structure, int row, ResolvedSet set)
        {
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

        /// <summary>True when at least one tag/discrete condition in the set is present at the row.</summary>
        private static bool HasAnyRow(Structure structure, int row, ResolvedSet set)
        {
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
        /// Cached result of <see cref="EvaluateStructure"/>: <see cref="Passes"/> is false when
        /// no row of the structure can match; <see cref="AnySatisfied"/> is true when the
        /// any-condition already holds for every row through the dense composition.
        /// </summary>
        internal readonly struct StructureMatch
        {
            /// <summary>True when the structure-level conditions are satisfied.</summary>
            public readonly bool Passes;

            /// <summary>True when the any-condition needs no row-level evaluation.</summary>
            public readonly bool AnySatisfied;

            /// <summary>Creates a structure-level match result.</summary>
            /// <param name="passes">Whether the structure-level conditions are satisfied.</param>
            /// <param name="anySatisfied">Whether the any-condition holds for every row.</param>
            public StructureMatch(bool passes, bool anySatisfied)
            {
                Passes = passes;
                AnySatisfied = anySatisfied;
            }
        }
```

- [ ] **Step 4: 在 `EntityMatchManager.Collector` 接入缓存**

(a) 在 `Matcher` 属性（`EntityMatchManager.cs:73-76`）后新增缓存字段：

```csharp
            /// <summary>
            /// Per-collector cache of the matcher's structure-level result, keyed by structure.
            /// Structures are immutable in composition (they are only created; rows change),
            /// so entries never need invalidation. Only used when <see cref="Matcher"/> is the
            /// built-in <see cref="EntityMatcher"/>.
            /// The matcher must be fully configured before the collector is created:
            /// reconfiguring a matcher afterwards (its condition sets stay mutable) can make
            /// cached structure-level results disagree with the row-level conditions.
            /// </summary>
            public readonly Dictionary<Structure, EntityMatcher.StructureMatch> StructureMatches = new();
```

(b) 在 `m_manager` 字段（`EntityMatchManager.cs:272-275`）后、`ContainsInBuffer` 前新增 `Matches`：

```csharp
            /// <summary>
            /// Evaluates the matcher for a live structure row. When the matcher is the built-in
            /// <see cref="EntityMatcher"/>, the structure-level result is cached per structure
            /// and only tag/discrete conditions are evaluated per row. Other
            /// <see cref="IEntityMatcher"/> implementations fall back to
            /// <see cref="IEntityMatcher.ComponentFilter"/> without caching.
            /// </summary>
            /// <param name="structure">Structure owning the row.</param>
            /// <param name="row">Live row inside the structure.</param>
            /// <returns>True when the row matches the collector's matcher.</returns>
            public bool Matches(Structure structure, int row)
            {
                if (Matcher is EntityMatcher fastMatcher)
                {
                    if (!StructureMatches.TryGetValue(structure, out var match))
                    {
                        match = fastMatcher.EvaluateStructure(structure);
                        StructureMatches.Add(structure, match);
                    }

                    return match.Passes && fastMatcher.RowFilter(structure, row, match.AnySatisfied);
                }

                return Matcher.ComponentFilter(structure, row);
            }
```

(c) 把 `EntityMatchManager.cs:480` 的调用点替换为：

old:

```csharp
            var isMatched = !destroyed && matcher.ComponentFilter(structure, row);
```

new:

```csharp
            var isMatched = !destroyed && collector.Matches(structure, row);
```

(d) `Collector.Dispose()`（`EntityMatchManager.cs:241-253`）在 buffer 清理循环后新增：

```csharp
                // Drop the cached structure-level results
                StructureMatches.Clear();
```

(e) `OnManagerDestroyed`（`EntityMatchManager.cs:607-618`）逐 collector 清理循环后新增：

```csharp
                collector.StructureMatches.Clear();
```

- [ ] **Step 5: 构建 ECS 库（两个 TFM）**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet build ECS/ECS.csproj`
Expected: Build succeeded（net8.0 + netstandard2.1，0 Error）

- [ ] **Step 6: 运行新增过滤测试**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --filter FullyQualifiedName~CollectorAccelerationTestUnit`
Expected: PASS（9 个测试，失败 0）

- [ ] **Step 7: 运行既有 collector 契约测试（语义不变）**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --filter FullyQualifiedName~EntityCollectorTestUnit`
Expected: PASS（65 个测试，失败 0；该文件零改动）

- [ ] **Step 8: 运行全量测试**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj`
Expected: 456 passed（基线 447 + 新增 9），0 failed

- [ ] **Step 9: 提交**

```bash
git add ECS/EntityMatcher.cs ECS/Managers/EntityMatchManager.cs Test/CollectorAccelerationTestUnit.cs
git commit -m "refactor(core): accelerate collector with structure level filter cache

EntityMatcher evaluation is split into a cacheable structure-level part
(mask + dense none/all/any) and a row-level part (tag/discrete none/all
plus row any). Each collector caches the structure-level result per
structure; structures are immutable in composition, so entries never
need invalidation. Third-party IEntityMatcher implementations keep the
uncached ComponentFilter path and the public API is unchanged."
```

---

## Self-Review 记录

1. **（Task 1）Spec 覆盖**：对应 spec 5.3（`s.RO<T>()` 不标记 / `s.RW<T>()` 获取即整结构标记 / 与 row 对齐）与已决事项 7；spec 3.2 的"容量按需扩容、span 与行对齐"由测试 1/7/8 钉死（`Count == 0` 与容量大于 Count 两种边界）。Phase 3 的 `IEntityQuery`（5.2）与 collector（5.4）不在本次 dispatch，已列入范围边界由后续任务追加。
2. **（Task 1）占位符扫描**：无 TBD/TODO；测试与（条件触发的）参考实现均为完整代码；命令与预期输出明确（新增 8 个测试，全量 438 passed）。
3. **（Task 1）类型一致性**：测试只使用现有 API——`Structure(StructureKey)` / `Append(ulong, EntityLocation)` / `SetDenseValue<T>(int, in T, uint)` / `GetDenseRef<T>(int)` / `GetDenseRevision<T>(int)` / `RO<T>()` / `RW<T>()` / `Count` / `Entities` / `SwapRemove(int)` / `Observer`、`StructureKey(uint[], ulong)`、`ComponentTypeRegistry.GetOrRegister<T>()`、`EntityLocation.Pool.Get()`、`ComponentVersion.Next()`；`RecordingObserver` 实现 `IStructureObserver` 三方法；`Structure` 构造为 internal，测试经 `InternalsVisibleTo("Test")` 访问。契约文件已用独立临时测试工程（引用当前 HEAD 的 ECS 项目）实测 8/8 通过。
4. **（Task 1）实勘偏差与处理**：任务文本假设 `RO/RW` 待实现，实勘为 Plan 1a Task 7 已落地且语义与 spec 5.3 / 已决事项 7 完全一致（`Structure.cs:192-219`，commit `0d75744`）；handoff 第 2 节 Phase 3 范围第 1 条同样滞后。处理：Task 1 改为契约测试补齐（生产代码零改动），参考实现留在设计说明中作为条件修正路径；建议后续修订 handoff 该条。
5. **（Task 2）Spec 覆盖**：spec 5.2 接口逐字落地（`IEntityQuery : IDisposable` + `Matcher` / `Structures` / `Entities` / `Refresh()`），创建入口 `world.Query(matcher)`；v1 `world.Query(matcher, ICollection<...>)` 两个重载删除（§11 破坏性变更）；匹配复用 5.1 的 `IEntityMatcher.ComponentFilter`（结构级 Dense + Mask 粗筛、行级 Tag / Discrete 精筛）；已决事项 6（非池化、`Dispose`、`Refresh` 快照、`IEnumerable<ulong> Entities`）由测试 1 / 3 / 4 / 8 / 9 / 10 钉死；`Structures` 去重与"只含匹配结构"由测试 7 钉死。
6. **（Task 2）占位符扫描**：无 TBD/TODO；接口、实现、World / 扩展替换、11 个新测试与逐调用点迁移均为完整代码；命令与预期输出明确（过滤 11 / 24 / 17，全量 447，失败 0）。
7. **（Task 2）类型一致性**：`IEntityQuery` / `EntityQuery` / `World.Query(IEntityMatcher)` / 两个扩展方法签名与所有调用点对齐；读取前统一显式 `Refresh()`；`Entities` 为 `IEnumerable<ulong>`（测试用 `ToList()` / `Count()`）；`EntityQuery` 经 `EntityManager.Table`（internal）访问 `EntityTable`，`EntityQuery` 为 internal 且 `World` 直接构造；测试经 `InternalsVisibleTo("Test")` 使用 `ComponentTypeRegistry` 核对 `Structures` 的 Dense 归属。`Matcher` 同实例断言（测试 10）使用 `AreSame`，接口引用不装箱。
8. **（Task 2）实勘偏差与处理**：任务文本只点名 World 的两个 v1 重载，实勘发现 `ECS/EntityMatcherExtension.cs:457-487` 的两个集合式扩展 `matcher.Query(world, ICollection<...>)` 也调用被删重载（grep 的 4 处生产调用点之二）。处理：保留其公开签名（spec §11 未列入删除清单，且设计原则要求用户可见 API 尽量与 v1 一致），把方法体迁移到 `using var query = world.Query(matcher); query.Refresh();` + 逐个拷贝（Entity 重载用 `world.GetEntity(entityId)` 还原句柄），校验顺序保持 world → Ready/matcher → result；其两个测试原样保留。另：任务文本示例 `using var query = world.Query(matcher); foreach ...` 省略了 `Refresh()`，按 spec 5.2 接口注释与 collector 显式 `Flush()` 先例，本计划要求读取前显式 `Refresh()`（绑定决定，`World.Query` 不自动刷新），所有迁移代码按此编写。
9. **（Task 2）测试删除理由**：`World_Query_Ulong_AppendsToExistingCollection` 的"向调用方集合追加 + 返回追加数"与 `World_Query_ThrowsWhenResultIsNull` 的 result 参数语义随 v1 重载删除而消失，无法在新 World API 上保留，故删除；"追加 + 返回数量"语义由新增 `World_Query_Extension_AppendsToExistingCollection`（Step 8(j)，扩展方法签名保留）恢复覆盖，其余由迁移后的 `World_Query_ReturnsIdsForEntitiesMatchingMatcher`、`World_Query_DoesNotReturnDestroyedEntities` 与新增 `EntityQueryTestUnit`（快照内容 / 计数 / null matcher 由 `World_Query_ThrowsWhenMatcherIsNull` 覆盖）提供。两个 `World_Query_ThrowsWhenWorldNotReady_*` 合并为 1（新 API 只有一个重载），未丢失任何断言。计数：438 + 11 − 3 + 1 = 447。
10. **（Task 2 质量评审修订）**：质量审查确认实现与计划一致、无正确性缺陷，但发现 1 处 Important + 3 处 Minor：(a) **Important**：绑定决定"`World.Query` 不自动 Refresh"没有测试——所有用例都先 Refresh，若构造函数改为自动刷新全部测试仍绿；新增 `Query_BeforeRefresh_SnapshotIsEmpty`（构造后快照为空，Refresh 后命中）。(b) 接口 XML 文档补充 `Dispose` 当前为 no-op、快照仍可读的说明（评审修订）。(c) 删除 `ECS/World.cs` 不再使用的 `using System.Collections.Generic;`。(d) 恢复被删除的"扩展方法追加到既有集合"覆盖：新增 `World_Query_Extension_AppendsToExistingCollection`。Task 2 测试数 10 → 11（EntityQueryTestUnit）、23 → 24（WorldTestUnit），全量 445 → 447。
11. **（Task 3）Spec 覆盖**：spec 5.4（用户 API 不变、内部结构级加速、Tag / Discrete 增删与 revision 变化进入 `Changed` 且受 `RelatedComponentOnly` 约束）与 5.1（结构级粗筛 = Dense 集合 + Mask；行级精筛 = Tag 位图 / Discrete 存在性）逐条落地；`EntityCollectorFlag` / `Flush` / `RelevantGate` / `_changeCollector` 分支逻辑零改动，由既有 65 个 `EntityCollectorTestUnit` 用例继续全绿钉死；加速本身（跨行复用、新结构缓存、混合 any、回退路径）由 9 个新用例钉死。
12. **（Task 3）占位符扫描**：无 TBD/TODO；测试文件、`EntityMatcher` 替换块、`EntityMatchManager` 5 处改动均为完整代码；命令与预期输出明确（Step 2 红灯为编译错误；过滤 9 / 65，全量 456，失败 0）。
13. **（Task 3）类型一致性**：`EvaluateStructure` 返回 `EntityMatcher.StructureMatch`（`Passes` + `AnySatisfied`），`RowFilter(structure, row, anySatisfied)` 与 `ComponentFilter` / `Collector.Matches` 的调用点一致；`Collector.Matches` 的 `is EntityMatcher` 模式匹配与 `internal` 可见性经 `InternalsVisibleTo("Test")` 对齐；测试只使用现有公开 API（`CreateCollector` / `CreateComponent<T>` / `Flush` / `GetComponent<T>().RW`）+ 新增 internal 计数钩子；`StructureMatches` 键为 `Structure` 引用（默认引用相等），`StructureMatch` 为 `readonly struct`、无装箱。
14. **（Task 3）实勘偏差与处理**：(a) 任务建议缓存 `Dictionary<Structure, bool>`，实勘发现 `OfAny` 混合 Dense + Tag / Discrete 时单 bool 无法既保持语义又消除行级 Dense 求值，改为 `StructureMatch` 双位值（测试 4 钉死该场景）。(b) 任务建议结构级过滤含 "Dense buckets of none/all/any"，实现明确为：Dense any 仅在 Any 桶不含行级条件时才是结构级必要条件；含行级条件时 Dense any 只置 `AnySatisfied`，由行级 or 合并。(c) 保留 `_changeCollector` 既有的 Mask 快速预筛（`EntityMatchManager.cs:470`）：既避免无谓字典查找，也保持"mask 不相交时提前返回、不更新 pending 缓冲"的既有行为（该行为由 `alreadyCollected` 分支决定，移除预筛会改变语义）。(d) `EntityQuery`（Task 2）按行调用 `ComponentFilter`，本任务明确不改动，查询侧缓存留给后续需要时再评审。
15. **（Task 3 质量评审修订）**：质量审查确认实现与计划逐字一致、语义等价（独立 912 组合等价性验证 0 不一致）、缓存不失效（结构组成不可变 + registry 只增）且加速真实，但发现 1 处 Important + 2 处 Minor：(a) **Important**：缓存隐含"matcher 在创建 collector 后不可再配置"的契约，而 `EntityMatcher` 的条件集合构造后仍可变（fluent 方法返回 `this`），文档只说明了结构不可变；在 `StructureMatches` XML 文档补充该契约（"matcher must be fully configured before the collector is created"）。(b) 失败结果缓存未被钉死——新增 `Collector_NonMatchingStructure_CachesTheFailureResult`（不匹配结构在后续行加入后不再重估）。(c) 每 collector 缓存隔离未被钉死——新增 `Collector_CacheIsPerCollector_NotSharedAcrossCollectors`（同一 matcher 两个 collector，各自评估一次并各自复用）。Task 3 测试数 7 → 9，全量 454 → 456。

**收尾总结**：Task 1-3 全部落地后 Phase 3（spec 5.2 / 5.3 / 5.4）交付完成，全量 456 passed / 0 failed；下一步为 Phase 4 调度（`RegisterGroup` / `Before` / `After` / 拓扑排序）。
