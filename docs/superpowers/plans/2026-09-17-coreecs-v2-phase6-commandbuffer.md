# CoreECS v2 Phase 6 CommandBuffer 与文档 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 交付 spec 第 8 节 CommandBuffer 与 1c 遗留的 `SetMask` 迁移，并把 README / QUICK_START 中英文文档更新到 v2（spec 交付表第 6 行「文档评审通过」）：Task 1 实现 `Entity.SetMask(ulong)` + `ComponentOrchestrator.SetMask`（结构键迁移，保留 dense/discrete/tag，不触发组件 hook）；Task 2 实现 `World.CreateCommandBuffer()` + `CommandBuffer` 记录层（占位实体、记录结构、`Dispose` 丢弃、记录期间零结构迁移）；Task 3 实现 `CommandBuffer.Playback()`（按记录顺序批量应用、占位实体解析、`cmd.SetMask`、Playback 后复用）；Task 4 更新 README / README.zh-CN / docs/QUICK_START.md / docs/QUICK_START.zh-CN.md 并做文档评审。

**Architecture:** `SetMask` 复用既有 archetype 迁移机制：以 `(当前 dense 组成, 新 mask)` 为 `StructureKey` 查/建目标 `Structure`，`Append` 绑定同一个池化 `EntityLocation`，`CopyDenseTo` / `CopyTagsTo` / `MoveDiscreteTo` 搬运数据后 `SwapRemove` 源行，全程不调用 `ComponentHookDispatcher`。`CommandBuffer` 是 `World` 创建的非池化记录器：`CreateEntity` 返回「占位实体」（同一 `World`、entityId 为静态全局计数器从 `1UL << 63` 递增的合成 id、location 为 null 的 `Entity`），命令记录进 `List<Command>`（`CommandKind` + `Target` + `Mask` + 装箱 `Value` + 静态泛型 `CommandHandler` 委托）；`Playback` 按记录顺序先解析占位实体（`m_resolved` 映射，`CreateEntity` 记录写入映射）再调用公开 `Entity` / `World` API；`Dispose` 只清空记录、零结构变更。库项目 C# 9、`net8.0` + `netstandard2.1`、`ImplicitUsings disable`、`Nullable disable`。

**Tech Stack:** C# 9（`LangVersion 9`）、`net8.0` + `netstandard2.1`、NUnit 3.14、`dotnet test --filter`

**Spec:** `docs/superpowers/specs/2026-09-17-coreecs-v2-design.md`（第 3.1/3.3 节 `SetMask` 触发迁移、第 8 节 CommandBuffer API、第 9 节交付表、第 11 节兼容性；第 8 节原文：`world.CreateCommandBuffer()`、`cmd.CreateEntity(mask)` 返回占位实体、`cmd.CreateComponent<T>(e[, value])`、`cmd.DestroyComponent<T>(e)`、`cmd.SetMask(e, mask)`、`cmd.DestroyEntity(e)`、手动 `cmd.Playback()` 按记录顺序立即应用且可复用、未 Playback 直接 `Dispose` = 丢弃记录并释放资源、记录期间零结构迁移）

**Handoff:** `docs/superpowers/plans/2026-09-17-coreecs-v2-handoff.md`（第 2 节 Phase 6 范围与计划编写约定；本计划把「SetMask 迁移」独立为 Task 1 以便每个任务结束时可编译全绿——`cmd.SetMask` 依赖 `Entity.SetMask`，理由记录在计划 Self-Review）

---

## File Structure

| 文件 | 职责 |
|---|---|
| `ECS/Structures/ComponentOrchestrator.cs` | Task 1：新增 `public void SetMask(ulong entityId, ulong mask)`——按 `(dense 组成, 新 mask)` 迁移行，保留 dense 数据 / discrete / tag，不触发组件 hook；同 mask 提前返回；busy / 死亡实体经 `RequireLocation` 抛异常 |
| `ECS/Entity.cs` | Task 1：在 `Mask` 属性后新增 `public void SetMask(ulong mask)`（spec 第 3.1/3.3 节；1c 遗留项） |
| `Test/EntitySetMaskTestUnit.cs` | Task 1 新增 8 个测试 |
| `ECS/CommandBuffer.cs` | Task 2 新增 + Task 3 扩展：记录层（占位实体 + `Command` 记录 + `EnsureTarget` + `Dispose` 丢弃）→ `Playback` 按序应用 + 占位解析 + `cmd.SetMask` |
| `ECS/World.cs` | Task 2：`#region PublicAPI` 新增 `CreateCommandBuffer()`（`Ready` 断言 + `new CommandBuffer(this)`） |
| `Test/CommandBufferTestUnit.cs` | Task 2 新增 8 个测试 |
| `Test/CommandBufferPlaybackTestUnit.cs` | Task 3 新增 12 个测试 |
| `README.md` / `README.zh-CN.md` | Task 4：intro / Features 表 / At a Glance / Key Concepts 表按 v2 更新（archetype、三组件类别、IEntityQuery、s.RO/RW、组排序、CommandBuffer） |
| `docs/QUICK_START.md` / `docs/QUICK_START.zh-CN.md` | Task 4：组件三类、掩码 + `SetMask`、批量访问 + `IEntityQuery`、系统分组排序、CommandBuffer 章节、v1→v2 破坏性变更清单 |

## 本计划范围边界

本计划覆盖 Phase 6 四个任务：

- Task 1（1c 遗留）：`Entity.SetMask` + `ComponentOrchestrator.SetMask` 迁移语义；全量 513 → 521
- Task 2：`World.CreateCommandBuffer()` + `CommandBuffer` 记录层（占位实体、`CreateEntity` / `CreateComponent` / `DestroyComponent` / `DestroyEntity` 记录、`EnsureTarget` 校验、`Dispose` 丢弃、记录期间零结构迁移）；全量 521 → 529
- Task 3：`CommandBuffer.Playback()`（按记录顺序应用、占位解析、`cmd.SetMask`、Playback 后复用、异常时记录仍消费）；全量 529 → 541
- Task 4：README / QUICK_START 中英文文档更新 + 文档评审（spec 交付表第 6 行）

**不包括**：`ICommandBuffer` 接口与 `CommandBufferFlag`（旧探索分支产物，spec 第 8 节未包含，明确不采用）；CommandBuffer 池化（spec 未要求）；`Playback` 返回创建实体句柄的 `Resolve` API（spec 未要求，占位实体为 buffer 批次内私有；决策记录在 Self-Review）；collector 对 `SetMask` 的事件通知（`SetMask` 不产生组件事件，collector 的事件驱动成员关系不会因 mask 变化刷新——已知限制，写入 Task 1 设计说明与文档）；`IWorld` / `EntityExtension` 不新增 `SetMask` / `CreateCommandBuffer` 成员（`World` 具体类 API）。

---

## Task 1: Entity.SetMask 迁移（1c 遗留 API）

**Files:**
- Modify: `ECS/Structures/ComponentOrchestrator.cs`（在 `RemoveComponent` 方法之后、`RequireLocation` 之前插入 `SetMask`）
- Modify: `ECS/Entity.cs`（在 `Mask` 属性（当前 47-49 行）之后、`CreateComponent<T>()` 之前插入 `SetMask`）
- Test: `Test/EntitySetMaskTestUnit.cs`（新增，8 个测试）

**前置:** 无。dispatch 前实测基线 `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj` → **513 passed / 0 failed**；`PATH="$HOME/.dotnet:$PATH" dotnet build ECS/ECS.csproj` 双目标 0 错误。

**设计说明（执行时不要改动，评审时按此核对）：**

- `SetMask` 是 spec 第 3.1 节「Mask 参与 Archetype 分类」+ 第 3.3 节「SetMask 触发迁移」的公开入口；v2 分支此前从未有 `SetMask`（1c 计划 Self-Review 第 28 条记录留待本阶段）。
- 迁移语义（绑定）：目标键 = `new StructureKey(current.Key.ToArray(), mask)`；`m_registry.GetOrCreate` 查/建；目标 `Observer` 按既有模式（`AddDenseComponent` 同款 `if (m_observer != null) target.Observer = m_observer;`）接线；`Append(entityId, location)` 复用同一池化 location（引用自动跟随）；依次 `CopyDenseTo` → `CopyTagsTo` → `MoveDiscreteTo` → 源 `SwapRemove(sourceRow)`。顺序与 `AddDenseComponent` 一致，先 Append 后搬运再移除源行。
- 不触发 hook（绑定）：不调用 `ComponentHookDispatcher`、不写 `m_mutating`；`SetMask` 不是组件增删，dense 的 `OnCreate`/`OnDestroy`、discrete 的 hook 都不运行。测试用静态计数组件钉死。
- 同 mask no-op（绑定）：`current.Mask == mask` 直接 return，避免无意义的 Append + SwapRemove 迁移（结构键相同，迁移只会是昂贵空操作）。
- 异常（绑定）：`RequireLocation(entityId)` 提供「死亡实体 / busy 实体」的 `InvalidOperationException`，与既有组件写 API 一致。
- 不产生组件事件（记录）：`SetMask` 迁移不经过 `ComponentManager.KernelObserver`，因此 collector 的事件驱动 `Matching`/`Clashing` 不会因 mask 变化更新；`IEntityQuery.Refresh`（逐行求值）与 `WithMask` matcher 能正确看到新 mask。这是已知限制，Task 4 文档中说明。
- 测试计数（绑定）：基线 513 + 8 = **521 passed**；过滤预期 `EntitySetMaskTestUnit` 8。

- [ ] **Step 1: 写失败测试（新增 `Test/EntitySetMaskTestUnit.cs`）**

创建 `Test/EntitySetMaskTestUnit.cs`：

```csharp
using CoreECS.Defines;
using CoreECS.Managers;
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class EntitySetMaskTestUnit
    {
        private struct Position : IComponent<Position>
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

        private struct LifecycleDense : IComponent<LifecycleDense>
        {
            public int Value;

            public void OnCreate(ulong entityId) => CreateCount += 1;

            public void OnDestroy(ulong entityId) => DestroyCount += 1;

            public static int CreateCount;
            public static int DestroyCount;
        }

        private World m_world;

        [SetUp]
        public void SetUp()
        {
            LifecycleDense.CreateCount = 0;
            LifecycleDense.DestroyCount = 0;
            m_world = new World();
            m_world.Startup();
        }

        [TearDown]
        public void TearDown()
        {
            m_world?.Shutdown();
        }

        [Test]
        public void SetMask_MigratesEntityAndUpdatesMask()
        {
            var entity = m_world.CreateEntity(0b01);
            entity.CreateComponent(new Position { X = 7 });

            entity.SetMask(0b10);

            Assert.IsTrue(entity.IsValid);
            Assert.AreEqual(0b10UL, entity.Mask);
            Assert.AreEqual(7, entity.GetComponent<Position>().RO.X);
            Assert.AreEqual(0b10UL, m_world.GetManager<EntityManager>().GetEntity(entity.EntityId).Mask);
        }

        [Test]
        public void SetMask_PreservesDiscreteComponentsAndTags()
        {
            var entity = m_world.CreateEntity(0b01);
            entity.CreateComponent(new Mana { Value = 3 });
            entity.CreateComponent<PlayerTag>();

            entity.SetMask(0b10);

            Assert.IsTrue(entity.HasComponent<Mana>());
            Assert.AreEqual(3, entity.GetComponent<Mana>().RO.Value);
            Assert.IsTrue(entity.HasComponent<PlayerTag>());
            Assert.IsFalse(entity.GetComponent<PlayerTag>().NotNull);
        }

        [Test]
        public void SetMask_MovesRowToStructureWithNewMask()
        {
            var entity = m_world.CreateEntity(0b01);
            entity.CreateComponent(new Position { X = 1 });
            var table = m_world.GetManager<EntityManager>().Table;
            Assert.IsTrue(table.TryGetLocation(entity.EntityId, out var location));
            var original = location.Structure;

            entity.SetMask(0b10);

            Assert.AreNotSame(original, location.Structure);
            Assert.AreEqual(0b10UL, location.Structure.Mask);
            Assert.AreEqual(0b01UL, original.Mask);
            Assert.AreEqual(0, original.Count);
            Assert.AreEqual(1, location.Structure.Count);
        }

        [Test]
        public void SetMask_ToSameMask_DoesNotMigrate()
        {
            var entity = m_world.CreateEntity(0b01);
            var table = m_world.GetManager<EntityManager>().Table;
            Assert.IsTrue(table.TryGetLocation(entity.EntityId, out var location));
            var structure = location.Structure;

            entity.SetMask(0b01);

            Assert.AreSame(structure, location.Structure);
            Assert.IsNull(structure.SpareSetOrNull);
        }

        [Test]
        public void SetMask_DoesNotRunComponentLifecycleHooks()
        {
            var entity = m_world.CreateEntity();
            entity.CreateComponent(new LifecycleDense { Value = 1 });
            Assert.AreEqual(1, LifecycleDense.CreateCount);

            entity.SetMask(0b10);

            Assert.AreEqual(1, LifecycleDense.CreateCount);
            Assert.AreEqual(0, LifecycleDense.DestroyCount);
        }

        [Test]
        public void SetMask_KeepsExistingComponentRefsValid()
        {
            var entity = m_world.CreateEntity();
            var positionRef = entity.CreateComponent(new Position { X = 1 });

            entity.SetMask(0b10);

            Assert.IsTrue(positionRef.NotNull);
            Assert.AreEqual(1, positionRef.RO.X);
            positionRef.RW.X = 5;
            Assert.AreEqual(5, entity.GetComponent<Position>().RO.X);
        }

        [Test]
        public void SetMask_OnDestroyedEntity_Throws()
        {
            var entity = m_world.CreateEntity();
            m_world.DestroyEntity(entity);

            Assert.Throws<InvalidOperationException>(() => entity.SetMask(0b10));
        }

        [Test]
        public void SetMask_ChangesQueryVisibilityByMask()
        {
            var entity = m_world.CreateEntity(0b01);
            entity.CreateComponent<Position>();

            using var query = m_world.Query(EntityMatcher.WithMask(0b10).OfAll<Position>());
            query.Refresh();
            Assert.AreEqual(0, CountEntities(query));

            entity.SetMask(0b10);
            query.Refresh();

            Assert.AreEqual(1, CountEntities(query));
            foreach (var entityId in query.Entities)
            {
                Assert.AreEqual(entity.EntityId, entityId);
            }
        }

        private static int CountEntities(IEntityQuery query)
        {
            var count = 0;
            foreach (var _ in query.Entities)
            {
                count += 1;
            }

            return count;
        }
    }
}
```

- [ ] **Step 2: 跑测试确认编译失败（红灯）**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --filter "FullyQualifiedName~EntitySetMaskTestUnit" --verbosity normal`

Expected: 构建失败，`error CS1061: 'Entity' does not contain a definition for 'SetMask'`（0 个测试执行）。

- [ ] **Step 3: 实现 `ComponentOrchestrator.SetMask`**

在 `ECS/Structures/ComponentOrchestrator.cs` 的 `RemoveComponent` 方法之后、`RequireLocation` 之前插入：

```csharp
        /// <summary>
        /// Changes the entity mask, migrating its row into the structure with the same dense
        /// composition and the new mask. Dense data, discrete components and tags are preserved;
        /// component lifecycle hooks do not run because no component is added or removed.
        /// Setting the current mask is a no-op. The mask is part of the structure key, so
        /// queries and mask-aware matchers see the entity under the new mask afterwards.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the entity is not alive or is busy (being mutated or destroyed by a hook).
        /// </exception>
        public void SetMask(ulong entityId, ulong mask)
        {
            var location = RequireLocation(entityId);
            var current = location.Structure;
            if (current.Mask == mask) return;

            var targetKey = new StructureKey(current.Key.ToArray(), mask);
            var target = m_registry.GetOrCreate(targetKey);
            if (m_observer != null) target.Observer = m_observer;

            var sourceRow = location.Row;
            var targetRow = target.Append(entityId, location);
            current.CopyDenseTo(target, sourceRow, targetRow);
            current.CopyTagsTo(target, sourceRow, targetRow);
            current.MoveDiscreteTo(target, sourceRow, targetRow);
            current.SwapRemove(sourceRow);
        }
```

- [ ] **Step 4: 实现 `Entity.SetMask`**

在 `ECS/Entity.cs` 的 `Mask` 属性之后、`CreateComponent<T>()` 之前插入：

```csharp
        /// <summary>
        /// Changes the entity mask, migrating the entity into the structure with the same
        /// dense composition and the new mask. Dense data, discrete components and tags are
        /// preserved; no component lifecycle hook runs. Setting the current mask is a no-op.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when the entity is no longer alive or is busy.</exception>
        public void SetMask(ulong mask)
        {
            RequireLocation();
            Orchestrator.SetMask(m_entityId, mask);
        }
```

- [ ] **Step 5: 跑过滤测试**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --filter "FullyQualifiedName~EntitySetMaskTestUnit" --verbosity normal`

Expected: `已通过! - 失败: 0，通过: 8`。

- [ ] **Step 6: 全量测试**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj`

Expected: **521 passed / 0 failed**（基线 513 + 新增 8）。

- [ ] **Step 7: 库构建双目标**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet build ECS/ECS.csproj`

Expected: `已成功生成`，0 错误（`net8.0` + `netstandard2.1` 双目标）。

- [ ] **Step 8: 提交**

```bash
git add ECS/Entity.cs ECS/Structures/ComponentOrchestrator.cs Test/EntitySetMaskTestUnit.cs && git commit -m "feat(core): add entity mask migration via SetMask"
```

**执行提示（已知限制）：** `SetMask` 有意不发出任何组件信号：迁移不经过 `ComponentManager.KernelObserver`，因此用 `WithMask` 创建的 collector 不会因 mask 变化重新分类实体，直到另一次组件事件触碰它。这是已知限制（Task 4 文档需说明），本 Task 的掩码可见性由逐行求值的 `IEntityQuery` 测试钉死——不要为此追加 collector 测试。

## Task 2: CommandBuffer 记录层（占位实体 + 记录结构 + Dispose 丢弃）

**Files:**
- Create: `ECS/CommandBuffer.cs`（完整新内容见 Step 3）
- Modify: `ECS/World.cs`（`#region PublicAPI` 内、`DestroyEntity(Entity entity)` 之后插入 `CreateCommandBuffer()`）
- Test: `Test/CommandBufferTestUnit.cs`（新增，8 个测试）

**前置:** Task 1 已完成（HEAD 含 `feat(core): add entity mask migration via SetMask`；基线 **521 passed / 0 failed**）。本任务不依赖 `SetMask`，但全量计数以 521 为基线。

**设计说明（执行时不要改动，评审时按此核对）：**

- **API 形态（绑定，spec 第 8 节）**：`public sealed class CommandBuffer : IDisposable`，无 `ICommandBuffer` 接口、无 `CommandBufferFlag`（旧探索分支产物，spec 未包含，明确不采用）；构造器 `internal CommandBuffer(World world)`，唯一创建入口是 `World.CreateCommandBuffer()`（`Ready` 断言）。
- **占位实体（绑定）**：`CreateEntity(mask)` 返回 `new Entity(m_world, placeholderId, null, 0)`——同一 World、location 为 null、`IsValid == false` 的合成句柄；`placeholderId` 取自**进程级静态计数器** `s_nextPlaceholderId`（初值 `1UL << 63`，单调递增）。必须用全局计数器而非每 buffer 计数器：`EnsureTarget` 用 `m_placeholders` 集合判定归属，若两个 buffer 各自从 0 编号，A buffer 会误把 B buffer 的同号占位实体当作自己的。实体表的真实 id 从 1 单调递增，63 位前缀不会碰撞；World 单线程使用，计数器无需同步。
- **记录期间零结构迁移（绑定，spec 第 8 节）**：所有记录方法只做 `List<Command>` / `HashSet<ulong>` 写入，不触碰 `EntityManager` / `StructureRegistry`；测试用 `Table.Count`、`Structures.Count`、真实实体组件状态钉死。
- **归属校验（绑定）**：`EnsureTarget(Entity)`——`m_placeholders.Contains(entity.EntityId)`（本 buffer 占位实体）或 `entity.IsValid && ReferenceEquals(entity.World, m_world)`（本 world 存活实体）通过；否则抛 `InvalidOperationException`。default(Entity) 因 `IsValid == false` 被拒；其他 buffer 的占位实体因不在本 buffer 集合且 `IsValid == false` 被拒；其他 world 的实体因 World 引用不同被拒。
- **记录结构（绑定）**：`private struct Command { CommandKind Kind; Entity Target; ulong Mask; object Value; CommandHandler Handler; }`；`private delegate void CommandHandler(CommandBuffer buffer, Command command)`；泛型静态委托 `Handlers<T>`（`CreateComponent` / `CreateComponentWithValue`（`(T)command.Value` 拆箱）/ `DestroyComponent`）与非泛型 `s_createEntityHandler` / `s_destroyEntityHandler`。handler 在 Task 3 的 `Playback` 中调用；本任务它们只被存储。
- **Dispose 语义（绑定，spec 第 8 节）**：未 Playback 直接 `Dispose` = 丢弃记录并释放资源；实现为清空 `m_commands` / `m_placeholders`、置 `m_disposed`、`m_world = null`；幂等；之后除 `Dispose` 外所有成员抛 `InvalidOperationException`（`EnsureOpen`）。
- **本任务不含 `Playback` 与 `SetMask`**（Task 3）；`s_createEntityHandler` 当前只创建真实实体（映射在 Task 3 加入）。
- **测试计数（绑定）**：521 + 8 = **529 passed**；过滤预期 `CommandBufferTestUnit` 8。

- [ ] **Step 1: 写失败测试（新增 `Test/CommandBufferTestUnit.cs`）**

```csharp
using CoreECS.Defines;
using CoreECS.Managers;

namespace CoreECS.Test
{
    [TestFixture]
    public class CommandBufferTestUnit
    {
        private struct Position : IComponent<Position>
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

        private World m_world;

        [SetUp]
        public void SetUp()
        {
            m_world = new World();
            m_world.Startup();
        }

        [TearDown]
        public void TearDown()
        {
            m_world?.Shutdown();
        }

        [Test]
        public void CreateCommandBuffer_RequiresReadyWorld()
        {
            var world = new World();

            Assert.Throws<InvalidOperationException>(() => world.CreateCommandBuffer());

            world.Startup();
            using (var commandBuffer = world.CreateCommandBuffer())
            {
                Assert.IsNotNull(commandBuffer);
            }

            world.Shutdown();
            Assert.Throws<InvalidOperationException>(() => world.CreateCommandBuffer());
        }

        [Test]
        public void CreateEntity_ReturnsDistinctPlaceholderHandles()
        {
            using var commandBuffer = m_world.CreateCommandBuffer();

            var first = commandBuffer.CreateEntity(0b01);
            var second = commandBuffer.CreateEntity();

            Assert.AreNotEqual(first, second);
            Assert.AreNotEqual(first.EntityId, second.EntityId);
            Assert.IsFalse(first.IsValid);
            Assert.IsFalse(second.IsValid);
            Assert.AreEqual(m_world, first.World);
        }

        [Test]
        public void RecordingCommands_DoesNotChangeWorldState()
        {
            var entity = m_world.CreateEntity();
            entity.CreateComponent(new Position { X = 1 });
            var componentManager = m_world.GetManager<ComponentManager>();
            var table = m_world.GetManager<EntityManager>().Table;
            var structureCount = componentManager.Structures.Count;

            using var commandBuffer = m_world.CreateCommandBuffer();
            var placeholder = commandBuffer.CreateEntity();
            commandBuffer.CreateComponent<Position>(placeholder, new Position { X = 9 });
            commandBuffer.CreateComponent<Mana>(placeholder);
            commandBuffer.CreateComponent<PlayerTag>(placeholder);
            commandBuffer.DestroyComponent<PlayerTag>(placeholder);
            commandBuffer.CreateComponent<Position>(entity, new Position { X = 2 });
            commandBuffer.DestroyComponent<Position>(entity);
            commandBuffer.DestroyEntity(entity);

            Assert.AreEqual(1, table.Count);
            Assert.AreEqual(structureCount, componentManager.Structures.Count);
            Assert.IsTrue(entity.IsValid);
            Assert.IsTrue(entity.HasComponent<Position>());
            Assert.AreEqual(1, entity.GetComponent<Position>().RO.X);
        }

        [Test]
        public void Dispose_DiscardsPendingCommands()
        {
            var componentManager = m_world.GetManager<ComponentManager>();
            var structureCount = componentManager.Structures.Count;

            using (var commandBuffer = m_world.CreateCommandBuffer())
            {
                var placeholder = commandBuffer.CreateEntity();
                commandBuffer.CreateComponent<Position>(placeholder, new Position { X = 9 });
                commandBuffer.CreateComponent<Mana>(placeholder);
            }

            Assert.AreEqual(0, m_world.GetManager<EntityManager>().Table.Count);
            Assert.AreEqual(structureCount, componentManager.Structures.Count);
        }

        [Test]
        public void Dispose_IsIdempotent()
        {
            var commandBuffer = m_world.CreateCommandBuffer();

            commandBuffer.Dispose();
            commandBuffer.Dispose();
        }

        [Test]
        public void AfterDispose_RecordingMethodsThrow()
        {
            var entity = m_world.CreateEntity();
            var commandBuffer = m_world.CreateCommandBuffer();
            commandBuffer.Dispose();

            Assert.Throws<InvalidOperationException>(() => commandBuffer.CreateEntity());
            Assert.Throws<InvalidOperationException>(() => commandBuffer.CreateComponent<Position>(entity));
            Assert.Throws<InvalidOperationException>(() => commandBuffer.CreateComponent(entity, new Position()));
            Assert.Throws<InvalidOperationException>(() => commandBuffer.DestroyComponent<Position>(entity));
            Assert.Throws<InvalidOperationException>(() => commandBuffer.DestroyEntity(entity));
        }

        [Test]
        public void Recording_RejectsDefaultAndForeignEntities()
        {
            using var commandBuffer = m_world.CreateCommandBuffer();

            Assert.Throws<InvalidOperationException>(() => commandBuffer.CreateComponent<Position>(default));

            var otherWorld = new World();
            otherWorld.Startup();
            try
            {
                var foreign = otherWorld.CreateEntity();
                Assert.Throws<InvalidOperationException>(() => commandBuffer.CreateComponent<Position>(foreign));
                Assert.Throws<InvalidOperationException>(() => commandBuffer.DestroyEntity(foreign));
            }
            finally
            {
                otherWorld.Shutdown();
            }
        }

        [Test]
        public void Recording_RejectsPlaceholderFromAnotherBuffer()
        {
            using var first = m_world.CreateCommandBuffer();
            using var second = m_world.CreateCommandBuffer();
            var firstPlaceholder = first.CreateEntity();
            var secondPlaceholder = second.CreateEntity();

            Assert.AreNotEqual(firstPlaceholder.EntityId, secondPlaceholder.EntityId);
            Assert.Throws<InvalidOperationException>(() => second.CreateComponent<Position>(firstPlaceholder));
            Assert.Throws<InvalidOperationException>(() => second.DestroyEntity(firstPlaceholder));
        }
    }
}
```

- [ ] **Step 2: 跑测试确认编译失败（红灯）**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --filter "FullyQualifiedName~CommandBufferTestUnit" --verbosity normal`

Expected: 编译失败，`error CS1061: 'World' does not contain a definition for 'CreateCommandBuffer'`（及/或 `CS0246: The type or namespace name 'CommandBuffer' could not be found`）。

- [ ] **Step 3: 创建 `ECS/CommandBuffer.cs`（完整文件）**

```csharp
using System;
using System.Collections.Generic;
using CoreECS.Defines;

namespace CoreECS
{
    /// <summary>
    /// Records entity and component commands for deferred application, so a batch of
    /// structural changes can be applied in one explicit playback. Recording never touches
    /// the world: while commands are pending the entity table and structure registry are
    /// unchanged. The buffer is created by <see cref="World.CreateCommandBuffer"/> and is
    /// not thread-safe (worlds are single-threaded).
    /// </summary>
    public sealed class CommandBuffer : IDisposable
    {
        private delegate void CommandHandler(CommandBuffer buffer, Command command);

        private enum CommandKind
        {
            CreateEntity,
            CreateComponent,
            DestroyComponent,
            DestroyEntity,
        }

        private struct Command
        {
            public CommandKind Kind;
            public Entity Target;
            public ulong Mask;
            public object Value;
            public CommandHandler Handler;
        }

        private static class Handlers<T> where T : struct, IComponent<T>
        {
            public static readonly CommandHandler CreateComponent =
                static (buffer, command) => command.Target.CreateComponent<T>();

            public static readonly CommandHandler CreateComponentWithValue =
                static (buffer, command) => command.Target.CreateComponent((T)command.Value);

            public static readonly CommandHandler DestroyComponent =
                static (buffer, command) => command.Target.DestroyComponent<T>();
        }

        private static readonly CommandHandler s_createEntityHandler =
            static (buffer, command) => buffer.m_world.CreateEntity(command.Mask);

        private static readonly CommandHandler s_destroyEntityHandler =
            static (buffer, command) => buffer.m_world.DestroyEntity(command.Target);

        private static ulong s_nextPlaceholderId = 1UL << 63;

        private readonly List<Command> m_commands = new();
        private readonly HashSet<ulong> m_placeholders = new();
        private World m_world;
        private bool m_disposed;

        internal CommandBuffer(World world)
        {
            m_world = world ?? throw new ArgumentNullException(nameof(world));
        }

        /// <summary>
        /// Records the creation of an entity with the given mask and returns the placeholder
        /// handle used to address it in later commands of this buffer. The real entity is
        /// created when the buffer is played back; the placeholder is never a live entity.
        /// </summary>
        /// <param name="mask">Initial entity mask; defaults to <see cref="ulong.MaxValue"/>.</param>
        /// <returns>A buffer-local placeholder handle.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the buffer has been disposed.</exception>
        public Entity CreateEntity(ulong mask = ulong.MaxValue)
        {
            EnsureOpen();

            var placeholderId = s_nextPlaceholderId++;
            var placeholder = new Entity(m_world, placeholderId, null, 0);
            m_placeholders.Add(placeholderId);
            m_commands.Add(new Command
            {
                Kind = CommandKind.CreateEntity,
                Target = placeholder,
                Mask = mask,
                Handler = s_createEntityHandler,
            });

            return placeholder;
        }

        /// <summary>Records adding a default-valued component to the entity.</summary>
        /// <typeparam name="T">Component type to create.</typeparam>
        /// <param name="entity">Placeholder or live entity addressed by this buffer.</param>
        /// <exception cref="InvalidOperationException">Thrown when the buffer is disposed or the entity is foreign.</exception>
        public void CreateComponent<T>(Entity entity) where T : struct, IComponent<T>
        {
            EnsureOpen();
            EnsureTarget(entity);
            m_commands.Add(new Command
            {
                Kind = CommandKind.CreateComponent,
                Target = entity,
                Handler = Handlers<T>.CreateComponent,
            });
        }

        /// <summary>Records adding a component with an initial value to the entity.</summary>
        /// <typeparam name="T">Component type to create.</typeparam>
        /// <param name="entity">Placeholder or live entity addressed by this buffer.</param>
        /// <param name="value">Initial component value.</param>
        /// <exception cref="InvalidOperationException">Thrown when the buffer is disposed or the entity is foreign.</exception>
        public void CreateComponent<T>(Entity entity, T value) where T : struct, IComponent<T>
        {
            EnsureOpen();
            EnsureTarget(entity);
            m_commands.Add(new Command
            {
                Kind = CommandKind.CreateComponent,
                Target = entity,
                Value = value,
                Handler = Handlers<T>.CreateComponentWithValue,
            });
        }

        /// <summary>Records destroying the component of type <typeparamref name="T"/>.</summary>
        /// <typeparam name="T">Component type to destroy.</typeparam>
        /// <param name="entity">Placeholder or live entity addressed by this buffer.</param>
        /// <exception cref="InvalidOperationException">Thrown when the buffer is disposed or the entity is foreign.</exception>
        public void DestroyComponent<T>(Entity entity) where T : struct, IComponent<T>
        {
            EnsureOpen();
            EnsureTarget(entity);
            m_commands.Add(new Command
            {
                Kind = CommandKind.DestroyComponent,
                Target = entity,
                Handler = Handlers<T>.DestroyComponent,
            });
        }

        /// <summary>Records destroying the entity (placeholder or live handle).</summary>
        /// <param name="entity">Placeholder or live entity addressed by this buffer.</param>
        /// <exception cref="InvalidOperationException">Thrown when the buffer is disposed or the entity is foreign.</exception>
        public void DestroyEntity(Entity entity)
        {
            EnsureOpen();
            EnsureTarget(entity);
            m_commands.Add(new Command
            {
                Kind = CommandKind.DestroyEntity,
                Target = entity,
                Handler = s_destroyEntityHandler,
            });
        }

        /// <summary>
        /// Discards all pending commands and releases the buffer. No recorded command is
        /// applied. Calling Dispose twice is a no-op; every other member throws afterwards.
        /// </summary>
        public void Dispose()
        {
            if (m_disposed) return;

            m_disposed = true;
            m_commands.Clear();
            m_placeholders.Clear();
            m_world = null;
        }

        private void EnsureOpen()
        {
            if (m_disposed) throw new InvalidOperationException("CommandBuffer has been disposed.");
        }

        private void EnsureTarget(Entity entity)
        {
            if (m_placeholders.Contains(entity.EntityId)) return;
            if (entity.IsValid && ReferenceEquals(entity.World, m_world)) return;

            throw new InvalidOperationException("Entity is not valid for this command buffer.");
        }
    }
}
```

- [ ] **Step 4: 修改 `ECS/World.cs` 新增 `CreateCommandBuffer()`**

在 `#region PublicAPI` 内、`DestroyEntity(Entity entity)` 方法之后、`Query(IEntityMatcher matcher)` 之前插入：

```csharp
        /// <summary>
        /// Creates a command buffer bound to this world. Record entity and component
        /// commands and apply them in one explicit playback; disposing without playback
        /// discards the pending records. Recording performs no structural change.
        /// </summary>
        /// <returns>A new command buffer bound to this world.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the world is not ready.</exception>
        public CommandBuffer CreateCommandBuffer()
        {
            Assertion.IsTrue(Ready, "World is not ready");
            return new CommandBuffer(this);
        }
```

- [ ] **Step 5: 跑过滤测试**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --filter "FullyQualifiedName~CommandBufferTestUnit" --verbosity normal`

Expected: `已通过! - 失败: 0，通过: 8`。

- [ ] **Step 6: 全量测试**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj`

Expected: `失败: 0，通过: 529，已跳过: 0，总计: 529`。

- [ ] **Step 7: 库构建双目标**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet build ECS/ECS.csproj`

Expected: `已成功生成`，0 错误（net8.0 + netstandard2.1）。

- [ ] **Step 8: 提交**

```bash
git add ECS/CommandBuffer.cs ECS/World.cs Test/CommandBufferTestUnit.cs
git commit -m "feat(core): add CommandBuffer recording layer"
```

## Task 3: CommandBuffer Playback（按记录顺序批量应用 + cmd.SetMask + Playback 后复用）

**Files:**
- Rewrite: `ECS/CommandBuffer.cs`（完整 Task 3 版本见 Step 3；相对 Task 2 新增 `Playback()`、`SetMask(Entity, ulong)`、`Resolve`、`m_resolved`、`CommandKind.SetMask`、`s_setMaskHandler`，并更新 `s_createEntityHandler`）
- Test: `Test/CommandBufferPlaybackTestUnit.cs`（新增，12 个测试）

**前置:** Task 1 / Task 2 已完成（HEAD 含 `feat(core): add CommandBuffer recording layer`；基线 **529 passed / 0 failed**）。

**设计说明（执行时不要改动，评审时按此核对）：**

- **按记录顺序立即应用（绑定，spec 第 8 节）**：`Playback` 遍历 `m_commands`，对每条非 `CreateEntity` 记录先 `Resolve(Target)` 再调用存储的 handler；`CreateEntity` 记录不解析（其 Target 就是占位句柄本身），handler 创建真实实体并写入 `m_resolved[placeholderId] = realEntity`。因为占位句柄只能由 `CreateEntity` 返回，`CreateEntity` 记录必然排在任何引用它的记录之前，解析顺序天然成立。
- **占位解析（绑定）**：`Resolve`——`target.IsValid` 为真（本 world 存活实体）直接返回；否则查 `m_resolved`（本批次已创建的真实实体）；两者都不满足抛 `InvalidOperationException`。真实实体在记录与 Playback 之间被销毁、或 Playback 中被更早的记录销毁，都会命中抛错分支（fail-fast）。
- **记录始终消费（绑定）**：`try { 应用循环 } finally { m_commands/m_placeholders/m_resolved 全清 }`——即使某条命令抛异常，异常向上传播且记录被清空，buffer 保持可复用。理由：异常时已发生部分应用，保留记录重放会二次应用；清空是确定性契约（XML 文档写明）。
- **批次私有占位句柄（绑定）**：`Playback` / `Dispose` 后 `m_placeholders` 为空，旧占位句柄再次传入 `EnsureTarget` 会抛 `InvalidOperationException`（已消费）；`Playback` 后可创建新批次并再次 `Playback`。
- **`cmd.SetMask`（绑定，spec 第 8 节）**：记录 `SetMask` 命令，handler 调 `command.Target.SetMask(command.Mask)`（Task 1 的公开 API）；对占位实体先 `CreateEntity` 再 `SetMask` 时，先按初始 mask 创建再迁移，语义与直接 API 一致。
- **Playback 与 tick 的关系（绑定）**：无特殊限制——`Playback` 可在 `BeginTick`/`EndTick` 之间调用，立即生效（结构变更 A 语义）；测试用 tick 内 Playback 钉死。
- **不提供公开 `Resolve` API（绑定）**：spec 第 8 节未要求，占位句柄是批次内私有句柄；需要真实句柄的场景应先用 `world.CreateEntity()` 再用 buffer 记录组件操作。决策记录在计划 Self-Review。
- **错误路径（绑定）**：`Playback` 对已销毁真实目标抛异常且 buffer 可复用；对「本批次先 `DestroyEntity` 再 `CreateComponent` 的占位实体」由 `Entity.RequireLocation` 抛异常。
- **测试计数（绑定）**：529 + 12 = **541 passed**；过滤预期 `CommandBufferPlaybackTestUnit` 12。

- [ ] **Step 1: 写失败测试（新增 `Test/CommandBufferPlaybackTestUnit.cs`）**

```csharp
using CoreECS.Defines;
using CoreECS.Managers;
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class CommandBufferPlaybackTestUnit
    {
        private struct Position : IComponent<Position>
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

        private struct LifecyclePosition : IComponent<LifecyclePosition>
        {
            public int X;

            public void OnCreate(ulong entityId)
            {
                CreateCount += 1;
                LastCreatedX = X;
            }

            public static int CreateCount;
            public static int LastCreatedX;
        }

        private World m_world;

        [SetUp]
        public void SetUp()
        {
            LifecyclePosition.CreateCount = 0;
            LifecyclePosition.LastCreatedX = 0;
            m_world = new World();
            m_world.Startup();
        }

        [TearDown]
        public void TearDown()
        {
            m_world?.Shutdown();
        }

        private EntityTable Table => m_world.GetManager<EntityManager>().Table;

        private static ulong SingleEntityId(World world)
        {
            ulong result = 0;
            foreach (var entityId in world.GetManager<EntityManager>().Table.EntityIds)
            {
                result = entityId;
            }

            return result;
        }

        private static ulong OtherEntityId(World world, ulong excluded)
        {
            foreach (var entityId in world.GetManager<EntityManager>().Table.EntityIds)
            {
                if (entityId != excluded) return entityId;
            }

            return 0;
        }

        [Test]
        public void Playback_CreatesEntityAndComponents_ForPlaceholder()
        {
            using var commandBuffer = m_world.CreateCommandBuffer();
            var placeholder = commandBuffer.CreateEntity(0b01);
            commandBuffer.CreateComponent<Position>(placeholder, new Position { X = 7 });
            commandBuffer.CreateComponent<Mana>(placeholder, new Mana { Value = 3 });
            commandBuffer.CreateComponent<PlayerTag>(placeholder);

            commandBuffer.Playback();

            var entity = m_world.GetEntity(SingleEntityId(m_world));
            Assert.IsTrue(entity.IsValid);
            Assert.AreEqual(0b01UL, entity.Mask);
            Assert.AreEqual(7, entity.GetComponent<Position>().RO.X);
            Assert.AreEqual(3, entity.GetComponent<Mana>().RO.Value);
            Assert.IsTrue(entity.HasComponent<PlayerTag>());
        }

        [Test]
        public void Playback_AppliesCommandsInRecordingOrder()
        {
            var entity = m_world.CreateEntity();
            entity.CreateComponent(new Position { X = 1 });

            using var commandBuffer = m_world.CreateCommandBuffer();
            commandBuffer.DestroyComponent<Position>(entity);
            commandBuffer.CreateComponent<Position>(entity, new Position { X = 9 });

            commandBuffer.Playback();

            Assert.IsTrue(entity.IsValid);
            Assert.AreEqual(9, entity.GetComponent<Position>().RO.X);
        }

        [Test]
        public void Playback_CreateThenDestroyComponent_LeavesEntityWithoutIt()
        {
            var entity = m_world.CreateEntity();

            using var commandBuffer = m_world.CreateCommandBuffer();
            commandBuffer.CreateComponent<Position>(entity, new Position { X = 1 });
            commandBuffer.DestroyComponent<Position>(entity);

            commandBuffer.Playback();

            Assert.IsTrue(entity.IsValid);
            Assert.IsFalse(entity.HasComponent<Position>());
        }

        [Test]
        public void Playback_CreateComponentWithValue_RunsOnCreateWithValue()
        {
            using var commandBuffer = m_world.CreateCommandBuffer();
            var placeholder = commandBuffer.CreateEntity();
            commandBuffer.CreateComponent<LifecyclePosition>(placeholder, new LifecyclePosition { X = 42 });

            commandBuffer.Playback();

            Assert.AreEqual(1, LifecyclePosition.CreateCount);
            Assert.AreEqual(42, LifecyclePosition.LastCreatedX);
        }

        [Test]
        public void Playback_DestroysPlaceholderAndRealEntities()
        {
            var real = m_world.CreateEntity();

            using var commandBuffer = m_world.CreateCommandBuffer();
            var placeholder = commandBuffer.CreateEntity();
            commandBuffer.CreateComponent<Position>(placeholder);
            commandBuffer.DestroyEntity(placeholder);
            commandBuffer.DestroyEntity(real);

            commandBuffer.Playback();

            Assert.AreEqual(0, Table.Count);
            Assert.IsFalse(real.IsValid);
        }

        [Test]
        public void Playback_SetMask_MigratesExistingAndCreatedEntities()
        {
            var existing = m_world.CreateEntity(0b01);
            existing.CreateComponent(new Position { X = 5 });
            existing.CreateComponent(new Mana { Value = 2 });

            using var commandBuffer = m_world.CreateCommandBuffer();
            var placeholder = commandBuffer.CreateEntity(0b10);
            commandBuffer.CreateComponent<Position>(placeholder, new Position { X = 8 });
            commandBuffer.SetMask(existing, 0b100);

            commandBuffer.Playback();

            Assert.AreEqual(0b100UL, existing.Mask);
            Assert.AreEqual(5, existing.GetComponent<Position>().RO.X);
            Assert.AreEqual(2, existing.GetComponent<Mana>().RO.Value);

            var created = m_world.GetEntity(OtherEntityId(m_world, existing.EntityId));
            Assert.AreEqual(0b10UL, created.Mask);
            Assert.AreEqual(8, created.GetComponent<Position>().RO.X);
        }

        [Test]
        public void Playback_ThenReuse_AppliesSecondBatchAndConsumesOldPlaceholders()
        {
            using var commandBuffer = m_world.CreateCommandBuffer();
            var first = commandBuffer.CreateEntity();
            commandBuffer.CreateComponent<Position>(first, new Position { X = 1 });

            commandBuffer.Playback();
            Assert.AreEqual(1, Table.Count);
            Assert.Throws<InvalidOperationException>(() => commandBuffer.CreateComponent<Position>(first));

            var second = commandBuffer.CreateEntity();
            commandBuffer.CreateComponent<Position>(second, new Position { X = 2 });
            commandBuffer.Playback();

            Assert.AreEqual(2, Table.Count);
        }

        [Test]
        public void Playback_EmptyBuffer_IsNoOpAndRepeatable()
        {
            using var commandBuffer = m_world.CreateCommandBuffer();

            commandBuffer.Playback();
            commandBuffer.Playback();

            Assert.AreEqual(0, Table.Count);
        }

        [Test]
        public void Playback_InsideTick_AppliesImmediately()
        {
            m_world.BeginTick();
            try
            {
                using var commandBuffer = m_world.CreateCommandBuffer();
                var placeholder = commandBuffer.CreateEntity();
                commandBuffer.CreateComponent<Position>(placeholder, new Position { X = 1 });
                commandBuffer.Playback();
            }
            finally
            {
                m_world.EndTick();
            }

            Assert.AreEqual(1, Table.Count);
        }

        [Test]
        public void Playback_AfterDispose_Throws()
        {
            var commandBuffer = m_world.CreateCommandBuffer();
            commandBuffer.Dispose();

            Assert.Throws<InvalidOperationException>(() => commandBuffer.Playback());
        }

        [Test]
        public void Playback_DeadRealTarget_Throws_AndBufferRemainsReusable()
        {
            var doomed = m_world.CreateEntity();

            using var commandBuffer = m_world.CreateCommandBuffer();
            commandBuffer.CreateComponent<Position>(doomed, new Position { X = 1 });
            m_world.DestroyEntity(doomed);

            Assert.Throws<InvalidOperationException>(() => commandBuffer.Playback());

            var placeholder = commandBuffer.CreateEntity();
            commandBuffer.CreateComponent<Position>(placeholder, new Position { X = 2 });
            commandBuffer.Playback();

            Assert.AreEqual(1, Table.Count);
            Assert.AreEqual(2, m_world.GetEntity(SingleEntityId(m_world)).GetComponent<Position>().RO.X);
        }

        [Test]
        public void Playback_CommandOnDestroyedPlaceholder_Throws()
        {
            using var commandBuffer = m_world.CreateCommandBuffer();
            var placeholder = commandBuffer.CreateEntity();
            commandBuffer.DestroyEntity(placeholder);
            commandBuffer.CreateComponent<Position>(placeholder);

            Assert.Throws<InvalidOperationException>(() => commandBuffer.Playback());
        }
    }
}
```

- [ ] **Step 2: 跑测试确认编译失败（红灯）**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --filter "FullyQualifiedName~CommandBufferPlaybackTestUnit" --verbosity normal`

Expected: 编译失败，`error CS1061: 'CommandBuffer' does not contain a definition for 'Playback'`（及/或 `'SetMask'`）。

- [ ] **Step 3: 重写 `ECS/CommandBuffer.cs` 为 Task 3 完整版本**

```csharp
using System;
using System.Collections.Generic;
using CoreECS.Defines;

namespace CoreECS
{
    /// <summary>
    /// Records entity and component commands for deferred application, so a batch of
    /// structural changes can be applied in one explicit <see cref="Playback"/>. Recording
    /// never touches the world: while commands are pending the entity table and structure
    /// registry are unchanged. The buffer is created by <see cref="World.CreateCommandBuffer"/>
    /// and is not thread-safe (worlds are single-threaded).
    /// </summary>
    public sealed class CommandBuffer : IDisposable
    {
        private delegate void CommandHandler(CommandBuffer buffer, Command command);

        private enum CommandKind
        {
            CreateEntity,
            CreateComponent,
            DestroyComponent,
            SetMask,
            DestroyEntity,
        }

        private struct Command
        {
            public CommandKind Kind;
            public Entity Target;
            public ulong Mask;
            public object Value;
            public CommandHandler Handler;
        }

        private static class Handlers<T> where T : struct, IComponent<T>
        {
            public static readonly CommandHandler CreateComponent =
                static (buffer, command) => command.Target.CreateComponent<T>();

            public static readonly CommandHandler CreateComponentWithValue =
                static (buffer, command) => command.Target.CreateComponent((T)command.Value);

            public static readonly CommandHandler DestroyComponent =
                static (buffer, command) => command.Target.DestroyComponent<T>();
        }

        private static readonly CommandHandler s_createEntityHandler =
            static (buffer, command) =>
            {
                var entity = buffer.m_world.CreateEntity(command.Mask);
                buffer.m_resolved[command.Target.EntityId] = entity;
            };

        private static readonly CommandHandler s_destroyEntityHandler =
            static (buffer, command) => buffer.m_world.DestroyEntity(command.Target);

        private static readonly CommandHandler s_setMaskHandler =
            static (buffer, command) => command.Target.SetMask(command.Mask);

        private static ulong s_nextPlaceholderId = 1UL << 63;

        private readonly List<Command> m_commands = new();
        private readonly HashSet<ulong> m_placeholders = new();
        private readonly Dictionary<ulong, Entity> m_resolved = new();
        private World m_world;
        private bool m_disposed;

        internal CommandBuffer(World world)
        {
            m_world = world ?? throw new ArgumentNullException(nameof(world));
        }

        /// <summary>
        /// Records the creation of an entity with the given mask and returns the placeholder
        /// handle used to address it in later commands of this buffer. The real entity is
        /// created when the buffer is played back; the placeholder is never a live entity.
        /// </summary>
        /// <param name="mask">Initial entity mask; defaults to <see cref="ulong.MaxValue"/>.</param>
        /// <returns>A buffer-local placeholder handle.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the buffer has been disposed.</exception>
        public Entity CreateEntity(ulong mask = ulong.MaxValue)
        {
            EnsureOpen();

            var placeholderId = s_nextPlaceholderId++;
            var placeholder = new Entity(m_world, placeholderId, null, 0);
            m_placeholders.Add(placeholderId);
            m_commands.Add(new Command
            {
                Kind = CommandKind.CreateEntity,
                Target = placeholder,
                Mask = mask,
                Handler = s_createEntityHandler,
            });

            return placeholder;
        }

        /// <summary>Records adding a default-valued component to the entity.</summary>
        /// <typeparam name="T">Component type to create.</typeparam>
        /// <param name="entity">Placeholder or live entity addressed by this buffer.</param>
        /// <exception cref="InvalidOperationException">Thrown when the buffer is disposed or the entity is foreign.</exception>
        public void CreateComponent<T>(Entity entity) where T : struct, IComponent<T>
        {
            EnsureOpen();
            EnsureTarget(entity);
            m_commands.Add(new Command
            {
                Kind = CommandKind.CreateComponent,
                Target = entity,
                Handler = Handlers<T>.CreateComponent,
            });
        }

        /// <summary>Records adding a component with an initial value to the entity.</summary>
        /// <typeparam name="T">Component type to create.</typeparam>
        /// <param name="entity">Placeholder or live entity addressed by this buffer.</param>
        /// <param name="value">Initial component value.</param>
        /// <exception cref="InvalidOperationException">Thrown when the buffer is disposed or the entity is foreign.</exception>
        public void CreateComponent<T>(Entity entity, T value) where T : struct, IComponent<T>
        {
            EnsureOpen();
            EnsureTarget(entity);
            m_commands.Add(new Command
            {
                Kind = CommandKind.CreateComponent,
                Target = entity,
                Value = value,
                Handler = Handlers<T>.CreateComponentWithValue,
            });
        }

        /// <summary>Records destroying the component of type <typeparamref name="T"/>.</summary>
        /// <typeparam name="T">Component type to destroy.</typeparam>
        /// <param name="entity">Placeholder or live entity addressed by this buffer.</param>
        /// <exception cref="InvalidOperationException">Thrown when the buffer is disposed or the entity is foreign.</exception>
        public void DestroyComponent<T>(Entity entity) where T : struct, IComponent<T>
        {
            EnsureOpen();
            EnsureTarget(entity);
            m_commands.Add(new Command
            {
                Kind = CommandKind.DestroyComponent,
                Target = entity,
                Handler = Handlers<T>.DestroyComponent,
            });
        }

        /// <summary>
        /// Records changing the entity mask. The mask is part of the structure key, so
        /// playback migrates the entity into the structure with the new mask; dense data,
        /// discrete components and tags are preserved and no component hook runs.
        /// </summary>
        /// <param name="entity">Placeholder or live entity addressed by this buffer.</param>
        /// <param name="mask">New entity mask.</param>
        /// <exception cref="InvalidOperationException">Thrown when the buffer is disposed or the entity is foreign.</exception>
        public void SetMask(Entity entity, ulong mask)
        {
            EnsureOpen();
            EnsureTarget(entity);
            m_commands.Add(new Command
            {
                Kind = CommandKind.SetMask,
                Target = entity,
                Mask = mask,
                Handler = s_setMaskHandler,
            });
        }

        /// <summary>Records destroying the entity (placeholder or live handle).</summary>
        /// <param name="entity">Placeholder or live entity addressed by this buffer.</param>
        /// <exception cref="InvalidOperationException">Thrown when the buffer is disposed or the entity is foreign.</exception>
        public void DestroyEntity(Entity entity)
        {
            EnsureOpen();
            EnsureTarget(entity);
            m_commands.Add(new Command
            {
                Kind = CommandKind.DestroyEntity,
                Target = entity,
                Handler = s_destroyEntityHandler,
            });
        }

        /// <summary>
        /// Applies every recorded command to the world in recording order, then clears the
        /// buffer so it can be reused. Placeholder entities are resolved to the real entities
        /// created earlier in the same playback. Records are consumed even when a command
        /// throws: the exception propagates and the buffer stays reusable.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when the buffer is disposed or a target entity is no longer valid.</exception>
        public void Playback()
        {
            EnsureOpen();

            try
            {
                for (var i = 0; i < m_commands.Count; i++)
                {
                    var command = m_commands[i];
                    if (command.Kind != CommandKind.CreateEntity)
                    {
                        command.Target = Resolve(command.Target);
                    }

                    command.Handler(this, command);
                }
            }
            finally
            {
                m_commands.Clear();
                m_placeholders.Clear();
                m_resolved.Clear();
            }
        }

        /// <summary>
        /// Discards all pending commands and releases the buffer. No recorded command is
        /// applied. Calling Dispose twice is a no-op; every other member throws afterwards.
        /// </summary>
        public void Dispose()
        {
            if (m_disposed) return;

            m_disposed = true;
            m_commands.Clear();
            m_placeholders.Clear();
            m_resolved.Clear();
            m_world = null;
        }

        private void EnsureOpen()
        {
            if (m_disposed) throw new InvalidOperationException("CommandBuffer has been disposed.");
        }

        private void EnsureTarget(Entity entity)
        {
            if (m_placeholders.Contains(entity.EntityId)) return;
            if (entity.IsValid && ReferenceEquals(entity.World, m_world)) return;

            throw new InvalidOperationException("Entity is not valid for this command buffer.");
        }

        private Entity Resolve(Entity target)
        {
            if (target.IsValid) return target;
            if (m_resolved.TryGetValue(target.EntityId, out var resolved)) return resolved;

            throw new InvalidOperationException("CommandBuffer target entity is no longer valid.");
        }
    }
}
```

- [ ] **Step 4: 跑过滤测试**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --filter "FullyQualifiedName~CommandBufferPlaybackTestUnit" --verbosity normal`

Expected: `已通过! - 失败: 0，通过: 12`。

- [ ] **Step 5: 全量测试**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj`

Expected: `失败: 0，通过: 541，已跳过: 0，总计: 541`。

- [ ] **Step 6: 库构建双目标**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet build ECS/ECS.csproj`

Expected: `已成功生成`，0 错误（net8.0 + netstandard2.1）。

- [ ] **Step 7: 提交**

```bash
git add ECS/CommandBuffer.cs Test/CommandBufferPlaybackTestUnit.cs
git commit -m "feat(core): apply CommandBuffer playback with placeholder resolution"
```

## Task 4: 文档更新（README / QUICK_START 中英文）与文档评审

**Files:**
- Modify: `README.md`（intro 第 9 行、Features 表、At a Glance、Key Concepts 表、Documentation 表）
- Modify: `README.zh-CN.md`（与英文逐项对应）
- Modify: `docs/QUICK_START.md`（第 1/2/3/5/8 节修订；新增 CommandBuffer 与 v1→v2 破坏性变更章节；目录重编号）
- Modify: `docs/QUICK_START.zh-CN.md`（与英文逐项对应）

**前置:** Task 1-3 已完成（HEAD 含 `feat(core): apply CommandBuffer playback with placeholder resolution`；基线 **541 passed / 0 failed**）。本任务不改任何 `.cs` 文件，全量测试数保持 541。

**设计说明（执行时不要改动，评审时按此核对）：**

- **交付依据（绑定）**：spec 第 9 节交付表第 6 行「文档：README / QUICK_START 中英文更新 → 文档评审通过」。文档必须覆盖：CommandBuffer（§8）、`IEntityQuery`（§5.2）、`s.RO/RW<T>()`（§5.3）、系统分组排序（§6）、World 生命周期钩子（§7）、破坏性变更清单（§11）。
- **禁止残留 v1 叙述（绑定）**：四份文档正文不得再出现 `ComponentStore` / `EntityGraph` / `MinimalWorld` / `OnTickBegin` / `OnTickEnd` / `OnRegisterManager` / `RegisterServices` / `OnConstruct` / `OnFirstStart` 作为现行 API；这些名字只允许出现在破坏性变更清单中作为「v1 名称」对照。不得再写「注册顺序即执行顺序」。
- **中英一致（绑定）**：中文文档与英文文档章节、代码块、表格一一对应；代码块内容相同（注释可本地化）。
- **不改文档以外的内容（绑定）**：不新增/删除文档文件，不修改 docs/ 下其他文件。

- [ ] **Step 1: 更新 `README.md`**

(a) intro 第 9 行：
old: `Lightweight ECS you can embed beside Unity ECS or other stacks — built around **ComponentStore**, **EntityGraph**, and **structural-change collectors**.`
new: `Lightweight ECS you can embed beside Unity ECS or other stacks — built around **archetype storage**, **structural-change collectors**, and an explicit **CommandBuffer**.`

(b) Features 表替换为：
```markdown
| **Architecture** | Archetype `Structure` storage: row-aligned dense SoA arrays, discrete component stores, tag bitmaps |
| **State-first** | `EntityCollector` with `Flush()`, `Matching` / `Clashing` / `Changed` buffers |
| **Queries** | Fluent `EntityMatcher`, non-pooled `IEntityQuery`, batch `s.RO<T>()` / `s.RW<T>()` spans |
| **Components** | Dense / discrete / tag kinds, `RO` / `RW` refs, optional `OnCreate` / `OnDestroy` |
| **Systems** | Nested groups with `Before` / `After` ordering, `TickGroup` masks, constructor DI via `IInjectionProxy` |
| **CommandBuffer** | Record structural changes and apply them in one explicit `Playback()` |
| **Targets** | `net8.0` and `netstandard2.1` |
```

(c) At a Glance：在两条 `entity.CreateComponent(...)` 之后、`world.BeginTick()` 之前插入：
```csharp
using var cmd = world.CreateCommandBuffer();
var spawned = cmd.CreateEntity();
cmd.CreateComponent(spawned, new PositionComponent { X = 1, Y = 2 });
cmd.Playback();
```

(d) Key Concepts 表：`Component` 行改为 `` Data structs (`IComponent<T>` dense, `IDiscreteComponent<T>`, `ITagComponent<T>`); logic lives in systems ``；`World` 行改为 `Lifecycle (`OnRegister` / `OnSetup` / `OnCleanup`), entities, components, systems, collectors`；并新增四行：
```markdown
| **Structure** | Archetype: entities sharing dense composition + mask, stored row-aligned |
| **Query** | `IEntityQuery` snapshot over matching entities/structures (`Refresh()`) |
| **Group** | Named ordering bucket for systems; `Before` / `After` anchors |
| **CommandBuffer** | Records create/destroy/mask commands; `Playback()` applies them in order |
```

(e) Documentation 表 Quick Start 行描述改为 `Full tutorial (English): world setup, components, queries, systems, collectors, CommandBuffer, breaking changes`。

- [ ] **Step 2: 更新 `README.zh-CN.md`（与 Step 1 逐项对应）**

(a) 第 7 行 intro：
old: `轻量级 ECS，可与 Unity ECS 或其他方案并存 —— 基于 **ComponentStore**、**EntityGraph** 与**结构性变更收集器（Collector）** 构建。`
new: `轻量级 ECS，可与 Unity ECS 或其他方案并存 —— 基于 **archetype 存储**、**结构性变更收集器（Collector）** 与显式的 **CommandBuffer** 构建。`

(b) 特性表：
```markdown
| **架构** | Archetype `Structure` 存储：行对齐 dense SoA 数组、discrete 组件存储、tag 位图 |
| **State-first** | `EntityCollector` 提供 `Flush()` 与 `Matching` / `Clashing` / `Changed` 缓冲区 |
| **查询** | 流式 `EntityMatcher`、非池化 `IEntityQuery`、批量 `s.RO<T>()` / `s.RW<T>()` Span |
| **组件** | Dense / Discrete / Tag 三类，`RO` / `RW` 引用，可选 `OnCreate` / `OnDestroy` |
| **系统** | 可嵌套分组与 `Before` / `After` 排序、`TickGroup` 掩码、`IInjectionProxy` 构造函数注入 |
| **CommandBuffer** | 记录结构性变更，一次显式 `Playback()` 批量应用 |
| **目标框架** | `net8.0` 与 `netstandard2.1` |
```

(c) 一览：同样插入 CommandBuffer 代码块（与英文相同）。

(d) 核心概念表：`Component（组件）` 行改为 `数据结构（dense：IComponent<T>；discrete：IDiscreteComponent<T>；tag：ITagComponent<T>），逻辑放在系统中`；`World（世界）` 行改为 `生命周期（OnRegister / OnSetup / OnCleanup）、实体、组件、系统、收集器`；新增：
```markdown
| **Structure（结构）** | Archetype：相同 dense 组成 + 掩码的实体共享行对齐存储 |
| **Query（查询）** | `IEntityQuery` 匹配实体/结构快照（`Refresh()` 重建） |
| **Group（分组）** | 系统的命名排序桶；`Before` / `After` 锚点 |
| **CommandBuffer** | 记录 create / destroy / SetMask 命令；`Playback()` 按序应用 |
```

(e) 文档表快速入门行描述同步更新。

- [ ] **Step 3: 更新 `docs/QUICK_START.md`**

(a) 目录重编号为 13 节：1-10 不变，新增 `11. Command Buffer`、`12. v1 → v2 Breaking Changes`，原 11 Complete Example 变为 `13`。

(b) 第 1 节：保留既有 `OnRegister(register, services)` / `OnSetup` / `OnCleanup` 描述，补充三钩子时机一句话（`OnRegister` 仅首次 `Startup()`；`OnSetup` 每次 `Startup()`；`OnCleanup` 每次 `Shutdown()`），并补一句 `Startup()` 后可用 `world.CreateCommandBuffer()`。

(c) 第 2 节：在既有 `IComponent<T>` 示例后补充三类组件：
```csharp
public struct PositionComponent : IComponent<PositionComponent>          // Dense
{
    public float X;
    public float Y;
}

public struct ManaComponent : IDiscreteComponent<ManaComponent>          // Discrete
{
    public int Value;
}

public struct PlayerTag : ITagComponent<PlayerTag>                       // Tag
{
}
```
规则要点：kind 按最派生接口判定（Tag > Discrete > Dense）；只有 Dense 与实体掩码决定 Structure 归属，Discrete / Tag 不迁移；Dense / Discrete 在增删时调用 `OnCreate` / `OnDestroy`，Tag 使用默认空实现且内核不调用 tag hook（tag 增删只翻转位图——Plan 1c Self-Review 第 10 条记录的既有偏差，spec §2.1 的「Tag 使用默认空实现」按此实现）；`GetComponent<Tag>` 返回 `default`（`NotNull == false`）。

(d) 第 3 节实体掩码后补充：
```csharp
var actor = world.CreateEntity((ulong)EntityType.Actor);
actor.SetMask((ulong)EntityType.Terrain);   // migrates the entity; data is preserved
```
说明：Mask 参与 archetype 结构键，`SetMask` 会迁移实体；dense 数据、discrete 组件与 tag 全部保留，且不触发组件生命周期钩子；同 mask 调用为 no-op。

(e) 第 5 节新增两个小节：
- `### Queries`：
```csharp
var query = world.Query(EntityMatcher.With.OfAll<PositionComponent>());
query.Refresh();   // the snapshot stays stable until the next Refresh()

foreach (var id in query.Entities)
    Console.WriteLine(id);

query.Dispose();
```
- `### Batch access (SoA)`：
```csharp
var query = world.Query(EntityMatcher.With.OfAll<PositionComponent>());
query.Refresh();

foreach (var structure in query.Structures)
{
    var positions = structure.RO<PositionComponent>();   // ReadOnlySpan<PositionComponent>
    for (var row = 0; row < positions.Length; row++)
        Console.WriteLine($"{structure.Entities[row]}: {positions[row].X}");
}

// RW marks every row of the structure (revision + change event) and is invalidated
// by structural changes; acquire once per structure.
var velocities = query.Structures[0].RW<VelocityComponent>();
for (var row = 0; row < velocities.Length; row++)
    velocities[row].X += 1;
```

(f) 第 8 节「Managing Systems」正文替换为分组与排序：
```csharp
world.RegisterGroup("Physics");
world.RegisterGroup("Gameplay", GroupInsertMode.Early);

world.RegisterSystem<InputSystem>("Gameplay").Before<MovementSystem>();
world.RegisterSystem<MovementSystem>("Gameplay");

world.RegisterGroup("Render").After("Gameplay");
world.RegisterSystem<RenderSystem>("Render");
world.RegisterSystem<RootLevelSystem>();   // no group → root level
```
规则要点：组是纯排序桶、可嵌套；`Before` / `After` 锚点可指向系统类型或组名，允许前向引用；锚点无法解析时记录错误并忽略；成环回退为展平注册序；无约束节点按注册序稳定排序；tick 内注册图变更在下一个 `BeginTick()` 统一重算（已注册系统复用实例）。保留原有 while 循环 tick 示例。

(g) 新增第 11 节 Command Buffer：
```csharp
using var cmd = world.CreateCommandBuffer();

var e = cmd.CreateEntity(0b01);                       // placeholder; resolved at Playback
cmd.CreateComponent<PositionComponent>(e, new PositionComponent { X = 1, Y = 2 });
cmd.CreateComponent<ManaComponent>(e);
cmd.CreateComponent<PlayerTag>(e);
cmd.DestroyComponent<PlayerTag>(e);
cmd.SetMask(e, 0b10);
cmd.DestroyEntity(e);

cmd.Playback();   // applies every record in order; the buffer is reusable afterwards
```
规则要点：记录期间零结构迁移；`Playback` 按记录顺序立即批量应用，可在 tick 内调用；占位实体是 buffer 批次内私有句柄，`Playback` 后失效（再引用抛异常）；未 `Playback` 直接 `Dispose()` = 丢弃记录；适用于批量生成 / 批量销毁；`SetMask` 不产生组件事件，事件驱动 collector 会在下一次相关组件事件时更新，而 `IEntityQuery.Refresh()` 总是能看到新 mask。

(h) 新增第 12 节 v1 → v2 Breaking Changes，逐条列出：
- `world.Query(matcher, ICollection<...>)` 重载删除 → 改用 `world.Query(matcher)` 返回 `IEntityQuery`。
- `MinimalWorld` 删除 → `World` 是唯一入口，核心 managers 内置。
- `EntityGraph` / `ComponentStore<T>` 不再公开（archetype 内核）。
- 生命周期钩子收敛：`OnRegisterManager` / `RegisterServices` / `OnConstruct` / `OnFirstStart` / `OnStart` / `OnShutdown` → `OnRegister(IManagerRegister, IServiceCollection)`（首次 `Startup`）/ `OnSetup`（每次 `Startup`）/ `OnCleanup`（每次 `Shutdown`）；`OnTickBegin` / `OnTick` / `OnTickEnd` 虚钩子删除，tick 由 `World.BeginTick` / `Tick` / `EndTick` 内部驱动。
- `IEntityCollector.Change()` 标记 obsolete → 使用 `Flush()`。
- 新增：`IDiscreteComponent<T>` / `ITagComponent<T>` 组件类别、`Entity.SetMask`、`World.CreateCommandBuffer()`、`IEntityQuery`、系统分组（`RegisterGroup` / `Before` / `After`）。
- 存量组件定义零改动；`CreateComponent` / `DestroyComponent` / `GetComponent` / `HasComponent` 命名保留。

(i) 原 Complete Example 顺延为第 13 节，内容保持可运行（如引用旧钩子名/旧 API 则同步修正），文末链接不变。

- [ ] **Step 4: 更新 `docs/QUICK_START.zh-CN.md`（与 Step 3 逐项对应）**

中文镜像：目录 13 节、三类组件、`SetMask`、Queries / Batch access (SoA)、分组排序、Command Buffer、v1 → v2 破坏性变更、完整示例顺延为 13。代码块与英文一致（注释可中文）。

- [ ] **Step 5: 文档自检（grep 证据）**

Run:
```bash
grep -rn "ComponentStore\|EntityGraph\|MinimalWorld\|OnTickBegin\|OnTickEnd\|OnRegisterManager\|RegisterServices\|OnConstruct\|OnFirstStart" README.md README.zh-CN.md docs/QUICK_START.md docs/QUICK_START.zh-CN.md
```
Expected: 仅出现在 v1→v2 破坏性变更清单（作为 v1 名称对照），正文其他地方零命中。

Run:
```bash
grep -rn "CommandBuffer\|IEntityQuery\|RW<\|RO<\|RegisterGroup\|OnCleanup" README.md README.zh-CN.md docs/QUICK_START.md docs/QUICK_START.zh-CN.md
```
Expected: 六项覆盖点均有命中（CommandBuffer / IEntityQuery / `s.RO/RW<T>()` / 系统分组 / 生命周期钩子）。

- [ ] **Step 6: 全量测试（文档改动不应影响测试）**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj`

Expected: `失败: 0，通过: 541，已跳过: 0，总计: 541`。

- [ ] **Step 7: 提交**

```bash
git add README.md README.zh-CN.md docs/QUICK_START.md docs/QUICK_START.zh-CN.md
git commit -m "doc(proj): update v2 README and quick start guides"
```

- [ ] **Step 8: 文档评审（由独立审查 subagent 执行，本节记录验收口径）**

评审必须独立读代码与 spec 核对，不得只读文档自述：
1. spec §8 CommandBuffer：API 名称、占位实体、Playback 顺序/复用、Dispose 丢弃语义与文档一致。
2. spec §5.2 / §5.3 / §6 / §7 / §11 覆盖点齐全。
3. 四份文档中英对应、无 v1 残留叙述。
4. 文档代码示例与真实 API 签名一致（例如 `Query` 返回 `IEntityQuery`、`RW<T>()` 需结构实例、`RegisterSystem` 返回 `SystemRegistration`）。
5. 结论必须为「文档评审通过」或列出具体缺口（缺口回到 Step 1-4 修复后重新评审）。

## Self-Review 记录

（计划作者自查 + 执行/评审期间的修订记录；后续修订按序号追加。）

1. **spec 覆盖**：§8 CommandBuffer 全部 API（`CreateCommandBuffer` / `CreateEntity(mask)` 占位 / `CreateComponent<T>(e[, value])` 三 kind / `DestroyComponent<T>(e)` / `SetMask(e, mask)` / `DestroyEntity(e)` / 手动 `Playback` / Dispose 丢弃 / 记录期零迁移）→ Task 2+3；§3.1/3.3 `SetMask` 迁移（mask 参与结构键）→ Task 1；§9 交付表第 6 行文档评审 → Task 4；§11 破坏性变更 → Task 4 Step 3(h)/4。
2. **任务拆分相对 handoff 的调整**：handoff 建议「Task 1 记录层 → Task 2 Playback」。本计划把 1c 遗留的 `SetMask` 迁移独立为 Task 1，因为 `cmd.SetMask` 的 handler 依赖 `Entity.SetMask`，若先写记录层会出现引用未实现 API 的编译红灯；调整后每个任务结束都可编译且全绿（513→521→529→541）。
3. **占位实体表示决策**：复用 `Entity` 结构（null location + 合成 entityId），不新增 `CommandEntity` 句柄类型——`cmd.CreateComponent<T>(e)` 因此可直接接收真实 `Entity` 与占位实体，API 面最小；占位实体 `IsValid == false`，不会与真实实体混淆。id 用进程级静态计数器（`1UL << 63` 起）避免跨 buffer 同号误判；World 单线程，计数器不加锁。
4. **不提供公开 `Resolve` API**：spec §8 未列出；占位句柄定位为批次内私有。需要真实句柄的调用方先用 `world.CreateEntity()` 再记录组件命令（Task 3 测试覆盖该路径）。若未来需要，可加 `TryResolve`，不属本阶段。
5. **Playback 异常语义决策**：`try/finally` 保证记录始终消费——异常时已部分应用，保留记录重放会二次应用；清空后 buffer 可复用，异常向上传播。Task 3 用「已销毁真实目标」与「先销毁占位实体再写组件」两个测试钉死。
6. **`SetMask` 不产生组件事件（已知限制）**：迁移不经过 `ComponentManager.KernelObserver`，事件驱动 collector 的 `Matching`/`Clashing` 不会因 mask 变化刷新；`IEntityQuery.Refresh` 与 `WithMask` matcher 正常。Task 1 用 Query 测试钉死，Task 4 文档说明。若未来需要 collector 感知 mask 变化，需要新的「结构迁移」信号，属新特性。
7. **不采用 `ICommandBuffer` / `CommandBufferFlag`**：旧探索分支（`origin/cursor/archetype-chunk-storage-design-2f9c` 的 `c5182e1`）实现与 spec §8 不同（无占位实体、有自动 Playback flag）；spec 是唯一权威，明确不引入接口与 flag，保持 `public sealed class CommandBuffer`。
8. **Dispose 不池化**：spec §8 只要求「丢弃记录并释放资源」；本阶段不引入池化（YAGNI），`Dispose` 仅清空集合并标记 disposed。
9. **类型一致性**：Task 2/3 的 `CommandBuffer` 成员签名与 Task 3 测试调用一致（`CreateEntity(ulong = ulong.MaxValue)`、`CreateComponent<T>(Entity)`、`CreateComponent<T>(Entity, T)`、`DestroyComponent<T>(Entity)`、`SetMask(Entity, ulong)`、`DestroyEntity(Entity)`、`Playback()`、`Dispose()`）；`Entity.SetMask(ulong)` 与 `ComponentOrchestrator.SetMask(ulong, ulong)` 参数顺序一致；测试计数 513→521→529→541 全链路一致。
10. **Placeholder 扫描**：各 Task 的步骤均含完整代码与完整命令，无 TBD / 「类似上文」；Task 4 文档内容以「章节 + 要点 + 可复制代码块」给出，避免文档任务留白。
11. **质量审查修订（Task 2，提交后）**：质量审查用变异测试发现 `Recording_RejectsPlaceholderFromAnotherBuffer` 未真正钉死「全局占位 id 计数器」设计——原测试里 `second` 从未创建占位实体，其 `m_placeholders` 为空，拒绝仅靠 `IsValid == false`；把计数器改为每 buffer 实例字段后测试仍全绿。计划已把该测试改为两个 buffer 各创建一个占位实体，并断言 `firstPlaceholder.EntityId != secondPlaceholder.EntityId`（每 buffer 计数器下两者同号，断言直接失败；同时 `second` 会误收 `first` 的占位实体，第二个断言也会失败）。审查的两条 Minor 记录：`EnsureOpen` 在非 `CreateEntity` 方法上会被 `EnsureTarget` 掩盖（保留纵深防御，不改）；`CommandKind` 在 Task 2 只写不读，Task 3 的 `Playback` 会读它（`!= CreateEntity` 才解析），故保留。
12. **质量审查记录（Task 3）**：结论 Ready to merge: Yes，无 Critical/Important。8 个变异全部按要求执行：`EnsureOpen` 缺失、去掉 `try/finally`、`Resolve` 跳过 `m_resolved`、kind 判断反转、`s_createEntityHandler` 不写映射、不清 `m_placeholders`、逆序应用——均被具名测试杀死；仅「不清 `m_resolved`」存活，属内存卫生问题（占位 id 全局唯一且不复用，陈旧映射不可达；`finally` 与 `Dispose` 均清空，长期复用的 buffer 无线性增长风险）。Minor 记录：`Playback` XML 文档可补一句「非事务：失败记录之前的命令已生效」；`cmd.SetMask` 的占位实体路径与 disposed/foreign 断言未覆盖（handler 本身已被 `Playback_SetMask_MigratesExistingAndCreatedEntities` 钉死）；`Resolve` 对 2^63 id 区间的依赖可加注释。均不影响本阶段验收，留待需要时加固。
13. **质量审查修订（Task 1，提交后）**：质量审查以变异测试发现 `SetMask_ToSameMask_DoesNotMigrate` 无法杀死「删除同 mask 提前返回」变异——同 mask 时 `GetOrCreate` 返回同一 `Structure`，`Assert.AreSame` 仍通过。审查建议补 `Assert.IsNull(structure.SpareSetOrNull)`，但实现者实测该断言在原测试（实体先加 `Position`）下**带正确实现也失败**：`AddDenseComponent` 迁移时 `MoveDiscreteTo` 已经过 `target.SpareSet` 给该结构分配了容器。修正为同时删除测试里的 `entity.CreateComponent(new Position { X = 1 });`——结构保持 `([], 0b01)`、`SpareSetOrNull` 为 null，无提前返回时 `MoveDiscreteTo` 才会分配容器，断言因而能杀死变异（实现者已实测：正确实现 8/8 通过；注释掉提前返回后该测试失败）。同时把 `Entity.SetMask` 的 XML 文档异常说明补充「busy 实体」（与 orchestrator 文档一致）。审查建议的重复 `RequireLocation` 防御保留（与既有写 API 模式一致，属纵深防御）；迁移序列的第三份拷贝按计划绑定形态保留，若出现第四个调用方再抽 `MigrateRow` 私有方法。
14. **文档修订（Task 4，文档评审前）**：实现者发现 Task 4 Step 3(c) 的「三类都会调用 `OnCreate` / `OnDestroy`（Tag 用默认空实现）」与内核实际行为不符——`AddTagComponent` / `RemoveTagComponent` 只翻转位图，不调用 `ComponentHookDispatcher`。该偏差是 Plan 1b/1c 的有意决定（Plan 1c Self-Review 第 10 条：spec §2.1 要求三类均调用 hook，内核有意跳过 Tag；Tag 默认空实现，行为等价；若未来需要 Tag 自定义 hook 需在内核补 `InvokeTagCreate/InvokeTagDestroy`）。文档必须描述真实行为，故计划改为「Dense / Discrete 在增删时调用 `OnCreate` / `OnDestroy`，Tag 使用默认空实现且内核不调用 tag hook」，并在文档修订提交中同步中英文。
