# CoreECS v2 Phase 5 World 合并与生命周期 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 交付 spec 第 7 节全部内容：Task 1 删除 `MinimalWorld`，`World` 成为唯一入口并内置核心 managers，`OnRegister` 保留为自定义 manager / 服务注册钩子；Task 2 生命周期钩子收敛为 `OnRegister`（仅首次 `Startup`）/ `OnSetup`（每次 `Startup`）/ `OnCleanup`（每次 `Shutdown`），删除 `OnTickBegin`/`OnTick`/`OnTickEnd` 虚钩子，tick 三段由 `World` 内部驱动。

**Architecture:** 把 `ECS/MinimalWorld.cs` 的字段、状态机、DI 与钩子整体搬入 `ECS/World.cs`，`World` 直接实现 `IWorld`（不再派生 `MinimalWorld`）；`Startup` 首次执行时先注册四个核心 manager（`ComponentManager` / `EntityManager` / `EntityMatchManager` / `SystemManager`），再调用新 virtual 钩子 `OnRegister(IManagerRegister)` 注册自定义 manager。原 `MinimalWorld` 的 abstract 钩子全部降级为 `protected virtual` 空实现，`GetInjectionProxyFactory` 降级为 virtual 并返回内置工厂。`OnTickBegin` / `OnTick` / `OnTickEnd` 暂保留为 virtual（默认实现驱动 `SystemManager`），由 Task 2 取消并改为 `BeginTick` / `Tick` / `EndTick` 内部直接驱动；`RegisterServices` 并入 `OnRegister(IManagerRegister, IServiceCollection)`，`OnConstruct` / `OnFirstStart` / `OnStart` 收敛为 `OnSetup`，`OnShutdown` 收敛为 `OnCleanup`，核心 manager 引用由 `Startup` 内部接线。测试做 3 处机械适配（`MinimalWorld` → `World`、`OnRegisterManager` → `OnRegister`）+ 新增 3 个合并契约测试，全量 506 → 509；Task 2 迁移 `WorldTestUnit` 钩子断言 + 追加 2 个收敛契约测试，全量 509 → 511。

**Tech Stack:** C# 9（`LangVersion 9`）、`net8.0` + `netstandard2.1`、NUnit 3.14、`dotnet test --filter`

**Spec:** `docs/superpowers/specs/2026-09-17-coreecs-v2-design.md`（第 7 节「World 合并与生命周期」第 1-2 条、第 11 节破坏性变更「`MinimalWorld` 删除」、已决事项 9；第 3-5 条钩子收敛与 tick 虚钩子取消属 Task 2）

**Handoff:** `docs/superpowers/plans/2026-09-17-coreecs-v2-handoff.md`（第 2 节 Phase 5 范围与「计划编写子代理单次只写 1-2 个任务」约定）

---

## File Structure

| 文件 | 职责 |
|---|---|
| `ECS/World.cs` | Task 1 重写 + Task 2 收敛：唯一入口 `public class World : IWorld`——内置核心 manager 注册 + `OnRegister(IManagerRegister, IServiceCollection)` 自定义 manager / 服务钩子 + `OnSetup` / `OnCleanup` + 状态机 / DI / tick 三段（内部驱动 `SystemManager`）+ 既有公开 API（`GetEntity` / `CreateEntity` / `DestroyEntity` / `Query` / `RegisterSystem` / `RegisterGroup` / `UnregisterSystem` / `CreateCollector` / `FindSystem`）全部保留 |
| `ECS/MinimalWorld.cs` | Task 1 删除：类型整体并入 `World`（spec §7 / §11），不留 obsolete 别名；由反射契约测试钉死类型不存在 |
| `Test/WorldTestUnit.cs` | Task 1 修改（3 处机械适配）：`MinimalWorld_LifecycleEvents_AreCalledCorrectly` → `World_LifecycleEvents_AreCalledCorrectly`（`:144`）；`TestMinimalWorld : MinimalWorld` → `TestWorld : World`（`:603`）且 `OnRegisterManager` → `OnRegister`（`:621`）；`TestWorldWithCustomManager`（`:756` / `:763`）同理。Task 2 修改（钩子断言迁移）：`World_LifecycleEvents_AreCalledCorrectly` 改为经公开状态（`Ticking` / `TickCount`）与注册系统观察 tick；`TestWorld` 覆写集合收敛为 `OnRegister` / `RegisterRequiredServices` / `OnSetup` / `OnCleanup`（`RegisterManagerCalled` → `OnRegisterCalled`）；`TestWorldWithCustomManager` 仅保留工厂 + `OnRegister`。测试数量始终 24 |
| `Test/WorldMergeTestUnit.cs` | Task 1 新增 3 个合并契约测试；Task 2 追加 2 个收敛契约测试（反射钉死旧虚钩子不存在 + `OnSetup` 覆盖不调 base 时 tick 仍由 `World` 内部驱动）。3 → 5 |
| `docs/QUICK_START.md` / `docs/QUICK_START.zh-CN.md` | Task 2 修改：`:46` / `:47` / `:190` 的 `RegisterServices` 与旧钩子名更新为 `OnRegister` / `OnSetup` / `OnCleanup` |
| `README.md` / `README.zh-CN.md` | Task 2 修改：`:92` 的 `InjectionProxy` 行 `RegisterServices` → `OnRegister` |

## 本计划范围边界

本计划覆盖 Phase 5 的 **Task 1（World 合并）** 与 **Task 2（生命周期钩子收敛）**：

- **Task 1（已完成，commit `8b12d83` + `6713169`）**：删除 `MinimalWorld`；`World` 直接实现 `IWorld` 并内置核心 managers；`OnRegister` 成为自定义 manager 注册钩子；`MinimalWorld` 的公开面（`TickCount` / `InjectionProxy` / `Ready` / `Ticking` / `GetManager` / `Startup` / `Shutdown` / `BeginTick` / `Tick` / `EndTick`）逐字保留在 `World`；既有测试机械适配 + 3 个合并契约测试；全量 506 → 509
- **Task 2（本计划已编写）**：钩子收敛——`OnRegister(IManagerRegister, IServiceCollection)`（首次 `Startup`，吸收 `RegisterServices`）、`OnSetup()`（每次 `Startup`，吸收 `OnConstruct` / `OnFirstStart` / `OnStart`）、`OnCleanup()`（每次 `Shutdown`，吸收 `OnShutdown`）；删除 `OnTickBegin` / `OnTick` / `OnTickEnd` 虚钩子，由 `World.BeginTick` / `Tick` / `EndTick` 内部直接驱动 `SystemManager`；核心引用由 `Startup` 内部接线；迁移 `WorldTestUnit` 钩子断言 + 追加 2 个收敛契约测试；全量 509 → 511；同步 QUICK_START / README 钩子名
- **Task 3（按需，handoff 建议）**：兼容性收口与文档迁移（`README` / `QUICK_START` 对 `MinimalWorld` 的引用，如有）

**不包括**：spec 第 8 节 CommandBuffer（Phase 6）、Phase 6 文档整体更新（本 Task 只修正被钩子删除直接影响的 8 行文档）；不改 `ManagerMediator` / `IWorld` / `IWorldManager` / `ISystem`；不改 tick 三段的执行顺序与掩码语义。

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

- [ ] **Step 10（质量评审修订）: 更新 QUICK_START 的钩子名**

`docs/QUICK_START.md:47` 与 `docs/QUICK_START.zh-CN.md:47` 仍教用户重写 `OnRegisterManager`（本 Task 已删除该名，子类按文档操作会得到 CS0115）；Task 3 的迁移清单只覆盖 `MinimalWorld` 引用，不会触及此处。两行改为 `OnRegister`：

```bash
git add docs/QUICK_START.md docs/QUICK_START.zh-CN.md
git commit -m "doc(proj): update quick start lifecycle hook name"
```

---

## Task 2: 生命周期钩子收敛（`OnRegister` / `OnSetup` / `OnCleanup`，删除 tick 虚钩子）

**Files:**
- Rewrite: `ECS/World.cs`（完整新内容见 Step 4）
- Modify: `Test/WorldTestUnit.cs`（生命周期测试 `:143-173`、`TestWorld` `:602-671`、`TestWorldWithCustomManager` `:755-802`）
- Modify: `Test/WorldMergeTestUnit.cs`（新增 usings + 2 个契约测试 + 2 个探针类型）
- Modify: `docs/QUICK_START.md`、`docs/QUICK_START.zh-CN.md`、`README.md`、`README.zh-CN.md`（钩子名同步，Step 8）

**前置:** Task 1 已完成（commit `8b12d83` + `6713169`）；本次实测全量 **509 passed / 0 failed**（`PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj`），`ECS/World.cs` 617 行。

**设计说明（执行时不要改动，评审时按此核对）：**

- **钩子映射（绑定）**：

  | 现钩子 | Task 2 后 | 调用时机 |
  |---|---|---|
  | `OnRegister(IManagerRegister)` | `OnRegister(IManagerRegister, IServiceCollection)` | 首次 `Startup`：核心 manager 注册之后、`RegisterRequiredServices` 之前；managers 与服务同钩子注册（spec §7「注册 managers / 服务」） |
  | `RegisterServices(IServiceCollection)` | 并入 `OnRegister` 的 `services` 参数 | 同上（不再单独存在） |
  | `OnConstruct()` | 并入 `OnSetup()` | 由「`Construct` 之后、`Boot` 之前」变为「`Boot` + `m_init = true` + 核心引用接线之后」（每次 `Startup`） |
  | `OnFirstStart()` | 并入 `OnSetup()` | 每次 `Startup`（spec §7「可自行判断是否首次」；当前断言下 `Startup` 只能成功一次，两者当前等价） |
  | `OnStart()` | 删除；默认体（取四个核心 manager 引用）上移为 `Startup` 内部接线 | 每次 `Startup`，在 `OnSetup()` 之前 |
  | `OnTickBegin()` / `OnTick(mask)` / `OnTickEnd()` | 删除；`BeginTick` / `Tick` / `EndTick` 内部直接调用 `System.TeardownSystems()` / `ExecuteSystems(mask)` / `CleanupSystems()` | 每 tick，与旧默认实现逐行等价 |
  | `OnShutdown()` | `OnCleanup()` | 每次 `Shutdown`，`m_mediator.Shutdown()` 之前 |
  | `RegisterRequiredServices(IServiceCollection)` | 保留（`protected internal virtual`）；内部 `AddSingleton` → `TryAddSingleton` / `TryAdd(ServiceDescriptor)`（见 DI 优先级说明） | 首次 `Startup`，`OnRegister` 之后 |

- **`OnRegister` 签名扩展（绑定）**：spec §7 明确 `OnRegister` 负责「注册 managers / 服务」，而服务必须在 `CreateProxy` / `Construct` 之前进入集合；Task 1 备注「`OnRegister` 已是 Task 2 最终形态」指名称，本 Task 仅扩展参数。`RegisterRequiredServices` 必须晚于 `OnRegister`（需枚举 `RegisteredManagers` 中的自定义 manager），因此用户服务先注册。
- **DI 优先级保持（绑定）**：旧顺序是 `RegisterRequiredServices`（`AddSingleton`）→ `RegisterServices`（`AddSingleton`），重复注册时用户后注册者胜；新顺序用户先注册，故框架侧改 `TryAddSingleton<IWorld>` / `TryAddSingleton(Type)` / `TryAdd(new ServiceDescriptor(GetType(), this))`（需 `using Microsoft.Extensions.DependencyInjection.Extensions;`）以保持「用户注册优先」。注意 `Microsoft.Extensions.DependencyInjection` 10.0.8 **没有** `TryAddSingleton(Type, object)` 重载（实勘：只有 `(Type)` / `(Type, Type)` / `(Type, Func<IServiceProvider, object>)`），故具体 world 类型必须走 `TryAdd(ServiceDescriptor)`。常规无重复用例行为不变；`TestWorld.RegisterRequiredServices` 覆写仍调用 base。
- **核心引用内部接线（绑定）**：`EntityMatch` / `Entity` / `Component` / `System` 由 `Startup` 在 `m_init = true` 之后、`OnSetup()` 之前接线，不再依赖子类调 base；消除 Task 1 记录的「覆盖 `OnStart` 不调 base + 未覆盖 tick 钩子 → NRE」耦合，`WorldMergeTestUnit` 新增测试钉死。
- **tick 内部驱动（绑定）**：`BeginTick` / `Tick` / `EndTick` 的断言、`TickCount++`、`m_ticking` 状态与 `Log.Exp` 吞异常行为逐行保留；`Log.Exp` 上下文名由 `nameof(OnTickBegin)` 等改为 `nameof(BeginTick)` / `nameof(Tick)` / `nameof(EndTick)`。子类不再能抑制或替换 Teardown / Execute / Cleanup。
- **时序差异记录（记录，不改）**：`OnConstruct` 的语义从「构造后、启动前」变为 `OnSetup` 的「启动后」；无测试观察该相对时序（`TestWorldWithCustomManager.OnConstruct` 原为空体）。`OnFirstStart` 与 `OnStart` 的区分消失；`Startup` 断言使其只能成功一次，无实际差异。
- **测试观察迁移（绑定）**：`TestWorld` 删除全部旧钩子覆写与 flag，保留 `OnRegisterCalled`（原 `RegisterManagerCalled` 更名，评审 Minor）、`RegisterRequiredServiceCalled`，新增 `SetupCalled` / `CleanupCalled`；`World_LifecycleEvents_AreCalledCorrectly` 用公开状态（`Ticking` / `TickCount`）与注册的 `TestSystem` 观察 tick。`TestWorldWithCustomManager` 仅保留工厂 + `OnRegister`，其 tick 测试继续通过（内部接线 + 内部驱动保证）。
- **测试计数（绑定）**：基线 509 + `WorldMergeTestUnit` 新增 2 = **511 passed**；`WorldTestUnit` 仍 24（0 增删，1 个测试体改写）；过滤预期 `WorldMergeTestUnit` 5、`WorldMergeTestUnit|WorldTestUnit` 29。
- **文档同步（绑定）**：`docs/QUICK_START.md:46,47,190`、`docs/QUICK_START.zh-CN.md:46,47,190`、`README.md:92`、`README.zh-CN.md:92` 仍指向 `RegisterServices` / 旧钩子名；Task 3 清单只覆盖 `MinimalWorld` 引用，故本 Task 一并修正（独立 `doc(proj)` 提交）。

- [ ] **Step 1: 追加失败契约测试（`Test/WorldMergeTestUnit.cs`）**

(a) usings（文件头）：

old：

```csharp
using CoreECS.Defines;
using CoreECS.Managers;
```

new：

```csharp
using System.Reflection;
using CoreECS.Defines;
using CoreECS.Managers;
using Microsoft.Extensions.DependencyInjection;
```

(b) 在 `World_OnRegister_RegistersCustomManagers` 方法之后、`private class CoreOnlyProbeWorld` 之前插入两个测试：

```csharp
        [Test]
        public void World_HookSurface_IsConvergedToRegisterSetupCleanup()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;

            Assert.IsNull(typeof(World).GetMethod("OnTickBegin", flags));
            Assert.IsNull(typeof(World).GetMethod("OnTick", flags));
            Assert.IsNull(typeof(World).GetMethod("OnTickEnd", flags));
            Assert.IsNull(typeof(World).GetMethod("RegisterServices", flags));
            Assert.IsNull(typeof(World).GetMethod("OnConstruct", flags));
            Assert.IsNull(typeof(World).GetMethod("OnFirstStart", flags));
            Assert.IsNull(typeof(World).GetMethod("OnStart", flags));
            Assert.IsNull(typeof(World).GetMethod("OnShutdown", flags));

            var setup = typeof(World).GetMethod("OnSetup", flags);
            Assert.IsNotNull(setup);
            Assert.IsTrue(setup!.IsVirtual);
            Assert.AreEqual(0, setup.GetParameters().Length);

            var cleanup = typeof(World).GetMethod("OnCleanup", flags);
            Assert.IsNotNull(cleanup);
            Assert.IsTrue(cleanup!.IsVirtual);
            Assert.AreEqual(0, cleanup.GetParameters().Length);

            var register = typeof(World).GetMethod("OnRegister", flags);
            Assert.IsNotNull(register);
            Assert.IsTrue(register!.IsVirtual);
            CollectionAssert.AreEqual(
                new[] { typeof(IManagerRegister), typeof(IServiceCollection) },
                register.GetParameters().Select(p => p.ParameterType).ToArray());
        }

        [Test]
        public void World_TicksSystems_WhenOnSetupIsOverriddenWithoutBase()
        {
            var world = new SetupProbeWorld();
            world.Startup();
            world.RegisterSystem<TickProbeSystem>();

            world.BeginTick();
            world.Tick();
            world.EndTick();

            Assert.IsTrue(world.SetupCalled);
            Assert.AreEqual(1u, world.TickCount);
            Assert.IsTrue(world.FindSystem<TickProbeSystem>().TickCalled);

            world.Shutdown();
        }
```

(c) 在 `CustomManagerProbeWorld` 之后插入两个探针类型：

```csharp
        private class SetupProbeWorld : World
        {
            public bool SetupCalled { get; private set; }

            protected override void OnSetup()
            {
                SetupCalled = true;
            }
        }

        private class TickProbeSystem : ISystem
        {
            public bool TickCalled { get; private set; }

            public void OnTick(ulong tickMask)
            {
                TickCalled = true;
            }
        }
```

(d) 两个既有探针的 `OnRegister` 迁移到双参签名（Task 1 引入的覆写，本 Task 必须同步，否则 Step 4 后 CS0115）：

old：

```csharp
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
```

new：

```csharp
        private class CoreOnlyProbeWorld : World
        {
            protected override void OnRegister(IManagerRegister register, IServiceCollection services)
            {
            }
        }

        private class CustomManagerProbeWorld : World
        {
            protected override void OnRegister(IManagerRegister register, IServiceCollection services)
            {
                register.RegisterManager<ProbeManager>();
            }
        }
```

- [ ] **Step 2: 迁移 `Test/WorldTestUnit.cs`（3 处）**

> 注意：该文件多处空行带行尾空格（`TestWorld` 内多为 12 个空格）；应用 old/new 前先 `Read` 目标区段，以实际文本为准（或把替换锚点放在非空行上）。

(a) 生命周期测试 `:143-173`：

old：

```csharp
        [Test]
        public void World_LifecycleEvents_AreCalledCorrectly()
        {
            // Arrange
            var testWorld = new TestWorld();
            
            // Act
            testWorld.Startup();
            
            // Assert
            Assert.IsTrue(testWorld.RegisterManagerCalled);
            Assert.IsTrue(testWorld.ConstructCalled);
            Assert.IsTrue(testWorld.FirstStartCalled);
            Assert.IsTrue(testWorld.StartCalled);
            
            // Act
            testWorld.BeginTick();
            testWorld.Tick();
            testWorld.EndTick();
            
            // Assert
            Assert.IsTrue(testWorld.TickBeginCalled);
            Assert.IsTrue(testWorld.TickCalled);
            Assert.IsTrue(testWorld.TickEndCalled);
            
            // Act
            testWorld.Shutdown();
            
            // Assert
            Assert.IsTrue(testWorld.ShutdownCalled);
        }
```

new：

```csharp
        [Test]
        public void World_LifecycleEvents_AreCalledCorrectly()
        {
            // Arrange
            var testWorld = new TestWorld();

            // Act
            testWorld.Startup();

            // Assert
            Assert.IsTrue(testWorld.OnRegisterCalled);
            Assert.IsTrue(testWorld.RegisterRequiredServiceCalled);
            Assert.IsTrue(testWorld.SetupCalled);
            Assert.IsFalse(testWorld.CleanupCalled);

            // Act - the world drives the system manager internally
            testWorld.RegisterSystem<TestSystem>();
            testWorld.BeginTick();
            Assert.IsTrue(testWorld.Ticking);
            Assert.AreEqual(1u, testWorld.TickCount);
            testWorld.Tick();
            Assert.IsTrue(testWorld.FindSystem<TestSystem>().OnTickCalled);
            testWorld.EndTick();
            Assert.IsFalse(testWorld.Ticking);

            // Act
            testWorld.Shutdown();

            // Assert
            Assert.IsTrue(testWorld.CleanupCalled);
        }
```

(b) `TestWorld` 整体替换 `:602-671`：

old：

```csharp
        // Test World implementation for testing lifecycle events
        private class TestWorld : World
        {
            protected override IInjectionProxyFactory GetInjectionProxyFactory()
            {
                return new TestInjectionProxyFactory();
            }

            public bool RegisterManagerCalled { get; private set; }
            public bool RegisterRequiredServiceCalled { get; private set; }
            public bool RegisterServiceCalled { get; private set; }
            public bool ConstructCalled { get; private set; }
            public bool FirstStartCalled { get; private set; }
            public bool StartCalled { get; private set; }
            public bool TickBeginCalled { get; private set; }
            public bool TickCalled { get; private set; }
            public bool TickEndCalled { get; private set; }
            public bool ShutdownCalled { get; private set; }
            
            protected override void OnRegister(IManagerRegister register)
            {
                RegisterManagerCalled = true;
            }

            protected internal override void RegisterRequiredServices(IServiceCollection services)
            {
                base.RegisterRequiredServices(services);
                RegisterRequiredServiceCalled = true;
            }

            protected override void RegisterServices(IServiceCollection services)
            {
                RegisterServiceCalled = true;
            }

            protected override void OnConstruct()
            {
                ConstructCalled = true;
            }

            protected override void OnFirstStart()
            {
                FirstStartCalled = true;
            }

            protected override void OnStart()
            {
                StartCalled = true;
            }
            
            protected override void OnTickBegin()
            {
                TickBeginCalled = true;
            }
            
            protected override void OnTick(ulong tickMask)
            {
                TickCalled = true;
            }
            
            protected override void OnTickEnd()
            {
                TickEndCalled = true;
            }
            
            protected override void OnShutdown()
            {
                ShutdownCalled = true;
            }
        }
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

            public bool OnRegisterCalled { get; private set; }
            public bool RegisterRequiredServiceCalled { get; private set; }
            public bool SetupCalled { get; private set; }
            public bool CleanupCalled { get; private set; }

            protected override void OnRegister(IManagerRegister register, IServiceCollection services)
            {
                OnRegisterCalled = true;
            }

            protected internal override void RegisterRequiredServices(IServiceCollection services)
            {
                base.RegisterRequiredServices(services);
                RegisterRequiredServiceCalled = true;
            }

            protected override void OnSetup()
            {
                SetupCalled = true;
            }

            protected override void OnCleanup()
            {
                CleanupCalled = true;
            }
        }
```

(c) `TestWorldWithCustomManager` 整体替换 `:755-802`：

old：

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

            protected override void RegisterServices(IServiceCollection services)
            {
            }

            protected override void OnConstruct()
            {
                // Manager should be constructed here
            }

            protected override void OnFirstStart()
            {
                
            }

            protected override void OnStart()
            {
            }
            
            protected override void OnTickBegin()
            {
            }
            
            protected override void OnTick(ulong tickMask)
            {
            }
            
            protected override void OnTickEnd()
            {
            }
            
            protected override void OnShutdown()
            {
            }
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

            protected override void OnRegister(IManagerRegister register, IServiceCollection services)
            {
                // Register our test manager
                register.RegisterManager<IWorldManager, TestWorldManager>();
            }
        }
```

- [ ] **Step 3: 运行过滤测试，确认失败（红灯）**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --filter FullyQualifiedName~WorldMergeTestUnit`

Expected: **构建失败，0 个测试执行**——`World` 尚无 `OnSetup` / `OnCleanup` 且 `OnRegister` 仍是单参，共 7 处 `CS0115`，典型错误：

```
error CS0115: 'WorldMergeTestUnit.SetupProbeWorld.OnSetup()': no suitable method found to override
error CS0115: 'WorldTestUnit.TestWorld.OnRegister(IManagerRegister, IServiceCollection)': no suitable method found to override
```

（同型错误另见：`WorldTestUnit.TestWorld.OnSetup` / `.OnCleanup`、`WorldTestUnit.TestWorldWithCustomManager.OnRegister`、`WorldMergeTestUnit.CoreOnlyProbeWorld.OnRegister`、`WorldMergeTestUnit.CustomManagerProbeWorld.OnRegister`。）

- [ ] **Step 4: 重写 `ECS/World.cs`（完整新文件）**

用以下完整内容覆盖 `ECS/World.cs`：

```csharp
using System;
using CoreECS.Defines;
using CoreECS.Managers;
using CoreECS.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CoreECS
{
    /// <summary>
    /// The only ECS world implementation and the single entry point of the framework.
    /// It owns the built-in core managers (components, entities, entity matching and
    /// systems) and handles the core lifecycle of managers, ticks and dependency
    /// injection. Subclasses can register additional managers and services by overriding
    /// <see cref="OnRegister"/> and hook into startup and cleanup through
    /// <see cref="OnSetup"/> and <see cref="OnCleanup"/>.
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
        /// managers registered by <see cref="OnRegister"/>, then calls <see cref="OnSetup"/>.
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
                    OnRegister(m_mediator, collection);
                }
                catch (Exception e)
                {
                    Log.Exp(e, nameof(OnRegister));
                }

                RegisterRequiredServices(collection);

                InjectionProxy = factory.CreateProxy(collection);
                m_mediator.Construct(InjectionProxy);
            }

            // Boot the mediator
            m_mediator.Boot();

            // Finalize initialization
            m_init = true;

            // Wire the built-in core manager references before OnSetup so that
            // overriding OnSetup can never break tick processing.
            EntityMatch = GetManager<EntityMatchManager>();
            Entity = GetManager<EntityManager>();
            Component = GetManager<ComponentManager>();
            System = GetManager<SystemManager>();

            try
            {
                OnSetup();
            }
            catch (Exception e)
            {
                Log.Exp(e, nameof(OnSetup));
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

            // Call user-defined cleanup logic
            try
            {
                OnCleanup();
            }
            catch (Exception e)
            {
                Log.Exp(e, nameof(OnCleanup));
            }

            // Shutdown mediator
            m_mediator.Shutdown();

            // Update state
            m_init = false;
            m_shutdown = true;
        }

        /// <summary>
        /// Begins a new tick, incrementing the tick count and tearing down the system
        /// schedule so pending registration changes are applied.
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
                System.TeardownSystems();
            }
            catch (Exception e)
            {
                Log.Exp(e, nameof(BeginTick));
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
                System.ExecuteSystems(tickMask);
            }
            catch (Exception e)
            {
                Log.Exp(e, nameof(Tick));
            }
        }

        /// <summary>
        /// Ends the current tick, cleaning up systems after execution.
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
                System.CleanupSystems();
            }
            catch (Exception e)
            {
                Log.Exp(e, nameof(EndTick));
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
        /// Services registered by <see cref="OnRegister"/> take precedence for the same service type.
        /// </summary>
        /// <param name="services">The service collection</param>
        protected internal virtual void RegisterRequiredServices(IServiceCollection services)
        {
            services.TryAddSingleton<IWorld>(this);
            services.TryAdd(new ServiceDescriptor(GetType(), this));

            // Register managers.
            foreach (var (_, implementationType) in m_mediator.RegisteredManagers)
            {
                services.TryAddSingleton(implementationType);
            }
        }

        #endregion

        #region Lifecycle Events

        /// <summary>
        /// Called during the first startup, after the built-in core managers have been
        /// registered and before framework services are added. Subclasses can register
        /// additional managers and services here.
        /// </summary>
        /// <param name="register">The manager register interface</param>
        /// <param name="services">The service collection used to build the injection proxy</param>
        protected virtual void OnRegister(IManagerRegister register, IServiceCollection services)
        {
        }

        /// <summary>
        /// Called after the world has been fully initialized on every startup.
        /// The built-in core manager references are already wired when this runs.
        /// Implementations can track their own state to detect the first startup.
        /// </summary>
        protected virtual void OnSetup()
        {
        }

        /// <summary>
        /// Called on every shutdown before the managers are shut down.
        /// Implementations should release resources here.
        /// </summary>
        protected virtual void OnCleanup()
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

- [ ] **Step 5: 构建 ECS 库（两个 TFM）**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet build ECS/ECS.csproj`

Expected: Build succeeded（net8.0 + netstandard2.1，0 Error）。

- [ ] **Step 6: 运行过滤测试（绿）**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --filter "FullyQualifiedName~WorldMergeTestUnit|FullyQualifiedName~WorldTestUnit"`

Expected: PASS（**29 个测试**：`WorldMergeTestUnit` 5 + `WorldTestUnit` 24，失败 0）

- [ ] **Step 7: 运行全量测试**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj`

Expected: **511 passed**（基线 509 + 新增 2），0 failed

- [ ] **Step 8: 同步文档钩子名（4 个文件各 1-3 行）**

`docs/QUICK_START.md`：

old：

```markdown
- Override `RegisterServices` to register DI services (built on first `Startup()`)
- Override lifecycle hooks (`OnRegister`, `OnConstruct`, `OnStart`, tick/shutdown) or register extra managers
```

new：

```markdown
- Override `OnRegister(register, services)` to register extra managers and DI services (built on first `Startup()`)
- Override lifecycle hooks (`OnSetup`, `OnCleanup`)
```

以及 `:190`：

old：

```markdown
- Register dependencies in `RegisterServices`; the world resolves constructor parameters via `IInjectionProxy`.
```

new：

```markdown
- Register dependencies in `OnRegister`; the world resolves constructor parameters via `IInjectionProxy`.
```

`docs/QUICK_START.zh-CN.md`：

old：

```markdown
- 重写 `RegisterServices` 注册 DI 服务（在首次 `Startup()` 时构建）
- 重写生命周期钩子（`OnRegister`、`OnConstruct`、`OnStart`、Tick/关闭等）或注册额外管理器
```

new：

```markdown
- 重写 `OnRegister(register, services)` 注册额外管理器与 DI 服务（在首次 `Startup()` 时构建）
- 重写生命周期钩子（`OnSetup`、`OnCleanup`）
```

以及 `:190`：

old：

```markdown
- 在 `RegisterServices` 中注册依赖；World 通过 `IInjectionProxy` 解析构造函数参数。
```

new：

```markdown
- 在 `OnRegister` 中注册依赖；World 通过 `IInjectionProxy` 解析构造函数参数。
```

`README.md:92`：

old：

```markdown
| **InjectionProxy** | DI for system constructors (`RegisterServices`) |
```

new：

```markdown
| **InjectionProxy** | DI for system constructors (`OnRegister`) |
```

`README.zh-CN.md:92`：

old：

```markdown
| **InjectionProxy** | 通过 `RegisterServices` 为系统构造函数提供 DI |
```

new：

```markdown
| **InjectionProxy** | 通过 `OnRegister` 为系统构造函数提供 DI |
```

- [ ] **Step 9: 提交核心与测试**

```bash
git add ECS/World.cs Test/WorldTestUnit.cs Test/WorldMergeTestUnit.cs
git commit -m "refactor(core): converge world lifecycle hooks

World now exposes OnRegister (first startup, managers and services),
OnSetup (every startup) and OnCleanup (every shutdown) instead of the
OnConstruct/OnFirstStart/OnStart/RegisterServices/OnShutdown set. The
OnTickBegin/OnTick/OnTickEnd virtual hooks are gone: BeginTick, Tick
and EndTick drive SystemManager directly, and the core manager
references are wired by the world itself so overriding OnSetup can no
longer break tick processing."
```

- [ ] **Step 10: 提交文档**

```bash
git add docs/QUICK_START.md docs/QUICK_START.zh-CN.md README.md README.zh-CN.md
git commit -m "doc(proj): update lifecycle hook names in docs"
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
8. **（质量评审修订）**：质量审查确认合并行为保持、核心 manager 不可再被子类覆盖丢失，但发现 1 处 Important：`docs/QUICK_START.md:47` 与 `docs/QUICK_START.zh-CN.md:47` 仍教用户重写 `OnRegisterManager`（本 Task 已删除该名，照做会 CS0115），而 Task 3 的迁移清单只覆盖 `MinimalWorld` 引用不会触及；新增 Step 10 在提交后以 `doc(proj): update quick start lifecycle hook name` 修正两行。另有两处 Minor 转入 Task 2：`Test/WorldTestUnit.cs:610` 的 `RegisterManagerCalled` 字段名待随钩子收敛一并改名；`WorldMergeTestUnit` 自定义 manager 用例可补核心 manager 共存断言（可选）。

**Task 2 自评：**

9. **Spec 覆盖（Task 2）**：spec §7 全部 5 条由 Task 2 落地——`OnRegister` 仅首次 `Startup`（并吸收 `RegisterServices`，对应「注册 managers / 服务」）、`OnSetup` 每次 `Startup`（吸收 `OnConstruct` / `OnFirstStart` / `OnStart`，对应「可自行判断是否首次」）、`OnCleanup` 每次 `Shutdown`（吸收 `OnShutdown`）、tick 三段保留且执行顺序与掩码语义不变、`OnTickBegin` / `OnTick` / `OnTickEnd` 虚钩子删除并由 `World` 内部驱动。`World_HookSurface_IsConvergedToRegisterSetupCleanup` 用反射钉死旧钩子不存在与新钩子签名。
10. **占位符扫描（Task 2）**：无 TBD/TODO；`ECS/World.cs` 为完整文件（可整文件覆盖），`Test/WorldTestUnit.cs` 3 处、`Test/WorldMergeTestUnit.cs` 4 处（usings + 测试 + 探针 + 两个既有探针签名迁移）、文档 8 行均为 old/new 可直接应用；命令与预期输出明确（Step 3 红灯 CS0115 ×7 且 0 测试执行，Step 6 过滤 29，Step 7 全量 511，失败 0）。
11. **类型一致性（Task 2）**：`OnRegister(IManagerRegister, IServiceCollection)` 与 `ManagerMediator : IManagerRegister`、`TestInjectionProxyFactory` 的 `IServiceCollection` 用法一致；`TryAddSingleton` / `TryAdd` 所需的 `using Microsoft.Extensions.DependencyInjection.Extensions;` 已加入文件头（包引用 `Microsoft.Extensions.DependencyInjection` 10.0.8 覆盖 net8.0 + netstandard2.1）；已实测确认该版本无 `TryAddSingleton(Type, object)` 重载，故具体 world 类型使用 `TryAdd(new ServiceDescriptor(GetType(), this))`（已写入设计说明与文件内容）；反射测试所需的 `System.Reflection` / `Microsoft.Extensions.DependencyInjection` 已加入测试文件头；`TickProbeSystem` 只需实现 `ISystem.OnTick`（其余成员为默认接口实现）。
12. **实勘偏差与处理（Task 2）**：(a) Task 1 备注「`OnRegister` 已是 Task 2 最终形态、无需再改名」与 spec §7「`OnRegister` 注册 managers / 服务」存在张力——Task 2 保留名称、扩展签名为双参以吸收 `RegisterServices`，已写入设计说明。(b) `TestWorldWithCustomManager` 原覆盖 `OnStart` 为空体（不接线核心引用）且覆盖全部 tick 钩子；Task 2 后由 `Startup` 内部接线 + 内部 tick 驱动保证其 tick 测试继续通过，未改其断言。(c) 全仓库钩子引用实勘：World 子类覆写点仅 `Test/WorldTestUnit.cs`（`TestWorld` / `TestWorldWithCustomManager`）与 `Test/WorldMergeTestUnit.cs` 的四个探针（`CoreOnlyProbeWorld` / `CustomManagerProbeWorld` / 新增 `SetupProbeWorld` / `TickProbeSystem`）；`Test/SystemConvergenceTestUnit.cs:480-502` 的 `base.OnTick` 是 `ISystem.OnTick`，与 World 钩子无关，不动。(d) `Test/WorldTestUnit.cs` 多处空行含行尾空格，Step 2 已给出提示，执行时以 `Read` 的实际文本为准（本次已实测计划中的 3 个 old 块与文件逐字节一致）。
13. **行为不变性（Task 2）**：`Startup` / `Shutdown` / `BeginTick` / `Tick` / `EndTick` 的断言、状态机与 `Log.Exp` 吞异常逐行保留（仅日志上下文名更换）；`TickCount` / `Ticking` / `Ready` / `InjectionProxy` 公开语义不变；四个核心 manager 注册顺序不变；`FindSystem` 与 `#region PublicAPI` 零改动；`ManagerMediator` / `IWorld` / `IWorldManager` / `ISystem` 零改动。有意差异仅两处并已记录：DI 重复注册优先级通过 `TryAddSingleton` 保持（旧：用户后注册胜；新：用户先注册 + 框架 TryAdd），`OnConstruct` 时序并入 `OnSetup`（无测试观察）。
14. **测试计数（Task 2，绑定）**：基线 509 + `WorldMergeTestUnit` 新增 2 = **511 passed**；`WorldTestUnit` 仍 24（1 个测试体改写、0 增删）；过滤预期 `WorldMergeTestUnit` 5、`WorldMergeTestUnit|WorldTestUnit` 29。
15. **后续任务衔接（Task 2）**：Task 3 只剩 `MinimalWorld` 文档引用排查（本 Task 已同步 `RegisterServices` / 旧钩子名，避免文档指向已删除 API）；Phase 6 仍负责 README / QUICK_START 的整体评审与 CommandBuffer 文档。
16. **Task 1 评审 Minor 收口**：`RegisterManagerCalled` → `OnRegisterCalled` 已在 Step 2 更名；「`WorldMergeTestUnit` 自定义 manager 用例可补核心 manager 共存断言」由新增的 `World_TicksSystems_WhenOnSetupIsOverriddenWithoutBase`（核心 manager 内置 + 内部驱动）与既有 `World_BuildsInCoreManagers_EvenWhenOnRegisterIsOverridden` 共同覆盖，不再追加。
