# CoreECS v2 Phase 4 调度 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 交付 spec 第 6 节（系统调度）的 Task 1：注册 API 与组树——`GroupInsertMode`、`RegisterGroup`（含嵌套与 Early/Later）、`RegisterSystem<T>([group])`、Before/After 锚点声明与存储、注册校验；不含展平 / 拓扑排序 / 执行顺序变更（Task 2）与 tick 内收敛（Task 3）。

**Architecture:** 在 `SystemManager` 内新增 internal 注册树 `SystemSchedule`（隐式根组 + 组/系统混排子节点 + 声明式锚点），公开 fluent 句柄 `SystemRegistration` / `GroupRegistration` 负责收集锚点；`World` 暴露 `RegisterGroup` / `RegisterSystem<T>(group)`。Task 1 中执行顺序仍沿用 v1 的 `m_systems` 注册顺序，树仅作为调度元数据，既有 456 个测试必须保持全绿。

**Tech Stack:** C# 9（`LangVersion 9`）、`net8.0` + `netstandard2.1`、NUnit 3.14、`dotnet test --filter`

**Spec:** `docs/superpowers/specs/2026-09-17-coreecs-v2-design.md`（6.1 / 6.2、已决事项 8；6.3 的展平 / 拓扑排序与 tick 内收敛属 Task 2/3，不在本计划）

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
| `Test/SystemGroupRegistrationTestUnit.cs` | Task 1 新增：注册 API 与组树契约测试（13 个） |

测试文件统一放 `Test/`，命名 `<TypeName>TestUnit.cs`，风格与现有测试一致（classic asserts；`Test.csproj` 已通过 `<Using Include="NUnit.Framework"/>` 提供全局 using）。`ECS.csproj` 已配置 `<InternalsVisibleTo Include="Test" />`，测试可直接访问 internal `SystemSchedule` / 节点 / 锚点类型。

## 本计划范围边界

本计划只覆盖 spec 6.1 / 6.2 中属于"注册 API 与组树"的部分（Phase 4 三个任务中的 Task 1）：

- **Task 1（本次 dispatch）**：组树数据结构 + `RegisterGroup` / `RegisterSystem` 注册 + fluent 句柄链式 `Before` / `After` + 注册校验（未知组、重复组名、空名）+ 公开 API 切换；锚点只存储不解析
- **Task 2（后续 dispatch 追加）**：`TeardownSystems`（`BeginTick`）展平组树 → 应用 Before/After 约束（跨层级、前向引用、无法解析记录错误并忽略）→ 拓扑排序 → 重建 `m_systems` 执行序列；无约束节点以注册序稳定 tie-break；成环 `Log.Err` + 回退展平序
- **Task 3（后续 dispatch 追加）**：tick 内注册图变更在下一个 `BeginTick` 统一收敛（已注册系统复用实例重排、新增系统实例化 + `OnCreate`、注销系统 `OnDestroy` 并移除；当前 tick 已排定序列不受影响）

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
- Test: `Test/SystemGroupRegistrationTestUnit.cs`（新增，13 个测试）

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
- **注销 / 清理的树同步（新增行为，已测）**：`UnregisterSystem` 立即分支、`CleanupSystems` 出队循环用 `m_schedule.RemoveSystem(type)` 移除系统节点；`OnWorldEnded` 在销毁全部系统后调用 `m_schedule.ClearSystems()`（一并覆盖 `m_addSystems` 中被清空的排队系统；world 关停时不可能处于 ticking，排队系统仅在 ticking 期间产生，`ClearSystems` 是防御性收口）。组节点不删除。
- **文件结构理由**：树模型独立成 `ECS/Managers/SystemSchedule.cs`（`SystemManager.cs` 已 361 行，Task 2 还要加展平 + 拓扑排序，混排会过大）；两个公开句柄各自独立文件，与 `EntityMatcher` 等公开类型放 `ECS/` 根目录的惯例一致。
- **测试计数（绑定）**：基线 456 + 新增 13 = **469 passed**；过滤预期 `SystemGroupRegistrationTestUnit` 13、其余 fixture 数量不变。

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
Expected: PASS（13 个测试，失败 0）

- [ ] **Step 10: 运行全量测试**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj`
Expected: **469 passed**（基线 456 + 新增 13），0 failed

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

## Self-Review 记录

1. **Spec 覆盖**：spec 6.1（组 = 纯排序桶、不承载掩码；组可嵌套；系统与组混排；根为隐式默认组；`Early` / `Later`）与 6.2（`GroupInsertMode` 枚举；`RegisterGroup` / `RegisterSystem` 句柄；Before/After 锚点可为系统类型或组名、可跨层级、允许前向引用；注册到未注册组名抛异常）逐条落地；6.3（展平 / 拓扑排序 / 成环回退 / tick 内收敛）明确划入 Task 2/3，本计划不含。已决事项 8 中"掩码仍留在系统上"由"零改动 `_systemPoll` / `TickGroup`"保证。
2. **占位符扫描**：无 TBD/TODO；测试文件、5 个新增文件、`SystemManager` 6 处改动与 `World` 替换段均为完整代码；命令与预期输出明确（Step 2 红灯为构建失败，Step 9 过滤 13，Step 10 全量 469，失败 0）。
3. **类型一致性**：测试只使用本计划定义的 internal 成员（`SystemManager.Schedule`、`SystemSchedule.Root` / `FindGroup` / `FindSystem`、`SystemGroupNode.Name` / `Parent` / `Children`、`SystemEntryNode.SystemType`、`SystemAnchor.Kind` / `SystemType` / `GroupName`、`SystemAnchorKind.Before` / `After`）与公开 API（`World.RegisterGroup` / `RegisterSystem<T>(group)` / `UnregisterSystem<T>` / `FindSystem<T>`、`GroupInsertMode.Early` / `Later`）；公开句柄方法在两个类型间逐字一致（`Before<T>` / `After<T>` / `Before(string)` / `After(string)`，返回自身类型）。测试私有嵌套系统类型满足 `where T : class, ISystem`。
4. **实勘偏差与处理**：(a) 任务文本的必读清单点名 `ECS/System.cs`、`ECS/WorldManager.cs`、`ECS/Defines/ITickGroup`——三者均不存在：系统就是实现 `ISystem` 的普通类，由 `SystemManager` 经 `IInjectionProxy` 实例化；manager 管线是 `ManagerMediator.cs` + `IWorldManager.cs` + `MinimalWorld.cs`；`TickGroup` 是 `ISystem` 的属性而非独立类型。(b) 任务文本假设存在 v1 `RegisterSystem<T>(string tickGroup)` 签名——不存在，v1 为 `RegisterSystem(Type)` / `RegisterSystem<T>()`（均 void）与 `SystemManager.RegisterSystem(Type)`；按"保留同名 + 可选组参数 + 返回句柄"处理（语句式调用源码兼容），已写入设计说明。(c) 嵌套 API 任务文本未指定，选择 `parentName` 重载（父组须已注册）而非 `.In()` / 点号路径，理由：注册时即可校验、无重挂载状态机、无名称解析规则。(d) 重复组名策略任务文本要求"决定 + 文档化"，选择抛 `InvalidOperationException`（已测）。
5. **行为不变性**：Task 1 不改 `TeardownSystems` / `ExecuteSystems` / `_systemPoll` / `ISystem.TickGroup`，执行顺序仍是 `m_systems` 注册序；树仅元数据。全量 456 个既有测试不得修改、必须保持通过（`SystemTestUnit` / `WorldTestUnit` / `IntegrationTestUnit` / `StressTestUnit` 的系统测试全部走语句式注册，返回类型变更不影响）。新增的树同步（注销 / 清理移除节点）不改变任何既有断言涉及的状态。
6. **测试计数（绑定）**：基线 456 + 新增 13 = **469 passed**；过滤预期 `SystemGroupRegistrationTestUnit` 13，其余 fixture 数量不变。
7. **后续任务衔接**：Task 2 的输入即本任务的 `SystemSchedule`（`Root` + 节点 `Children` 注册序 + 每节点 `Anchors`）；Task 2 需实现展平 → 约束解析（跨层级、前向引用、无法解析记录错误并忽略）→ 拓扑排序 → `m_systems` 重建 → 成环 `Log.Err` + 回退展平序；Task 3 复用 `AddSystem` / `RemoveSystem` 与 v1 的 `m_addSystems` / `m_delSystems` 队列实现 tick 内收敛。本计划的树结构已为三者预留全部信息（注册序、父子关系、锚点声明序）。
