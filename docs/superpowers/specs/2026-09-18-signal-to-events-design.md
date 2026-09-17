# CoreECS v3：公共通知从 Signal 切换为 public event

- 日期：2026-09-18
- 状态：待评审
- 目标：v3 破坏性变更（不兼容 v1/v2 facade）
- 关联：PR #17（v2 性能优化）、`docs/superpowers/specs/2026-09-18-rw-ro-performance-design.md`

## 1. 背景与目标

v2 的所有公共通知面都是自定义 `Signal<T>`：`EntityManager`（3 个）、`ComponentManager`（3 个）、`SystemManager`（4 个）。`Signal` 提供 `.Add(handler, order, allowDuplication)` / `.Remove` / `SignalDisposal`、有序派发、重复订阅检测、逐 handler 异常隔离与嵌套 Emit 断言。

v3 目标：**保留 `Signal` 类（作为公共工具），但 Kernel 内部不再使用；公共通知改用 C# `public event`**，让 API 符合 .NET 惯例（`+=` / `-=`），同时保住 v2 性能优化的关键机制（兴趣位短路、内部快速 Sink、collector 延迟结算）。

## 2. 范围

- 10 个公共 `Signal<T>` 属性改为 `public event`（复用现有 delegate 类型）
- 热事件的自定义访问器维护兴趣位（性能关键）
- 新增 `EventDispatchGuard` 辅助结构体，防止同一事件嵌套 dispatch
- 内部快速 Sink 保留；所有 manager 订阅/绑定移入 `OnManagerCreated` / `OnManagerDestroyed` 自管理
- 异常不再逐 handler 捕获，直接向外抛
- 迁移 Kernel、Test、README/QUICK_START（中英）的全部订阅调用点
- 移除 `Signal<T>` 内部 `ReceiversChanged` 钩子（仅为旧兴趣位机制存在）

## 3. 非目标

- 不改变兴趣位缓存的架构（`Structure.HasChangeInterest` / `HasMutatingChangeHandlers` 保留）
- 不改变 collector 的 journal/延迟结算语义
- 不改进 `Signal` 自身实现（保留给用户，测试不动）
- 不引入事件排序、去重或逐 handler 异常隔离（v3 明确放弃这些语义）

## 4. 设计

### 4.1 公共事件面

| 类 | 事件 | delegate 类型 |
|---|---|---|
| `EntityManager` | `OnEntityGotComp` | `EntityGetComponent(ulong, Type)` |
| `EntityManager` | `OnEntityLoseComp` | `EntityLoseComponent(ulong, Type)` |
| `EntityManager` | `OnEntityChangeComp` | `EntityChangeComponent(ulong, Type)` |
| `ComponentManager` | `OnComponentCreated` | `ComponentCreated(ulong, Type)` |
| `ComponentManager` | `OnComponentRemoved` | `ComponentDestroyed(ulong, Type)` |
| `ComponentManager` | `OnComponentChanged` | `ComponentChanged(ulong, Type)` |
| `SystemManager` | `OnSystemTeardown` | `SystemTeardown` |
| `SystemManager` | `OnSystemBeginExecute` | `SystemBeginExecute` |
| `SystemManager` | `OnSystemEndExecute` | `SystemEndExecute` |
| `SystemManager` | `OnSystemCleanup` | `SystemCleanup` |

- 订阅方式：`world.GetManager<EntityManager>().OnEntityChangeComp += handler;`（解除用 `-=`）
- `OnComponentChanged` 与 `OnEntityChangeComp` 使用**自定义 add/remove 访问器**（见 4.2）；其余为 field-like event
- emit 处使用 backing field 判空：`m_onX?.Invoke(...)`（无 handler 时零开销）
- 方法组 `-=` 依赖委托的目标+方法相等语义，即使每次生成新委托实例也能正确解除

### 4.2 兴趣位跟踪（仅热事件）

`ComponentManager`：

```csharp
private ComponentChanged m_onComponentChanged;
public event ComponentChanged OnComponentChanged
{
    add { m_onComponentChanged += value; _refreshChangeInterest(); }
    remove { m_onComponentChanged -= value; _refreshChangeInterest(); }
}
```

- `_refreshChangeInterest` 中 `OnComponentChanged.HasReceivers` 改为 `m_onComponentChanged != null`
- `EntityManager.OnEntityChangeComp` 同样处理（`m_onEntityChangeComp != null`）
- `EntityMatchManager.RevisionInterestChanged` → `EntityManager._refreshChangeInterest` → `ComponentManager.SetSinkInterest` 链路不变
- 其余 8 个事件不需要兴趣位，保持 field-like

### 4.3 EventDispatchGuard（防嵌套 dispatch）

新增 `Kernel/Utils/EventDispatchGuard.cs`：

```csharp
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
```

- 每个事件一个 `EventDispatchState`（同一事件的 handler 重入该事件 → 抛异常；不同事件互相嵌套允许）
- emit 辅助方法模式：

```csharp
internal void EmitEntityChangeComp(ulong entityId, Type type)
{
    var handlers = m_onEntityChangeComp;
    if (handlers == null) return;

    using (new EventDispatchGuard(m_changeDispatch))
    {
        handlers(entityId, type);
    }
}
```

- 所有 10 个事件的 emit 都走对应辅助方法（含 guard），不直接 `?.Invoke`

### 4.4 内部快速 Sink 与生命周期自绑定

保留 v2 的内部快速通道：`ComponentManager.ChangeSink`（`Action<ulong, uint, EntityLocation>`）→ `EntityManager.OnRevisionChanged` → `EntityMatchManager.OnRevisionChanged`（journal）。revision 不走公共事件。

绑定全部移入 manager 生命周期（`World.Startup` 不再接线）：

- `EntityManager.OnManagerCreated`：

```csharp
m_compManager.OnComponentCreated += _onComponentAdded;
m_compManager.OnComponentRemoved += _onComponentRemoved;
m_compManager.ChangeSink = OnRevisionChanged;
```

  `OnManagerDestroyed`：对应 `-=`、`ChangeSink = null`（现有清理逻辑保留）
- `EntityMatchManager.OnManagerCreated`：

```csharp
m_entityManager.ConnectMatchManager(this);
```

  `ConnectMatchManager` 负责设置 `m_matchManager`、`RevisionInterestChanged` 钩子并刷新兴趣；`OnEntityGotComp/OnEntityLoseComp` 的订阅沿用现有懒订阅（首个 collector 订阅、最后一个释放），改为 `+=`/`-=`
  `OnManagerDestroyed`：`m_entityManager.DisconnectMatchManager();`（新增，幂等，清引用与兴趣钩子）
- `World.Startup` 删除 `Entity.ConnectMatchManager(EntityMatch);` 与 `Component.ChangeSink = Entity.OnRevisionChanged;`；manager 属性赋值（`EntityMatch/Entity/Component/System`）保留
- **顺序无关性**：`ManagerMediator.Boot/Shutdown` 按 `ImmutableDictionary.Values` 枚举（顺序不保证），但所有 manager 在 `Construct` 后都已存在，且 Boot 期间不派发事件；两个 manager 的绑定各自幂等，最终状态一致

### 4.5 异常语义

- emit 辅助方法**不做 try/catch**；订阅者异常向外传播，可能中断本次 dispatch 的后续 handler，并从触发 API（如 `RW`）抛出
- `ManagerMediator.Boot/Shutdown` 对生命周期调用仍有 try/catch + `Log.Exp`（现有行为，不属本次范围）
- 文档需明确：需要隔离的订阅者自行在 handler 内 try/catch

### 4.6 Signal 类去留

- `Kernel/Utils/Signal.cs` 保留为公共工具，Kernel 内部零引用（可用 grep 验证）
- 移除内部 `ReceiversChanged` 钩子（及其在 Add/Remove/Clear/Disposal 中的调用）
- `Test/SignalTestUnit.cs` 保持不变（继续覆盖 Signal 自身语义）

## 5. 破坏性变化（v3）

| 项 | v2（Signal） | v3（event） |
|---|---|---|
| 订阅 | `.Add(handler, order, allowDuplication)` + `SignalDisposal` | `+=` / `-=` |
| 顺序 | `order` 参数 + 稳定排序 | 订阅顺序（委托组合顺序） |
| 重复订阅 | `allowDuplication: false` 可拒绝 | 允许 |
| 异常 | 逐 handler 捕获 + `Log.Exp` | 向外传播 |
| 嵌套 Emit | `Assertion.IsFalse(m_executing)` | `EventDispatchGuard` 抛 `InvalidOperationException` |
| 兴趣位/短路 | 有 | 保持（自定义访问器） |
| revision 热路径 | 内部快速 Sink | 保持 |

## 6. 测试与迁移

- **调用点迁移**：Kernel（`EntityMatchManager`、`EntityManager`、`World`、其他订阅点）、全部订阅测试、README/QUICK_START（中英）
- **新增测试**：
  - 嵌套 dispatch 抛异常（handler 内再次触发同一事件）
  - 异常传播：抛异常的订阅者会中断后续 handler 并从触发 API 抛出
  - 兴趣位：无订阅时 `HasChangeInterest == false`、订阅后恢复；RW/RO 比率与 Flush 基准保持
  - 生命周期自绑定：World 启动后内部绑定生效（collector 收 Got/Lose、revision 进 journal）；Shutdown 后无残留订阅（再次 Startup 不重复）
- **回归门禁**：全量测试通过（Signal 测试不动）；性能基准（比率 1.2/1.5/2.0 与 F1/F2/F3）保持
- **文档**：README/QUICK_START 的 `.Add(...)` 示例改 `+=`；spec 的 §9 执行期修订与 v3 迁移说明

## 7. 风险

| 风险 | 缓解 |
|---|---|
| 异常语义变化导致用户管线中断 | v3 破坏性变更，文档显著标注；需要隔离者自行 try/catch |
| 自定义访问器遗漏兴趣位刷新 | 专项测试 + 性能基准复跑 |
| Boot/Shutdown 顺序不保证 | 绑定顺序无关、幂等；不在 Boot 期间派发 |
| 方法组 `-=` 解除失败（委托不相等） | 用同一方法组表达式（目标+方法相等语义），测试覆盖重复 Startup/Shutdown |
| 热路径 emit 变慢 | 保留兴趣位与快速 Sink；比率基准作为门禁 |

## 8. 参考

- `Kernel/Utils/Signal.cs`、`Kernel/Utils/Assertion.cs`
- `Kernel/Managers/EntityManager.cs`、`Kernel/Managers/ComponentManager.cs`、`Kernel/Managers/SystemManager.cs`、`Kernel/Managers/EntityMatchManager.cs`
- `Kernel/World.cs`、`Kernel/ManagerMediator.cs`
- `Test/SignalTestUnit.cs`、`Test/ComponentChangeSinkTestUnit.cs`、`Test/PerformanceBaselineTestUnit.cs`
- `docs/QUICK_START.md`、`docs/QUICK_START.zh-CN.md`
