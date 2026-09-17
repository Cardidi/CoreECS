# CoreECS v2 Phase 4 调度 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 交付 spec 第 6 节（系统调度）的全部三个任务：Task 1 = 注册 API 与组树（`GroupInsertMode`、`RegisterGroup`（含嵌套与 Early/Later）、`RegisterSystem<T>([group])`、Before/After 锚点声明与存储、注册校验）；Task 2（已追加，见下文）= `TeardownSystems`（`BeginTick`）展平组树 → 解析 Before/After 约束 → 稳定拓扑排序 → 重建 `m_systems` 执行序列，成环 `Log.Err` + 回退展平序；Task 3（已追加，见下文）= tick 内注册图变更在下一个 `BeginTick` 统一收敛（成对变更折叠为最终图状态、实例复用/重定位、排队取消），当前 tick 已排定序列用快照钉死不受影响。

**Architecture:** 在 `SystemManager` 内新增 internal 注册树 `SystemSchedule`（隐式根组 + 组/系统混排子节点 + 声明式锚点），公开 fluent 句柄 `SystemRegistration` / `GroupRegistration` 负责收集锚点；`World` 暴露 `RegisterGroup` / `RegisterSystem<T>(group)`。Task 1 中执行顺序仍沿用 v1 的 `m_systems` 注册顺序（树仅元数据）；Task 2 起由 `SystemSchedule.BuildExecutionOrder()` 在 `TeardownSystems` 重建 `m_systems`（复用实例、不重建），无锚点时输出与 v1 注册序一致；Task 3 收敛 tick 内注册图变更（成对变更折叠、实例复用、序列快照），既有 490 个测试必须保持全绿。

**Tech Stack:** C# 9（`LangVersion 9`）、`net8.0` + `netstandard2.1`、NUnit 3.14、`dotnet test --filter`

**Spec:** `docs/superpowers/specs/2026-09-17-coreecs-v2-design.md`（6.1 / 6.2 / 6.3、已决事项 8；展平 / 拓扑排序 / 成环回退属 Task 2，tick 内收敛属 Task 3）

**Handoff:** `docs/superpowers/plans/2026-09-17-coreecs-v2-handoff.md`（第 2 节 Phase 4 范围与"计划编写子代理单次只写 1-2 个任务"约定）

---

## File Structure

| 文件 | 职责 |
|---|---|
| `ECS/Defines/GroupInsertMode.cs` | Task 1 新增：`public enum GroupInsertMode { Early, Later }`（同层插入锚定；与 `ISystem` / `IEntityQuery` 等同放 `CoreECS.Defines`） |
| `ECS/Managers/SystemSchedule.cs` | Task 1 新增：internal 注册树——`SystemSchedule`（隐式根 + 组名索引 + 系统类型索引）、`SystemScheduleNode` / `SystemGroupNode` / `SystemEntryNode`、`SystemAnchor` / `SystemAnchorKind` |
| `ECS/SystemRegistration.cs` | Task 1 新增：`public sealed class SystemRegistration`——系统注册句柄，`.Before<T>()` / `.After<T>()`（系统类型锚点）与 `.Before(string)` / `.After(string)`（组名锚点） |
| `ECS/GroupRegistration.cs` | Task 1 新增：`public sealed class GroupRegistration`——组注册句柄，同一组锚点 API |
| `ECS/Managers/SystemManager.cs` | Task 1 修改：新增 `m_schedule` 与 internal `Schedule`；`RegisterSystem` 增加可选组参数并返回句柄；新增 `RegisterGroup` 两个重载、`ResolveGroup`、`AddSystemAnchor` / `AddGroupAnchor`；`UnregisterSystem` / `CleanupSystems` / `OnWorldEnded` 同步移除树节点 |
| `ECS/World.cs` | Task 1 修改：`RegisterSystem(Type)` / `RegisterSystem<T>(string group = null)` 返回句柄；新增 `RegisterGroup(name[, mode])` 与 `RegisterGroup(name, parentName[, mode])` |
| `Test/SystemGroupRegistrationTestUnit.cs` | Task 1 新增：注册 API 与组树契约测试（16 个） |
| `ECS/Managers/SystemSchedule.cs` | Task 2 修改：新增 `BuildExecutionOrder()`——DFS 展平（组内容落在组位置）、锚点解析（系统类型 / 组子树、跨层级、前向引用、无法解析 `Log.Err` 并忽略、自环跳过）、稳定拓扑排序（每步取展平索引最小的入度 0 节点）、成环 `Log.Err` + 全量回退展平序 |
| `ECS/Managers/SystemManager.cs` | Task 2 修改：`TeardownSystems` 在实例化排队系统后调用 `_rebuildExecutionOrder()`，按解析结果映射既有实例重建 `m_systems`；`OnSystemTeardown` 发射位置与语义不变 |
| `Test/SystemOrderingTestUnit.cs` | Task 2 新增：执行顺序契约测试（18 个）——展平 / Early / After / Before / 组锚点 / 跨层级 / 前向引用 / 无法解析 / 成环回退 / 稳定 tie-break / tick 执行序 |
| `ECS/Managers/SystemManager.cs` | Task 3 修改：tick 内注册图收敛——新增 `m_cancelledAdds` 标记（仅排队系统的注销 = 取消待添加，且不改变 `m_addSystems` 队列结构）；`RegisterSystem` 不可变更分支取消待移除并按请求重定位（`_cancelPendingRemoval` / `_repositionSystem`）；`UnregisterSystem` 对仅排队系统取消待添加；`TeardownSystems` 实例化循环跳过已取消的排队系统；`ExecuteSystems` 对当前 tick 序列做快照；`AddSystemAnchor` / `AddGroupAnchor` 补 `!m_shutdown` 断言；`OnWorldEnded` 清理标记 |
| `Test/SystemConvergenceTestUnit.cs` | Task 3 新增：tick 内收敛契约测试（13 个）——同 tick 成对变更折叠（注销+重注册复用实例/重定位、注册+注销取消待添加、取消后再注册、再注销）、tick 内锚点与建组、当前 tick 序列不受影响、序列快照、`OnSystemTeardown` / `OnSystemCleanup` 时序、shutdown 后过期句柄 |

测试文件统一放 `Test/`，命名 `<TypeName>TestUnit.cs`，风格与现有测试一致（classic asserts；`Test.csproj` 已通过 `<Using Include="NUnit.Framework"/>` 提供全局 using）。`ECS.csproj` 已配置 `<InternalsVisibleTo Include="Test" />`，测试可直接访问 internal `SystemSchedule` / 节点 / 锚点类型。

## 本计划范围边界

本计划覆盖 Phase 4 全部三个任务（Task 2 / Task 3 均已追加，见下文）：

- **Task 1（本次 dispatch）**：组树数据结构 + `RegisterGroup` / `RegisterSystem` 注册 + fluent 句柄链式 `Before` / `After` + 注册校验（未知组、重复组名、空名）+ 公开 API 切换；锚点只存储不解析
- **Task 2（已追加，见下文）**：`TeardownSystems`（`BeginTick`）展平组树 → 应用 Before/After 约束（跨层级、前向引用、无法解析记录错误并忽略）→ 拓扑排序 → 重建 `m_systems` 执行序列；无约束节点以注册序稳定 tie-break；成环 `Log.Err` + 回退展平序
- **Task 3（已追加，见下文）**：tick 内注册图变更在下一个 `BeginTick` 统一收敛（已注册系统复用实例重排、新增系统实例化 + `OnCreate`、注销系统 `OnDestroy` 并移除）；同一 tick 内的成对变更折叠为最终图状态（注销 + 重注册 = 取消待移除并重定位、注册 + 注销 = 取消待添加）；当前 tick 已排定序列用 `ExecuteSystems` 快照钉死不受影响

**不包括**（spec 明确后置）：World 合并与生命周期收敛（Phase 5）、CommandBuffer 与文档更新（Phase 6）。本计划不改 `SystemManager._systemPoll` / `ExecuteSystems` 的掩码过滤逻辑（`ISystem.TickGroup` 语义与 v1 完全一致；组不承载掩码）。

---

## Task 1: 注册 API 与组树（不含排序解析）

**Files:**
- Create: `ECS/Defines/GroupInsertMode.cs`
- Create: `ECS/Managers/SystemSchedule.cs`
- Create: `ECS/SystemRegistration.cs`
- Create: `ECS/GroupRegistration.cs`
- Modify: `ECS/Managers/SystemManager.cs`（字段与 internal 访问器；`RegisterSystem` 替换 `SystemManager.cs:242-266`；`UnregisterSystem` 立即分支 `SystemManager.cs:282-287`；`CleanupSystems` 出队循环 `SystemManager.cs:229-235`；`OnWorldEnded` `SystemManager.cs:324-339`）
- Modify: `ECS/World.cs`（`RegisterSystem` 段 `World.cs:215-245` 替换 + 追加 `RegisterGroup`）
- Test: `Test/SystemGroupRegistrationTestUnit.cs`（新增，16 个测试）

**前置:** 无。本次 dispatch 前实测全量 **456 passed / 0 failed**（`PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj`），双目标库构建 0 错误。

**设计说明（执行时不要改动，评审时按此核对）：**

- **绑定语义（spec 6.1 + 已决事项 8）**：组 = 纯排序桶，不承载掩码；`ISystem.TickGroup` 与 `_systemPoll` 的 `(system.TickGroup & systemMask) > 0` 过滤零改动。组可嵌套；系统与组在同一层级按注册序混排（同一 `Children` 列表）；根为隐式默认组（`SystemGroupNode.Name == null`，不可作为锚点目标，也不可被 `RegisterSystem` 的组名引用）；`GroupInsertMode.Early` = 插入当前层级最前（`Children.Insert(0, node)`），`Later` = 追加（默认）；**只有组有插入模式**，系统在其组内总是追加，精确位置用 Before/After 声明。
- **公开 API 面（绑定）**：

  ```csharp
  // World
  public SystemRegistration RegisterSystem(Type systemType);
  public SystemRegistration RegisterSystem<T>(string groupName = null) where T : class, ISystem;
  public GroupRegistration RegisterGroup(string name, GroupInsertMode mode = GroupInsertMode.Later);
  public GroupRegistration RegisterGroup(string name, string parentName, GroupInsertMode mode = GroupInsertMode.Later);
  // UnregisterSystem(Type) / UnregisterSystem<T>() 签名不变

  // SystemManager
  public SystemRegistration RegisterSystem(Type systemType, string groupName = null);
  public GroupRegistration RegisterGroup(string name, GroupInsertMode mode = GroupInsertMode.Later);
  public GroupRegistration RegisterGroup(string name, string parentName, GroupInsertMode mode = GroupInsertMode.Later);
  internal SystemSchedule Schedule { get; }   // 测试钩子，非公开 API

  // 句柄（两个类型的方法集合一致）
  public sealed class SystemRegistration
  {
      public SystemRegistration Before<T>() where T : class, ISystem;
      public SystemRegistration After<T>() where T : class, ISystem;
      public SystemRegistration Before(string groupName);
      public SystemRegistration After(string groupName);
  }
  public sealed class GroupRegistration { /* 同上，返回 GroupRegistration */ }

  // Defines
  public enum GroupInsertMode { Early, Later }
  ```

- **锚点存储（Task 2 的输入，绑定）**：每个节点（组或系统）持有 `List<SystemAnchor>`，按声明顺序保存；`SystemAnchor` = `(SystemAnchorKind Kind, Type SystemType, string GroupName)`，`Kind` 为 `Before` / `After`，两个 target 恰有一个非空。**注册时不解析、不校验锚点目标**（前向引用合法，spec 6.2）；Task 2 展平后统一解析，无法解析的约束记录错误并忽略。同一节点重复声明同向锚点不去重（Task 2 自行处理，重复边无副作用）。
- **嵌套 API 决策（实勘决定，任务文本未指定）**：嵌套通过 `RegisterGroup(name, parentName[, mode])` 重载显式表达（如 `world.RegisterGroup("Collision", "Physics")`），不提供 `.In()` 或点号路径。父组必须已注册（"注册到未注册的组名 → 抛异常"同样适用于父组）。
- **重复组名 / 非法名决策（绑定）**：重复组名抛 `InvalidOperationException("Group 'X' is already registered.")`；空字符串抛 `InvalidOperationException("Group name must not be empty.")`；null 抛 `ArgumentNullException`（`Assertion.ArgumentNotNull`）。组没有注销 API，一旦注册持续到 world 结束。
- **v1 签名决策（实勘偏差，重要）**：任务文本假设存在 v1 `RegisterSystem<T>(string tickGroup)`——实勘不存在：`TickGroup` 是 `ISystem` 的属性而不是注册参数，v1 实际签名为 `World.RegisterSystem(Type)` / `World.RegisterSystem<T>()`（均 `void`）与 `SystemManager.RegisterSystem(Type)`（`void`）。处理：**保留同名方法并新增可选组参数，返回类型从 `void` 改为注册句柄**——全部现有调用点均为语句式调用（`world.RegisterSystem<TestSystem>();`），返回值被丢弃，源码兼容；`RegisterSystem(Type)` 不接受组参数（根注册 + 锚点），需要非泛型 + 组时用 manager 层 `RegisterSystem(Type, string)`。
- **树与执行顺序解耦（绑定）**：Task 1 中 `m_systems` 仍是唯一执行序列，`TeardownSystems` 与 `ExecuteSystems` 不改；`SystemSchedule` 只记录注册结构。`Early` 只改变树内位置，不改变 Task 1 的执行顺序（Task 2 展平后生效）。
- **注册失败的一致性（绑定）**：`RegisterSystem` 先解析组（未注册立即抛，且不实例化系统、不污染树），再走 v1 的实例化 / 入队路径；树节点在实例化成功后（可变更分支）或入队时（不可变更分支）才添加。重复系统类型在可变更分支由 `_instantSystem` 的 `Assertion.IsFalse(m_systemTransformer.ContainsKey(...))` 抛出（v1 行为），此时树未被污染；不可变更分支的重复注册保持 v1 静默忽略，树也不重复添加。系统注销后同 tick 内重复注册仍按 v1 忽略（`m_systemTransformer` 仍含该类型），树保持无节点，与 v1 最终状态一致。
- **注销 / 清理的树同步（新增行为）**：`UnregisterSystem` 立即分支、`CleanupSystems` 出队循环用 `m_schedule.RemoveSystem(type)` 移除系统节点；`OnWorldEnded` 在销毁全部系统后调用 `m_schedule.ClearSystems()`（一并覆盖 `m_addSystems` 中被清空的排队系统；world 关停时不可能处于 ticking，排队系统仅在 ticking 期间产生，`ClearSystems` 是防御性收口）。组节点不删除。由 `UnregisterSystem_RemovesNodeFromTree`、`UnregisterSystem_DuringTick_RemovesNodeAtCleanup`、`Shutdown_ClearsSystemNodesAndKeepsGroups` 与 `RegisterSystem_DuringTick_AddsNodeAndInstantiatesAtNextBeginTick` 钉死（评审修订补充）。
- **文件结构理由**：树模型独立成 `ECS/Managers/SystemSchedule.cs`（`SystemManager.cs` 已 361 行，Task 2 还要加展平 + 拓扑排序，混排会过大）；两个公开句柄各自独立文件，与 `EntityMatcher` 等公开类型放 `ECS/` 根目录的惯例一致。
- **测试计数（绑定）**：基线 456 + 新增 16 = **472 passed**；过滤预期 `SystemGroupRegistrationTestUnit` 16、其余 fixture 数量不变。

- [ ] **Step 1: 写失败测试**

创建 `Test/SystemGroupRegistrationTestUnit.cs`：

```csharp
using CoreECS.Defines;
using CoreECS.Managers;

namespace CoreECS.Test
{
    /// <summary>
    /// Registration contract for the Phase 4 scheduling tree: groups are pure sort buckets
    /// (no masks) that may nest and mix with systems at the same level, the root level is an
    /// implicit default group, Early/Later control same-level insertion, and Before/After
    /// anchors are stored for later resolution (forward references allowed). Execution order
    /// resolution (flatten + topological sort) is covered by later tasks.
    /// </summary>
    [TestFixture]
    public class SystemGroupRegistrationTestUnit
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

        private SystemSchedule Schedule => _world.GetManager<SystemManager>().Schedule;

        private static string[] ChildNames(SystemGroupNode group)
        {
            var names = new List<string>();
            foreach (var child in group.Children)
            {
                if (child is SystemGroupNode childGroup) names.Add(childGroup.Name);
                else names.Add(((SystemEntryNode)child).SystemType.Name);
            }

            return names.ToArray();
        }

        [Test]
        public void RegisterGroup_RootGroup_IsChildOfImplicitRoot()
        {
            _world.RegisterGroup("Physics");

            var root = Schedule.Root;
            var physics = Schedule.FindGroup("Physics");

            Assert.IsNull(root.Name);
            Assert.IsNotNull(physics);
            Assert.AreSame(root, physics.Parent);
            CollectionAssert.AreEqual(new[] { "Physics" }, ChildNames(root));
        }

        [Test]
        public void RegisterGroup_WithParent_NestsUnderParentGroup()
        {
            _world.RegisterGroup("Physics");
            _world.RegisterGroup("Collision", "Physics");

            var physics = Schedule.FindGroup("Physics");
            var collision = Schedule.FindGroup("Collision");

            Assert.IsNotNull(physics);
            Assert.IsNotNull(collision);
            Assert.AreSame(physics, collision.Parent);
            CollectionAssert.AreEqual(new[] { "Collision" }, ChildNames(physics));
            CollectionAssert.AreEqual(new[] { "Physics" }, ChildNames(Schedule.Root));
        }

        [Test]
        public void RegisterGroup_NestedThreeLevels_PreservesHierarchy()
        {
            _world.RegisterGroup("A");
            _world.RegisterGroup("B", "A");
            _world.RegisterGroup("C", "B");

            var a = Schedule.FindGroup("A");
            var b = Schedule.FindGroup("B");
            var c = Schedule.FindGroup("C");

            Assert.AreSame(Schedule.Root, a.Parent);
            Assert.AreSame(a, b.Parent);
            Assert.AreSame(b, c.Parent);
            CollectionAssert.AreEqual(new[] { "B" }, ChildNames(a));
            CollectionAssert.AreEqual(new[] { "C" }, ChildNames(b));
        }

        [Test]
        public void RegisterGroup_EarlyAndLater_ControlPositionAmongSameLevelSiblings()
        {
            _world.RegisterSystem<InputSystem>();
            _world.RegisterSystem<MovementSystem>();
            _world.RegisterGroup("LaterGroup");
            _world.RegisterGroup("EarlyGroup", GroupInsertMode.Early);

            CollectionAssert.AreEqual(
                new[] { "EarlyGroup", "InputSystem", "MovementSystem", "LaterGroup" },
                ChildNames(Schedule.Root));
        }

        [Test]
        public void RegisterGroup_InvalidNameOrDuplicate_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _world.RegisterGroup(null));

            var emptyEx = Assert.Throws<InvalidOperationException>(() => _world.RegisterGroup(string.Empty));
            Assert.AreEqual("Group name must not be empty.", emptyEx!.Message);

            _world.RegisterGroup("Physics");
            var duplicateEx = Assert.Throws<InvalidOperationException>(() => _world.RegisterGroup("Physics"));
            Assert.AreEqual("Group 'Physics' is already registered.", duplicateEx!.Message);
        }

        [Test]
        public void RegisterGroup_WithUnknownParent_Throws()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => _world.RegisterGroup("Collision", "Physics"));

            Assert.AreEqual("Group 'Physics' is not registered.", ex!.Message);
            Assert.IsNull(Schedule.FindGroup("Collision"));
        }

        [Test]
        public void RegisterSystem_Placement_UsesGroupOrRoot()
        {
            _world.RegisterGroup("Gameplay");
            _world.RegisterSystem<InputSystem>();
            _world.RegisterSystem<MovementSystem>("Gameplay");

            var rootNode = Schedule.FindSystem(typeof(InputSystem));
            var groupNode = Schedule.FindSystem(typeof(MovementSystem));

            Assert.AreSame(Schedule.Root, rootNode.Parent);
            Assert.AreSame(Schedule.FindGroup("Gameplay"), groupNode.Parent);
            CollectionAssert.AreEqual(new[] { "Gameplay", "InputSystem" }, ChildNames(Schedule.Root));
            CollectionAssert.AreEqual(new[] { "MovementSystem" }, ChildNames(Schedule.FindGroup("Gameplay")));
        }

        [Test]
        public void RegisterSystem_WithUnknownGroup_Throws()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => _world.RegisterSystem<InputSystem>("Gameplay"));

            Assert.AreEqual("Group 'Gameplay' is not registered.", ex!.Message);
            Assert.IsNull(Schedule.FindSystem(typeof(InputSystem)));
            Assert.IsNull(_world.FindSystem<InputSystem>());
        }

        [Test]
        public void RegistrationAnchors_AllowForwardReferencesAndCrossLevelTargets()
        {
            _world.RegisterGroup("Gameplay");
            _world.RegisterGroup("Physics");

            _world.RegisterSystem<InputSystem>("Gameplay")
                .Before<MovementSystem>()
                .After("Physics");

            var inputNode = Schedule.FindSystem(typeof(InputSystem));
            Assert.AreEqual(2, inputNode.Anchors.Count);
            Assert.AreEqual(SystemAnchorKind.Before, inputNode.Anchors[0].Kind);
            Assert.AreEqual(typeof(MovementSystem), inputNode.Anchors[0].SystemType);
            Assert.IsNull(inputNode.Anchors[0].GroupName);
            Assert.AreEqual(SystemAnchorKind.After, inputNode.Anchors[1].Kind);
            Assert.IsNull(inputNode.Anchors[1].SystemType);
            Assert.AreEqual("Physics", inputNode.Anchors[1].GroupName);

            // Forward reference: MovementSystem registers later without disturbing the stored anchor
            _world.RegisterSystem<MovementSystem>("Gameplay");

            Assert.AreSame(inputNode, Schedule.FindSystem(typeof(InputSystem)));
            Assert.AreEqual(typeof(MovementSystem), inputNode.Anchors[0].SystemType);
        }

        [Test]
        public void RegisterGroup_AnchorsAreStoredForSystemTypesAndGroupNames()
        {
            _world.RegisterGroup("Gameplay");
            _world.RegisterSystem<CleanupSystem>();

            _world.RegisterGroup("Render").After("Gameplay").Before<CleanupSystem>();

            var renderNode = Schedule.FindGroup("Render");
            Assert.AreEqual(2, renderNode.Anchors.Count);
            Assert.AreEqual(SystemAnchorKind.After, renderNode.Anchors[0].Kind);
            Assert.AreEqual("Gameplay", renderNode.Anchors[0].GroupName);
            Assert.AreEqual(SystemAnchorKind.Before, renderNode.Anchors[1].Kind);
            Assert.AreEqual(typeof(CleanupSystem), renderNode.Anchors[1].SystemType);
        }

        [Test]
        public void RegistrationOrder_IsPreservedAsTreeChildOrder()
        {
            _world.RegisterGroup("First");
            _world.RegisterSystem<InputSystem>();
            _world.RegisterGroup("Second", "First");
            _world.RegisterSystem<MovementSystem>("First");
            _world.RegisterSystem<CleanupSystem>();

            CollectionAssert.AreEqual(new[] { "First", "InputSystem", "CleanupSystem" }, ChildNames(Schedule.Root));
            CollectionAssert.AreEqual(new[] { "Second", "MovementSystem" }, ChildNames(Schedule.FindGroup("First")));
        }

        [Test]
        public void RegisterSystem_LegacyCallShapes_KeepV1Behavior()
        {
            _world.RegisterSystem<LegacySystem>();
            _world.GetManager<SystemManager>().RegisterSystem(typeof(OtherLegacySystem));

            var legacy = _world.FindSystem<LegacySystem>();
            Assert.IsNotNull(legacy);
            Assert.IsTrue(legacy.OnCreateCalled);
            Assert.AreSame(Schedule.Root, Schedule.FindSystem(typeof(LegacySystem)).Parent);

            _world.BeginTick();
            _world.Tick();
            _world.EndTick();

            Assert.IsTrue(legacy.OnTickCalled);
            Assert.IsTrue(_world.FindSystem<OtherLegacySystem>().OnTickCalled);
        }

        [Test]
        public void UnregisterSystem_RemovesNodeFromTree()
        {
            _world.RegisterSystem<InputSystem>();
            Assert.IsNotNull(Schedule.FindSystem(typeof(InputSystem)));

            _world.UnregisterSystem<InputSystem>();

            Assert.IsNull(Schedule.FindSystem(typeof(InputSystem)));
            CollectionAssert.DoesNotContain(ChildNames(Schedule.Root), "InputSystem");
        }

        [Test]
        public void RegisterSystem_DuringTick_AddsNodeAndInstantiatesAtNextBeginTick()
        {
            _world.BeginTick();
            _world.RegisterSystem<InputSystem>();

            Assert.IsNotNull(Schedule.FindSystem(typeof(InputSystem)));
            Assert.IsNull(_world.FindSystem<InputSystem>());

            _world.Tick();
            _world.EndTick();

            Assert.IsNull(_world.FindSystem<InputSystem>());

            _world.BeginTick();
            Assert.IsNotNull(_world.FindSystem<InputSystem>());
            Assert.IsNotNull(Schedule.FindSystem(typeof(InputSystem)));

            _world.Tick();
            _world.EndTick();
        }

        [Test]
        public void UnregisterSystem_DuringTick_RemovesNodeAtCleanup()
        {
            _world.RegisterSystem<InputSystem>();
            _world.BeginTick();
            _world.UnregisterSystem<InputSystem>();

            Assert.IsNotNull(Schedule.FindSystem(typeof(InputSystem)));

            _world.Tick();
            _world.EndTick();

            Assert.IsNull(Schedule.FindSystem(typeof(InputSystem)));
            CollectionAssert.DoesNotContain(ChildNames(Schedule.Root), "InputSystem");
        }

        [Test]
        public void Shutdown_ClearsSystemNodesAndKeepsGroups()
        {
            _world.RegisterGroup("Physics");
            _world.RegisterSystem<InputSystem>("Physics");
            var schedule = Schedule;

            _world.Shutdown();

            Assert.IsNull(schedule.FindSystem(typeof(InputSystem)));
            Assert.IsNotNull(schedule.FindGroup("Physics"));

            _world = null!;
        }

        private class InputSystem : ISystem
        {
            public void OnTick(ulong tickMask)
            {
            }
        }

        private class MovementSystem : ISystem
        {
            public void OnTick(ulong tickMask)
            {
            }
        }

        private class CleanupSystem : ISystem
        {
            public void OnTick(ulong tickMask)
            {
            }
        }

        private class LegacySystem : ISystem
        {
            public bool OnCreateCalled { get; private set; }

            public bool OnTickCalled { get; private set; }

            public void OnCreate()
            {
                OnCreateCalled = true;
            }

            public void OnTick(ulong tickMask)
            {
                OnTickCalled = true;
            }
        }

        private class OtherLegacySystem : ISystem
        {
            public bool OnTickCalled { get; private set; }

            public void OnTick(ulong tickMask)
            {
                OnTickCalled = true;
            }
        }
    }
}
```

- [ ] **Step 2: 运行过滤测试，确认失败（红灯）**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --filter FullyQualifiedName~SystemGroupRegistrationTestUnit`
Expected: **FAIL（构建失败，0 个测试执行）**——测试先行引用尚未存在的 API，典型错误：
`error CS1061: 'World' does not contain a definition for 'RegisterGroup'` / `error CS0246: The type or namespace name 'SystemSchedule' could not be found`（以及 `SystemGroupNode` / `SystemEntryNode` / `SystemAnchorKind` / `Schedule` 同类错误）。

- [ ] **Step 3: 新增 `GroupInsertMode` 定义**

创建 `ECS/Defines/GroupInsertMode.cs`：

```csharp
namespace CoreECS.Defines
{
    /// <summary>
    /// Controls where a newly registered group is inserted among its same-level siblings.
    /// </summary>
    public enum GroupInsertMode
    {
        /// <summary>Insert at the front of the current level.</summary>
        Early = 0,

        /// <summary>Append to the current level (default).</summary>
        Later = 1,
    }
}
```

- [ ] **Step 4: 新增 internal 注册树 `SystemSchedule`**

创建 `ECS/Managers/SystemSchedule.cs`：

```csharp
using System;
using System.Collections.Generic;
using CoreECS.Defines;

namespace CoreECS.Managers
{
    /// <summary>
    /// Registration tree of groups and systems. The root node is the implicit default
    /// group; groups may nest and may be mixed with systems in registration order.
    /// This type only stores the registration structure and the declared Before / After
    /// anchors; flattening and execution order resolution are layered on top in later tasks.
    /// </summary>
    internal sealed class SystemSchedule
    {
        /// <summary>Gets the implicit root group that owns root-level registrations.</summary>
        public SystemGroupNode Root { get; } = new SystemGroupNode(null, null);

        /// <summary>Groups indexed by their unique name.</summary>
        private readonly Dictionary<string, SystemGroupNode> m_groups = new Dictionary<string, SystemGroupNode>();

        /// <summary>System entry nodes indexed by system type.</summary>
        private readonly Dictionary<Type, SystemEntryNode> m_systems = new Dictionary<Type, SystemEntryNode>();

        /// <summary>
        /// Finds a registered group by name.
        /// </summary>
        /// <param name="name">Group name; null returns null.</param>
        /// <returns>The group node, or null when no group with the name is registered.</returns>
        public SystemGroupNode FindGroup(string name)
        {
            if (name == null) return null;
            return m_groups.TryGetValue(name, out var group) ? group : null;
        }

        /// <summary>
        /// Finds the registration node of a registered system.
        /// </summary>
        /// <param name="systemType">System type to look up.</param>
        /// <returns>The system entry node, or null when the system is not registered.</returns>
        public SystemEntryNode FindSystem(Type systemType)
        {
            return m_systems.TryGetValue(systemType, out var system) ? system : null;
        }

        /// <summary>
        /// Adds a group under the specified parent. Early inserts at the front of the
        /// parent's children, Later appends; children mix groups and systems in
        /// registration order.
        /// </summary>
        /// <param name="name">Unique group name.</param>
        /// <param name="parent">Parent group node.</param>
        /// <param name="mode">Insertion position among the parent's children.</param>
        /// <returns>The created group node.</returns>
        public SystemGroupNode AddGroup(string name, SystemGroupNode parent, GroupInsertMode mode)
        {
            var node = new SystemGroupNode(name, parent);
            if (mode == GroupInsertMode.Early) parent.Children.Insert(0, node);
            else parent.Children.Add(node);

            m_groups.Add(name, node);
            return node;
        }

        /// <summary>
        /// Adds a system to a group. Systems always append at their level; precise
        /// placement is expressed with Before / After anchors.
        /// </summary>
        /// <param name="systemType">System type being registered.</param>
        /// <param name="group">Group that owns the system.</param>
        /// <returns>The created system entry node.</returns>
        public SystemEntryNode AddSystem(Type systemType, SystemGroupNode group)
        {
            var node = new SystemEntryNode(systemType, group);
            group.Children.Add(node);
            m_systems.Add(systemType, node);
            return node;
        }

        /// <summary>
        /// Removes a system from the tree. Does nothing when the system is not registered.
        /// </summary>
        /// <param name="systemType">System type to remove.</param>
        public void RemoveSystem(Type systemType)
        {
            if (!m_systems.TryGetValue(systemType, out var node)) return;

            m_systems.Remove(systemType);
            node.Parent.Children.Remove(node);
        }

        /// <summary>
        /// Removes every system entry from the tree. Used when the world ends; groups are
        /// kept because there is no unregister-group API.
        /// </summary>
        public void ClearSystems()
        {
            foreach (var pair in m_systems)
            {
                pair.Value.Parent.Children.Remove(pair.Value);
            }

            m_systems.Clear();
        }
    }

    /// <summary>
    /// Base node of the schedule tree: a group or a system entry. Nodes carry the
    /// Before / After anchors declared at registration time; anchors may reference
    /// targets that are not registered yet and are resolved when the order is rebuilt.
    /// </summary>
    internal abstract class SystemScheduleNode
    {
        /// <summary>Gets the group that owns this node.</summary>
        public SystemGroupNode Parent { get; }

        /// <summary>Gets the anchors declared for this node, in declaration order.</summary>
        public List<SystemAnchor> Anchors { get; } = new List<SystemAnchor>();

        /// <summary>Creates a node owned by the specified group.</summary>
        /// <param name="parent">Owning group.</param>
        protected SystemScheduleNode(SystemGroupNode parent)
        {
            Parent = parent;
        }
    }

    /// <summary>
    /// A group node: a pure sort bucket without masks. Children are groups and systems
    /// mixed in registration order (Early inserts at the front, Later appends).
    /// </summary>
    internal sealed class SystemGroupNode : SystemScheduleNode
    {
        /// <summary>Gets the group name; null for the implicit root group.</summary>
        public string Name { get; }

        /// <summary>Gets the child nodes in registration order.</summary>
        public List<SystemScheduleNode> Children { get; } = new List<SystemScheduleNode>();

        /// <summary>Creates a group node.</summary>
        /// <param name="name">Group name; null for the implicit root.</param>
        /// <param name="parent">Owning group; null for the implicit root.</param>
        public SystemGroupNode(string name, SystemGroupNode parent) : base(parent)
        {
            Name = name;
        }
    }

    /// <summary>A system entry node: a leaf that references the registered system type.</summary>
    internal sealed class SystemEntryNode : SystemScheduleNode
    {
        /// <summary>Gets the registered system type.</summary>
        public Type SystemType { get; }

        /// <summary>Creates a system entry node.</summary>
        /// <param name="systemType">Registered system type.</param>
        /// <param name="parent">Owning group.</param>
        public SystemEntryNode(Type systemType, SystemGroupNode parent) : base(parent)
        {
            SystemType = systemType;
        }
    }

    /// <summary>Anchor direction of a declared scheduling constraint.</summary>
    internal enum SystemAnchorKind
    {
        /// <summary>The node must run before the anchor target.</summary>
        Before = 0,

        /// <summary>The node must run after the anchor target.</summary>
        After = 1,
    }

    /// <summary>
    /// A declared Before / After anchor. Exactly one of <see cref="SystemType"/> and
    /// <see cref="GroupName"/> is set: a system-type anchor or a group-name anchor.
    /// Anchors are resolved when the execution order is rebuilt, so forward references
    /// are allowed.
    /// </summary>
    internal readonly struct SystemAnchor
    {
        /// <summary>Gets the anchor direction.</summary>
        public SystemAnchorKind Kind { get; }

        /// <summary>Gets the anchor system type; null for group-name anchors.</summary>
        public Type SystemType { get; }

        /// <summary>Gets the anchor group name; null for system-type anchors.</summary>
        public string GroupName { get; }

        /// <summary>Creates an anchor.</summary>
        /// <param name="kind">Anchor direction.</param>
        /// <param name="systemType">Anchor system type; null for group-name anchors.</param>
        /// <param name="groupName">Anchor group name; null for system-type anchors.</param>
        public SystemAnchor(SystemAnchorKind kind, Type systemType, string groupName)
        {
            Kind = kind;
            SystemType = systemType;
            GroupName = groupName;
        }
    }
}
```

- [ ] **Step 5: 新增公开句柄 `SystemRegistration` / `GroupRegistration`**

创建 `ECS/SystemRegistration.cs`：

```csharp
using System;
using CoreECS.Defines;
using CoreECS.Managers;
using CoreECS.Utils;

namespace CoreECS
{
    /// <summary>
    /// Fluent registration handle for a system. Declares Before / After anchors relative
    /// to other systems or registered groups; anchors are resolved when the execution
    /// order is rebuilt, so forward references (targets registered later) are allowed.
    /// </summary>
    public sealed class SystemRegistration
    {
        /// <summary>Manager that owns the registered system.</summary>
        private readonly SystemManager m_manager;

        /// <summary>Registered system type.</summary>
        private readonly Type m_systemType;

        /// <summary>Creates a handle for a registered system.</summary>
        /// <param name="manager">Manager owning the system.</param>
        /// <param name="systemType">Registered system type.</param>
        internal SystemRegistration(SystemManager manager, Type systemType)
        {
            m_manager = manager;
            m_systemType = systemType;
        }

        /// <summary>Declares that this system runs before the specified system type.</summary>
        /// <typeparam name="T">Anchor system type; may be registered later.</typeparam>
        /// <returns>This handle for chaining.</returns>
        public SystemRegistration Before<T>() where T : class, ISystem
        {
            m_manager.AddSystemAnchor(m_systemType, new SystemAnchor(SystemAnchorKind.Before, typeof(T), null));
            return this;
        }

        /// <summary>Declares that this system runs after the specified system type.</summary>
        /// <typeparam name="T">Anchor system type; may be registered later.</typeparam>
        /// <returns>This handle for chaining.</returns>
        public SystemRegistration After<T>() where T : class, ISystem
        {
            m_manager.AddSystemAnchor(m_systemType, new SystemAnchor(SystemAnchorKind.After, typeof(T), null));
            return this;
        }

        /// <summary>Declares that this system runs before the specified group.</summary>
        /// <param name="groupName">Anchor group name; may be registered later.</param>
        /// <returns>This handle for chaining.</returns>
        public SystemRegistration Before(string groupName)
        {
            Assertion.ArgumentNotNull(groupName, nameof(groupName));
            m_manager.AddSystemAnchor(m_systemType, new SystemAnchor(SystemAnchorKind.Before, null, groupName));
            return this;
        }

        /// <summary>Declares that this system runs after the specified group.</summary>
        /// <param name="groupName">Anchor group name; may be registered later.</param>
        /// <returns>This handle for chaining.</returns>
        public SystemRegistration After(string groupName)
        {
            Assertion.ArgumentNotNull(groupName, nameof(groupName));
            m_manager.AddSystemAnchor(m_systemType, new SystemAnchor(SystemAnchorKind.After, null, groupName));
            return this;
        }
    }
}
```

创建 `ECS/GroupRegistration.cs`：

```csharp
using System;
using CoreECS.Defines;
using CoreECS.Managers;
using CoreECS.Utils;

namespace CoreECS
{
    /// <summary>
    /// Fluent registration handle for a group. Declares Before / After anchors relative
    /// to systems or other groups; anchors are resolved when the execution order is
    /// rebuilt, so forward references (targets registered later) are allowed.
    /// </summary>
    public sealed class GroupRegistration
    {
        /// <summary>Manager that owns the registered group.</summary>
        private readonly SystemManager m_manager;

        /// <summary>Registered group name.</summary>
        private readonly string m_groupName;

        /// <summary>Creates a handle for a registered group.</summary>
        /// <param name="manager">Manager owning the group.</param>
        /// <param name="groupName">Registered group name.</param>
        internal GroupRegistration(SystemManager manager, string groupName)
        {
            m_manager = manager;
            m_groupName = groupName;
        }

        /// <summary>Declares that this group runs before the specified system type.</summary>
        /// <typeparam name="T">Anchor system type; may be registered later.</typeparam>
        /// <returns>This handle for chaining.</returns>
        public GroupRegistration Before<T>() where T : class, ISystem
        {
            m_manager.AddGroupAnchor(m_groupName, new SystemAnchor(SystemAnchorKind.Before, typeof(T), null));
            return this;
        }

        /// <summary>Declares that this group runs after the specified system type.</summary>
        /// <typeparam name="T">Anchor system type; may be registered later.</typeparam>
        /// <returns>This handle for chaining.</returns>
        public GroupRegistration After<T>() where T : class, ISystem
        {
            m_manager.AddGroupAnchor(m_groupName, new SystemAnchor(SystemAnchorKind.After, typeof(T), null));
            return this;
        }

        /// <summary>Declares that this group runs before the specified group.</summary>
        /// <param name="groupName">Anchor group name; may be registered later.</param>
        /// <returns>This handle for chaining.</returns>
        public GroupRegistration Before(string groupName)
        {
            Assertion.ArgumentNotNull(groupName, nameof(groupName));
            m_manager.AddGroupAnchor(m_groupName, new SystemAnchor(SystemAnchorKind.Before, null, groupName));
            return this;
        }

        /// <summary>Declares that this group runs after the specified group.</summary>
        /// <param name="groupName">Anchor group name; may be registered later.</param>
        /// <returns>This handle for chaining.</returns>
        public GroupRegistration After(string groupName)
        {
            Assertion.ArgumentNotNull(groupName, nameof(groupName));
            m_manager.AddGroupAnchor(m_groupName, new SystemAnchor(SystemAnchorKind.After, null, groupName));
            return this;
        }
    }
}
```

- [ ] **Step 6: 修改 `SystemManager`（字段 / 注册 / 锚点 / 注销同步）**

(a) 在 `m_addSystems` 与 `m_injectionProxy` 之间新增 `m_schedule` 字段：

old（`SystemManager.cs:90-98`）：

```csharp
        /// <summary>
        /// Queue of system types to be added.
        /// </summary>
        private readonly Queue<Type> m_addSystems = new();

        /// <summary>
        /// Injection proxy for resolving system constructor dependencies.
        /// </summary>
        private readonly IInjectionProxy m_injectionProxy;
```

new：

```csharp
        /// <summary>
        /// Queue of system types to be added.
        /// </summary>
        private readonly Queue<Type> m_addSystems = new();

        /// <summary>
        /// Registration tree of groups and systems. The root node is the implicit default
        /// group; execution order resolution is layered on top of this tree in later tasks.
        /// </summary>
        private readonly SystemSchedule m_schedule = new SystemSchedule();

        /// <summary>
        /// Injection proxy for resolving system constructor dependencies.
        /// </summary>
        private readonly IInjectionProxy m_injectionProxy;
```

(b) 在 `SystemTransformer` 属性后新增 internal 测试钩子：

old（`SystemManager.cs:50-53`）：

```csharp
        /// <summary>
        /// Gets all registered systems in the manager by type.
        /// </summary>
        public IReadOnlyDictionary<Type, ISystem> SystemTransformer => m_systemTransformer;
```

new：

```csharp
        /// <summary>
        /// Gets all registered systems in the manager by type.
        /// </summary>
        public IReadOnlyDictionary<Type, ISystem> SystemTransformer => m_systemTransformer;

        /// <summary>
        /// Gets the registration tree of groups and systems.
        /// Internal test hook used to pin the registration structure; not part of the public API.
        /// </summary>
        internal SystemSchedule Schedule => m_schedule;
```

(c) 用下面整段替换旧 `RegisterSystem(Type)` 方法（`SystemManager.cs:242-266`）：

old：

```csharp
        /// <summary>
        /// Registers a system type with the manager.
        /// </summary>
        /// <param name="systemType">The type of system to register</param>
        public void RegisterSystem(Type systemType)
        {
            Assertion.IsFalse(m_shutdown, "SystemManager has already shutdown.");
            Assertion.IsNotNull(systemType);
            Assertion.IsParentTypeTo<ISystem>(systemType);

            if (m_changable)
            {
                var sys = _instantSystem(systemType);
                m_systemTransformer.Add(systemType, sys);
                m_systems.Add(sys);
                _createSystem(sys);
            }
            else
            {
                if (!m_systemTransformer.ContainsKey(systemType) && !m_addSystems.Contains(systemType))
                {
                    m_addSystems.Enqueue(systemType);
                }
            }
        }
```

new：

```csharp
        /// <summary>
        /// Registers a system type with the manager, optionally inside a registered group.
        /// The system is instantiated immediately when the manager accepts structural
        /// changes, otherwise it is queued until the next teardown (v1 behavior).
        /// </summary>
        /// <param name="systemType">The type of system to register</param>
        /// <param name="groupName">Name of the group to place the system in; null places it at the root level</param>
        /// <returns>A registration handle used to declare Before / After anchors</returns>
        /// <exception cref="InvalidOperationException">Thrown when the manager has shut down or the group is not registered</exception>
        public SystemRegistration RegisterSystem(Type systemType, string groupName = null)
        {
            Assertion.IsFalse(m_shutdown, "SystemManager has already shutdown.");
            Assertion.IsNotNull(systemType);
            Assertion.IsParentTypeTo<ISystem>(systemType);

            var group = ResolveGroup(groupName);

            if (m_changable)
            {
                var sys = _instantSystem(systemType);
                m_systemTransformer.Add(systemType, sys);
                m_systems.Add(sys);
                m_schedule.AddSystem(systemType, group);
                _createSystem(sys);
            }
            else
            {
                if (!m_systemTransformer.ContainsKey(systemType) && !m_addSystems.Contains(systemType))
                {
                    m_addSystems.Enqueue(systemType);
                    m_schedule.AddSystem(systemType, group);
                }
            }

            return new SystemRegistration(this, systemType);
        }

        /// <summary>
        /// Registers a group at the root level of the schedule.
        /// </summary>
        /// <param name="name">Unique group name</param>
        /// <param name="mode">Insertion position among the root children; defaults to Later (append)</param>
        /// <returns>A registration handle used to declare Before / After anchors</returns>
        /// <exception cref="InvalidOperationException">Thrown when the manager has shut down, the name is empty or the name is already registered</exception>
        public GroupRegistration RegisterGroup(string name, GroupInsertMode mode = GroupInsertMode.Later)
        {
            return RegisterGroupCore(name, null, mode);
        }

        /// <summary>
        /// Registers a nested group inside another registered group.
        /// </summary>
        /// <param name="name">Unique group name</param>
        /// <param name="parentName">Name of the already registered parent group</param>
        /// <param name="mode">Insertion position among the parent children; defaults to Later (append)</param>
        /// <returns>A registration handle used to declare Before / After anchors</returns>
        /// <exception cref="InvalidOperationException">Thrown when the manager has shut down, the name is empty, the name is already registered or the parent is not registered</exception>
        public GroupRegistration RegisterGroup(string name, string parentName, GroupInsertMode mode = GroupInsertMode.Later)
        {
            Assertion.ArgumentNotNull(parentName, nameof(parentName));
            return RegisterGroupCore(name, parentName, mode);
        }

        /// <summary>
        /// Adds a Before / After anchor to a registered system. Called by <see cref="SystemRegistration"/>.
        /// </summary>
        /// <param name="systemType">Registered system type</param>
        /// <param name="anchor">Anchor to store</param>
        /// <exception cref="InvalidOperationException">Thrown when the system is not registered</exception>
        internal void AddSystemAnchor(Type systemType, SystemAnchor anchor)
        {
            var node = m_schedule.FindSystem(systemType);
            if (node == null)
                throw new InvalidOperationException($"System {systemType.FullName} is not registered.");

            node.Anchors.Add(anchor);
        }

        /// <summary>
        /// Adds a Before / After anchor to a registered group. Called by <see cref="GroupRegistration"/>.
        /// </summary>
        /// <param name="groupName">Registered group name</param>
        /// <param name="anchor">Anchor to store</param>
        /// <exception cref="InvalidOperationException">Thrown when the group is not registered</exception>
        internal void AddGroupAnchor(string groupName, SystemAnchor anchor)
        {
            var node = m_schedule.FindGroup(groupName);
            if (node == null)
                throw new InvalidOperationException($"Group '{groupName}' is not registered.");

            node.Anchors.Add(anchor);
        }

        /// <summary>
        /// Resolves a group name to its node; null resolves to the implicit root.
        /// </summary>
        /// <param name="groupName">Group name to resolve; null for the root level</param>
        /// <returns>The resolved group node</returns>
        /// <exception cref="InvalidOperationException">Thrown when the group is not registered</exception>
        private SystemGroupNode ResolveGroup(string groupName)
        {
            if (groupName == null) return m_schedule.Root;

            var group = m_schedule.FindGroup(groupName);
            if (group == null)
                throw new InvalidOperationException($"Group '{groupName}' is not registered.");

            return group;
        }

        /// <summary>
        /// Shared implementation of the group registration overloads.
        /// </summary>
        /// <param name="name">Unique group name</param>
        /// <param name="parentName">Parent group name; null registers at the root level</param>
        /// <param name="mode">Insertion position among the parent children</param>
        /// <returns>A registration handle used to declare Before / After anchors</returns>
        private GroupRegistration RegisterGroupCore(string name, string parentName, GroupInsertMode mode)
        {
            Assertion.IsFalse(m_shutdown, "SystemManager has already shutdown.");
            Assertion.ArgumentNotNull(name, nameof(name));
            Assertion.IsFalse(string.IsNullOrEmpty(name), "Group name must not be empty.");

            if (m_schedule.FindGroup(name) != null)
                throw new InvalidOperationException($"Group '{name}' is already registered.");

            var parent = ResolveGroup(parentName);
            m_schedule.AddGroup(name, parent, mode);

            return new GroupRegistration(this, name);
        }
```

(d) `UnregisterSystem` 立即分支同步移除树节点（`SystemManager.cs:282-287`）：

old：

```csharp
            if (m_changable)
            {
                m_systemTransformer.Remove(systemType);
                m_systems.Remove(sys);
                _destroySystem(sys);
            }
```

new：

```csharp
            if (m_changable)
            {
                m_systemTransformer.Remove(systemType);
                m_systems.Remove(sys);
                m_schedule.RemoveSystem(systemType);
                _destroySystem(sys);
            }
```

(e) `CleanupSystems` 出队循环同步移除树节点（`SystemManager.cs:229-237`）：

old：

```csharp
            while (m_delSystems.TryDequeue(out var type))
            {
                var sys = m_systemTransformer[type];
                m_systemTransformer.Remove(type);
                m_systems.Remove(sys);
                _destroySystem(sys);
            }
            
            m_changable = true;
```

new：

```csharp
            while (m_delSystems.TryDequeue(out var type))
            {
                var sys = m_systemTransformer[type];
                m_systemTransformer.Remove(type);
                m_systems.Remove(sys);
                m_schedule.RemoveSystem(type);
                _destroySystem(sys);
            }
            
            m_changable = true;
```

(f) `OnWorldEnded` 在销毁全部系统后清空系统节点（`SystemManager.cs:324-339`）：

old：

```csharp
        public void OnWorldEnded()
        {
            m_shutdown = true;
            
            m_addSystems.Clear();
            m_delSystems.Clear();
            foreach (var system in m_systems) m_delSystems.Enqueue(system.GetType());
            
            while (m_delSystems.TryDequeue(out var type))
            {
                var sys = m_systemTransformer[type];
                m_systemTransformer.Remove(type);
                m_systems.Remove(sys);
                _destroySystem(sys);
            }
        }
```

new：

```csharp
        public void OnWorldEnded()
        {
            m_shutdown = true;
            
            m_addSystems.Clear();
            m_delSystems.Clear();
            foreach (var system in m_systems) m_delSystems.Enqueue(system.GetType());
            
            while (m_delSystems.TryDequeue(out var type))
            {
                var sys = m_systemTransformer[type];
                m_systemTransformer.Remove(type);
                m_systems.Remove(sys);
                _destroySystem(sys);
            }

            m_schedule.ClearSystems();
        }
```

- [ ] **Step 7: 修改 `World`（公开 API）**

用下面整段替换旧 `RegisterSystem(Type)` / `RegisterSystem<T>()` 两个方法（`World.cs:215-245`）：

old：

```csharp
        /// <summary>
        /// Registers a system with the world.
        /// </summary>
        /// <param name="systemType">The type of system to register</param>
        /// <exception cref="InvalidOperationException">Thrown when system manager is not available</exception>
        public void RegisterSystem(Type systemType)
        {
            Assertion.IsTrue(Ready, "World is not ready");

            if (System == null)
                throw new InvalidOperationException("Core ECS managers are not available");
            
            System.RegisterSystem(systemType);
            
        }

        /// <summary>
        /// Registers a system with the world.
        /// </summary>
        /// <typeparam name="T">The type of system to register, must implement ISystem</typeparam>
        /// <exception cref="InvalidOperationException">Thrown when system manager is not available</exception>
        public void RegisterSystem<T>() where T : class, ISystem
        {
            Assertion.IsTrue(Ready, "World is not ready");

            if (System == null)
                throw new InvalidOperationException("Core ECS managers are not available");
            
            System.RegisterSystem(typeof(T));
            
        }
```

new：

```csharp
        /// <summary>
        /// Registers a system with the world at the root level of the schedule.
        /// </summary>
        /// <param name="systemType">The type of system to register</param>
        /// <returns>A registration handle used to declare Before / After anchors</returns>
        /// <exception cref="InvalidOperationException">Thrown when the world is not ready or system manager is not available</exception>
        public SystemRegistration RegisterSystem(Type systemType)
        {
            Assertion.IsTrue(Ready, "World is not ready");

            if (System == null)
                throw new InvalidOperationException("Core ECS managers are not available");
            
            return System.RegisterSystem(systemType);
        }

        /// <summary>
        /// Registers a system with the world, optionally inside a registered group.
        /// </summary>
        /// <typeparam name="T">The type of system to register, must implement ISystem</typeparam>
        /// <param name="groupName">Name of the group to place the system in; null places it at the root level</param>
        /// <returns>A registration handle used to declare Before / After anchors</returns>
        /// <exception cref="InvalidOperationException">Thrown when the world is not ready, system manager is not available or the group is not registered</exception>
        public SystemRegistration RegisterSystem<T>(string groupName = null) where T : class, ISystem
        {
            Assertion.IsTrue(Ready, "World is not ready");

            if (System == null)
                throw new InvalidOperationException("Core ECS managers are not available");
            
            return System.RegisterSystem(typeof(T), groupName);
        }

        /// <summary>
        /// Registers a group at the root level of the system schedule.
        /// </summary>
        /// <param name="name">Unique group name</param>
        /// <param name="mode">Insertion position among the root level; defaults to Later (append)</param>
        /// <returns>A registration handle used to declare Before / After anchors</returns>
        /// <exception cref="InvalidOperationException">Thrown when the world is not ready, system manager is not available or the name is already registered</exception>
        public GroupRegistration RegisterGroup(string name, GroupInsertMode mode = GroupInsertMode.Later)
        {
            Assertion.IsTrue(Ready, "World is not ready");

            if (System == null)
                throw new InvalidOperationException("Core ECS managers are not available");

            return System.RegisterGroup(name, mode);
        }

        /// <summary>
        /// Registers a nested group inside another registered group.
        /// </summary>
        /// <param name="name">Unique group name</param>
        /// <param name="parentName">Name of the already registered parent group</param>
        /// <param name="mode">Insertion position among the parent level; defaults to Later (append)</param>
        /// <returns>A registration handle used to declare Before / After anchors</returns>
        /// <exception cref="InvalidOperationException">Thrown when the world is not ready, system manager is not available, the name is already registered or the parent is not registered</exception>
        public GroupRegistration RegisterGroup(string name, string parentName, GroupInsertMode mode = GroupInsertMode.Later)
        {
            Assertion.IsTrue(Ready, "World is not ready");

            if (System == null)
                throw new InvalidOperationException("Core ECS managers are not available");

            return System.RegisterGroup(name, parentName, mode);
        }
```

- [ ] **Step 8: 构建 ECS 库（两个 TFM）**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet build ECS/ECS.csproj`
Expected: Build succeeded（net8.0 + netstandard2.1，0 Error）。此时测试项目也已可编译，继续下一步。

- [ ] **Step 9: 运行新增过滤测试**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --filter FullyQualifiedName~SystemGroupRegistrationTestUnit`
Expected: PASS（18 个测试，失败 0）

- [ ] **Step 10: 运行全量测试**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj`
Expected: **472 passed**（基线 456 + 新增 16），0 failed

- [ ] **Step 11: 提交**

```bash
git add ECS/Defines/GroupInsertMode.cs ECS/Managers/SystemSchedule.cs ECS/SystemRegistration.cs ECS/GroupRegistration.cs ECS/Managers/SystemManager.cs ECS/World.cs Test/SystemGroupRegistrationTestUnit.cs
git commit -m "feat(core): add system group registration api

Groups are pure sort buckets in a registration tree: groups may nest and
mix with systems at the same level, the root level is implicit, and
Early/Later control same-level insertion. RegisterSystem and RegisterGroup
return fluent handles that store Before/After anchors for later
resolution; anchors may forward-reference systems or group names. This
task only records the registration structure - flattening, topological
sort and tick-internal convergence follow in later tasks."
```

---

## Task 2: 展平 + Before/After 拓扑排序 + 成环回退

**Files:**
- Modify: `ECS/Managers/SystemSchedule.cs`（文件顶部补 `using CoreECS.Utils;`；在 `ClearSystems` 之后（`SystemSchedule.cs:95-104`）插入 `BuildExecutionOrder()` 与 6 个私有辅助）
- Modify: `ECS/Managers/SystemManager.cs`（`TeardownSystems` `SystemManager.cs:198-215` 调用新私有方法 `_rebuildExecutionOrder`；新方法紧随其后）
- Test: `Test/SystemOrderingTestUnit.cs`（新增，18 个测试）

**前置:** Task 1 已提交（`be81b99`、`0755991`、`8b0b90d`、`b959226`）；本次 dispatch 前实测全量 **472 passed / 0 failed**（`PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj`），双目标库构建 0 错误。

**设计说明（执行时不要改动，评审时按此核对）：**

- **排序职责位置（绑定）**：展平 + 锚点解析 + 拓扑排序全部放 `SystemSchedule.BuildExecutionOrder()`（返回 `List<Type>`，internal 类型上的 public 成员，测试经 `InternalsVisibleTo` 直接调用）；`SystemManager.TeardownSystems` 只负责在实例化排队系统后把结果映射回既有实例、重建 `m_systems`（`_rebuildExecutionOrder`）。`ExecuteSystems` / `_systemPoll` / `ISystem.TickGroup` 零改动，掩码过滤语义与 v1 完全一致。
- **展平规则（绑定）**：从隐式根开始 DFS，子节点按 `Children` 顺序（注册序 + `Early` / `Later` 插入结果）；组节点本身不产出执行项，只把其子树内容放在组所在位置——"父组先于其子内容"体现为子树内容的相对位置由组在父层的位置决定。产出仅 `SystemEntryNode`，组 = 纯排序桶、不承载掩码（spec 6.1）。
- **锚点语义（绑定，统一规则）**：
  - 系统节点的锚点作用于它自己；组节点的锚点作用于其整个子树（组内每个系统都受约束）。
  - 系统类型目标解析为单个系统节点；组名目标解析为**目标组子树的全体系统**（不区分组内顺序，语义不依赖最终排序结果）。`X.After(G)` 等价于"X 在 G 的每个系统之后"，`X.Before(G)` 等价于"X 在 G 的每个系统之前"。任务文本示例"node after group = node after the group's last system in flatten order"在组内无约束时与本语义等价；选"子树全体"是因为它不依赖排序中间结果（评审时按此核对）。
  - 目标解析发生在 `BuildExecutionOrder` 内（即 `BeginTick` 的 Teardown），此时树完整，前向引用自然成立；系统/组目标均查 Task 1 的 `FindSystem` / `FindGroup` 索引。
  - **自环跳过（绑定）**：subject == target 的边不添加（节点对自己"前后"无意义）。因此组内系统声明 `After("本组")` 退化为"在本组其他系统之后"，不会自锁；`groupA.Before("groupA")` 退化为 no-op。
  - **空组锚点 = no-op（绑定）**：组名存在但子树为空时不产生边、不报错（空桶不约束任何东西）。
  - 重复锚点（同向多次声明）产生重复边；入度与后继列表同步重复，拓扑结果不变（Task 1 已声明不去重）。
- **无法解析的锚点（绑定）**：`anchor.SystemType` 未注册 → `Log.Err($"System schedule anchor target '{FullName}' is not registered; constraint ignored.")`；`anchor.GroupName` 未注册 → `Log.Err($"System schedule anchor target group '{name}' is not registered; constraint ignored.")`；随后 `continue`（该条约束忽略，其余锚点照常）。测试钉死错误条数与消息子串。
- **稳定 tie-break（绑定）**：拓扑排序每步在所有"未发射且入度为 0"的节点中选**展平索引最小**者（线性扫描 `bool[] emitted`）。这产生相对展平序的字典序最小拓扑序；完全无约束时输出 == 展平序 == v1 注册序。不引入 `PriorityQueue`（`netstandard2.1` 无此类型），节点数为系统数量级，O(n²) 扫描可接受。
- **成环回退（绑定）**：某一步找不到入度为 0 的未发射节点即判定成环 → `Log.Err("System execution order contains a cycle; falling back to registration order. Blocked systems: " + names + ".")`（names 为剩余未发射系统的类型短名，逗号分隔）→ **全量回退**返回展平序（不是逐分量回退；spec 6.3"回退到展平序，保证可运行"）。世界继续可 tick，成环约束整体忽略。
- **Teardown 接线（绑定）**：`TeardownSystems` 保持 v1 语义——`m_changable = false` → 实例化 `m_addSystems` 中排队的系统（`_instantSystem` + `OnCreate`）→ **新增** `_rebuildExecutionOrder()` → 发射 `OnSystemTeardown`（位置不变，顺序重建对信号处理器可见）。`_rebuildExecutionOrder` 用 `m_systemTransformer.TryGetValue` 映射类型到既有实例（复用、不重建、不重复 `OnCreate`）；schedule 中尚未实例化的类型（在实例化循环期间由构造函数 / `OnCreate` 注册进 `m_addSystems` 的系统）本轮跳过，下一轮 Teardown 实例化后再纳入。实例化循环本身不改（`for (var i = m_addSystems.Count; i > 0; i--)` 只处理进入时已排队的系统，保持 v1 行为）。
- **防御性收口（绑定，当前不可达）**：`_rebuildExecutionOrder` 末尾把"已实例化但不在解析结果中"的系统按 `m_systemTransformer` 枚举序追加回 `m_systems`，保证任何实例都不会因重建而从执行序列消失。当前所有实例化路径都先添加树节点（Task 1），`OnWorldStarted` 的遗留出队路径（`SystemManager.cs:432-438`，不添加树节点）在 `Ready` 断言下不可达，因此该分支当前无测试（评审知悉）。
- **不变性（绑定）**：无锚点时 `BuildExecutionOrder()` 输出 == 展平序 == v1 注册序，既有 472 个测试零修改、全绿；`Early` 对执行顺序的影响从 Task 2 起生效（Task 1 只影响树序）。`OnSystemTeardown` 语义/时机不变（仅顺序重建提前到发射之前；既有测试不订阅该信号）。
- **测试计数（绑定）**：Task 1 后基线 472 + 新增 18 = **490 passed**；过滤预期 `SystemOrderingTestUnit` 18，其余 fixture 数量不变。

- [ ] **Step 1: 写失败测试**

创建 `Test/SystemOrderingTestUnit.cs`：

```csharp
using CoreECS.Defines;
using CoreECS.Managers;
using CoreECS.Utils;

namespace CoreECS.Test
{
    /// <summary>
    /// Execution order resolution for the Phase 4 scheduling tree: the tree is flattened in
    /// registration order (depth-first; a group's contents land at the group's position),
    /// Before / After anchors are resolved into a stable topological sort, unresolvable
    /// anchors are logged and ignored, and cycles fall back to the flatten order. The resolved
    /// sequence is applied to the running systems at the next BeginTick.
    /// </summary>
    [TestFixture]
    public class SystemOrderingTestUnit
    {
        private static readonly List<string> ExecutionLog = new List<string>();

        private World _world = null!;
        private OrderTestLogger _logger = null!;

        [SetUp]
        public void Setup()
        {
            ExecutionLog.Clear();
            _logger = new OrderTestLogger();
            Log.Logger = _logger;
            _world = new World();
            _world.Startup();
        }

        [TearDown]
        public void TearDown()
        {
            _world?.Shutdown();
            Log.Logger = null;
        }

        private static string[] ResolvedOrder(World world)
        {
            var order = world.GetManager<SystemManager>().Schedule.BuildExecutionOrder();
            var names = new List<string>();
            foreach (var type in order) names.Add(type.Name);
            return names.ToArray();
        }

        private static string[] RunningOrder(World world)
        {
            var names = new List<string>();
            foreach (var system in world.GetManager<SystemManager>().Systems) names.Add(system.GetType().Name);
            return names.ToArray();
        }

        [Test]
        public void BuildExecutionOrder_WithoutAnchors_ReturnsFlattenOrder()
        {
            _world.RegisterSystem<SystemA>();
            _world.RegisterSystem<SystemB>();
            _world.RegisterSystem<SystemC>();

            CollectionAssert.AreEqual(new[] { "SystemA", "SystemB", "SystemC" }, ResolvedOrder(_world));
        }

        [Test]
        public void NestedGroups_FlattenDepthFirstInChildOrder()
        {
            _world.RegisterGroup("Outer");
            _world.RegisterSystem<SystemA>("Outer");
            _world.RegisterGroup("Inner", "Outer");
            _world.RegisterSystem<SystemB>("Inner");
            _world.RegisterSystem<SystemC>("Outer");
            _world.RegisterSystem<SystemD>();

            CollectionAssert.AreEqual(new[] { "SystemA", "SystemB", "SystemC", "SystemD" }, ResolvedOrder(_world));
        }

        [Test]
        public void EarlyGroup_ChangesFlattenPosition()
        {
            _world.RegisterSystem<SystemA>();
            _world.RegisterSystem<SystemB>();
            _world.RegisterGroup("Early", GroupInsertMode.Early);
            _world.RegisterSystem<SystemC>("Early");

            CollectionAssert.AreEqual(new[] { "SystemC", "SystemA", "SystemB" }, ResolvedOrder(_world));
        }

        [Test]
        public void AfterSystem_ReordersDeclaringSystemAfterForwardReferencedTarget()
        {
            _world.RegisterSystem<SystemA>().After<SystemB>();
            _world.RegisterSystem<SystemB>();

            CollectionAssert.AreEqual(new[] { "SystemB", "SystemA" }, ResolvedOrder(_world));
            Assert.AreEqual(0, _logger.ErrorMessages.Count);
        }

        [Test]
        public void BeforeSystem_ReordersDeclaringSystemBeforeTarget()
        {
            _world.RegisterSystem<SystemB>();
            _world.RegisterSystem<SystemA>().Before<SystemB>();

            CollectionAssert.AreEqual(new[] { "SystemA", "SystemB" }, ResolvedOrder(_world));
        }

        [Test]
        public void AfterGroup_PlacesSystemAfterEntireGroupSubtree()
        {
            _world.RegisterSystem<SystemA>().After("Gameplay");
            _world.RegisterGroup("Gameplay");
            _world.RegisterSystem<SystemB>("Gameplay");
            _world.RegisterSystem<SystemC>("Gameplay");

            CollectionAssert.AreEqual(new[] { "SystemB", "SystemC", "SystemA" }, ResolvedOrder(_world));
        }

        [Test]
        public void BeforeGroup_PlacesSystemBeforeEntireGroupSubtree()
        {
            _world.RegisterGroup("Gameplay");
            _world.RegisterSystem<SystemB>("Gameplay");
            _world.RegisterSystem<SystemC>("Gameplay");
            _world.RegisterSystem<SystemA>().Before("Gameplay");

            CollectionAssert.AreEqual(new[] { "SystemA", "SystemB", "SystemC" }, ResolvedOrder(_world));
        }

        [Test]
        public void GroupAnchor_AppliesToEntireSubtree()
        {
            _world.RegisterGroup("Gameplay").After<SystemX>();
            _world.RegisterSystem<SystemA>("Gameplay");
            _world.RegisterSystem<SystemB>("Gameplay");
            _world.RegisterSystem<SystemX>();

            CollectionAssert.AreEqual(new[] { "SystemX", "SystemA", "SystemB" }, ResolvedOrder(_world));
        }

        [Test]
        public void CrossLevelAnchor_TargetsNestedGroupFromRootSystem()
        {
            _world.RegisterSystem<SystemX>().After("Inner");
            _world.RegisterGroup("Outer");
            _world.RegisterGroup("Inner", "Outer");
            _world.RegisterSystem<SystemA>("Inner");

            CollectionAssert.AreEqual(new[] { "SystemA", "SystemX" }, ResolvedOrder(_world));
        }

        [Test]
        public void UnresolvableSystemAnchor_IsLoggedAndIgnored()
        {
            _world.RegisterSystem<SystemA>().After<SystemMissing>();
            _world.RegisterSystem<SystemB>();

            CollectionAssert.AreEqual(new[] { "SystemA", "SystemB" }, ResolvedOrder(_world));
            Assert.AreEqual(1, _logger.ErrorMessages.Count);
            StringAssert.Contains("SystemMissing", _logger.ErrorMessages[0]);
        }

        [Test]
        public void UnresolvableGroupAnchor_IsLoggedAndIgnored()
        {
            _world.RegisterSystem<SystemA>().After("MissingGroup");
            _world.RegisterSystem<SystemB>();

            CollectionAssert.AreEqual(new[] { "SystemA", "SystemB" }, ResolvedOrder(_world));
            Assert.AreEqual(1, _logger.ErrorMessages.Count);
            StringAssert.Contains("MissingGroup", _logger.ErrorMessages[0]);
        }

        [Test]
        public void Cycle_FallsBackToFlattenOrderLogsErrorAndStaysRunnable()
        {
            _world.RegisterSystem<SystemA>().Before<SystemB>();
            _world.RegisterSystem<SystemB>().Before<SystemA>();

            _world.BeginTick();

            CollectionAssert.AreEqual(new[] { "SystemA", "SystemB" }, RunningOrder(_world));
            Assert.AreEqual(1, _logger.ErrorMessages.Count);
            StringAssert.Contains("cycle", _logger.ErrorMessages[0]);

            _world.Tick();
            _world.EndTick();

            Assert.IsTrue(_world.FindSystem<SystemA>().OnTickCalled);
            Assert.IsTrue(_world.FindSystem<SystemB>().OnTickCalled);
        }

        [Test]
        public void UnconstrainedSystems_KeepRegistrationOrderAroundAnchoredSystem()
        {
            _world.RegisterSystem<SystemA>();
            _world.RegisterSystem<SystemB>();
            _world.RegisterSystem<SystemC>().After<SystemD>();
            _world.RegisterSystem<SystemD>();

            CollectionAssert.AreEqual(new[] { "SystemA", "SystemB", "SystemD", "SystemC" }, ResolvedOrder(_world));
        }

        [Test]
        public void BeginTick_ExecutionFollowsResolvedOrder()
        {
            _world.RegisterSystem<SystemB>();
            _world.RegisterSystem<SystemA>().Before<SystemB>();

            _world.BeginTick();

            CollectionAssert.AreEqual(new[] { "SystemA", "SystemB" }, RunningOrder(_world));

            _world.Tick();
            _world.EndTick();

            CollectionAssert.AreEqual(new[] { "SystemA", "SystemB" }, ExecutionLog);
        }

        [Test]
        public void GroupSelfAnchor_IsNoOpAndKeepsOtherConstraints()
        {
            _world.RegisterGroup("Group").Before("Group");
            _world.RegisterSystem<SystemA>("Group");
            _world.RegisterSystem<SystemB>("Group");
            _world.RegisterSystem<SystemC>().Before<SystemA>();

            // Flatten is [A, B, C]; the only edge is C -> A. Stable selection takes the
            // lowest flatten index with in-degree 0, so B (unconstrained) comes first.
            CollectionAssert.AreEqual(new[] { "SystemB", "SystemC", "SystemA" }, ResolvedOrder(_world));
            Assert.AreEqual(0, _logger.ErrorMessages.Count);
        }

        [Test]
        public void SystemAfterOwnGroup_ConstrainsAgainstOtherGroupMembersOnly()
        {
            _world.RegisterGroup("Group");
            _world.RegisterSystem<SystemA>("Group").After("Group");
            _world.RegisterSystem<SystemB>("Group");

            // Flatten [A, B]; A.After(own group) adds only B -> A (self edge skipped).
            CollectionAssert.AreEqual(new[] { "SystemB", "SystemA" }, ResolvedOrder(_world));
            Assert.AreEqual(0, _logger.ErrorMessages.Count);
        }

        [Test]
        public void SystemBeforeOwnGroup_ConstrainsAgainstOtherGroupMembersOnly()
        {
            _world.RegisterGroup("Group");
            _world.RegisterSystem<SystemA>("Group").Before("Group");
            _world.RegisterSystem<SystemB>("Group");

            // Flatten [A, B]; A.Before(own group) adds only A -> B, so flatten order holds.
            CollectionAssert.AreEqual(new[] { "SystemA", "SystemB" }, ResolvedOrder(_world));
            Assert.AreEqual(0, _logger.ErrorMessages.Count);
        }

        [Test]
        public void TeardownSystems_ReusesInstancesAndDoesNotRepeatOnCreate()
        {
            CreatingSystem.CreateCount = 0;
            _world.RegisterSystem<CreatingSystem>();
            var first = _world.FindSystem<CreatingSystem>();
            Assert.AreEqual(1, CreatingSystem.CreateCount);

            _world.BeginTick();
            _world.Tick();
            _world.EndTick();
            _world.BeginTick();
            _world.Tick();
            _world.EndTick();

            var second = _world.FindSystem<CreatingSystem>();
            Assert.AreSame(first, second);
            Assert.AreEqual(1, CreatingSystem.CreateCount);
        }

        private class RecordingSystem : ISystem
        {
            public bool OnTickCalled { get; private set; }

            public void OnTick(ulong tickMask)
            {
                OnTickCalled = true;
                ExecutionLog.Add(GetType().Name);
            }
        }

        private class SystemA : RecordingSystem
        {
        }

        private class SystemB : RecordingSystem
        {
        }

        private class SystemC : RecordingSystem
        {
        }

        private class SystemD : RecordingSystem
        {
        }

        private class SystemX : RecordingSystem
        {
        }

        private class SystemMissing : RecordingSystem
        {
        }

        private class CreatingSystem : ISystem
        {
            public static int CreateCount;

            public void OnCreate() => CreateCount += 1;

            public void OnTick(ulong tickMask)
            {
            }
        }

        private class OrderTestLogger : ILogger
        {
            public readonly List<string> ErrorMessages = new List<string>();

            public void Debug(string msg, object context = null)
            {
            }

            public void Info(string msg, object context = null)
            {
            }

            public void Warn(string msg, object context = null)
            {
            }

            public void Err(string msg, object context = null)
            {
                ErrorMessages.Add(msg);
            }

            public void Exp(Exception err, object context = null)
            {
            }
        }
    }
}
```

- [ ] **Step 2: 运行过滤测试，确认失败（红灯）**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --filter FullyQualifiedName~SystemOrderingTestUnit`
Expected: **FAIL（构建失败，0 个测试执行）**——测试先行引用尚未存在的方法，典型错误：
`error CS1061: 'SystemSchedule' does not contain a definition for 'BuildExecutionOrder'`（Step 3 修复）。

- [ ] **Step 3: 在 `SystemSchedule` 实现展平 + 锚点解析 + 稳定拓扑排序**

(a) 文件顶部补 `using CoreECS.Utils;`：

old（`SystemSchedule.cs:1-5`）：

```csharp
using System;
using System.Collections.Generic;
using CoreECS.Defines;

namespace CoreECS.Managers
```

new：

```csharp
using System;
using System.Collections.Generic;
using CoreECS.Defines;
using CoreECS.Utils;

namespace CoreECS.Managers
```

(b) 在 `ClearSystems` 之后、类结束前插入 `BuildExecutionOrder()` 与私有辅助（`SystemSchedule.cs:95-104` 整段替换）：

old：

```csharp
        public void ClearSystems()
        {
            foreach (var pair in m_systems)
            {
                pair.Value.Parent.Children.Remove(pair.Value);
            }

            m_systems.Clear();
        }
    }
```

new：

```csharp
        public void ClearSystems()
        {
            foreach (var pair in m_systems)
            {
                pair.Value.Parent.Children.Remove(pair.Value);
            }

            m_systems.Clear();
        }

        /// <summary>
        /// Builds the execution order of the registered systems: the tree is flattened in
        /// registration order (depth-first; a group's contents land at the group's position),
        /// Before / After anchors are resolved into ordering edges, and a stable topological
        /// sort produces the sequence. Unconstrained systems keep their flatten order.
        /// </summary>
        /// <returns>
        /// System types in execution order. When the constraints contain a cycle, the error is
        /// logged and the flatten order is returned for the whole sequence.
        /// </returns>
        public List<Type> BuildExecutionOrder()
        {
            var flattened = new List<SystemEntryNode>();
            _flatten(Root, flattened);

            var indexes = new Dictionary<SystemEntryNode, int>();
            for (var i = 0; i < flattened.Count; i++)
                indexes.Add(flattened[i], i);

            var inDegree = new int[flattened.Count];
            var successors = new List<int>[flattened.Count];
            for (var i = 0; i < successors.Length; i++)
                successors[i] = new List<int>();

            _collectConstraints(Root, indexes, inDegree, successors);

            var order = new List<SystemEntryNode>(flattened.Count);
            var emitted = new bool[flattened.Count];

            for (var step = 0; step < flattened.Count; step++)
            {
                var pick = -1;
                for (var i = 0; i < flattened.Count; i++)
                {
                    if (!emitted[i] && inDegree[i] == 0)
                    {
                        pick = i;
                        break;
                    }
                }

                if (pick < 0)
                {
                    Log.Err("System execution order contains a cycle; falling back to registration order. Blocked systems: "
                        + _blockedSystemNames(flattened, emitted) + ".");
                    return _toTypes(flattened);
                }

                emitted[pick] = true;
                order.Add(flattened[pick]);
                foreach (var successor in successors[pick])
                    inDegree[successor]--;
            }

            return _toTypes(order);
        }

        /// <summary>
        /// Flattens a group subtree depth-first in child order; only system entries are emitted,
        /// so a group's contents land at the group's position among its siblings.
        /// </summary>
        /// <param name="group">Group whose subtree is flattened.</param>
        /// <param name="output">Receives the system entries in flatten order.</param>
        private static void _flatten(SystemGroupNode group, List<SystemEntryNode> output)
        {
            foreach (var child in group.Children)
            {
                if (child is SystemGroupNode childGroup) _flatten(childGroup, output);
                else output.Add((SystemEntryNode)child);
            }
        }

        /// <summary>
        /// Applies the anchors of every tree node to the edge arrays. A group's anchors apply
        /// to its entire subtree; a group-name anchor target expands to the target group's
        /// entire subtree. Unresolvable targets are logged and ignored.
        /// </summary>
        /// <param name="group">Group whose subtree is visited.</param>
        /// <param name="indexes">Flatten index of every system entry.</param>
        /// <param name="inDegree">In-degree accumulator per flatten index.</param>
        /// <param name="successors">Successor lists per flatten index.</param>
        private void _collectConstraints(SystemGroupNode group, Dictionary<SystemEntryNode, int> indexes, int[] inDegree, List<int>[] successors)
        {
            _applyAnchors(group, indexes, inDegree, successors);

            foreach (var child in group.Children)
            {
                if (child is SystemGroupNode childGroup) _collectConstraints(childGroup, indexes, inDegree, successors);
                else _applyAnchors(child, indexes, inDegree, successors);
            }
        }

        /// <summary>
        /// Resolves the anchors of a single node into edges. A system node constrains itself;
        /// a group node constrains every system in its subtree.
        /// </summary>
        /// <param name="node">Node whose anchors are applied.</param>
        /// <param name="indexes">Flatten index of every system entry.</param>
        /// <param name="inDegree">In-degree accumulator per flatten index.</param>
        /// <param name="successors">Successor lists per flatten index.</param>
        private void _applyAnchors(SystemScheduleNode node, Dictionary<SystemEntryNode, int> indexes, int[] inDegree, List<int>[] successors)
        {
            if (node.Anchors.Count == 0) return;

            var subjects = new List<SystemEntryNode>();
            if (node is SystemEntryNode entry) subjects.Add(entry);
            else _flatten((SystemGroupNode)node, subjects);

            for (var i = 0; i < node.Anchors.Count; i++)
            {
                var anchor = node.Anchors[i];

                if (anchor.SystemType != null)
                {
                    var target = FindSystem(anchor.SystemType);
                    if (target == null)
                    {
                        Log.Err($"System schedule anchor target '{anchor.SystemType.FullName}' is not registered; constraint ignored.");
                        continue;
                    }

                    _addEdges(subjects, new List<SystemEntryNode> { target }, anchor.Kind, indexes, inDegree, successors);
                }
                else
                {
                    var targetGroup = FindGroup(anchor.GroupName);
                    if (targetGroup == null)
                    {
                        Log.Err($"System schedule anchor target group '{anchor.GroupName}' is not registered; constraint ignored.");
                        continue;
                    }

                    var targets = new List<SystemEntryNode>();
                    _flatten(targetGroup, targets);

                    // A group node anchored to itself or an ancestor is degenerate: skip it
                    // instead of adding mutual edges. System anchors to their own group are
                    // not skipped; _addEdges drops only the self edge, so the system is
                    // constrained against the other group members.
                    if (node is SystemGroupNode && _isSubset(subjects, targets)) continue;

                    _addEdges(subjects, targets, anchor.Kind, indexes, inDegree, successors);
                }
            }
        }

        /// <summary>True when every entry in <paramref name="subset"/> is also in <paramref name="superset"/>.</summary>
        private static bool _isSubset(List<SystemEntryNode> subset, List<SystemEntryNode> superset)
        {
            for (var i = 0; i < subset.Count; i++)
            {
                if (!superset.Contains(subset[i])) return false;
            }

            return true;
        }

        /// <summary>
        /// Adds the edges for a subject / target cartesian product. Self edges are skipped:
        /// a system trivially precedes and follows itself, so a system anchored to a group
        /// that contains it is constrained against the other systems of that group only.
        /// </summary>
        /// <param name="subjects">Systems constrained by the anchor.</param>
        /// <param name="targets">Systems the anchor points at.</param>
        /// <param name="kind">Anchor direction.</param>
        /// <param name="indexes">Flatten index of every system entry.</param>
        /// <param name="inDegree">In-degree accumulator per flatten index.</param>
        /// <param name="successors">Successor lists per flatten index.</param>
        private static void _addEdges(List<SystemEntryNode> subjects, List<SystemEntryNode> targets, SystemAnchorKind kind,
            Dictionary<SystemEntryNode, int> indexes, int[] inDegree, List<int>[] successors)
        {
            for (var s = 0; s < subjects.Count; s++)
            {
                for (var t = 0; t < targets.Count; t++)
                {
                    var subject = subjects[s];
                    var target = targets[t];
                    if (ReferenceEquals(subject, target)) continue;

                    var subjectIndex = indexes[subject];
                    var targetIndex = indexes[target];

                    if (kind == SystemAnchorKind.Before)
                    {
                        successors[subjectIndex].Add(targetIndex);
                        inDegree[targetIndex]++;
                    }
                    else
                    {
                        successors[targetIndex].Add(subjectIndex);
                        inDegree[subjectIndex]++;
                    }
                }
            }
        }

        /// <summary>Collects the type names of the systems that could not be emitted, for cycle logging.</summary>
        /// <param name="flattened">System entries in flatten order.</param>
        /// <param name="emitted">Emission state per flatten index.</param>
        /// <returns>Comma separated system type names.</returns>
        private static string _blockedSystemNames(List<SystemEntryNode> flattened, bool[] emitted)
        {
            var names = new List<string>();
            for (var i = 0; i < flattened.Count; i++)
            {
                if (!emitted[i]) names.Add(flattened[i].SystemType.Name);
            }

            return string.Join(", ", names);
        }

        /// <summary>Projects flatten entries to their system types.</summary>
        /// <param name="entries">System entries in execution order.</param>
        /// <returns>System types in the same order.</returns>
        private static List<Type> _toTypes(List<SystemEntryNode> entries)
        {
            var result = new List<Type>(entries.Count);
            for (var i = 0; i < entries.Count; i++) result.Add(entries[i].SystemType);
            return result;
        }
    }
```

- [ ] **Step 4: 修改 `SystemManager.TeardownSystems`（重建执行序列）**

old（`SystemManager.cs:195-215`）：

```csharp
        /// <summary>
        /// Sets up all queued systems for execution.
        /// </summary>
        public void TeardownSystems()
        {
            Assertion.IsTrue(m_init, "SystemManager is not initialized yet.");
            Assertion.IsFalse(m_shutdown, "SystemManager has already shutdown.");
            
            m_changable = false;

            for (var i = m_addSystems.Count; i > 0; i--)
            {
                var systemType = m_addSystems.Dequeue();
                var sys = _instantSystem(systemType);
                m_systemTransformer.Add(systemType, sys);
                m_systems.Add(sys);
                _createSystem(sys);
            }
            
            OnSystemTeardown.Emit(World, static (h, w) => h(w));
        }
```

new：

```csharp
        /// <summary>
        /// Sets up all queued systems and rebuilds the execution order.
        /// </summary>
        public void TeardownSystems()
        {
            Assertion.IsTrue(m_init, "SystemManager is not initialized yet.");
            Assertion.IsFalse(m_shutdown, "SystemManager has already shutdown.");
            
            m_changable = false;

            for (var i = m_addSystems.Count; i > 0; i--)
            {
                var systemType = m_addSystems.Dequeue();
                var sys = _instantSystem(systemType);
                m_systemTransformer.Add(systemType, sys);
                m_systems.Add(sys);
                _createSystem(sys);
            }

            _rebuildExecutionOrder();
            
            OnSystemTeardown.Emit(World, static (h, w) => h(w));
        }

        /// <summary>
        /// Rebuilds the execution sequence from the schedule. Existing instances are reused and
        /// only re-ordered; systems that are in the schedule but not instantiated yet (queued
        /// while systems were being instantiated) are skipped until the next teardown. As a
        /// defensive measure, instantiated systems missing from the resolved order are appended
        /// so no instance can disappear from the execution sequence.
        /// </summary>
        private void _rebuildExecutionOrder()
        {
            var order = m_schedule.BuildExecutionOrder();

            m_systems.Clear();
            for (var i = 0; i < order.Count; i++)
            {
                if (m_systemTransformer.TryGetValue(order[i], out var system))
                    m_systems.Add(system);
            }

            if (m_systems.Count == m_systemTransformer.Count) return;

            foreach (var pair in m_systemTransformer)
            {
                if (!m_systems.Contains(pair.Value))
                    m_systems.Add(pair.Value);
            }
        }
```

- [ ] **Step 5: 构建 ECS 库（两个 TFM）**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet build ECS/ECS.csproj`
Expected: Build succeeded（net8.0 + netstandard2.1，0 Error）。此时测试项目也已可编译，继续下一步。

- [ ] **Step 6: 运行新增过滤测试**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --filter FullyQualifiedName~SystemOrderingTestUnit`
Expected: PASS（18 个测试，失败 0）

- [ ] **Step 7: 运行全量测试**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj`
Expected: **490 passed**（Task 1 基线 472 + 新增 18），0 failed；`SystemTestUnit` / `SystemGroupRegistrationTestUnit` / `IntegrationTestUnit` / `StressTestUnit` / `WorldTestUnit` 全部保持通过且未修改。

- [ ] **Step 8: 提交**

```bash
git add ECS/Managers/SystemSchedule.cs ECS/Managers/SystemManager.cs Test/SystemOrderingTestUnit.cs
git commit -m "feat(core): resolve system execution order with topological sort

TeardownSystems now flattens the registration tree (depth-first; a
group's contents land at the group's position), resolves Before/After
anchors - system type or group subtree, across levels and forward
references - and rebuilds m_systems through a stable topological sort
that keeps registration order among unconstrained systems. Unresolvable
anchors are logged and ignored; cycles log an error and fall back to
the flatten order for the whole sequence. Existing system instances are
reused, never re-created."
```

---

## Task 3: tick 内注册图收敛（成对变更折叠 + 当前 tick 序列快照）

**Files:**
- Modify: `ECS/Managers/SystemManager.cs`（10 处：字段区 `SystemManager.cs:96-99` 后新增 `m_cancelledAdds`；`TeardownSystems` 实例化循环 `SystemManager.cs:205-212`；`_rebuildExecutionOrder` 后（`SystemManager.cs:244` 与 `SystemManager.cs:246` 之间）新增两个私有辅助（`_repositionSystem` 保留锚点）；`ExecuteSystems` `SystemManager.cs:250-260`；`RegisterSystem` 可变更分支消费排队中的 tick 内添加 `SystemManager.cs:307-314`；`RegisterSystem` 不可变更分支 `SystemManager.cs:309-316`；`AddSystemAnchor` `SystemManager.cs:353-360`；`AddGroupAnchor` `SystemManager.cs:368-375`；`UnregisterSystem` `SystemManager.cs:421-442`；`OnWorldEnded` `SystemManager.cs:477-478`）
- Test: `Test/SystemConvergenceTestUnit.cs`（新增，16 个测试）

**前置:** Task 2 已提交（`cf50da6`、`1032b80`、`09958bb`、`04b624f`）；本次 dispatch 前实测全量 **490 passed / 0 failed**（`PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj`），双目标库构建 0 错误。

**设计说明（执行时不要改动，评审时按此核对）：**

- **实勘结论（绑定，逐项；Task 3 范围由本表决定）**：

| 候选点 | 修复前现状（代码实勘） | 结论 |
|---|---|---|
| 同 tick 注销 + 重注册同类型 | `UnregisterSystem` 仅把类型排入 `m_delSystems`（`m_systemTransformer` 仍含该类型）；随后的 `RegisterSystem` 走不可变更分支，因 `m_systemTransformer.ContainsKey` 为真而**静默丢弃**；`CleanupSystems` 照常销毁实例并移除 | **真实缺口** → 生产修复：取消待移除、复用实例、按请求重定位 |
| 注销"仅排队未实例化"的系统（同 tick 先注册后注销） | `UnregisterSystem` 的 `m_systemTransformer.TryGetValue` 失败 → 抛 `InvalidOperationException("... is not registered.")` | **真实缺口** → 生产修复：取消待添加（移除节点、不实例化） |
| tick 内声明锚点（已有系统 / 排队新系统 / 新组） | `AddSystemAnchor` / `AddGroupAnchor` 立即写入节点；`BuildExecutionOrder` 在下一个 `BeginTick` 读取；不触碰 `m_systems` | 已正确 → 契约测试钉死 |
| tick 内注册新组 | `RegisterGroupCore` 无 `m_changable` 分支，组节点立即入树，可承载同 tick 注册的系统；下个 `BeginTick` 展平生效 | 已正确 → 契约测试钉死 |
| 当前 tick 序列稳定性 | 受支持的注册 / 注销 / 锚点路径在 tick 内都不改 `m_systems`；但 `ExecuteSystems` 按下标遍历活的 `m_systems`，而 `TeardownSystems` / `CleanupSystems` 是公开方法（既有测试直接调用），在 `OnTick` 内直接调用会重建 / 改写列表，导致跳系统或把新系统塞进当前 tick | **潜在隐患** → 生产修复：`ExecuteSystems` 开头对序列做快照 |
| `OnSystemTeardown` / `OnSystemCleanup` 时序 | Teardown 在实例化排队系统 + `_rebuildExecutionOrder` 之后发射；Cleanup 在销毁待移除系统 + `m_changable = true` 之后发射 | 已正确 → 契约测试钉死 |
| shutdown / 未初始化 | `RegisterSystem` / `RegisterGroup` / `UnregisterSystem` 已有 `!m_shutdown` 断言；`AddSystemAnchor` / `AddGroupAnchor` 无断言——shutdown 后过期系统句柄抛 "not registered"，过期**组**句柄却静默写入已死 schedule（组节点在 shutdown 后保留） | 小缺口 → 生产修复：两个锚点方法补 `!m_shutdown` 断言 |

- **收敛语义（绑定，spec 6.3 + 已决事项 8）**：tick 内对注册图的所有变更在下一个 `BeginTick` 统一应用并重算。v1 的"注销在 `EndTick` 的 `CleanupSystems` 及时销毁"时序保留（spec 原文"及时销毁（调用 `OnDestroy`）并从执行序列移除"）：
  - 新增（tick 内 `RegisterSystem`）：节点立即入树，实例化 + `OnCreate` 在下一个 `BeginTick` 的 `TeardownSystems`；
  - 注销（tick 内 `UnregisterSystem`，已实例化）：`CleanupSystems` 销毁（`OnDestroy`）并移除；当前 tick 执行序列不受影响；
  - 已注册系统：`TeardownSystems` 复用实例、按解析顺序重排（Task 2 已具备）。
- **成对变更折叠为最终图状态（绑定，Task 3 生产修复）**：同一不可变更窗口（tick / Teardown 内）内的成对变更按"最终状态"收敛，而不是按操作顺序依次应用：
  - 注销 + 重新注册同一类型：取消待移除，实例复用（**不调用 `OnDestroy`、不重复 `OnCreate`**）；若重新注册指定了不同的组，节点从原组移除并追加到目标组，否则保持原位（不因重复注册扰动位置）；
  - 注册（仅排队）+ 注销：取消待添加——从语义上取消 `m_addSystems` 中的排队项、立即从树中移除节点、**永不实例化**（无 `OnCreate` / `OnDestroy`）；取消后再次注销按 v1 抛 `InvalidOperationException`；
  - 取消 + 再注册：恢复待添加（节点重建），下一次 `BeginTick` 实例化一次；
  - 对已注册（无待移除）系统的重复注册：保持 v1 静默忽略（不重定位、不重复实例化、树不重复添加）。
- **`m_cancelledAdds` 标记的实现理由（绑定）**：取消待添加**不从 `m_addSystems` 出队**，而是把类型记入 `HashSet<Type> m_cancelledAdds`，`TeardownSystems` 实例化循环出队时遇到标记则 `continue`。原因：实例化循环 `for (var i = m_addSystems.Count; i > 0; i--)` 在进入时捕获队列长度；若某系统的 `OnCreate` 注销了队列中尚未实例化的另一个系统，出队式取消会让循环下溢（`Queue.Dequeue` 抛异常并使本轮 `_rebuildExecutionOrder` 被跳过）。标记方案不改动队列结构，循环出队次数始终与进入时一致；标记在下一次 Teardown 出队时移除，`OnWorldEnded` 清空。取消后再注册会先移除标记（节点重建，队列项保留并复用）。
- **当前 tick 序列快照（绑定，Task 3 生产修复）**：`ExecuteSystems` 在轮询前执行 `var sequence = m_systems.ToArray();` 并只遍历快照。理由：`TeardownSystems` / `CleanupSystems` 是公开方法，`OnTick` 内直接调用会 `m_systems.Clear()` 重建或移除元素；按下标遍历活列表会跳系统、重复执行或把新系统追加进当前 tick。快照钉死 spec 6.3"当前 tick 内已排定的执行序列不受影响；`Tick` 执行期间不重建序列"。受支持的注册 / 注销 / 锚点路径在 tick 内本就不改 `m_systems`，因此快照对既有行为零影响（逐元素一致），唯一代价是每次 `ExecuteSystems` 一次小数组分配。
- **锚点时序（绑定）**：锚点在声明时立即写入节点、不触发重建；生效点为下一个 `BeginTick` 的 `BuildExecutionOrder`。tick 内对已有系统、排队新系统、tick 内新建组声明锚点均如此。
- **信号时序（绑定，零改动，测试钉死）**：
  - `OnSystemTeardown`：在排队系统实例化 + `_rebuildExecutionOrder` 之后发射——处理器看到已收敛状态（含本轮新实例化系统）；处理器内的注册按"不可变更"排队到下一个 `BeginTick`；
  - `OnSystemCleanup`：在待移除系统销毁 + `m_changable = true` 之后发射——处理器看到移除后的状态，且其注册 / 注销立即生效（下一个 `BeginTick` 仍会重排）。
- **shutdown / 未初始化（绑定）**：world 未 `Startup` 时 `World.GetManager` 断言 `m_init`，manager 层注册不可达，不加断言（记录）；`AddSystemAnchor` / `AddGroupAnchor` 补 `Assertion.IsFalse(m_shutdown, "SystemManager has already shutdown.")`，与注册 / 注销的关闭断言一致。`OnWorldStarted` 的遗留出队路径（`SystemManager.cs:458-468`）保持不可达、不改（Task 2 已记录）。
- **不在范围内**：`CleanupSystems` 的销毁时机保持 v1（EndTick）；world 关停时的信号行为不变；`_rebuildExecutionOrder` 的防御分支保持不可达；不做注册图变更的线程安全（v1 无此保证）。
- **测试计数（绑定）**：Task 2 后基线 490 + 新增 16 = **506 passed**；过滤预期 `SystemConvergenceTestUnit` 16，其余 fixture 数量不变。

- [ ] **Step 1: 写失败测试**

创建 `Test/SystemConvergenceTestUnit.cs`：

```csharp
using CoreECS.Defines;
using CoreECS.Managers;

namespace CoreECS.Test
{
    /// <summary>
    /// Tick-time convergence contract for the Phase 4 scheduling tree: changes made to the
    /// registration graph while a tick is running are applied at the next BeginTick, paired
    /// changes collapse to the final graph state (unregister + re-register keeps the instance,
    /// register + unregister cancels the pending add), anchors declared during a tick take
    /// effect at the next BeginTick, and the sequence scheduled for the current tick is never
    /// rebuilt or extended while it is executing.
    /// </summary>
    [TestFixture]
    public class SystemConvergenceTestUnit
    {
        private static readonly List<string> ExecutionLog = new List<string>();
        private static World CurrentWorld = null!;

        private World _world = null!;

        [SetUp]
        public void Setup()
        {
            ExecutionLog.Clear();
            _world = new World();
            _world.Startup();
            CurrentWorld = _world;
        }

        [TearDown]
        public void TearDown()
        {
            CurrentWorld = null!;
            _world?.Shutdown();
        }

        private SystemSchedule Schedule => _world.GetManager<SystemManager>().Schedule;

        private static string[] RunningOrder(World world)
        {
            var names = new List<string>();
            foreach (var system in world.GetManager<SystemManager>().Systems) names.Add(system.GetType().Name);
            return names.ToArray();
        }

        [Test]
        public void UnregisterThenRegister_DuringTick_CancelsRemovalAndKeepsInstance()
        {
            _world.RegisterSystem<SystemA>();
            var first = _world.FindSystem<SystemA>();
            Assert.IsNotNull(first);

            _world.BeginTick();
            _world.UnregisterSystem<SystemA>();
            _world.RegisterSystem<SystemA>();
            _world.Tick();
            _world.EndTick();

            var second = _world.FindSystem<SystemA>();
            Assert.AreSame(first, second);
            Assert.AreEqual(1, second.CreateCount);
            Assert.AreEqual(0, second.DestroyCount);
            Assert.AreEqual(1, second.TickCount);
            Assert.IsNotNull(Schedule.FindSystem(typeof(SystemA)));

            _world.BeginTick();
            _world.Tick();
            _world.EndTick();

            Assert.AreSame(first, _world.FindSystem<SystemA>());
            Assert.AreEqual(1, second.CreateCount);
        }

        [Test]
        public void UnregisterThenRegister_DuringTick_AppliesRequestedGroupAtNextBeginTick()
        {
            _world.RegisterGroup("First");
            _world.RegisterGroup("Second");
            _world.RegisterSystem<SystemB>("Second");
            _world.RegisterSystem<SystemA>("First");
            var first = _world.FindSystem<SystemA>();

            _world.BeginTick();

            // Duplicate registration of a live system stays a no-op: the node does not move.
            _world.RegisterSystem<SystemA>("Second");
            Assert.AreSame(Schedule.FindGroup("First"), Schedule.FindSystem(typeof(SystemA)).Parent);

            // Unregister + re-register collapses into "registered in Second": the removal is
            // cancelled and the node is repositioned without touching the current tick.
            _world.UnregisterSystem<SystemA>();
            _world.RegisterSystem<SystemA>("Second");
            CollectionAssert.AreEqual(new[] { "SystemA", "SystemB" }, RunningOrder(_world));

            _world.Tick();
            _world.EndTick();

            Assert.AreSame(first, _world.FindSystem<SystemA>());
            Assert.AreSame(Schedule.FindGroup("Second"), Schedule.FindSystem(typeof(SystemA)).Parent);

            _world.BeginTick();
            CollectionAssert.AreEqual(new[] { "SystemB", "SystemA" }, RunningOrder(_world));
            _world.Tick();
            _world.EndTick();
        }

        [Test]
        public void UnregisterThenRegister_DuringTick_IntoDifferentGroup_KeepsAnchors()
        {
            _world.RegisterGroup("Late");
            _world.RegisterGroup("Early", GroupInsertMode.Early);
            _world.RegisterSystem<SystemA>("Late").After<SystemB>();
            _world.RegisterSystem<SystemB>();

            _world.BeginTick();
            _world.UnregisterSystem<SystemA>();
            _world.RegisterSystem<SystemA>("Early");
            _world.Tick();
            _world.EndTick();
            _world.BeginTick();

            // The B -> A anchor survives the group move: B still runs before A.
            CollectionAssert.AreEqual(new[] { "SystemB", "SystemA" }, RunningOrder(_world));
            _world.Tick();
            _world.EndTick();
        }

        [Test]
        public void RegisterAfterTick_QueuedThenChangable_InstantiatesWithoutDuplicateNode()
        {
            _world.BeginTick();
            _world.RegisterSystem<SystemA>();
            _world.Tick();
            _world.EndTick();

            // The queued add survived CleanupSystems; a second register converges on the
            // pending registration instead of adding a duplicate schedule node.
            Assert.DoesNotThrow(() => _world.RegisterSystem<SystemA>());
            Assert.IsNotNull(_world.FindSystem<SystemA>());
            CollectionAssert.AreEqual(new[] { "SystemA" }, RunningOrder(_world));

            _world.BeginTick();
            CollectionAssert.AreEqual(new[] { "SystemA" }, RunningOrder(_world));
            _world.Tick();
            _world.EndTick();
        }

        [Test]
        public void RegisterThenUnregisterThenRegisterAfterTick_RestoresScheduleNode()
        {
            _world.BeginTick();
            _world.RegisterSystem<SystemA>();
            _world.UnregisterSystem<SystemA>();
            _world.Tick();
            _world.EndTick();

            // The cancelled add left no schedule node; the changable re-register must
            // restore both the instance and the tree entry.
            Assert.DoesNotThrow(() => _world.RegisterSystem<SystemA>());
            Assert.IsNotNull(_world.FindSystem<SystemA>());
            Assert.IsNotNull(Schedule.FindSystem(typeof(SystemA)));
            CollectionAssert.AreEqual(new[] { "SystemA" }, RunningOrder(_world));

            _world.BeginTick();
            CollectionAssert.AreEqual(new[] { "SystemA" }, RunningOrder(_world));
            _world.Tick();
            _world.EndTick();
        }

        [Test]
        public void RegisterThenUnregister_DuringTick_CancelsPendingAdd()
        {
            _world.BeginTick();
            _world.RegisterSystem<SystemA>();
            Assert.IsNotNull(Schedule.FindSystem(typeof(SystemA)));
            Assert.IsNull(_world.FindSystem<SystemA>());

            _world.UnregisterSystem<SystemA>();
            Assert.IsNull(Schedule.FindSystem(typeof(SystemA)));

            var ex = Assert.Throws<InvalidOperationException>(() => _world.UnregisterSystem<SystemA>());
            StringAssert.Contains("is not registered", ex!.Message);

            _world.Tick();
            _world.EndTick();

            _world.BeginTick();
            Assert.IsNull(_world.FindSystem<SystemA>());
            Assert.IsNull(Schedule.FindSystem(typeof(SystemA)));
            _world.Tick();
            _world.EndTick();
        }

        [Test]
        public void RegisterThenUnregisterThenRegister_DuringTick_InstantiatesOnceAtNextBeginTick()
        {
            _world.BeginTick();
            _world.RegisterSystem<SystemA>();
            _world.UnregisterSystem<SystemA>();
            _world.RegisterSystem<SystemA>();

            Assert.IsNull(_world.FindSystem<SystemA>());

            _world.Tick();
            _world.EndTick();
            Assert.IsNull(_world.FindSystem<SystemA>());

            _world.BeginTick();
            var system = _world.FindSystem<SystemA>();
            Assert.IsNotNull(system);
            Assert.AreEqual(1, system.CreateCount);
            Assert.IsNotNull(Schedule.FindSystem(typeof(SystemA)));

            _world.Tick();
            _world.EndTick();
            Assert.AreEqual(1, system.TickCount);
        }

        [Test]
        public void UnregisterThenRegisterThenUnregister_DuringTick_DestroysAtCleanup()
        {
            _world.RegisterSystem<SystemA>();
            var system = _world.FindSystem<SystemA>();

            _world.BeginTick();
            _world.UnregisterSystem<SystemA>();
            _world.RegisterSystem<SystemA>();
            _world.UnregisterSystem<SystemA>();
            _world.Tick();
            _world.EndTick();

            Assert.IsNull(_world.FindSystem<SystemA>());
            Assert.IsNull(Schedule.FindSystem(typeof(SystemA)));
            Assert.AreEqual(1, system.DestroyCount);
            Assert.AreEqual(1, system.TickCount);
        }

        [Test]
        public void AnchorsDeclaredDuringTick_ApplyAtNextBeginTick()
        {
            var handle = _world.RegisterSystem<SystemA>();
            _world.RegisterSystem<SystemB>();

            _world.BeginTick();
            handle.After<SystemB>();
            CollectionAssert.AreEqual(new[] { "SystemA", "SystemB" }, RunningOrder(_world));

            _world.Tick();
            _world.EndTick();

            _world.BeginTick();
            CollectionAssert.AreEqual(new[] { "SystemB", "SystemA" }, RunningOrder(_world));
            _world.Tick();
            _world.EndTick();
        }

        [Test]
        public void AnchorDeclaredOnNewSystemDuringTick_AppliesWithItsFirstTick()
        {
            _world.RegisterSystem<SystemB>();

            _world.BeginTick();
            _world.RegisterSystem<SystemA>().Before<SystemB>();
            Assert.IsNull(_world.FindSystem<SystemA>());

            _world.Tick();
            _world.EndTick();

            _world.BeginTick();
            Assert.IsNotNull(_world.FindSystem<SystemA>());
            CollectionAssert.AreEqual(new[] { "SystemA", "SystemB" }, RunningOrder(_world));
            _world.Tick();
            _world.EndTick();
        }

        [Test]
        public void TreeChangesDuringTick_DoNotAffectRemainingExecution()
        {
            _world.RegisterSystem<MutatorSystem>();
            _world.RegisterSystem<VictimSystem>();
            _world.RegisterSystem<TailSystem>();

            _world.BeginTick();
            _world.Tick();

            CollectionAssert.AreEqual(new[] { "MutatorSystem", "VictimSystem", "TailSystem" }, ExecutionLog);
            Assert.AreEqual(1, _world.FindSystem<VictimSystem>().TickCount);
            Assert.IsNull(_world.FindSystem<NewSystem>());

            _world.EndTick();

            Assert.IsNull(_world.FindSystem<VictimSystem>());
            Assert.IsNull(Schedule.FindSystem(typeof(VictimSystem)));

            _world.BeginTick();
            Assert.IsNotNull(_world.FindSystem<NewSystem>());
            CollectionAssert.AreEqual(new[] { "MutatorSystem", "TailSystem", "NewSystem" }, RunningOrder(_world));
            _world.Tick();
            _world.EndTick();
        }

        [Test]
        public void ExecuteSystems_ScheduledSequenceIsSnapshot_MidTickRebuildDoesNotExtendIt()
        {
            _world.RegisterSystem<QueueAddSystem>();
            _world.RegisterSystem<RebuildSystem>();
            _world.RegisterSystem<TailSystem>();

            _world.BeginTick();
            _world.Tick();

            // The direct TeardownSystems call from OnTick instantiated LateSystem and rebuilt
            // m_systems, but the sequence scheduled for this tick was already snapshotted.
            CollectionAssert.AreEqual(new[] { "QueueAddSystem", "RebuildSystem", "TailSystem" }, ExecutionLog);
            var late = _world.FindSystem<LateSystem>();
            Assert.IsNotNull(late);
            Assert.AreEqual(0, late.TickCount);
            _world.EndTick();

            ExecutionLog.Clear();
            _world.BeginTick();
            _world.Tick();
            CollectionAssert.AreEqual(new[] { "QueueAddSystem", "RebuildSystem", "TailSystem", "LateSystem" }, ExecutionLog);
            Assert.AreEqual(1, late.TickCount);
            _world.EndTick();
        }

        [Test]
        public void GroupRegisteredDuringTick_IsUsableForSameTickRegistration()
        {
            _world.RegisterSystem<SystemB>();

            _world.BeginTick();
            _world.RegisterGroup("Late");
            _world.RegisterSystem<SystemA>("Late");

            Assert.IsNotNull(Schedule.FindGroup("Late"));
            Assert.IsNull(_world.FindSystem<SystemA>());
            CollectionAssert.AreEqual(new[] { "SystemB" }, RunningOrder(_world));

            _world.Tick();
            _world.EndTick();

            _world.BeginTick();
            Assert.IsNotNull(_world.FindSystem<SystemA>());
            CollectionAssert.AreEqual(new[] { "SystemB", "SystemA" }, RunningOrder(_world));
            _world.Tick();
            _world.EndTick();
        }

        [Test]
        public void OnSystemTeardown_SeesConvergedStateAndQueuesHandlerRegistrations()
        {
            var manager = _world.GetManager<SystemManager>();
            var observed = new List<string>();
            var registeredFromHandler = false;

            manager.OnSystemTeardown.Add(world =>
            {
                var systemManager = world.GetManager<SystemManager>();
                observed.Add(systemManager.SystemTransformer.ContainsKey(typeof(SystemA)) ? "A" : "-");

                if (registeredFromHandler) return;
                registeredFromHandler = true;
                _world.RegisterSystem<SystemB>();
            });

            _world.RegisterSystem<SystemA>();

            _world.BeginTick();
            Assert.IsNull(_world.FindSystem<SystemB>());
            _world.Tick();
            _world.EndTick();

            _world.BeginTick();
            Assert.IsNotNull(_world.FindSystem<SystemB>());
            CollectionAssert.AreEqual(new[] { "A", "A" }, observed);
            _world.Tick();
            _world.EndTick();
        }

        [Test]
        public void OnSystemCleanup_RunsAfterRemovalAndAllowsImmediateRegistration()
        {
            var manager = _world.GetManager<SystemManager>();
            var removalObserved = false;
            var immediateRegistrationObserved = false;

            _world.RegisterSystem<SystemA>();
            var system = _world.FindSystem<SystemA>();

            manager.OnSystemCleanup.Add(world =>
            {
                var systemManager = world.GetManager<SystemManager>();
                removalObserved = !systemManager.SystemTransformer.ContainsKey(typeof(SystemA))
                    && system.DestroyCount == 1;

                _world.RegisterSystem<SystemB>();
                immediateRegistrationObserved = _world.FindSystem<SystemB>() != null;
            });

            _world.BeginTick();
            _world.UnregisterSystem<SystemA>();
            _world.Tick();
            _world.EndTick();

            Assert.IsTrue(removalObserved);
            Assert.IsTrue(immediateRegistrationObserved);
            Assert.IsNull(_world.FindSystem<SystemA>());
            Assert.IsNotNull(_world.FindSystem<SystemB>());
            Assert.AreEqual(1, system.DestroyCount);
        }

        [Test]
        public void RegistrationHandles_AfterShutdown_Throw()
        {
            _world.RegisterGroup("Physics");
            var systemHandle = _world.RegisterSystem<SystemA>("Physics");
            var groupHandle = _world.RegisterGroup("Render");

            _world.Shutdown();
            _world = null!;

            var systemEx = Assert.Throws<InvalidOperationException>(() => systemHandle.After<SystemB>());
            Assert.AreEqual("SystemManager has already shutdown.", systemEx!.Message);

            var groupTypeEx = Assert.Throws<InvalidOperationException>(() => groupHandle.After<SystemB>());
            Assert.AreEqual("SystemManager has already shutdown.", groupTypeEx!.Message);

            var groupNameEx = Assert.Throws<InvalidOperationException>(() => groupHandle.After("Physics"));
            Assert.AreEqual("SystemManager has already shutdown.", groupNameEx!.Message);
        }

        private class RecordingSystem : ISystem
        {
            public int CreateCount { get; private set; }

            public int DestroyCount { get; private set; }

            public int TickCount { get; private set; }

            public void OnCreate() => CreateCount += 1;

            public virtual void OnTick(ulong tickMask)
            {
                TickCount += 1;
                ExecutionLog.Add(GetType().Name);
            }

            public void OnDestroy() => DestroyCount += 1;
        }

        private class SystemA : RecordingSystem
        {
        }

        private class SystemB : RecordingSystem
        {
        }

        private class NewSystem : RecordingSystem
        {
        }

        private class TailSystem : RecordingSystem
        {
        }

        private class LateSystem : RecordingSystem
        {
        }

        private class VictimSystem : RecordingSystem
        {
        }

        private class MutatorSystem : RecordingSystem
        {
            public override void OnTick(ulong tickMask)
            {
                base.OnTick(tickMask);
                CurrentWorld.UnregisterSystem<VictimSystem>();
                CurrentWorld.RegisterSystem<NewSystem>();
            }
        }

        private class QueueAddSystem : RecordingSystem
        {
            public override void OnTick(ulong tickMask)
            {
                base.OnTick(tickMask);
                CurrentWorld.RegisterSystem<LateSystem>();
            }
        }

        private class RebuildSystem : RecordingSystem
        {
            public override void OnTick(ulong tickMask)
            {
                base.OnTick(tickMask);
                CurrentWorld.GetManager<SystemManager>().TeardownSystems();
            }
        }
    }
}
```

- [ ] **Step 2: 运行过滤测试，确认失败（红灯）**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --filter FullyQualifiedName~SystemConvergenceTestUnit`
Expected: **FAIL（9 failed / 7 passed，总计 16）**——修复前失败者为 `UnregisterThenRegister_DuringTick_CancelsRemovalAndKeepsInstance`、`UnregisterThenRegister_DuringTick_AppliesRequestedGroupAtNextBeginTick`、`RegisterThenUnregister_DuringTick_CancelsPendingAdd`、`RegisterThenUnregisterThenRegister_DuringTick_InstantiatesOnceAtNextBeginTick`、`ExecuteSystems_ScheduledSequenceIsSnapshot_MidTickRebuildDoesNotExtendIt`、`RegistrationHandles_AfterShutdown_Throw`、`UnregisterThenRegister_DuringTick_IntoDifferentGroup_KeepsAnchors`（评审修订新增：跨组重定位丢锚点）、`RegisterAfterTick_QueuedThenChangable_InstantiatesWithoutDuplicateNode`（评审修订新增：重复节点 `ArgumentException`）、`RegisterThenUnregisterThenRegisterAfterTick_RestoresScheduleNode`（复审修订新增：取消后重注册丢节点）（分别对应"重注册被静默丢弃 + 实例被销毁"、"仅排队注销抛异常"、"无快照导致新系统挤进当前 tick"、"组句柄 shutdown 后不抛"）；其余 7 个为既有行为契约测试，修复前即通过（Step 3 后仍须通过）。典型失败信息：`Expected: not null, But was: null` / `Unexpected exception: InvalidOperationException` / `Expected: 0, But was: 1` / `ArgumentException: An item with the same key has already been added`。

- [ ] **Step 3: 修改 `SystemManager`（10 处，全部为 old/new 精确替换）**

(a) 新增取消标记字段（`SystemManager.cs:96-99` 之后）：

old：

```csharp
        /// <summary>
        /// Queue of system types to be added.
        /// </summary>
        private readonly Queue<Type> m_addSystems = new();
```

new：

```csharp
        /// <summary>
        /// Queue of system types to be added.
        /// </summary>
        private readonly Queue<Type> m_addSystems = new();

        /// <summary>
        /// System types whose queued registration was cancelled by an unregister in the same
        /// non-changable window. They stay in the add queue until the next teardown so the
        /// queue never under-runs while systems are being instantiated; the teardown skips
        /// them when it drains the queue.
        /// </summary>
        private readonly HashSet<Type> m_cancelledAdds = new HashSet<Type>();
```

(b) `TeardownSystems` 实例化循环跳过已取消的排队系统（`SystemManager.cs:205-212`）：

old：

```csharp
            for (var i = m_addSystems.Count; i > 0; i--)
            {
                var systemType = m_addSystems.Dequeue();
                var sys = _instantSystem(systemType);
                m_systemTransformer.Add(systemType, sys);
                m_systems.Add(sys);
                _createSystem(sys);
            }
```

new：

```csharp
            for (var i = m_addSystems.Count; i > 0; i--)
            {
                var systemType = m_addSystems.Dequeue();

                // A queued add can be cancelled by the OnCreate of an earlier system in this
                // same teardown (unregister of a system that was never instantiated); such a
                // type must not be instantiated.
                if (m_cancelledAdds.Remove(systemType)) continue;

                var sys = _instantSystem(systemType);
                m_systemTransformer.Add(systemType, sys);
                m_systems.Add(sys);
                _createSystem(sys);
            }
```

(c) `_rebuildExecutionOrder` 之后新增两个私有辅助（`SystemManager.cs:244` 与 `SystemManager.cs:246` 之间）：

old：

```csharp
            foreach (var pair in m_systemTransformer)
            {
                if (!m_systems.Contains(pair.Value))
                    m_systems.Add(pair.Value);
            }
        }
        
        /// <summary>
        /// Executes all systems that match the specified system mask.
        /// </summary>
```

new：

```csharp
            foreach (var pair in m_systemTransformer)
            {
                if (!m_systems.Contains(pair.Value))
                    m_systems.Add(pair.Value);
            }
        }

        /// <summary>
        /// Cancels a removal enqueued earlier in the current tick. The queue is rebuilt in
        /// place so the remaining removals keep their relative order.
        /// </summary>
        /// <param name="systemType">System type whose pending removal is cancelled.</param>
        private void _cancelPendingRemoval(Type systemType)
        {
            var count = m_delSystems.Count;
            for (var i = 0; i < count; i++)
            {
                var type = m_delSystems.Dequeue();
                if (type != systemType) m_delSystems.Enqueue(type);
            }
        }

        /// <summary>
        /// Moves a registered system node to the requested group when the placement differs.
        /// The node keeps its current position when the group is unchanged. Declared anchors
        /// are copied to the new node so a group change never drops ordering constraints.
        /// A missing node (cancelled then re-registered in the same tick) is re-created.
        /// </summary>
        /// <param name="systemType">Registered system type.</param>
        /// <param name="group">Requested group node.</param>
        private void _repositionSystem(Type systemType, SystemGroupNode group)
        {
            var node = m_schedule.FindSystem(systemType);
            if (node == null)
            {
                m_schedule.AddSystem(systemType, group);
                return;
            }

            if (ReferenceEquals(node.Parent, group)) return;

            var anchors = new List<SystemAnchor>(node.Anchors);
            m_schedule.RemoveSystem(systemType);
            var moved = m_schedule.AddSystem(systemType, group);
            moved.Anchors.AddRange(anchors);
        }
        
        /// <summary>
        /// Executes all systems that match the specified system mask.
        /// </summary>
```

(d) `ExecuteSystems` 序列快照（`SystemManager.cs:250-260`）：

old：

```csharp
        public void ExecuteSystems(ulong systemMask)
        {
            Assertion.IsTrue(m_init, "SystemManager is not initialized yet.");
            Assertion.IsFalse(m_shutdown, "SystemManager has already shutdown.");
            
            for (var i = 0; i < Systems.Count; i++)
            {
                var system = Systems[i];
                _systemPoll(system, systemMask);
            }
        }
```

new：

```csharp
        public void ExecuteSystems(ulong systemMask)
        {
            Assertion.IsTrue(m_init, "SystemManager is not initialized yet.");
            Assertion.IsFalse(m_shutdown, "SystemManager has already shutdown.");

            // The sequence scheduled for the current tick is snapshotted: graph changes made
            // while executing must not shift, skip or extend this tick's execution (spec 6.3).
            var sequence = m_systems.ToArray();
            for (var i = 0; i < sequence.Length; i++)
            {
                _systemPoll(sequence[i], systemMask);
            }
        }
```

(e) `RegisterSystem` 不可变更分支：取消待移除 / 恢复已取消的待添加 / 重定位（`SystemManager.cs:309-316`）：

old：

```csharp
            else
            {
                if (!m_systemTransformer.ContainsKey(systemType) && !m_addSystems.Contains(systemType))
                {
                    m_addSystems.Enqueue(systemType);
                    m_schedule.AddSystem(systemType, group);
                }
            }
```

new：

```csharp
            else
            {
                if (m_cancelledAdds.Remove(systemType))
                {
                    // Re-registering a system whose queued add was cancelled earlier in this
                    // tick restores the registration; the node is re-created and the system
                    // is instantiated at the next BeginTick like any other pending add.
                    m_schedule.AddSystem(systemType, group);
                }
                else if (m_delSystems.Contains(systemType))
                {
                    // Re-registering a system that was unregistered earlier in this tick
                    // cancels the pending removal: the instance is kept (no OnDestroy and no
                    // repeated OnCreate) and only the requested placement may change.
                    _cancelPendingRemoval(systemType);
                    _repositionSystem(systemType, group);
                }
                else if (!m_systemTransformer.ContainsKey(systemType) && !m_addSystems.Contains(systemType))
                {
                    m_addSystems.Enqueue(systemType);
                    m_schedule.AddSystem(systemType, group);
                }
            }
```

(f) `RegisterSystem` 可变更分支：消费仍在排队的 tick 内添加（`SystemManager.cs:307-314`）：

old：

```csharp
            if (m_changable)
            {
                var sys = _instantSystem(systemType);
                m_systemTransformer.Add(systemType, sys);
                m_systems.Add(sys);
                m_schedule.AddSystem(systemType, group);
                _createSystem(sys);
            }
```

new：

```csharp
            if (m_changable)
            {
                if (m_addSystems.Contains(systemType))
                {
                    // A tick-time add is still queued (CleanupSystems only re-enables
                    // structural changes): consume the queue entry and instantiate now,
                    // keeping the existing schedule node instead of adding a duplicate.
                    m_cancelledAdds.Add(systemType);
                    var queued = _instantSystem(systemType);
                    m_systemTransformer.Add(systemType, queued);
                    m_systems.Add(queued);
                    _repositionSystem(systemType, group);
                    _createSystem(queued);
                }
                else
                {
                    var sys = _instantSystem(systemType);
                    m_systemTransformer.Add(systemType, sys);
                    m_systems.Add(sys);
                    m_schedule.AddSystem(systemType, group);
                    _createSystem(sys);
                }
            }
```

(g) `AddSystemAnchor` 补 shutdown 断言（`SystemManager.cs:353-360`）：

old：

```csharp
        internal void AddSystemAnchor(Type systemType, SystemAnchor anchor)
        {
            var node = m_schedule.FindSystem(systemType);
```

new：

```csharp
        internal void AddSystemAnchor(Type systemType, SystemAnchor anchor)
        {
            Assertion.IsFalse(m_shutdown, "SystemManager has already shutdown.");

            var node = m_schedule.FindSystem(systemType);
```

(h) `AddGroupAnchor` 补 shutdown 断言（`SystemManager.cs:368-375`）：

old：

```csharp
        internal void AddGroupAnchor(string groupName, SystemAnchor anchor)
        {
            var node = m_schedule.FindGroup(groupName);
```

new：

```csharp
        internal void AddGroupAnchor(string groupName, SystemAnchor anchor)
        {
            Assertion.IsFalse(m_shutdown, "SystemManager has already shutdown.");

            var node = m_schedule.FindGroup(groupName);
```

(i) `UnregisterSystem` 对仅排队系统取消待添加（`SystemManager.cs:421-442`）：

old：

```csharp
        public void UnregisterSystem(Type systemType)
        {
            Assertion.IsFalse(m_shutdown, "SystemManager has already shutdown.");
            Assertion.IsNotNull(systemType);
            Assertion.IsParentTypeTo<ISystem>(systemType);
            
            if (!m_systemTransformer.TryGetValue(systemType, out var sys))
                throw new InvalidOperationException($"System {systemType.FullName} is not registered.");

            if (m_changable)
            {
                m_systemTransformer.Remove(systemType);
                m_systems.Remove(sys);
                m_schedule.RemoveSystem(systemType);
                _destroySystem(sys);
            }
            else
            {
                if (!m_delSystems.Contains(systemType))
                    m_delSystems.Enqueue(systemType);
            }
        }
```

new：

```csharp
        public void UnregisterSystem(Type systemType)
        {
            Assertion.IsFalse(m_shutdown, "SystemManager has already shutdown.");
            Assertion.IsNotNull(systemType);
            Assertion.IsParentTypeTo<ISystem>(systemType);
            
            if (!m_systemTransformer.TryGetValue(systemType, out var sys))
            {
                // A system registered earlier in this tick is still only queued: cancel the
                // pending add instead of failing, so the graph converges to "not registered"
                // without ever instantiating the system.
                if (m_addSystems.Contains(systemType) && !m_cancelledAdds.Contains(systemType))
                {
                    m_cancelledAdds.Add(systemType);
                    m_schedule.RemoveSystem(systemType);
                    return;
                }

                throw new InvalidOperationException($"System {systemType.FullName} is not registered.");
            }

            if (m_changable)
            {
                m_systemTransformer.Remove(systemType);
                m_systems.Remove(sys);
                m_schedule.RemoveSystem(systemType);
                _destroySystem(sys);
            }
            else
            {
                if (!m_delSystems.Contains(systemType))
                    m_delSystems.Enqueue(systemType);
            }
        }
```

(j) `OnWorldEnded` 清理取消标记（`SystemManager.cs:477-478`）：

old：

```csharp
            m_addSystems.Clear();
            m_delSystems.Clear();
```

new：

```csharp
            m_addSystems.Clear();
            m_delSystems.Clear();
            m_cancelledAdds.Clear();
```

- [ ] **Step 4: 构建 ECS 库（两个 TFM）**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet build ECS/ECS.csproj`
Expected: Build succeeded（net8.0 + netstandard2.1，0 Error）。此时测试项目也已可编译，继续下一步。

- [ ] **Step 5: 运行新增过滤测试**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --filter FullyQualifiedName~SystemConvergenceTestUnit`
Expected: PASS（16 个测试，失败 0）

- [ ] **Step 6: 运行全量测试**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj`
Expected: **506 passed**（Task 2 基线 490 + 新增 16），0 failed；`SystemTestUnit` / `SystemGroupRegistrationTestUnit` / `SystemOrderingTestUnit` / `IntegrationTestUnit` / `StressTestUnit` / `WorldTestUnit` 全部保持通过且未修改。

- [ ] **Step 7: 提交**

```bash
git add ECS/Managers/SystemManager.cs Test/SystemConvergenceTestUnit.cs
git commit -m "feat(core): converge tick-time system graph changes

Apply graph changes made while a tick is running at the next BeginTick
boundary and collapse paired changes to the final graph state:
unregister + re-register in the same tick cancels the pending removal
(reusing the instance and applying the requested group placement),
while register + unregister cancels the pending add so the system is
never instantiated. ExecuteSystems now executes a snapshot of the
sequence scheduled at BeginTick, so mid-tick rebuilds cannot shift,
skip or extend the current tick. Stale registration handles fail after
shutdown instead of writing anchors into a dead schedule."
```

---

## Self-Review 记录

1. **Spec 覆盖**：spec 6.1（组 = 纯排序桶、不承载掩码；组可嵌套；系统与组混排；根为隐式默认组；`Early` / `Later`）与 6.2（`GroupInsertMode` 枚举；`RegisterGroup` / `RegisterSystem` 句柄；Before/After 锚点可为系统类型或组名、可跨层级、允许前向引用；注册到未注册组名抛异常）逐条落地；6.3（展平 / 拓扑排序 / 成环回退 / tick 内收敛）明确划入 Task 2/3，本计划不含。已决事项 8 中"掩码仍留在系统上"由"零改动 `_systemPoll` / `TickGroup`"保证。
2. **占位符扫描**：无 TBD/TODO；测试文件、5 个新增文件、`SystemManager` 6 处改动与 `World` 替换段均为完整代码；命令与预期输出明确（Step 2 红灯为构建失败，Step 9 过滤 16，Step 10 全量 472，失败 0）。
3. **类型一致性**：测试只使用本计划定义的 internal 成员（`SystemManager.Schedule`、`SystemSchedule.Root` / `FindGroup` / `FindSystem`、`SystemGroupNode.Name` / `Parent` / `Children`、`SystemEntryNode.SystemType`、`SystemAnchor.Kind` / `SystemType` / `GroupName`、`SystemAnchorKind.Before` / `After`）与公开 API（`World.RegisterGroup` / `RegisterSystem<T>(group)` / `UnregisterSystem<T>` / `FindSystem<T>`、`GroupInsertMode.Early` / `Later`）；公开句柄方法在两个类型间逐字一致（`Before<T>` / `After<T>` / `Before(string)` / `After(string)`，返回自身类型）。测试私有嵌套系统类型满足 `where T : class, ISystem`。
4. **实勘偏差与处理**：(a) 任务文本的必读清单点名 `ECS/System.cs`、`ECS/WorldManager.cs`、`ECS/Defines/ITickGroup`——三者均不存在：系统就是实现 `ISystem` 的普通类，由 `SystemManager` 经 `IInjectionProxy` 实例化；manager 管线是 `ManagerMediator.cs` + `IWorldManager.cs` + `MinimalWorld.cs`；`TickGroup` 是 `ISystem` 的属性而非独立类型。(b) 任务文本假设存在 v1 `RegisterSystem<T>(string tickGroup)` 签名——不存在，v1 为 `RegisterSystem(Type)` / `RegisterSystem<T>()`（均 void）与 `SystemManager.RegisterSystem(Type)`；按"保留同名 + 可选组参数 + 返回句柄"处理（语句式调用源码兼容），已写入设计说明。(c) 嵌套 API 任务文本未指定，选择 `parentName` 重载（父组须已注册）而非 `.In()` / 点号路径，理由：注册时即可校验、无重挂载状态机、无名称解析规则。(d) 重复组名策略任务文本要求"决定 + 文档化"，选择抛 `InvalidOperationException`（已测）。
5. **行为不变性**：Task 1 不改 `TeardownSystems` / `ExecuteSystems` / `_systemPoll` / `ISystem.TickGroup`，执行顺序仍是 `m_systems` 注册序；树仅元数据。全量 456 个既有测试不得修改、必须保持通过（`SystemTestUnit` / `WorldTestUnit` / `IntegrationTestUnit` / `StressTestUnit` 的系统测试全部走语句式注册，返回类型变更不影响）。新增的树同步（注销 / 清理移除节点）不改变任何既有断言涉及的状态。
6. **测试计数（绑定）**：基线 456 + 新增 16 = **472 passed**；过滤预期 `SystemGroupRegistrationTestUnit` 16，其余 fixture 数量不变。
7. **后续任务衔接**：Task 2 的输入即本任务的 `SystemSchedule`（`Root` + 节点 `Children` 注册序 + 每节点 `Anchors`）；Task 2 需实现展平 → 约束解析（跨层级、前向引用、无法解析记录错误并忽略）→ 拓扑排序 → `m_systems` 重建 → 成环 `Log.Err` + 回退展平序；Task 3 复用 `AddSystem` / `RemoveSystem` 与 v1 的 `m_addSystems` / `m_delSystems` 队列实现 tick 内收敛。本计划的树结构已为三者预留全部信息（注册序、父子关系、锚点声明序）。
8. **（质量评审修订）**：质量审查确认实现与计划逐字一致、执行顺序零改动、48 处既有调用点源码兼容，但发现 1 处 Important：计划设计说明声称"注销 / 清理的树同步（新增行为，已测）"，而 13 个测试只覆盖 `UnregisterSystem` 立即分支；`CleanupSystems` 延迟移除、`OnWorldEnded` → `ClearSystems`、tick 内注册排队路径均无测试（Task 3 正要改造这些队列）。修订：新增 3 个测试——`RegisterSystem_DuringTick_AddsNodeAndInstantiatesAtNextBeginTick`（tick 内注册：节点立即入树、实例在下一个 `BeginTick` 的 Teardown 才创建）、`UnregisterSystem_DuringTick_RemovesNodeAtCleanup`（tick 内注销：节点在 `CleanupSystems` 才移除）、`Shutdown_ClearsSystemNodesAndKeepsGroups`（关停清系统节点、保留组）；并把设计说明的"已测"改为指向这 4 个测试。Task 1 测试数 13 → 16，全量 469 → 472。

9. **（Task 2）Spec 覆盖**：spec 6.3"`TeardownSystems`（`BeginTick`）时：树按注册序展平 → 应用 Before/After 约束 → 拓扑排序 → 生成执行序列"由 `SystemSchedule.BuildExecutionOrder()` + `SystemManager._rebuildExecutionOrder()` 落地；"成环：`Log.Err` + 回退到展平序，保证可运行"由全量回退 + 成环后仍可 tick 的测试钉死；"无约束节点之间以注册序作稳定 tie-break"由字典序最小稳定排序钉死；"锚点允许前向引用、跨层级、无法解析记录错误并忽略"由 `AfterSystem_...ForwardReferencedTarget`、`CrossLevelAnchor_...`、`Unresolvable{System,Group}Anchor_...` 钉死。6.3 的 tick 内收敛（复用/新增/注销、当前 tick 序列不受影响）明确划入 Task 3；Task 2 保持 v1"排队系统在 Teardown 实例化"的时序。
10. **（Task 2）占位符扫描**：无 TBD/TODO；测试文件 18 个测试完整，`SystemSchedule` 新增 `BuildExecutionOrder` + 6 个私有辅助完整，`SystemManager` 改动为精确 old/new；命令与预期输出明确（Step 2 红灯为构建失败，Step 6 过滤 18，Step 7 全量 490）。
11. **（Task 2）类型一致性**：只消费 Task 1 已存在的 internal 成员（`SystemSchedule.Root` / `FindGroup` / `FindSystem`、`SystemGroupNode.Name` / `Children`、`SystemScheduleNode.Parent` / `Anchors`、`SystemEntryNode.SystemType`、`SystemAnchor.Kind` / `SystemType` / `GroupName`、`SystemAnchorKind.Before` / `After`）与既有 `SystemManager.Systems` / `Schedule` / `SystemTransformer`；新增 `BuildExecutionOrder` 返回 `List<Type>`，`_rebuildExecutionOrder` 经 `m_systemTransformer` 映射回 `ISystem` 实例；无新公开 API，`ISystem` / `_systemPoll` / `TickGroup` 零改动。
12. **（Task 2）排序语义（绑定）**：组节点锚点作用于自身子树、组名目标展开为目标组子树（自环跳过、空组 no-op、重复边无副作用）；稳定 tie-break = 每步线性扫描取展平索引最小的入度 0 节点（相对展平序的字典序最小拓扑序；无约束时 == 展平序）；成环为全量回退展平序（非逐分量）；无法解析的单条约束 `Log.Err` 后忽略。
13. **（Task 2）行为不变性**：无锚点时 `BuildExecutionOrder` 输出 == 展平序 == v1 注册序（含 `SystemTestUnit.SystemManager_CanHandleMultipleSystemExecution_OrderTest` 的注册序断言）；`OnSystemTeardown` 在重排后发射，既有测试不订阅该信号；`SystemTestUnit` 直接调用 `TeardownSystems` / `CleanupSystems` 的路径经 `_rebuildExecutionOrder` 后行为一致；既有 472 测试零修改全绿。
14. **（Task 2）实勘偏差与处理**：(a) 任务文本建议"node after group = node after the group's last system in flatten order"——实现选择更强的"组子树全体"语义（对每个组内系统加边），在组内无约束时与"最后一个系统"等价，且不依赖排序中间结果，已写入设计说明。(b) `Log` 只有 `Err(string)` / `Exp(Exception)`（`ECS/Utils/Log.cs:92/102`），无 `Log.Err(Exception)`；无法解析与成环均用 `Log.Err(string)`。(c) `OnWorldStarted` 遗留出队路径（`SystemManager.cs:432-438`）实例化系统但不添加树节点；`World.RegisterSystem` / `GetManager` 的 `Ready` 断言使其不可达，`_rebuildExecutionOrder` 仍保留"已实例化但不在解析结果中 → 追加"的防御分支，当前无测试（不可达，评审知悉）。(d) `netstandard2.1` 无 `PriorityQueue`，稳定选择用 O(n²) 线性扫描（系统数量级，可接受）。
15. **（Task 2）测试计数（绑定）**：Task 1 后基线 472 + 新增 18 = **490 passed**；过滤预期 `SystemOrderingTestUnit` 18，其余 fixture 数量不变。
16. **（Task 2 质量评审修订）**：质量审查确认实现与计划逐字一致、13 项探针中 12 项正确，但发现 2 处 Important：(a) **组自锚 / 祖先锚产生环**——计划绑定"`groupA.Before("groupA")` 退化为 no-op"，实现只跳过对角线，导致组内两两互加边成环，整个序列回退展平序并**丢弃其他合法约束**（探针：`G.Before("G")` + `A.Before<C>` 后合法约束被静默丢弃）；修订 `_applyAnchors`：组目标展开后，若 subject 集合 ⊆ target 集合（自锚/祖先锚）则整条锚跳过（no-op、不记错误），新增 `_isSubset` 辅助与测试 `GroupSelfAnchor_IsNoOpAndKeepsOtherConstraints`（断言顺序 `[B,C,A]`（稳定 tie-break）且 0 条错误）。(b) 实例复用绑定无测试——`TeardownSystems` 复用实例、不重复 `OnCreate` 是 `_rebuildExecutionOrder` 存在的理由，新增 `TeardownSystems_ReusesInstancesAndDoesNotRepeatOnCreate`（3 次 BeginTick/EndTick 后同一实例、`CreateCount == 1`）与 `CreatingSystem` 测试组件。Task 2 测试数 14 → 16，全量 486 → 488。
17. **（Task 2 复审修订）**：复审确认组自锚/祖先锚与实例复用已修复，但发现修复过度：`_isSubset` 守卫对**系统**锚点也生效，而计划绑定"`A.After("本组")` 退化为在本组其他系统之后"（探针：`A("G").After("G")` 实际被整条丢弃，顺序错误）。修订守卫为仅组 subject 生效：`if (node is SystemGroupNode && _isSubset(subjects, targets)) continue;`——系统锚点继续走 `_addEdges` 的对角线跳过，只与同组其他系统建立约束。新增 `SystemAfterOwnGroup_ConstrainsAgainstOtherGroupMembersOnly`（`[B,A]`）与 `SystemBeforeOwnGroup_ConstrainsAgainstOtherGroupMembersOnly`（`[A,B]`）两个测试。Task 2 测试数 16 → 18，全量 488 → 490。

18. **（Task 3）Spec 覆盖**：spec 6.3"tick 内（Update 期间）系统注册图发生更改时，变更统一在下一个 `BeginTick` 应用并重算"——新增系统在下一个 `BeginTick` 的 `TeardownSystems` 实例化 + `OnCreate`（测试 4 / 7 / 10 钉死）；已注册系统复用实例、按新顺序重新放置、不重复 `OnCreate`（测试 1 / 2 / 6 钉死）；已注销系统 `OnDestroy` + 从执行序列移除（`CleanupSystems` 及时销毁，测试 5 / 8 钉死）；"当前 tick 内已排定的执行序列不受影响；`Tick` 执行期间不重建序列"——tick 内变更全部排队 + `ExecuteSystems` 快照（测试 1 / 6 / 8 / 9 钉死）。同一 tick 内成对变更折叠为最终图状态是本次新增的绑定语义（任务文本要求实勘决定，已写入设计说明）。
19. **（Task 3）占位符扫描**：无 TBD/TODO；16 个测试与 10 处 old/new 替换均为完整代码；命令与预期输出明确（Step 2 红灯为 9 failed / 7 passed，Step 5 过滤 16，Step 6 全量 506）。
20. **（Task 3）类型一致性**：测试只使用既有 API——internal `SystemManager.Schedule` / `Systems` / `SystemTransformer` / `OnSystemTeardown` / `OnSystemCleanup`、`SystemSchedule.FindSystem` / `FindGroup`、`SystemEntryNode.Parent`，公开 `World.FindSystem<T>` / `RegisterSystem<T>` / `UnregisterSystem<T>` / `RegisterGroup` / `BeginTick` / `Tick` / `EndTick` / `Shutdown`；生产改动无新公开 API（`m_cancelledAdds` 为私有字段，只经行为断言）；`Signal<T>.Add` / `Emit` 用法与既有 `SignalTestUnit` 一致。
21. **（Task 3）实勘结论与决策**：(a) 同 tick 注销 + 重注册：修复前重注册被静默丢弃、`CleanupSystems` 照常销毁实例（真实缺口）→ 取消待移除 + 实例复用 + 按请求重定位；(b) 仅排队系统的注销：修复前抛 "not registered"（真实缺口）→ 取消待添加，二次注销抛 v1 异常；(c) tick 内锚点 / 建组：已正确，仅补契约测试；(d) 当前 tick 稳定性：受支持路径不改 `m_systems`，但 `ExecuteSystems` 按下标遍历活列表，直接调用公开的 `TeardownSystems` / `CleanupSystems` 会跳系统或把新系统塞进当前 tick（潜在隐患）→ 快照修复；(e) 信号时序：已正确（Teardown 在收敛后、Cleanup 在移除后且 `m_changable = true`），仅补契约测试；(f) shutdown 后过期锚点句柄：系统句柄抛 "not registered"、组句柄静默写入死 schedule（不一致的小缺口）→ 两个锚点方法统一 `!m_shutdown` 断言。取消标记用 `HashSet` 而非出队，是因为 Teardown 实例化循环在进入时捕获队列长度，出队式取消会让 `OnCreate` 中注销排队系统的场景下溢（已写入设计说明）。
22. **（Task 3）行为不变性**：受支持的注册 / 注销 / 锚点路径在 tick 内不改 `m_systems`，快照与活列表逐元素一致（既有 `SystemTestUnit` 直接调用 `TeardownSystems` / `ExecuteSystems` / `CleanupSystems` 的路径行为不变）；`m_cancelledAdds` 仅在成对变更时非空，普通 tick 零影响；`TeardownSystems` 的"仅实例化进入时已排队的系统"语义保持（取消不改变队列结构）；`_systemPoll` / `ISystem.TickGroup` / 掩码过滤零改动；既有 490 个测试零修改全绿。
23. **（Task 3）测试计数（绑定）**：Task 2 后基线 490 + 新增 16 = **506 passed**；过滤预期 `SystemConvergenceTestUnit` 16，其余 fixture 数量不变。
24. **（Task 3）后续衔接**：Phase 4 三任务全部落地后 spec 第 6 节（系统调度）完成，Phase 5（World 合并与生命周期收敛）可开始；本次改动不引入新公开 API，`World` 重构无接口影响；`SystemManager` 内 `OnWorldStarted` 遗留出队路径仍不可达（Task 2 已记录，本次未改）。
25. **（Task 3 质量评审修订）**：质量审查确认实现与计划逐字一致、RED 6/7 与 GREEN 13/13 可复现，但发现 2 处 Important：(a) `_repositionSystem` 跨组重定位时 `RemoveSystem` + `AddSystem` 重建节点、丢失已声明锚点（探针：`A("Late").After<B>()` 后 tick 内 `Unregister + Register("Early")`，下一 BeginTick 顺序变为 `A,B`，约束静默消失）；修订为移动前复制 `node.Anchors` 到新节点。(b) 可变更分支对"tick 内已排队但未实例化"的重复注册会向 schedule 添加重复节点并抛 `ArgumentException`（序列：`BeginTick; Register<A>; EndTick; Register<A>`；该缺陷在基线即存在，但正属本任务收敛语义）；修订为可变更分支先检查 `m_addSystems.Contains`，命中则用 `m_cancelledAdds` 消费排队项、立即实例化并复用既有节点（`_repositionSystem` 按需移动）。新增 2 个回归测试 `UnregisterThenRegister_DuringTick_IntoDifferentGroup_KeepsAnchors`、`RegisterAfterTick_QueuedThenChangable_InstantiatesWithoutDuplicateNode`。Task 3 测试数 13 → 15，全量 503 → 505；Step 3 的 old/new 替换数 9 → 10。
26. **（Task 3 复审修订）**：修复提交后实现者主动指出残留边界：`BeginTick; Register<A>; Unregister<A>; EndTick; Register<A>` 序列中，取消标记 + 节点移除使可变更分支实例化后 `_repositionSystem` 找不到节点而 no-op，系统虽经 `_rebuildExecutionOrder` 防御追加执行、但树中无节点（锚点不可用、`Schedule.FindSystem` 为 null）。修订 `_repositionSystem`：节点缺失时直接 `AddSystem` 重建；新增回归测试 `RegisterThenUnregisterThenRegisterAfterTick_RestoresScheduleNode`。Task 3 测试数 15 → 16，全量 505 → 506。
