# CoreECS v3 Signal→Event 迁移实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 10 个公共 `Signal<T>` 通知面切换为 `public event`，保留内部快速 Sink 与兴趣位短路，绑定移入 manager 生命周期自管理。

**Architecture:** 每个 manager 用 backing delegate 字段 + `public event`；热事件（`OnComponentChanged`/`OnEntityChangeComp`）用自定义 `add/remove` 维护兴趣位；每个事件的派发经 `EventDispatchGuard` 防嵌套；异常不捕获；`Signal` 类保留但 Kernel 零引用。

**Tech Stack:** C#（Kernel：net8.0 + netstandard2.1，`LangVersion 9`），NUnit 测试（net8.0），`~/.dotnet/dotnet`（SDK 8.0.425）。

**Spec:** `docs/superpowers/specs/2026-09-18-signal-to-events-design.md`

**硬门禁：** `Test/EntityCollectorTestUnit.cs`、`Test/CollectorAccelerationTestUnit.cs` 零改动；性能基准（RW/RO 比率与 F1/F2/F3）不退化；`Test/SignalTestUnit.cs` 不动。

---

## 文件结构

| 文件 | 职责 | 动作 |
|---|---|---|
| `Kernel/Utils/EventDispatchGuard.cs` | 每事件 dispatch 状态 + 防嵌套结构体 | 新建 |
| `Kernel/Managers/ComponentManager.cs` | 3 个公共事件 + 兴趣位访问器 + emit 辅助 | 修改 |
| `Kernel/Managers/EntityManager.cs` | 3 个公共事件 + 兴趣位访问器 + 生命周期自绑定 | 修改 |
| `Kernel/Managers/EntityMatchManager.cs` | 订阅改事件 + 生命周期自绑定 Matcher | 修改 |
| `Kernel/Managers/SystemManager.cs` | 4 个公共事件 + emit 辅助 | 修改 |
| `Kernel/World.cs` | 删除两条外部接线 | 修改 |
| `Kernel/Utils/Signal.cs` | 移除内部 `ReceiversChanged` 钩子 | 修改 |
| `Test/*` | 34 处订阅改 `+=`；新增守卫/兴趣/生命周期测试 | 修改/新建 |
| `docs/QUICK_START.md`、`docs/QUICK_START.zh-CN.md` | 示例改 `+=` | 修改 |

---

## Task 1: EventDispatchGuard 工具与测试

**Files:**
- Create: `Kernel/Utils/EventDispatchGuard.cs`
- Test: `Test/EventDispatchGuardTestUnit.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using CoreECS.Utils;

namespace CoreECS.Test
{
    [TestFixture]
    public class EventDispatchGuardTestUnit
    {
        [Test]
        public void Enter_TwiceWithoutExit_Throws()
        {
            var state = new EventDispatchState();
            using var guard = new EventDispatchGuard(state);

            Assert.Throws<InvalidOperationException>(() => new EventDispatchGuard(state));
        }

        [Test]
        public void Dispose_AllowsReentry()
        {
            var state = new EventDispatchState();
            using (new EventDispatchGuard(state))
            {
            }

            Assert.DoesNotThrow(() => new EventDispatchGuard(state).Dispose());
        }

        [Test]
        public void IndependentStates_DoNotInterfere()
        {
            var first = new EventDispatchState();
            var second = new EventDispatchState();

            using var guardA = new EventDispatchGuard(first);
            Assert.DoesNotThrow(() => new EventDispatchGuard(second).Dispose());
        }
    }
}
```

- [ ] **Step 2: 运行确认失败**

Run: `~/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~EventDispatchGuardTestUnit" --verbosity minimal`
Expected: 编译失败（类型不存在）。

- [ ] **Step 3: 实现**

`Kernel/Utils/EventDispatchGuard.cs`:

```csharp
using System;

namespace CoreECS.Utils
{
    /// <summary>
    /// Per-event dispatch flag. Re-entering the same event from one of its own handlers
    /// is rejected instead of recursing.
    /// </summary>
    internal sealed class EventDispatchState
    {
        private bool m_dispatching;

        public void Enter()
        {
            if (m_dispatching)
                throw new InvalidOperationException("Re-entrant event dispatch detected.");
            m_dispatching = true;
        }

        public void Exit() => m_dispatching = false;
    }

    /// <summary>
    /// Scope guard used around event dispatch: sets the state on construction and clears
    /// it on dispose.
    /// </summary>
    internal readonly struct EventDispatchGuard : IDisposable
    {
        private readonly EventDispatchState m_state;

        public EventDispatchGuard(EventDispatchState state)
        {
            m_state = state;
            state.Enter();
        }

        public void Dispose() => m_state.Exit();
    }
}
```

- [ ] **Step 4: 运行新测试 + 全量**

Run: `~/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~EventDispatchGuardTestUnit" --verbosity minimal` → 3/3
Run: `~/.dotnet/dotnet test --verbosity minimal` → all pass

- [ ] **Step 5: Commit**

```bash
git add Kernel/Utils/EventDispatchGuard.cs Test/EventDispatchGuardTestUnit.cs
git commit -m "feat(core): add event dispatch reentrancy guard"
```

---

## Task 2: ComponentManager 事件化（含 EntityManager 内部订阅迁移与 Sink 自绑定）

**Files:**
- Modify: `Kernel/Managers/ComponentManager.cs`
- Modify: `Kernel/Managers/EntityManager.cs`（仅内部订阅与 Sink 绑定部分）
- Modify: `Kernel/World.cs`（删除 `Component.ChangeSink = ...`）
- Test: `Test/ComponentManagerTestUnit.cs`、`Test/ComponentChangeSinkTestUnit.cs`、`Test/RevisionHandlerReentrancyTestUnit.cs`（仅 ComponentManager 部分）

- [ ] **Step 1: 迁移订阅测试（先让编译失败）**

三个测试文件里所有 `OnComponentCreated/OnComponentRemoved/OnComponentChanged` 的 `.Add(lambda)` 改为 `+= lambda`（去掉 `.Add(` 与结尾多余的 `)`）。示例：

```csharp
// before
_componentManager.OnComponentCreated.Add((entityId, compType) =>
{
    capturedEntityId = entityId;
    capturedType = compType;
});
// after
_componentManager.OnComponentCreated += (entityId, compType) =>
{
    capturedEntityId = entityId;
    capturedType = compType;
};
```

`Test/ComponentChangeSinkTestUnit.cs` 的 `ComponentManager_ChangeSignal_HasNoInternalSubscribers` 改为兴趣位断言：

```csharp
[Test]
public void ComponentManager_NoRevisionListeners_MeansNoChangeInterest()
{
    var component = _world.GetManager<ComponentManager>();
    Assert.IsFalse(component.HasChangeInterest, "no revision listeners means no change interest");

    ComponentChanged handler = (_, _) => { };
    component.OnComponentChanged += handler;
    Assert.IsTrue(component.HasChangeInterest);

    component.OnComponentChanged -= handler;
    Assert.IsFalse(component.HasChangeInterest);
}
```

- [ ] **Step 2: 运行确认失败**

Run: `~/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~ComponentManagerTestUnit" --verbosity minimal`
Expected: 编译失败（`Signal` 的 `.Add` 已无、事件尚未定义）。

- [ ] **Step 3: ComponentManager 事件化**

`Kernel/Managers/ComponentManager.cs`：

1. 删除 `s_addEmitter` / `s_rmEmitter` / `s_changeEmitter` 三个静态 `Emitter` 字段。
2. 属性替换：

```csharp
private ComponentCreated m_onComponentCreated;
public event ComponentCreated OnComponentCreated;

private ComponentDestroyed m_onComponentRemoved;
public event ComponentDestroyed OnComponentRemoved;

private ComponentChanged m_onComponentChanged;
public event ComponentChanged OnComponentChanged
{
    add { m_onComponentChanged += value; _refreshChangeInterest(); }
    remove { m_onComponentChanged -= value; _refreshChangeInterest(); }
}

private readonly EventDispatchState m_createdDispatch = new();
private readonly EventDispatchState m_removedDispatch = new();
private readonly EventDispatchState m_changedDispatch = new();
```

3. 新增 emit 辅助（`KernelObserver` 改为调用它们）：

```csharp
private void EmitComponentCreated(ulong entityId, Type type)
{
    var handlers = m_onComponentCreated;
    if (handlers == null) return;

    using (new EventDispatchGuard(m_createdDispatch))
        handlers(entityId, type);
}

private void EmitComponentRemoved(ulong entityId, Type type)
{
    var handlers = m_onComponentRemoved;
    if (handlers == null) return;

    using (new EventDispatchGuard(m_removedDispatch))
        handlers(entityId, type);
}

private void EmitComponentChanged(ulong entityId, Type type)
{
    var handlers = m_onComponentChanged;
    if (handlers == null) return;

    using (new EventDispatchGuard(m_changedDispatch))
        handlers(entityId, type);
}
```

4. `KernelObserver`：

```csharp
public void OnComponentAdded(Structure structure, int row, uint typeId)
{
    structure.HasChangeInterest = m_manager.m_hasChangeInterest;
    structure.HasMutatingChangeHandlers = m_manager.m_hasMutatingChangeHandlers;
    m_manager.EmitComponentCreated(structure.Entities[row], ComponentTypeRegistry.GetById(typeId).Type);
}

public void OnComponentRemoved(Structure structure, int row, uint typeId)
{
    m_manager.EmitComponentRemoved(structure.Entities[row], ComponentTypeRegistry.GetById(typeId).Type);
}

public void OnComponentChanged(Structure structure, int row, uint typeId)
{
    var entityId = structure.Entities[row];
    var location = structure.GetLocationAt(row);

    m_manager.EmitComponentChanged(entityId, ComponentTypeRegistry.GetById(typeId).Type);
    m_manager.ChangeSink?.Invoke(entityId, typeId, location);
}
```

5. `_refreshChangeInterest` 内两处 `OnComponentChanged.HasReceivers` 改为 `m_onComponentChanged != null`。
6. 构造函数删除 `OnComponentChanged.ReceiversChanged = _refreshChangeInterest;`；`OnManagerDestroyed` 删除 `OnComponentChanged.ReceiversChanged = null;`。
7. 顶部按需删除不再使用的 `using`（`CoreECS.Utils` 仍需 `EventDispatchGuard`）。

- [ ] **Step 4: EntityManager 内部订阅与 Sink 自绑定**

`Kernel/Managers/EntityManager.cs`：

1. `OnManagerCreated`：

```csharp
public void OnManagerCreated()
{
    m_compManager.OnComponentCreated += _onComponentAdded;
    m_compManager.OnComponentRemoved += _onComponentRemoved;
    m_compManager.ChangeSink = OnRevisionChanged;
    _refreshChangeInterest();

    m_init = true;
}
```

2. `OnManagerDestroyed` 对应改为 `-=` 与 `ChangeSink = null`（保留其余清理）。
3. `World.Startup` 删除 `Component.ChangeSink = Entity.OnRevisionChanged;`（保留 manager 属性赋值与 `Entity.ConnectMatchManager(...)`，后者在 Task 4 处理）。

- [ ] **Step 5: 运行 focused + 全量**

Run: `~/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~ComponentManagerTestUnit|FullyQualifiedName~ComponentChangeSinkTestUnit|FullyQualifiedName~RevisionHandlerReentrancyTestUnit" --verbosity minimal` → all pass
Run: `~/.dotnet/dotnet test --verbosity minimal` → all pass（含性能基准）

- [ ] **Step 6: Commit**

```bash
git add Kernel/Managers/ComponentManager.cs Kernel/Managers/EntityManager.cs Kernel/World.cs Test/ComponentManagerTestUnit.cs Test/ComponentChangeSinkTestUnit.cs Test/RevisionHandlerReentrancyTestUnit.cs
git commit -m "refactor(core): expose component notifications as public events"
```

---

## Task 3: EntityManager 事件化（含 EntityMatchManager 订阅迁移）

**Files:**
- Modify: `Kernel/Managers/EntityManager.cs`
- Modify: `Kernel/Managers/EntityMatchManager.cs`（仅 `_ensureEntitySignalSubscriptions` / `_release...` / `OnManagerDestroyed` 的 `.Add/.Remove` → `+=/-=`）
- Test: `Test/EntityManagerTestUnit.cs`、`Test/ComponentAddReentrancyTestUnit.cs`、`Test/ComponentRefPoolingTestUnit.cs`、`Test/RevisionHandlerReentrancyTestUnit.cs`

- [ ] **Step 1: 迁移订阅测试**

`OnEntityGotComp/OnEntityLoseComp/OnEntityChangeComp` 的 `.Add(lambda)` → `+= lambda`（`EntityManagerTestUnit` 6 处、`ComponentAddReentrancyTestUnit` 3 处、`ComponentRefPoolingTestUnit` 1 处）。

`Test/RevisionHandlerReentrancyTestUnit.cs` 的自移除订阅改为委托变量 + `-=`：

```csharp
// before
Signal<EntityChangeComponent>.SignalDisposal sub = default;
sub = _entityManager.OnEntityChangeComp.Add((entityId, _) =>
{
    ...
    sub.Dispose();
});
// after
EntityChangeComponent handler = null;
handler = (entityId, _) =>
{
    ...
    _entityManager.OnEntityChangeComp -= handler;
};
_entityManager.OnEntityChangeComp += handler;
```

- [ ] **Step 2: 运行确认失败**

Run: `~/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~EntityManagerTestUnit" --verbosity minimal`
Expected: 编译失败。

- [ ] **Step 3: EntityManager 事件化**

`Kernel/Managers/EntityManager.cs`：

1. 删除 `s_gotEmitter` / `s_loseEmitter` / `s_changeEmitter`。
2. 属性替换：

```csharp
private EntityGetComponent m_onEntityGotComp;
public event EntityGetComponent OnEntityGotComp;

private EntityLoseComponent m_onEntityLoseComp;
public event EntityLoseComponent OnEntityLoseComp;

private EntityChangeComponent m_onEntityChangeComp;
public event EntityChangeComponent OnEntityChangeComp
{
    add { m_onEntityChangeComp += value; _refreshChangeInterest(); }
    remove { m_onEntityChangeComp -= value; _refreshChangeInterest(); }
}

private readonly EventDispatchState m_gotDispatch = new();
private readonly EventDispatchState m_loseDispatch = new();
private readonly EventDispatchState m_changeDispatch = new();
```

3. emit 辅助：

```csharp
private void EmitEntityGotComp(ulong entityId, Type componentType)
{
    var handlers = m_onEntityGotComp;
    if (handlers == null) return;

    using (new EventDispatchGuard(m_gotDispatch))
        handlers(entityId, componentType);
}

private void EmitEntityLoseComp(ulong entityId, Type componentType)
{
    var handlers = m_onEntityLoseComp;
    if (handlers == null) return;

    using (new EventDispatchGuard(m_loseDispatch))
        handlers(entityId, componentType);
}

private void EmitEntityChangeComp(ulong entityId, Type componentType)
{
    var handlers = m_onEntityChangeComp;
    if (handlers == null) return;

    using (new EventDispatchGuard(m_changeDispatch))
        handlers(entityId, componentType);
}
```

4. 替换 emit 调用点：
   - `DestroyEntity`：`EmitEntityLoseComp(entityId, null);`
   - `_onComponentAdded`：`EmitEntityGotComp(entityId, compType);`
   - `_onComponentRemoved`：`EmitEntityLoseComp(entityId, compType);`
   - `OnRevisionChanged`：`EmitEntityChangeComp(entityId, ComponentTypeRegistry.GetById(typeId).Type);`（不再需要 `HasReceivers` 判断，emit 辅助内部判空）
5. `_refreshChangeInterest` 两处 `OnEntityChangeComp.HasReceivers` 改为 `m_onEntityChangeComp != null`。
6. 构造函数删除 `OnEntityChangeComp.ReceiversChanged = _refreshChangeInterest;`；`OnManagerDestroyed` 删除 `OnEntityChangeComp.ReceiversChanged = null;`。

- [ ] **Step 4: EntityMatchManager 订阅迁移**

`Kernel/Managers/EntityMatchManager.cs`：

- `_ensureEntitySignalSubscriptions`：`m_entityManager.OnEntityGotComp += _onComponentAdded;`、`m_entityManager.OnEntityLoseComp += _onComponentRemoved;`
- `_releaseEntitySignalSubscriptionsIfUnused` 与 `OnManagerDestroyed`：对应 `-=`。

- [ ] **Step 5: 运行 focused + 全量**

Run: `~/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~EntityManagerTestUnit|FullyQualifiedName~ComponentAddReentrancyTestUnit|FullyQualifiedName~ComponentRefPoolingTestUnit|FullyQualifiedName~RevisionHandlerReentrancyTestUnit|FullyQualifiedName~EntityCollectorTestUnit" --verbosity minimal` → all pass
Run: `~/.dotnet/dotnet test --verbosity minimal` → all pass

- [ ] **Step 6: Commit**

```bash
git add Kernel/Managers/EntityManager.cs Kernel/Managers/EntityMatchManager.cs Test/EntityManagerTestUnit.cs Test/ComponentAddReentrancyTestUnit.cs Test/ComponentRefPoolingTestUnit.cs Test/RevisionHandlerReentrancyTestUnit.cs
git commit -m "refactor(core): expose entity notifications as public events"
```

---

## Task 4: Matcher 快速 Sink 生命周期自绑定

**Files:**
- Modify: `Kernel/Managers/EntityManager.cs`（新增 `DisconnectMatchManager`）
- Modify: `Kernel/Managers/EntityMatchManager.cs`（`OnManagerCreated` / `OnManagerDestroyed`）
- Modify: `Kernel/World.cs`（删除 `Entity.ConnectMatchManager(EntityMatch);`）
- Test: `Test/ManagerLifecycleWiringTestUnit.cs`（新建）

- [ ] **Step 1: 写失败测试**

```csharp
using CoreECS.Managers;

namespace CoreECS.Test
{
    [TestFixture]
    public class ManagerLifecycleWiringTestUnit
    {
        private struct Position : IComponent<Position> { public int X; }

        [Test]
        public void Startup_WiresMatcherSink_CollectorsSeeStructuralEvents()
        {
            var world = new World();
            world.Startup();

            var entity = world.CreateEntity();
            var collector = world.CreateCollector(EntityMatcher.With.OfAll<Position>());
            collector.Flush();

            entity.CreateComponent<Position>();
            collector.Flush();

            Assert.AreEqual(1, collector.Matching.Count);
            world.Shutdown();
        }

        [Test]
        public void Shutdown_WithLiveCollector_DoesNotThrow()
        {
            var world = new World();
            world.Startup();
            var collector = world.CreateCollector(EntityMatcher.With.OfAll<Position>());
            var entity = world.CreateEntity();
            entity.CreateComponent<Position>();
            collector.Flush();

            Assert.DoesNotThrow(() => world.Shutdown());
        }
    }
}
```

- [ ] **Step 2: 删除 World 接线并确认失败**

`Kernel/World.cs` 删除 `Entity.ConnectMatchManager(EntityMatch);`（Task 2 已删除 `Component.ChangeSink = ...`）。

Run: `~/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~ManagerLifecycleWiringTestUnit" --verbosity minimal`
Expected: FAIL（matcher 未绑定，collector 收不到结构事件）。

- [ ] **Step 3: 生命周期自绑定**

`Kernel/Managers/EntityManager.cs` 新增：

```csharp
internal void DisconnectMatchManager()
{
    if (m_matchManager == null) return;

    m_matchManager.RevisionInterestChanged = null;
    m_matchManager = null;
    _refreshChangeInterest();
}
```

`Kernel/Managers/EntityMatchManager.cs`：

```csharp
public void OnManagerCreated()
{
    m_entityManager.ConnectMatchManager(this);
}

public void OnManagerDestroyed()
{
    m_entityManager.DisconnectMatchManager();
    // ...existing teardown (unsubscribe, clear collectors/journal) stays...
}
```

- [ ] **Step 4: 运行 focused + 全量**

Run: `~/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~ManagerLifecycleWiringTestUnit|FullyQualifiedName~EntityCollectorTestUnit|FullyQualifiedName~CollectorDeferredSettlementTestUnit" --verbosity minimal` → all pass
Run: `~/.dotnet/dotnet test --verbosity minimal` → all pass

- [ ] **Step 5: Commit**

```bash
git add Kernel/Managers/EntityManager.cs Kernel/Managers/EntityMatchManager.cs Kernel/World.cs Test/ManagerLifecycleWiringTestUnit.cs
git commit -m "refactor(core): bind matcher revision sink in manager lifecycle"
```

---

## Task 5: SystemManager 事件化

**Files:**
- Modify: `Kernel/Managers/SystemManager.cs`
- Test: `Test/SystemTestUnit.cs`、`Test/SystemConvergenceTestUnit.cs`

- [ ] **Step 1: 迁移订阅测试**

`OnSystemTeardown.Add(world => ...)` → `OnSystemTeardown += world => ...;`（`SystemTestUnit` 2 处、`SystemConvergenceTestUnit` 2 处，后者为多行 lambda，去掉 `.Add(` 与结尾 `)`）。

- [ ] **Step 2: 运行确认失败**

Run: `~/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~SystemTestUnit" --verbosity minimal`
Expected: 编译失败。

- [ ] **Step 3: SystemManager 事件化**

`Kernel/Managers/SystemManager.cs`：

1. 属性替换（field-like，冷路径不需要兴趣位）：

```csharp
public event SystemTeardown OnSystemTeardown;
public event SystemBeginExecute OnSystemBeginExecute;
public event SystemEndExecute OnSystemEndExecute;
public event SystemCleanup OnSystemCleanup;

private readonly EventDispatchState m_teardownDispatch = new();
private readonly EventDispatchState m_beginDispatch = new();
private readonly EventDispatchState m_endDispatch = new();
private readonly EventDispatchState m_cleanupDispatch = new();
```

2. emit 辅助：

```csharp
private void EmitSystemTeardown(IWorld world)
{
    var handlers = OnSystemTeardown;
    if (handlers == null) return;

    using (new EventDispatchGuard(m_teardownDispatch))
        handlers(world);
}

private void EmitSystemBeginExecute(IWorld world, ISystem system)
{
    var handlers = OnSystemBeginExecute;
    if (handlers == null) return;

    using (new EventDispatchGuard(m_beginDispatch))
        handlers(world, system);
}

private void EmitSystemEndExecute(IWorld world, ISystem system)
{
    var handlers = OnSystemEndExecute;
    if (handlers == null) return;

    using (new EventDispatchGuard(m_endDispatch))
        handlers(world, system);
}

private void EmitSystemCleanup(IWorld world)
{
    var handlers = OnSystemCleanup;
    if (handlers == null) return;

    using (new EventDispatchGuard(m_cleanupDispatch))
        handlers(world);
}
```

3. 替换 4 个 emit 调用点（`Emit(...)` → `EmitSystemXxx(...)`）。
4. 顶部按需删除不再使用的 `using`。

- [ ] **Step 4: 运行 focused + 全量**

Run: `~/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~SystemTestUnit|FullyQualifiedName~SystemConvergenceTestUnit|FullyQualifiedName~SystemOrderingTestUnit|FullyQualifiedName~SystemGroupRegistrationTestUnit" --verbosity minimal` → all pass
Run: `~/.dotnet/dotnet test --verbosity minimal` → all pass

- [ ] **Step 5: Commit**

```bash
git add Kernel/Managers/SystemManager.cs Test/SystemTestUnit.cs Test/SystemConvergenceTestUnit.cs
git commit -m "refactor(core): expose system notifications as public events"
```

---

## Task 6: 移除 Signal 内部钩子并确认 Kernel 零引用

**Files:**
- Modify: `Kernel/Utils/Signal.cs`
- Test: 无新增（`Test/SignalTestUnit.cs` 不动）

- [ ] **Step 1: 移除 `ReceiversChanged`**

`Kernel/Utils/Signal.cs`：

- 删除字段 `internal Action ReceiversChanged;`
- 删除 `SignalDisposal.Dispose`、`Add`、`Remove`、`Clear` 内的 `m_signal.ReceiversChanged?.Invoke()` / `ReceiversChanged?.Invoke()` 调用

- [ ] **Step 2: 确认 Kernel 零引用**

Run: `grep -rn "Signal<\|\.Emit(" Kernel --include="*.cs" | grep -v "/obj/" | grep -v "Kernel/Utils/Signal.cs"`
Expected: 无输出（Kernel 仅 `Signal.cs` 自身包含 Signal 定义）。

- [ ] **Step 3: 运行 Signal 测试与全量**

Run: `~/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~SignalTestUnit" --verbosity minimal` → all pass
Run: `~/.dotnet/dotnet test --verbosity minimal` → all pass

- [ ] **Step 4: Commit**

```bash
git add Kernel/Utils/Signal.cs
git commit -m "refactor(core): drop the unused signal receiver hook"
```

---

## Task 7: 文档迁移

**Files:**
- Modify: `docs/QUICK_START.md`、`docs/QUICK_START.zh-CN.md`
- Modify: `docs/superpowers/specs/2026-09-18-signal-to-events-design.md`（状态改"已实现"）

- [ ] **Step 1: 更新订阅示例**

把两份 QUICK_START 中所有 `Signal` 订阅示例改为事件写法。典型替换：

```csharp
// before
world.EntityManager.OnEntityGotComp.Add((entityId, type) => { ... });
// after
world.EntityManager.OnEntityGotComp += (entityId, type) => { ... };
```

并在事件章节补充 v3 语义说明：异常向外传播、无排序/去重、嵌套 dispatch 抛异常、`Signal<T>` 仍可作为独立工具使用。

- [ ] **Step 2: 更新 spec 状态**

`docs/superpowers/specs/2026-09-18-signal-to-events-design.md` 状态改为"已实现"，并在文末补执行期修订（如有）。

- [ ] **Step 3: Commit**

```bash
git add docs/QUICK_START.md docs/QUICK_START.zh-CN.md docs/superpowers/specs/2026-09-18-signal-to-events-design.md
git commit -m "doc(proj): document event subscriptions for v3"
```

---

## Task 8: 全量回归与性能门禁

**Files:** 无（验证任务；若发现回归则修复并单独提交）

- [ ] **Step 1: 全量测试**

Run: `~/.dotnet/dotnet test --verbosity minimal`
Expected: 全部通过（Signal 测试与 collector 测试零改动）

- [ ] **Step 2: collector 零改动门禁**

Run: `git diff --stat 2b87ef0..HEAD -- Test/EntityCollectorTestUnit.cs Test/CollectorAccelerationTestUnit.cs`
Expected: 空输出（`2b87ef0` 是 spec 提交，collector 测试自那以后不应被改动）

- [ ] **Step 3: 性能基准**

Run: `~/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~PerformanceBaselineTestUnit" --verbosity normal`
Expected: 4/4 通过；比率 0/100/1000 分别 < 1.2/1.5/2.0，F1/F2/F3 在新上限内

- [ ] **Step 4: Release 构建**

Run: `~/.dotnet/dotnet build --configuration Release --verbosity minimal 2>&1 | grep -E "error|warning CS" | grep -v "CS0649" | head`
Expected: 无新增错误/警告

- [ ] **Step 5: 记录**

把性能输出追加到 `docs/superpowers/plans/2026-09-18-rw-ro-performance-baseline.md`，提交：

```bash
git add docs/superpowers/plans/2026-09-18-rw-ro-performance-baseline.md
git commit -m "test(test): record v3 event migration performance gate"
```

---

## 自检记录

- **Spec 覆盖**：10 个事件（Task 2/3/5）、兴趣位访问器（Task 2/3）、EventDispatchGuard（Task 1，全部 emit 使用）、快速 Sink 与生命周期自绑定（Task 2/4）、异常传播（Task 2/3/5 的 emit 辅助无 try/catch）、Signal 去留（Task 6）、文档（Task 7）、门禁（Task 8）。
- **占位符**：无 TBD/TODO；Task 8 的对照 commit 已写死为 `2b87ef0`（spec 提交）。
- **类型一致性**：`EventDispatchState` / `EventDispatchGuard`、`EmitComponentCreated/Removed/Changed`、`EmitEntityGotComp/LoseComp/ChangeComp`、`EmitSystemTeardown/BeginExecute/EndExecute/Cleanup`、`ConnectMatchManager` / `DisconnectMatchManager`、`HasChangeInterest` 在任务间保持一致。
