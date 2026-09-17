# CoreECS v2 Phase 5 World 合并与生命周期 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 交付 spec 第 7 节的第一部分（本次 dispatch 只写 Task 1）：删除 `MinimalWorld`，`World` 成为唯一入口并内置核心 managers，`OnRegister` 保留为自定义 manager 注册钩子；生命周期钩子收敛（`OnRegister` 仅首次 / `OnSetup` 每次 / `OnCleanup` 每次、删除 `OnTickBegin`/`OnTick`/`OnTickEnd` 虚钩子）划入 Task 2，本计划只预留接口。

**Architecture:** 把 `ECS/MinimalWorld.cs` 的字段、状态机、DI 与钩子整体搬入 `ECS/World.cs`，`World` 直接实现 `IWorld`（不再派生 `MinimalWorld`）；`Startup` 首次执行时先注册四个核心 manager（`ComponentManager` / `EntityManager` / `EntityMatchManager` / `SystemManager`），再调用新 virtual 钩子 `OnRegister(IManagerRegister)` 注册自定义 manager。原 `MinimalWorld` 的 abstract 钩子全部降级为 `protected virtual` 空实现，`GetInjectionProxyFactory` 降级为 virtual 并返回内置工厂。`OnTickBegin` / `OnTick` / `OnTickEnd` 暂保留为 virtual（默认实现驱动 `SystemManager`），由 Task 2 取消。测试做 3 处机械适配（`MinimalWorld` → `World`、`OnRegisterManager` → `OnRegister`）+ 新增 3 个合并契约测试，全量 506 → 509。

**Tech Stack:** C# 9（`LangVersion 9`）、`net8.0` + `netstandard2.1`、NUnit 3.14、`dotnet test --filter`

**Spec:** `docs/superpowers/specs/2026-09-17-coreecs-v2-design.md`（第 7 节「World 合并与生命周期」第 1-2 条、第 11 节破坏性变更「`MinimalWorld` 删除」、已决事项 9；第 3-5 条钩子收敛与 tick 虚钩子取消属 Task 2）

**Handoff:** `docs/superpowers/plans/2026-09-17-coreecs-v2-handoff.md`（第 2 节 Phase 5 范围与「计划编写子代理单次只写 1-2 个任务」约定）

---

## File Structure

| 文件 | 职责 |
|---|---|
| `ECS/World.cs` | Task 1 重写：唯一入口 `public class World : IWorld`——内置核心 manager 注册 + `OnRegister` 自定义 manager 钩子 + 原 `MinimalWorld` 的状态机 / DI / 生命周期钩子 + 既有公开 API（`GetEntity` / `CreateEntity` / `DestroyEntity` / `Query` / `RegisterSystem` / `RegisterGroup` / `UnregisterSystem` / `CreateCollector` / `FindSystem`）全部保留 |
| `ECS/MinimalWorld.cs` | Task 1 删除：类型整体并入 `World`（spec §7 / §11），不留 obsolete 别名；由反射契约测试钉死类型不存在 |
| `Test/WorldTestUnit.cs` | Task 1 修改（3 处机械适配，测试数量不变仍 24）：`MinimalWorld_LifecycleEvents_AreCalledCorrectly` → `World_LifecycleEvents_AreCalledCorrectly`（`:144`，`new TestMinimalWorld()` → `new TestWorld()`）；`TestMinimalWorld : MinimalWorld` → `TestWorld : World`（`:603`）且 `OnRegisterManager` → `OnRegister`（`:621`）；`TestWorldWithCustomManager : MinimalWorld` → `: World`（`:756`）且 `OnRegisterManager` → `OnRegister`（`:763`） |
| `Test/WorldMergeTestUnit.cs` | Task 1 新增：合并契约测试（3 个）——`MinimalWorld` 已删除且 `World` 为唯一具体入口；子类覆盖 `OnRegister` 时核心 manager 仍内置；`OnRegister` 注册的自定义 manager 可用且生命周期正常 |

## 本计划范围边界

本计划只覆盖 Phase 5 的 **Task 1（World 合并）**：

- **Task 1（本次 dispatch）**：删除 `MinimalWorld`；`World` 直接实现 `IWorld` 并内置核心 managers；`OnRegister` 成为自定义 manager 注册钩子；`MinimalWorld` 的公开面（`TickCount` / `InjectionProxy` / `Ready` / `Ticking` / `GetManager` / `Startup` / `Shutdown` / `BeginTick` / `Tick` / `EndTick`）逐字保留在 `World`；既有测试机械适配 + 3 个合并契约测试；全量 506 → 509
- **Task 2（尚未编写）**：钩子收敛——`RegisterServices` / `OnConstruct` / `OnFirstStart` / `OnStart` 合并为 `OnSetup`（每次 `Startup`）、`OnShutdown` 收敛为 `OnCleanup`（每次 `Shutdown`）；删除 `OnTickBegin` / `OnTick` / `OnTickEnd` 虚钩子，由 `World.BeginTick` / `Tick` / `EndTick` 内部直接驱动 `SystemManager`；适配 `WorldTestUnit` 的钩子断言
- **Task 3（按需，handoff 建议）**：兼容性收口与文档迁移（`README` / `QUICK_START` 对 `MinimalWorld` 的引用，如有）

**不包括**：spec 第 8 节 CommandBuffer（Phase 6）、Phase 6 文档更新；不改 `ManagerMediator` / `IWorld` / `IWorldManager` / `ISystem`；不改 tick 三段的执行顺序与掩码语义。

---

## Task 1: 合并 `MinimalWorld` 到 `World`（核心 manager 内置 + `OnRegister`，钩子收敛留待 Task 2）

**Files:**
- Delete: `ECS/MinimalWorld.cs`
- Rewrite: `ECS/World.cs`（完整新内容见 Step 5）
- Modify: `Test/WorldTestUnit.cs`（测试名 `:144`、构造 `:147`、`TestMinimalWorld` `:602-671`、`TestWorldWithCustomManager` `:755-802`）
- Test: `Test/WorldMergeTestUnit.cs`（新增，3 个测试）

**前置:** 无。本次 dispatch 前实测全量 **506 passed / 0 failed**（`PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj`），双目标库构建 0 错误。

**设计说明（执行时不要改动，评审时按此核对）：**

- **合并范围（实勘结论）**：`MinimalWorld` 是 `public abstract class MinimalWorld : IWorld`，生产代码唯一子类是 `World`；`MinimalWorld` 的全部引用点为 `ECS/MinimalWorld.cs`（定义）、`ECS/World.cs`（基类与类注释）与 `Test/WorldTestUnit.cs` 的 3 处（`:144` 测试名、`:603` `TestMinimalWorld`、`:756` `TestWorldWithCustomManager`），无其他测试或生产子类。handoff 点名的 `ECS/WorldManager.cs` **不存在**：manager 管线是 `ManagerMediator.cs` + `IWorldManager.cs` + `MinimalWorld.cs`，合并只涉及 `MinimalWorld.cs` / `World.cs` 两个文件。
- **类形态（绑定）**：`World` 保持 `public class World`（非 abstract、非 sealed、不派生任何基类），直接实现 `IWorld`。不保留 `MinimalWorld` 作为 obsolete 别名（spec §7 / §11 明确删除）；`Test/WorldMergeTestUnit.cs` 用 `typeof(World).Assembly.GetType("CoreECS.MinimalWorld")` 断言类型不存在。
- **公开面无损失（绑定）**：`MinimalWorld` 的所有 public 成员都被 `World` 继承，合并后逐字搬到 `World`：`TickCount`、`InjectionProxy`、`Ready`、`Ticking`、`GetManager<TMgr>()`、`Startup()`、`Shutdown()`、`BeginTick()`、`Tick(ulong)`、`EndTick()`。`FindSystem<T>()` 与 `#region PublicAPI` 的方法体零改动。唯一消失的是 abstract 类型本身与「子类必须 override 全部钩子」的编译期约束。
- **钩子降级（绑定）**：`GetInjectionProxyFactory` 由 `protected abstract` 变为 `protected virtual`，默认返回 `BuiltinInjectionProxyFactory.Instance`；`OnRegisterManager`、`RegisterServices`、`OnConstruct`、`OnFirstStart`、`OnStart`、`OnTickBegin`、`OnTick`、`OnTickEnd`、`OnShutdown` 由 `protected abstract` 变为 `protected virtual` 空实现；`RegisterRequiredServices` 保持 `protected internal virtual`。`OnStart` / `OnTickBegin` / `OnTick` / `OnTickEnd` 的默认实现分别是「取核心 manager 引用」与「驱动 `SystemManager`」，与原 `World` 的 override 体逐字一致。
- **`OnRegister` 命名决策（合并强制，绑定）**：原 `OnRegisterManager` 由 `World` override 用于注册核心 managers；合并后核心 managers 不再经钩子注册，钩子语义收窄为「注册自定义 manager」，按 spec §7 改名为 `protected virtual void OnRegister(IManagerRegister register)`，空实现。这是 spec 钩子收敛中**被合并强制**的唯一一项（核心注册不再能被子类覆盖丢失）；`OnSetup` / `OnCleanup` 的收敛与 tick 虚钩子删除**不**在本 Task 做，原 `RegisterServices` / `OnConstruct` / `OnFirstStart` / `OnStart` / `OnShutdown` / `OnTick*` 名称与调用时机保持不变，Task 2 统一收敛。
- **核心 manager 内置（绑定）**：`Startup` 首次执行时在创建 `ManagerMediator` 后直接注册四个核心 manager（顺序 `ComponentManager` → `EntityManager` → `EntityMatchManager` → `SystemManager`，与原 `World.OnRegisterManager` 一致），随后才调用 `OnRegister`（包在原有 try/catch + `Log.Exp` 中）。子类覆盖 `OnRegister` 且不注册任何东西时核心 manager 依然存在（新增契约测试钉死）。`RegisterRequiredServices` 在 `OnRegister` 之后调用，因此自定义 manager 的 DI 注册不受影响。
- **子类耦合说明（记录，不改）**：默认 `OnStart` 才把 `Entity` / `Component` / `System` / `EntityMatch` 引用接上；若子类覆盖 `OnStart` 不调 base，又未覆盖 tick 钩子，默认 tick 钩子会 NRE。现有测试子类（`TestWorld`）覆盖了全部 tick 钩子，因此安全；Task 2 删除 tick 虚钩子后该耦合消失，本 Task 不加固。
- **状态机零改动（绑定）**：`Startup` 的 `Assertion.IsFalse(m_init)` / `Assertion.IsFalse(m_shutdown)`、`firstStart = m_mediator == null`、`TickCount = 0`、`m_ticking = false`、`m_mediator.Boot()`、`m_init = true`、首次 `OnFirstStart` 与每次 `OnStart` 的调用顺序；`Shutdown` / `BeginTick` / `Tick` / `EndTick` 的断言、`Log.Exp` 吞异常与状态更新逐行保留。现有断言使 `Startup` 只能成功执行一次，`firstStart` 实际恒为 true；本 Task 原样保留，不引入重启语义。
- **文件布局（绑定）**：合并后 `ECS/World.cs` 约 560 行，与既有 `ECS/Managers/EntityMatchManager.cs`（684 行）同量级；代码库无 partial class 惯例，因此**不拆 partial**，单文件承载。
- **测试计数（绑定）**：基线 506 + 新增 3 = **509 passed**；`WorldTestUnit` 仍 24 个（1 个改名，0 增删）；过滤预期 `WorldMergeTestUnit` 3、`WorldMergeTestUnit|WorldTestUnit` 27。

- [ ] **Step 1: 写失败契约测试（新增 fixture）**

创建 `Test/WorldMergeTestUnit.cs`：

```csharp
using CoreECS.Defines;
using CoreECS.Managers;

namespace CoreECS.Test
{
    /// <summary>
    /// Contract for the Phase 5 World merge: World is the only world type
    /// (MinimalWorld is deleted), core managers are built in so subclasses
    /// cannot lose them, and OnRegister stays the extension point for
    /// custom managers.
    /// </summary>
    [TestFixture]
    public class WorldMergeTestUnit
    {
        [Test]
        public void World_IsSoleConcreteEntryPoint_MinimalWorldRemoved()
        {
            var world = new World();

            Assert.IsFalse(typeof(World).IsAbstract);
            Assert.IsInstanceOf<IWorld>(world);
            Assert.IsNull(typeof(World).Assembly.GetType("CoreECS.MinimalWorld"));
        }

        [Test]
        public void World_BuildsInCoreManagers_EvenWhenOnRegisterIsOverridden()
        {
            var world = new CoreOnlyProbeWorld();
            world.Startup();

            Assert.IsNotNull(world.GetManager<ComponentManager>());
            Assert.IsNotNull(world.GetManager<EntityManager>());
            Assert.IsNotNull(world.GetManager<EntityMatchManager>());
            Assert.IsNotNull(world.GetManager<SystemManager>());

            world.Shutdown();
        }

        [Test]
        public void World_OnRegister_RegistersCustomManagers()
        {
            var world = new CustomManagerProbeWorld();
            world.Startup();

            var manager = world.GetManager<ProbeManager>();
            Assert.IsNotNull(manager);
            Assert.IsTrue(manager.OnManagerCreatedCalled);
            Assert.IsTrue(manager.OnWorldStartedCalled);

            world.Shutdown();
            Assert.IsTrue(manager.OnWorldEndedCalled);
            Assert.IsTrue(manager.OnManagerDestroyedCalled);
        }

        private class CoreOnlyProbeWorld : World
        {
            protected override void OnRegister(IManagerRegister register)
            {
            }
        }

        private class CustomManagerProbeWorld : World
        {
            protected override void OnRegister(IManagerRegister register)
            {
                register.RegisterManager<ProbeManager>();
            }
        }

        private class ProbeManager : IWorldManager
        {
            public bool OnManagerCreatedCalled { get; private set; }

            public bool OnWorldStartedCalled { get; private set; }

            public bool OnWorldEndedCalled { get; private set; }

            public bool OnManagerDestroyedCalled { get; private set; }

            public void OnManagerCreated()
            {
                OnManagerCreatedCalled = true;
            }

            public void OnWorldStarted()
            {
                OnWorldStartedCalled = true;
            }

            public void OnWorldEnded()
            {
                OnWorldEndedCalled = true;
            }

            public void OnManagerDestroyed()
            {
                OnManagerDestroyedCalled = true;
            }
        }
    }
}
```

- [ ] **Step 2: 机械适配 `Test/WorldTestUnit.cs`（3 处）**

(a) 测试方法名与构造（`Test/WorldTestUnit.cs:143-147`）：

old：

```csharp
        [Test]
        public void MinimalWorld_LifecycleEvents_AreCalledCorrectly()
        {
            // Arrange
            var testWorld = new TestMinimalWorld();
```

new：

```csharp
        [Test]
        public void World_LifecycleEvents_AreCalledCorrectly()
        {
            // Arrange
            var testWorld = new TestWorld();
```

(b) 生命周期测试用 world（`Test/WorldTestUnit.cs:602-621`）：

old：

```csharp
        // Test MinimalWorld implementation for testing lifecycle events
        private class TestMinimalWorld : MinimalWorld
        {
            protected override IInjectionProxyFactory GetInjectionProxyFactory()
            {
                return new TestInjectionProxyFactory();
            }

            public bool RegisterManagerCalled { get; private set; }
```

new：

```csharp
        // Test World implementation for testing lifecycle events
        private class TestWorld : World
        {
            protected override IInjectionProxyFactory GetInjectionProxyFactory()
            {
                return new TestInjectionProxyFactory();
            }

            public bool RegisterManagerCalled { get; private set; }
```

并在同文件 `:621-624`：

old：

```csharp
            protected override void OnRegisterManager(IManagerRegister register)
            {
                RegisterManagerCalled = true;
            }
```

new：

```csharp
            protected override void OnRegister(IManagerRegister register)
            {
                RegisterManagerCalled = true;
            }
```

（`RegisterManagerCalled` 属性名保留不改，避免扩大机械适配面；Task 2 收敛钩子时再改名。该类的 `RegisterServices` / `OnConstruct` / `OnFirstStart` / `OnStart` / `OnTickBegin` / `OnTick` / `OnTickEnd` / `OnShutdown` / `RegisterRequiredServices` 覆写全部保留。）

(c) 自定义 manager 测试 world（`Test/WorldTestUnit.cs:755-766`）：

old：

```csharp
        // Custom world to test manager lifecycle
        private class TestWorldWithCustomManager : MinimalWorld
        {
            protected override IInjectionProxyFactory GetInjectionProxyFactory()
            {
                return new TestInjectionProxyFactory();
            }

            protected override void OnRegisterManager(IManagerRegister register)
            {
                // Register our test manager
                register.RegisterManager<IWorldManager, TestWorldManager>();
            }
```

new：

```csharp
        // Custom world to test manager lifecycle
        private class TestWorldWithCustomManager : World
        {
            protected override IInjectionProxyFactory GetInjectionProxyFactory()
            {
                return new TestInjectionProxyFactory();
            }

            protected override void OnRegister(IManagerRegister register)
            {
                // Register our test manager
                register.RegisterManager<IWorldManager, TestWorldManager>();
            }
```

- [ ] **Step 3: 运行过滤测试，确认失败（红灯）**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --filter FullyQualifiedName~WorldMergeTestUnit`
Expected: **FAIL（构建失败，0 个测试执行）**——`World` 尚无 `OnRegister`，典型错误（共 4 处 CS0115）：
`error CS0115: 'WorldMergeTestUnit.CoreOnlyProbeWorld.OnRegister(IManagerRegister)': no suitable method found to override`
（同型错误见 `CustomManagerProbeWorld`、`WorldTestUnit.TestWorld`、`WorldTestUnit.TestWorldWithCustomManager`）。

- [ ] **Step 4: 删除 `ECS/MinimalWorld.cs`**

Run: `git rm ECS/MinimalWorld.cs`
Expected: 文件删除并暂存（`deleted: ECS/MinimalWorld.cs`）。此时构建仍红（`World` 还引用旧基类），下一步重写 `World.cs` 后恢复。

- [ ] **Step 5: 重写 `ECS/World.cs`（完整新文件）**

用以下完整内容覆盖 `ECS/World.cs`：

```csharp
using System;
using CoreECS.Defines;
using CoreECS.Managers;
using CoreECS.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace CoreECS
{
    /// <summary>
    /// The only ECS world implementation and the single entry point of the framework.
    /// It owns the built-in core managers (components, entities, entity matching and
    /// systems) and handles the core lifecycle of managers, ticks and dependency
    /// injection. Subclasses can register additional managers by overriding
    /// <see cref="OnRegister"/> and hook into the remaining lifecycle events.
    /// </summary>
    public class World : IWorld
    {
        #region Private Area

        /// <summary>
        /// Mediator for managing world managers.
        /// </summary>
        private ManagerMediator m_mediator = null;

        /// <summary>
        /// Flag indicating whether the world has been initialized.
        /// </summary>
        private bool m_init = false;

        /// <summary>
        /// Flag indicating whether the world is currently in a tick.
        /// </summary>
        private bool m_ticking = false;

        /// <summary>
        /// Flag indicating whether the world has been shut down.
        /// </summary>
        private bool m_shutdown = false;

        #endregion

        /// <summary>
        /// Gets the current tick count of the world.
        /// This value increments at the beginning of each tick.
        /// </summary>
        public uint TickCount { get; private set; } = 0;

        /// <summary>
        /// Gets the injection proxy built on first startup. Null before <see cref="Startup"/>.
        /// </summary>
        public IInjectionProxy InjectionProxy { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the world is ready for operation.
        /// The world is ready after initialization and before shutdown.
        /// </summary>
        public bool Ready => m_init && !m_shutdown;

        /// <summary>
        /// Gets a value indicating whether the world is currently in a tick.
        /// </summary>
        public bool Ticking => m_ticking;

        /// <summary>
        /// Gets the entity match manager responsible for creating entity collectors.
        /// </summary>
        protected EntityMatchManager EntityMatch { get; private set; }

        /// <summary>
        /// Gets the entity manager responsible for creating and managing entities.
        /// </summary>
        protected EntityManager Entity { get; private set; }

        /// <summary>
        /// Gets the component manager responsible for managing components.
        /// </summary>
        protected ComponentManager Component { get; private set; }

        /// <summary>
        /// Gets the system manager responsible for managing and executing systems.
        /// </summary>
        protected SystemManager System { get; private set; }

        /// <summary>
        /// Gets a manager by its type.
        /// </summary>
        /// <typeparam name="TMgr">The type of manager to retrieve</typeparam>
        /// <returns>The manager instance</returns>
        /// <exception cref="InvalidOperationException">Thrown when the world is not initialized, shut down, or the manager is not found</exception>
        public TMgr GetManager<TMgr>() where TMgr : IWorldManager
        {
            Assertion.IsTrue(m_init, "World is not initialized");
            Assertion.IsFalse(m_shutdown, "World is shutdown");

            if (m_mediator.Managers.TryGetValue(typeof(TMgr), out var manager))
                return (TMgr)manager;

            throw new InvalidOperationException($"Manager of type {typeof(TMgr).Name} not found.");
        }

        /// <summary>
        /// Starts up the world, initializing the built-in core managers and all custom
        /// managers registered by <see cref="OnRegister"/>.
        /// This method should be called once before any ticks are processed.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when the world is already initialized or shut down</exception>
        public void Startup()
        {
            Assertion.IsFalse(m_init, "World is already initialized");
            Assertion.IsFalse(m_shutdown, "World is shutdown");

            // Initialize state
            var firstStart = m_mediator == null;
            TickCount = 0;
            m_ticking = false;

            // Create mediator if not exists
            if (firstStart)
            {
                var factory = GetInjectionProxyFactory();
                var collection = factory.CreateServiceCollection();

                m_mediator = new ManagerMediator(this);

                // Built-in core managers; subclasses add custom managers in OnRegister.
                m_mediator.RegisterManager<ComponentManager>();
                m_mediator.RegisterManager<EntityManager>();
                m_mediator.RegisterManager<EntityMatchManager>();
                m_mediator.RegisterManager<SystemManager>();

                try
                {
                    OnRegister(m_mediator);
                }
                catch (Exception e)
                {
                    Log.Exp(e, nameof(OnRegister));
                }

                RegisterRequiredServices(collection);

                try
                {
                    RegisterServices(collection);
                }
                catch (Exception e)
                {
                    Log.Exp(e, nameof(RegisterServices));
                }

                InjectionProxy = factory.CreateProxy(collection);
                m_mediator.Construct(InjectionProxy);

                try
                {
                    OnConstruct();
                }
                catch (Exception e)
                {
                    Log.Exp(e, nameof(OnConstruct));
                }
            }

            // Boot the mediator
            m_mediator.Boot();

            // Finalize initialization
            m_init = true;

            if (firstStart)
            {
                try
                {
                    OnFirstStart();
                }
                catch (Exception e)
                {
                    Log.Exp(e, nameof(OnFirstStart));
                }
            }

            try
            {
                OnStart();
            }
            catch (Exception e)
            {
                Log.Exp(e, nameof(OnStart));
            }
        }

        /// <summary>
        /// Shuts down the world, releasing all resources.
        /// This method should be called when the world is no longer needed.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when the world is not initialized, already shut down, or currently ticking</exception>
        public void Shutdown()
        {
            Assertion.IsTrue(m_init, "World is not initialized");
            Assertion.IsFalse(m_shutdown, "World is already shutdown");
            Assertion.IsFalse(m_ticking, "World is ticking and should not be shutdown");

            // Call user-defined shutdown logic
            try
            {
                OnShutdown();
            }
            catch (Exception e)
            {
                Log.Exp(e, nameof(OnShutdown));
            }

            // Shutdown mediator
            m_mediator.Shutdown();

            // Update state
            m_init = false;
            m_shutdown = true;
        }

        /// <summary>
        /// Begins a new tick, incrementing the tick count.
        /// This method should be called before processing any systems.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when the world is not initialized, shut down, or already ticking</exception>
        public void BeginTick()
        {
            Assertion.IsTrue(m_init, "World is not initialized");
            Assertion.IsFalse(m_shutdown, "World is shutdown");
            Assertion.IsFalse(m_ticking, "World is already ticking");

            // Increment tick count at the beginning of each tick
            TickCount++;
            m_ticking = true;
            try
            {
                OnTickBegin();
            }
            catch (Exception e)
            {
                Log.Exp(e, nameof(OnTickBegin));
            }
        }

        /// <summary>
        /// Executes the tick, processing systems based on the tick mask.
        /// This method should be called after BeginTick and before EndTick.
        /// </summary>
        /// <param name="tickMask">Optional mask to filter which systems should execute</param>
        /// <exception cref="InvalidOperationException">Thrown when the world is not initialized, shut down, or not in ticking state</exception>
        public void Tick(ulong tickMask = ulong.MaxValue)
        {
            Assertion.IsTrue(m_init, "World is not initialized");
            Assertion.IsFalse(m_shutdown, "World is shutdown");
            Assertion.IsTrue(m_ticking, "World must enter ticking first");

            try
            {
                OnTick(tickMask);
            }
            catch (Exception e)
            {
                Log.Exp(e, nameof(OnTick));
            }
        }

        /// <summary>
        /// Ends the current tick, completing the tick cycle.
        /// This method should be called after all systems have been processed.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when the world is not initialized, shut down, or not in ticking state</exception>
        public void EndTick()
        {
            Assertion.IsTrue(m_init, "World is not initialized");
            Assertion.IsFalse(m_shutdown, "World is shutdown");
            Assertion.IsTrue(m_ticking, "World must be in ticking state");

            try
            {
                OnTickEnd();
            }
            catch (Exception e)
            {
                Log.Exp(e, nameof(OnTickEnd));
            }
            m_ticking = false;
        }

        #region Dependency Injection

        /// <summary>
        /// Returns the factory used to build <see cref="InjectionProxy"/>.
        /// The default implementation uses the built-in dependency injection factory.
        /// </summary>
        protected virtual IInjectionProxyFactory GetInjectionProxyFactory()
        {
            return BuiltinInjectionProxyFactory.Instance;
        }

        /// <summary>
        /// Registers framework-required services into the collection before the proxy is built.
        /// </summary>
        /// <param name="services">The service collection</param>
        protected internal virtual void RegisterRequiredServices(IServiceCollection services)
        {
            services.AddSingleton<IWorld>(this);
            services.AddSingleton(GetType(), this);

            // Register managers.
            foreach (var (_, implementationType) in m_mediator.RegisteredManagers)
            {
                services.AddSingleton(implementationType);
            }
        }

        #endregion

        #region Lifecycle Events

        /// <summary>
        /// Called during the first startup, after the built-in core managers have been
        /// registered. Subclasses can register additional managers with the provided register.
        /// </summary>
        /// <param name="register">The manager register interface</param>
        protected virtual void OnRegister(IManagerRegister register)
        {
        }

        /// <summary>
        /// Registers additional services after <see cref="OnRegister"/>.
        /// </summary>
        /// <param name="services">The service collection</param>
        protected virtual void RegisterServices(IServiceCollection services)
        {
        }

        /// <summary>
        /// Called after all managers have been constructed.
        /// Implementations can perform additional initialization here.
        /// </summary>
        protected virtual void OnConstruct()
        {
        }

        /// <summary>
        /// Called after the world has been fully initialized for the first time.
        /// Implementations can perform startup logic here.
        /// </summary>
        protected virtual void OnFirstStart()
        {
        }

        /// <summary>
        /// Called after the world has been fully initialized.
        /// The default implementation wires the built-in core manager references.
        /// </summary>
        protected virtual void OnStart()
        {
            EntityMatch = GetManager<EntityMatchManager>();
            Entity = GetManager<EntityManager>();
            Component = GetManager<ComponentManager>();
            System = GetManager<SystemManager>();
        }

        /// <summary>
        /// Called at the beginning of each tick.
        /// The default implementation tears down systems to prepare for the new tick.
        /// </summary>
        protected virtual void OnTickBegin()
        {
            System.TeardownSystems();
        }

        /// <summary>
        /// Called during each tick to process systems.
        /// The default implementation executes systems based on the tick mask.
        /// </summary>
        /// <param name="tickMask">The tick mask determining which systems to execute</param>
        protected virtual void OnTick(ulong tickMask)
        {
            System.ExecuteSystems(tickMask);
        }

        /// <summary>
        /// Called at the end of each tick.
        /// The default implementation cleans up systems after execution.
        /// </summary>
        protected virtual void OnTickEnd()
        {
            System.CleanupSystems();
        }

        /// <summary>
        /// Called during world shutdown.
        /// Implementations should release resources here.
        /// </summary>
        protected virtual void OnShutdown()
        {
        }

        #endregion

        /// <summary>
        /// Finds a system of the specified type.
        /// </summary>
        /// <typeparam name="T">The type of system to find, must implement ISystem</typeparam>
        /// <returns>The system instance if found, otherwise null</returns>
        public T FindSystem<T>() where T : class, ISystem
        {
            Assertion.IsTrue(Ready, "World is not ready");

            if (System != null && System.SystemTransformer.TryGetValue(typeof(T), out var system))
            {
                return (T)system;
            }

            return null;
        }

        #region PublicAPI

        /// <summary>
        /// Gets an entity by its ID.
        /// </summary>
        /// <param name="entityId">The ID of the entity to retrieve</param>
        /// <returns>The Entity instance if found, otherwise default(Entity)</returns>
        public Entity GetEntity(ulong entityId)
        {
            if (Entity == null || Component == null)
                throw new InvalidOperationException("Core ECS managers are not available");

            return Entity.GetEntity(entityId);
        }

        /// <summary>
        /// Creates a new entity in the world.
        /// </summary>
        /// <param name="mask">Optional mask for the entity, defaults to ulong.MaxValue</param>
        /// <returns>A new Entity instance</returns>
        /// <exception cref="InvalidOperationException">Thrown when core ECS managers are not available</exception>
        public Entity CreateEntity(ulong mask = ulong.MaxValue)
        {
            Assertion.IsTrue(Ready, "World is not ready");

            if (Entity == null || Component == null)
                throw new InvalidOperationException("Core ECS managers are not available");

            return Entity.CreateEntity(mask);
        }

        /// <summary>
        /// Destroys an entity by its ID.
        /// </summary>
        /// <param name="entityId">The ID of the entity to destroy</param>
        /// <exception cref="InvalidOperationException">Thrown when core ECS managers are not available</exception>
        public void DestroyEntity(ulong entityId)
        {
            Assertion.IsTrue(Ready, "World is not ready");

            if (Entity == null || Component == null)
                throw new InvalidOperationException("Core ECS managers are not available");

            Entity.DestroyEntity(entityId);
        }

        /// <summary>
        /// Destroys an entity.
        /// </summary>
        /// <param name="entity">The entity to destroy</param>
        public void DestroyEntity(Entity entity)
        {
            Assertion.IsTrue(Ready, "World is not ready");

            if (entity.IsValid)
            {
                DestroyEntity(entity.EntityId);
            }
        }

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

        /// <summary>
        /// Unregisters a system from the world.
        /// </summary>
        /// <param name="systemType">The type of system to unregister</param>
        /// <exception cref="InvalidOperationException">Thrown when the world is not ready, system manager is not available, or the system is not registered</exception>
        public void UnregisterSystem(Type systemType)
        {
            Assertion.IsTrue(Ready, "World is not ready");

            if (System == null)
                throw new InvalidOperationException("Core ECS managers are not available");

            System.UnregisterSystem(systemType);
        }

        /// <summary>
        /// Unregisters a system from the world.
        /// </summary>
        /// <typeparam name="T">The type of system to unregister, must implement ISystem</typeparam>
        /// <exception cref="InvalidOperationException">Thrown when the world is not ready, system manager is not available, or the system is not registered</exception>
        public void UnregisterSystem<T>() where T : class, ISystem
        {
            Assertion.IsTrue(Ready, "World is not ready");

            if (System == null)
                throw new InvalidOperationException("Core ECS managers are not available");

            System.UnregisterSystem(typeof(T));
        }

        /// <summary>
        /// Creates a structural-change entity collector for the specified matcher.
        /// </summary>
        /// <param name="matcher">The entity matcher to use for filtering entities</param>
        /// <param name="flag">Flags controlling which events are mirrored into <see cref="IEntityCollector.Changed"/>; defaults to <see cref="EntityCollectorFlag.Default"/></param>
        /// <returns>A new IEntityCollector instance</returns>
        /// <exception cref="InvalidOperationException">Thrown when EntityMatch manager is not available</exception>
        public IEntityCollector CreateCollector(IEntityMatcher matcher,
            EntityCollectorFlag flag = EntityCollectorFlag.Default)
        {
            Assertion.IsTrue(Ready, "World is not ready");

            if (EntityMatch == null)
                throw new InvalidOperationException("Core ECS managers are not available");

            return EntityMatch.MakeCollector(flag, matcher);
        }

        #endregion
    }
}
```

- [ ] **Step 6: 构建 ECS 库（两个 TFM）**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet build ECS/ECS.csproj`
Expected: Build succeeded（net8.0 + netstandard2.1，0 Error）。此时测试项目也已可编译，继续下一步。

- [ ] **Step 7: 运行新增过滤测试（含适配后的 WorldTestUnit）**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --filter "FullyQualifiedName~WorldMergeTestUnit|FullyQualifiedName~WorldTestUnit"`
Expected: PASS（**27 个测试**：`WorldMergeTestUnit` 3 + `WorldTestUnit` 24，失败 0）

- [ ] **Step 8: 运行全量测试**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj`
Expected: **509 passed**（基线 506 + 新增 3），0 failed

- [ ] **Step 9: 提交**

```bash
git add ECS/World.cs ECS/MinimalWorld.cs Test/WorldTestUnit.cs Test/WorldMergeTestUnit.cs
git commit -m "refactor(core): merge minimal world into world

World is now the only world type: it implements IWorld directly and
registers the built-in core managers itself, so subclasses can no longer
lose them by overriding the manager registration hook. OnRegister stays
as the extension point for custom managers. The lifecycle hook
convergence (OnSetup/OnCleanup, removal of the tick hooks) follows in
the next task."
```

---

## Self-Review 记录

1. **Spec 覆盖**：spec §7 第 1-2 条（删除 `MinimalWorld`、`World` 唯一入口、核心 managers 内置、`OnRegister` 保留自定义 manager 注册）由 Step 4 / Step 5 落地并由 `WorldMergeTestUnit` 三个测试钉死；§11 破坏性变更「`MinimalWorld` 删除」由反射断言 `typeof(World).Assembly.GetType("CoreECS.MinimalWorld") == null` 钉死；§12.9 钩子收敛在本 Task 只落地被合并强制的 `OnRegister` 命名与语义，`OnSetup` / `OnCleanup` 与 tick 虚钩子删除明确划入 Task 2。
2. **占位符扫描**：无 TBD/TODO；`ECS/World.cs` 完整文件、`Test/WorldMergeTestUnit.cs` 完整文件、`Test/WorldTestUnit.cs` 3 处 old/new 均可直接应用；命令与预期输出明确（Step 3 红灯为构建失败 CS0115 ×4，Step 7 过滤 27，Step 8 全量 509，失败 0）。
3. **类型一致性**：测试只使用公开 API（`World` / `IWorld` / `GetManager<T>` / `Startup` / `Shutdown` / `IWorldManager` / `IManagerRegister`）与既有 manager 类型（`ComponentManager` / `EntityManager` / `EntityMatchManager` / `SystemManager`）；`OnRegister(IManagerRegister)` 的签名与 `ManagerMediator : IManagerRegister` 的 `RegisterManager<T>()` 一致；`WorldMergeTestUnit` 不触碰 `World` 的 protected 成员，只经 `GetManager` 观察，避免测试对继承细节过拟合。
4. **实勘偏差与处理**：(a) handoff 点名的 `ECS/WorldManager.cs` 不存在——manager 管线是 `ManagerMediator.cs` + `IWorldManager.cs` + `MinimalWorld.cs`，合并只涉及 `MinimalWorld.cs` / `World.cs`（已写入设计说明）。(b) `MinimalWorld` 生产代码唯一子类是 `World`；测试引用共 3 处（`Test/WorldTestUnit.cs:144` / `:603` / `:756`）及两处 `OnRegisterManager` 覆写（`:621` / `:763`），无其他子类（已写入 File Structure 与 Step 2）。(c) `Startup` 现有断言使其只能成功执行一次（`m_init == false` 且 `m_shutdown == false`），`firstStart` 实际恒为 true；本 Task 原样保留，不引入重启语义（Task 2 若需要再议）。(d) 现有 `World.OnStart` 取核心 manager 引用、`OnTick*` 驱动 `SystemManager`；合并后成为 `World` 的默认 virtual 实现，子类覆盖 `OnStart` 不调 base 时必须同时覆盖 tick 钩子（现有测试即如此，已记录不加固）。
5. **行为不变性**：`Startup` / `Shutdown` / `BeginTick` / `Tick` / `EndTick` 的断言、状态机、`Log.Exp` 吞异常与调用顺序逐行保留；四个核心 manager 注册顺序（`ComponentManager` → `EntityManager` → `EntityMatchManager` → `SystemManager`）与原 `World.OnRegisterManager` 一致；`FindSystem` 与 `#region PublicAPI` 方法体零改动；`ManagerMediator` / `IWorld` / `IWorldManager` / `ISystem` 零改动；既有 506 个测试除 `WorldTestUnit` 3 处机械适配外零修改。
6. **测试计数（绑定）**：基线 506 + 新增 3 = **509 passed**；`WorldTestUnit` 仍 24 个（1 个改名，无增删）；过滤预期 `WorldMergeTestUnit` 3、`WorldMergeTestUnit|WorldTestUnit` 27。
7. **后续任务衔接**：Task 2 的输入即本 Task 的 `World`——`RegisterServices` / `OnConstruct` / `OnFirstStart` / `OnStart` 收敛为 `OnSetup`（每次 `Startup`）、`OnShutdown` 收敛为 `OnCleanup`（每次 `Shutdown`）；删除 `OnTickBegin` / `OnTick` / `OnTickEnd` 虚钩子，`BeginTick` / `Tick` / `EndTick` 内部直接驱动 `SystemManager`；同步适配 `WorldTestUnit` 的钩子断言（`TestWorld` 覆写集合收缩）与 `TestWorldWithCustomManager`。本 Task 保留的 `OnRegister` 已是 Task 2 的最终形态，无需再改名。
