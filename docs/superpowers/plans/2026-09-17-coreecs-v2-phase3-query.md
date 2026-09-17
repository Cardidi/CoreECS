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

Task 2（`IEntityQuery` + `World.Query(matcher)` + 删除 v1 `Query(IEntityMatcher, ICollection<...>)` 重载 + 迁移调用点）与 Task 3（collector 结构级加速）的文件行由后续 dispatch 追加任务时补入本表。

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

## Self-Review 记录

1. **（Task 1）Spec 覆盖**：对应 spec 5.3（`s.RO<T>()` 不标记 / `s.RW<T>()` 获取即整结构标记 / 与 row 对齐）与已决事项 7；spec 3.2 的"容量按需扩容、span 与行对齐"由测试 1/7/8 钉死（`Count == 0` 与容量大于 Count 两种边界）。Phase 3 的 `IEntityQuery`（5.2）与 collector（5.4）不在本次 dispatch，已列入范围边界由后续任务追加。
2. **（Task 1）占位符扫描**：无 TBD/TODO；测试与（条件触发的）参考实现均为完整代码；命令与预期输出明确（新增 8 个测试，全量 438 passed）。
3. **（Task 1）类型一致性**：测试只使用现有 API——`Structure(StructureKey)` / `Append(ulong, EntityLocation)` / `SetDenseValue<T>(int, in T, uint)` / `GetDenseRef<T>(int)` / `GetDenseRevision<T>(int)` / `RO<T>()` / `RW<T>()` / `Count` / `Entities` / `SwapRemove(int)` / `Observer`、`StructureKey(uint[], ulong)`、`ComponentTypeRegistry.GetOrRegister<T>()`、`EntityLocation.Pool.Get()`、`ComponentVersion.Next()`；`RecordingObserver` 实现 `IStructureObserver` 三方法；`Structure` 构造为 internal，测试经 `InternalsVisibleTo("Test")` 访问。契约文件已用独立临时测试工程（引用当前 HEAD 的 ECS 项目）实测 8/8 通过。
4. **（Task 1）实勘偏差与处理**：任务文本假设 `RO/RW` 待实现，实勘为 Plan 1a Task 7 已落地且语义与 spec 5.3 / 已决事项 7 完全一致（`Structure.cs:192-219`，commit `0d75744`）；handoff 第 2 节 Phase 3 范围第 1 条同样滞后。处理：Task 1 改为契约测试补齐（生产代码零改动），参考实现留在设计说明中作为条件修正路径；建议后续修订 handoff 该条。
