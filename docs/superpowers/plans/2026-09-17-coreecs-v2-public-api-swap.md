# CoreECS v2 公开 API 切换（Phase 1c）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把公开 `Entity` / `ComponentRef` / `EntityExtension` 切换到 v2 archetype 内核（`EntityLocation` + `Structure` + `ComponentOrchestrator`），保持 v1 公开签名与行为语义，为 Task 2 的管理器/World 接线与 Task 3 的测试迁移铺路。

**Architecture:** `Entity` 持有 `(world, entityId, EntityLocation, generation)`，按 `ComponentTypeRegistry` 解析出的 kind 经 `ComponentOrchestrator` 分发读写；`ComponentRef` / `ComponentRef<T>` 内部改为持有 `CoreECS.Structures.ComponentRefCore`，`RO` / `RW` 直接读写 `Structure` 存储并复用 v1 异常语义。统一写 API 要求从 `IComponent<T>` 宽约束上下文调用 discrete/tag 的泛型内核方法，而 C# 泛型约束不能在运行时收窄（CS0314），因此本任务先把内核相关泛型约束从 `IDiscreteComponent<T>` / `ITagComponent<T>` 放宽为 `IComponent<T>`（kind 仍由运行时注册表唯一决定），并新增非泛型 `RemoveComponent`。

**Tech Stack:** C# 9（`LangVersion 9`）、`net8.0` + `netstandard2.1`、NUnit 3.14、`dotnet test --filter`

**Spec:** `docs/superpowers/specs/2026-09-17-coreecs-v2-design.md`

**Handoff:** `docs/superpowers/plans/2026-09-17-coreecs-v2-handoff.md`

**前置计划:** `docs/superpowers/plans/2026-09-17-coreecs-v2-kernel-containers.md`（1a，已完成）、`docs/superpowers/plans/2026-09-17-coreecs-v2-world-integration-internals.md`（1b，必须先实现且全绿）

---

## File Structure

| 文件 | 职责 |
|---|---|
| `ECS/Defines/ComponentRef.cs` | （Task 1 重写）公开 `ComponentRef` / `ComponentRef<T>` 内部持有 `CoreECS.Structures.ComponentRefCore`；文件开头保留 v1 `IComponentRefLocator` / `IComponentRefCore` 兼容接口（Task 2 删除） |
| `ECS/Entity.cs` | （Task 1 重写）`Entity` 改为 `(world, entityId, EntityLocation, generation)`；写 API 按 kind 经 `ComponentOrchestrator` 分发；读 API 保持 v1 失效语义 |
| `ECS/EntityExtension.cs` | （Task 1 重写）`TryGetComponent` / `GetOrCreateComponent` 适配新 API，公开签名不变 |
| `ECS/Structures/ComponentOrchestrator.cs` | （Task 1 前向适配）放宽 discrete/tag 泛型约束；新增非泛型 `RemoveComponent`；`RemoveDenseComponent<T>` 委托给它 |
| `ECS/Structures/ComponentHookDispatcher.cs` | （Task 1 前向适配）`RegisterDiscrete<T>` 约束放宽 |
| `ECS/Structures/Structure.cs` | （Task 1 前向适配）`SetDiscrete<T>` / `GetDiscreteRef<T>` 约束放宽 |
| `ECS/Structures/SpareSetComponentContainer.cs` | （Task 1 前向适配）`GetOrCreateStore<T>` 约束放宽 |
| `ECS/Structures/DiscreteStore.cs` | （Task 1 前向适配）`DiscreteStore<T>` 类约束放宽 |
| `ECS/Managers/ComponentManager.cs` | （Task 1 前向声明）`internal ComponentOrchestrator Orchestrator { get; set; }`；Task 2 注入实例并删除 v1 存储 |
| `ECS/EntityGraph.cs`、`ComponentStore<T>`、v1 `ComponentRefCore`、`IComponentRefLocator`、`IComponentRefCore` | **Task 2 删除**（随 v1 存储一起） |
| `ECS/World.cs` / `ECS/MinimalWorld.cs` | **Task 2 接线**（`CreateEntity(mask)` 选择初始结构、Entity 构造） |
| `ECS/Managers/EntityManager.cs` / `EntityMatchManager.cs` | **Task 2 重写**（实体注册表 / Structure 求值接线） |
| `Test/*` | **Task 3 迁移**（内部测试重写 + 行为测试适配 + 全量验证） |

Task 2 / Task 3 将在本文件末尾追加（Task 2 = 管理器与 World 接线 + v1 存储删除；Task 3 = 内部测试重写 + 全量验证），本任务不预写其步骤。

## 本计划范围边界

- **Task 1（本文件）**：公开 `Entity` / `ComponentRef` / `EntityExtension` 切换到 v2 内核；含为使统一写 API 可编译的内核前向适配（泛型约束放宽 + 非泛型 `RemoveComponent`）与 `ComponentManager.Orchestrator` 访问器声明。本任务结束时 `ECS/ECS.csproj` 预期编译失败（仅 `EntityGraph.cs` 与 `World.cs`，Task 2 修复），Test 项目同样无法编译，因此本任务不跑测试。
- **Task 2（后续追加）**：三个管理器 + `World` / `MinimalWorld` 接线（含 `EntityExtension` 若需随管理器微调）、`CreateEntity(mask)` 选择初始结构、`EntityMatchManager` 改用 Plan 1b Task 5 的 `ComponentFilter(Structure, row)`、删除 v1 存储（`EntityGraph`、`ComponentStore<T>`、v1 `ComponentRefCore`、`IComponentRefCore`、`IComponentRefLocator` 等）与 v1 文件，恢复 `ECS/ECS.csproj` 编译。
- **Task 3（后续追加）**：内部测试重写（`ComponentManagerTestUnit` / `EntityGraphTestUnit` / `EntityManagerTestUnit`）+ 行为测试适配 + 全量 `dotnet test` 验证。
- **不包括**（spec 后置阶段）：`IEntityQuery` / `s.RO/RW<T>()` 批量访问（Phase 3）、系统分组排序（Phase 4）、World 合并与生命周期收敛（Phase 5）、CommandBuffer（Phase 2）。

---

## Task 1: 公开 Entity 与 ComponentRef 切换到新内核

**Files:**
- Modify: `ECS/Defines/ComponentRef.cs`（重写公开结构体；文件开头的 v1 兼容接口原样保留）
- Modify: `ECS/Entity.cs`（整体重写）
- Modify: `ECS/EntityExtension.cs`（重写实现，公开签名不变）
- Modify（Plan 1b 内核前向适配）: `ECS/Structures/DiscreteStore.cs`、`ECS/Structures/SpareSetComponentContainer.cs`、`ECS/Structures/Structure.cs`、`ECS/Structures/ComponentHookDispatcher.cs`、`ECS/Structures/ComponentOrchestrator.cs`
- Modify（前向声明）: `ECS/Managers/ComponentManager.cs`
- Test: 无新增测试（验证方式见 Step 5 / Step 6）

**前置:** Plan 1b 已实现且全绿（`EntityTable` / `ComponentRefCore` / `ComponentHookDispatcher` / `ComponentOrchestrator` / `Structure` 非泛型访问器 / `EntityMatcher.ComponentFilter(Structure, int)` 均存在）。

**设计决策（执行时不要改动，评审时按此核对）：**

1. **内核泛型约束放宽（CS0314 规避）**：统一写 API 要求 `Entity.CreateComponent<T>() where T : struct, IComponent<T>` 在运行时按 kind 分发，而宽约束上下文不能调用 `AddDiscreteComponent<T>() where T : struct, IDiscreteComponent<T>`（CS0314，泛型约束无法在运行时收窄）。因此把 discrete/tag 调用链上的泛型方法约束放宽为 `IComponent<T>`；kind 仍由 `ComponentTypeRegistry.ResolveKind`（Tag > Discrete > Dense）在运行时唯一决定，公开调用点不可能把 dense 类型路由进 discrete/tag 分支。放宽只改签名，不改任何现有行为，Step 1 结束时全量测试应保持 Plan 1b 基线全绿。
2. **`ComponentRef.Core` 改为 internal**：`Core` 的类型是 internal 的 `CoreECS.Structures.ComponentRefCore`，public 字段会触发 CS0052。测试经 `InternalsVisibleTo("Test")` 仍可访问；公开成员 `NotNull` / `RuntimeType` / `EntityId` / `Revision` / `Inspect` / `Typed` / `Untyped` 全部保留。
3. **相等性改为结构相等**：v1 的 core 是每组件共享的池化对象，引用相等即可；v2 每次 `GetComponentRef` 新建 core，因此 `ComponentRef` / `ComponentRef<T>` 的相等与哈希按 `(Location 引用, Generation, TypeId, Kind, Version)` 计算，保证 `CreateComponent` 与 `GetComponent` 拿到的同一组件引用相等（对应 `EntityTestUnit` 的两个相等测试）。
4. **Tag 无 ref**：`CreateComponent<Tag>` / `GetComponent<Tag>` 返回 `default`（`NotNull == false`，不抛）；`HasComponent<Tag>` / `DestroyComponent<Tag>` 正常。`ComponentRef<T>.RO/RW` 对 tag core（公开 API 已不可获得，仅内核测试可构造）抛 `InvalidOperationException("Tag components carry no data.")`。
5. **失效实体语义保持 v1**：`IsValid` 返回 false；`World` 抛 `InvalidOperationException`；`Mask` / `GetComponent` / `GetComponents` / `HasComponent` / `CreateComponent` / `DestroyComponent` 在失效实体上抛 `InvalidOperationException`（`RequireLocation`），保持 `EntityTestUnit.Entity_InvalidAccessThrowsException` 等行为。
6. **change 事件路径**：`RW` → `ComponentRefCore.ChangeRevision()` → `Structure.ChangeDenseRevision/ChangeDiscreteRevision` → `Structure.Observer.OnComponentChanged`。本任务不接线 observer（Task 2 在管理器/World 侧注入），因此不新增事件测试。
7. **前向依赖**：`ComponentManager` 的 `internal ComponentOrchestrator Orchestrator { get; set; }` 在 Task 1 只声明（`Entity` 取用），Task 2 在管理器接线时注入实例。`Entity` 构造函数签名变化使 `EntityGraph.cs` / `World.cs` 在本任务结束时编译失败——这是预期的中间状态，Task 2 修复。
8. **保留 v1 兼容接口**：`ComponentRef.cs` 顶部的 `IComponentRefLocator` / `IComponentRefCore` 原样保留（`IComponentRefCore.RefLocator` 依赖前者，且 `EntityGraph` / `ComponentManager` / `EntityMatcher` 的 v1 路径仍引用它们；Task 2 随 v1 存储一起删除）。新公开结构体不再实现它们。注意 v1 `ComponentRefCore` 类在 `ECS/Managers/ComponentManager.cs`，不在本任务三个文件内，同样由 Task 2 删除。
9. **`GetComponents()` 不返回 Tag**（tag 无数据无 ref），顺序 = Dense（TypeId 升序）→ Discrete（store 枚举序），调用方不应依赖顺序。
10. **命名冲突**：v1 `CoreECS.Managers.ComponentRefCore` 与新 `CoreECS.Structures.ComponentRefCore` 同名，`Entity.cs` 同时 using 两个命名空间，必须使用 using 别名。

**测试说明：** 本任务不写新测试。公开 swap 的正确性由现有行为套件在 Task 2 恢复编译后验证；Task 1 结束时仓库处于预期的中间红状态（见 Step 5 / Step 6），不是回归。

---

- [ ] **Step 1: 内核前向适配**

1a. `ECS/Structures/DiscreteStore.cs`：

```diff
-    internal sealed class DiscreteStore<T> : DiscreteStore
-        where T : struct, IDiscreteComponent<T>
+    internal sealed class DiscreteStore<T> : DiscreteStore
+        where T : struct, IComponent<T>
```

1b. `ECS/Structures/SpareSetComponentContainer.cs`（`GetOrCreateStore`）：

```diff
-        public DiscreteStore<T> GetOrCreateStore<T>() where T : struct, IDiscreteComponent<T>
+        public DiscreteStore<T> GetOrCreateStore<T>() where T : struct, IComponent<T>
```

1c. `ECS/Structures/Structure.cs`（仅放宽 Task 1 代码实际调用的两个方法；其余 discrete 泛型访问器保持窄约束，`ComponentRefCore` 走非泛型重载）：

```diff
-        public void SetDiscrete<T>(int row, in T value, uint version)
-            where T : struct, IDiscreteComponent<T>
+        public void SetDiscrete<T>(int row, in T value, uint version)
+            where T : struct, IComponent<T>
```

```diff
-        public ref T GetDiscreteRef<T>(int row) where T : struct, IDiscreteComponent<T>
+        public ref T GetDiscreteRef<T>(int row) where T : struct, IComponent<T>
```

1d. `ECS/Structures/ComponentHookDispatcher.cs`：

```diff
-        public static void RegisterDiscrete<T>() where T : struct, IDiscreteComponent<T>
+        public static void RegisterDiscrete<T>() where T : struct, IComponent<T>
```

1e. `ECS/Structures/ComponentOrchestrator.cs` 放宽 4 个方法约束：

```diff
-        public ComponentRefCore AddDiscreteComponent<T>(ulong entityId, in T value)
-            where T : struct, IDiscreteComponent<T>
+        public ComponentRefCore AddDiscreteComponent<T>(ulong entityId, in T value)
+            where T : struct, IComponent<T>
```

```diff
-        public ComponentRefCore AddTagComponent<T>(ulong entityId) where T : struct, ITagComponent<T>
+        public ComponentRefCore AddTagComponent<T>(ulong entityId) where T : struct, IComponent<T>
```

```diff
-        public void RemoveDiscreteComponent<T>(ulong entityId) where T : struct, IDiscreteComponent<T>
+        public void RemoveDiscreteComponent<T>(ulong entityId) where T : struct, IComponent<T>
```

```diff
-        public void RemoveTagComponent<T>(ulong entityId) where T : struct, ITagComponent<T>
+        public void RemoveTagComponent<T>(ulong entityId) where T : struct, IComponent<T>
```

1f. `ECS/Structures/ComponentOrchestrator.cs` 新增非泛型 `RemoveComponent`（放在 `RemoveTagComponent<T>` 之后、`RequireLocation` 之前），并把 `RemoveDenseComponent<T>` 方法体改为委托：

```csharp
/// <summary>
/// Removes a component by type id and kind. Dense removal migrates the row into the
/// structure without the type (invoking OnDestroy first and reporting the removal);
/// discrete/tag removal clears the row slot/bit and ignores absent components.
/// </summary>
/// <exception cref="InvalidOperationException">
/// Thrown when a dense component is absent, matching <see cref="RemoveDenseComponent{T}"/>.
/// </exception>
public void RemoveComponent(ulong entityId, uint typeId, ComponentKind kind)
{
    var location = RequireLocation(entityId);
    var current = location.Structure;

    switch (kind)
    {
        case ComponentKind.Dense:
            if (!current.HasDense(typeId))
            {
                throw new InvalidOperationException(
                    $"Entity {entityId} does not have dense component type id {typeId}.");
            }

            ComponentHookDispatcher.InvokeDenseDestroy(current, location.Row, typeId, entityId);

            var targetKey = new StructureKey(
                StructureKey.RemoveType(current.Key.ToArray(), typeId), current.Mask);
            var target = m_registry.GetOrCreate(targetKey);
            if (m_observer != null) target.Observer = m_observer;

            var sourceRow = location.Row;
            var targetRow = target.Append(entityId, location);
            current.CopyDenseTo(target, sourceRow, targetRow);
            current.CopyTagsTo(target, sourceRow, targetRow);
            current.MoveDiscreteTo(target, sourceRow, targetRow);
            current.SwapRemove(sourceRow);

            m_observer?.OnComponentRemoved(target, targetRow, typeId);
            return;

        case ComponentKind.Discrete:
            if (!current.HasDiscrete(typeId, location.Row)) return;
            ComponentHookDispatcher.InvokeDiscreteDestroy(current, location.Row, typeId, entityId);
            current.RemoveDiscrete(typeId, location.Row);
            return;

        case ComponentKind.Tag:
            current.RemoveTag(typeId, location.Row);
            return;
    }
}
```

`RemoveDenseComponent<T>` 的方法体替换为：

```csharp
public void RemoveDenseComponent<T>(ulong entityId) where T : struct, IComponent<T>
{
    RemoveComponent(entityId, ComponentTypeRegistry.GetOrRegister<T>().TypeId, ComponentKind.Dense);
}
```

1g. `ECS/Managers/ComponentManager.cs`：文件顶部 `using CoreECS.Utils;` 之后加 `using CoreECS.Structures;`；类体内加前向访问器（放在 `OnManagerCreated()` 之前）：

```csharp
/// <summary>
/// v2 component kernel orchestrator. Declared here by Plan 1c Task 1 so the public
/// Entity swap compiles; Plan 1c Task 2 creates and injects the instance when the
/// manager is rewired to the kernel.
/// </summary>
internal ComponentOrchestrator Orchestrator { get; set; }
```

- [ ] **Step 2: 重写 `ECS/Defines/ComponentRef.cs`**

文件顶部 `using` 增加 `System.Runtime.CompilerServices` 与 `CoreECS.Structures`；`IComponentRefLocator`（原第 10-68 行）与 `IComponentRefCore`（原第 74-90 行）两个 v1 接口定义原样保留；从 `ComponentRef` 结构体开始整体替换为以下内容（含新的 `ComponentRefCoreComparer` 辅助类）：

```csharp
    /// <summary>
    /// Value equality for v2 ref cores. v1 compared shared pooled cores by reference;
    /// v2 creates a core per access, so identity is the component instance coordinates.
    /// </summary>
    internal static class ComponentRefCoreComparer
    {
        public static bool Equals(ComponentRefCore left, ComponentRefCore right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null) return false;

            return ReferenceEquals(left.Location, right.Location)
                   && left.Generation == right.Generation
                   && left.TypeId == right.TypeId
                   && left.Kind == right.Kind
                   && left.Version == right.Version;
        }

        public static int GetHashCode(ComponentRefCore core)
        {
            if (core == null) return 0;

            unchecked
            {
                var hash = core.Location == null ? 0 : RuntimeHelpers.GetHashCode(core.Location);
                hash = (hash * 397) ^ (int)core.Generation;
                hash = (hash * 397) ^ (int)core.TypeId;
                hash = (hash * 397) ^ (int)core.Kind;
                hash = (hash * 397) ^ (int)core.Version;
                return hash;
            }
        }
    }

    /// <summary>
    /// Typeless component reference over the v2 kernel core. <see cref="Core"/> is internal
    /// because the kernel core type is internal; public members keep v1 semantics.
    /// </summary>
    public readonly struct ComponentRef : IEquatable<ComponentRef>
    {
        /// <summary>Kernel reference core; null for default/invalid refs.</summary>
        internal readonly ComponentRefCore Core;

        /// <summary>Creates a ref around a kernel core (ECS integration only).</summary>
        internal ComponentRef(ComponentRefCore core)
        {
            Core = core;
        }

        /// <summary>True when the referenced component instance still exists.</summary>
        public bool NotNull => Core != null && Core.NotNull;

        /// <summary>Runtime type of the referenced component, or null when invalid.</summary>
        public Type RuntimeType => NotNull ? ComponentTypeRegistry.GetById(Core.TypeId).Type : null;

        /// <summary>Entity owning the component, or 0 when invalid.</summary>
        public ulong EntityId => Core?.EntityId ?? 0UL;

        /// <summary>Current revision, or 0 when invalid/tag.</summary>
        public ulong Revision => Core?.Revision ?? 0UL;

        /// <summary>Checks whether the ref points at a component of type <typeparamref name="T"/>.</summary>
        public bool Inspect<T>() where T : struct, IComponent<T>
            => NotNull && Core.TypeId == ComponentTypeRegistry.GetOrRegister<T>().TypeId;

        /// <summary>Checks whether the ref points at a component of the given type.</summary>
        public bool Inspect(Type type)
            => NotNull
               && type != null
               && ComponentTypeRegistry.TryGet(type, out var info)
               && info.TypeId == Core.TypeId;

        /// <summary>Converts to a typed ref, validating presence and type unless skipped.</summary>
        /// <exception cref="NullReferenceException">Thrown when the ref is invalid.</exception>
        /// <exception cref="InvalidCastException">Thrown when the component type differs.</exception>
        public ComponentRef<T> Typed<T>(bool noSafeCheck = false) where T : struct, IComponent<T>
        {
            if (!noSafeCheck)
            {
                if (Core == null || !Core.NotNull) throw new NullReferenceException("Component Reference is cut.");
                if (Core.TypeId != ComponentTypeRegistry.GetOrRegister<T>().TypeId)
                    throw new InvalidCastException("Given type is unmatched with actual component type.");
            }

            return new ComponentRef<T>(Core);
        }

        /// <inheritdoc />
        public bool Equals(ComponentRef other) => ComponentRefCoreComparer.Equals(Core, other.Core);

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (obj is null) return Core is null;
            return obj is ComponentRef other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode() => ComponentRefCoreComparer.GetHashCode(Core);

        public static bool operator ==(ComponentRef left, ComponentRef right) => left.Equals(right);

        public static bool operator !=(ComponentRef left, ComponentRef right) => !left.Equals(right);
    }

    /// <summary>
    /// Typed component reference over the v2 kernel core. RO/RW read the owning structure
    /// directly; RW bumps the revision (emitting the change event through the structure
    /// observer) before handing out the writable ref.
    /// </summary>
    public readonly struct ComponentRef<T> : IEquatable<ComponentRef<T>> where T : struct, IComponent<T>
    {
        /// <summary>Kernel reference core; null for default/invalid refs.</summary>
        internal readonly ComponentRefCore Core;

        /// <summary>Creates a ref around a kernel core (ECS integration only).</summary>
        internal ComponentRef(ComponentRefCore core)
        {
            Core = core;
        }

        /// <summary>True when the referenced component instance still exists.</summary>
        public bool NotNull => Core != null && Core.NotNull;

        /// <summary>Entity owning the component, or 0 when invalid.</summary>
        public ulong EntityId => Core?.EntityId ?? 0UL;

        /// <summary>Current revision, or 0 when invalid/tag.</summary>
        public ulong Revision => Core?.Revision ?? 0UL;

        /// <summary>Readonly ref to the component data.</summary>
        /// <exception cref="NullReferenceException">Thrown when the ref is invalid.</exception>
        public ref readonly T RO
        {
            get
            {
                var structure = RequireStructure();
                var row = Core.Location.Row;
                switch (Core.Kind)
                {
                    case ComponentKind.Dense:
                        return ref structure.GetDenseRef<T>(row);
                    case ComponentKind.Discrete:
                        return ref structure.GetDiscreteRef<T>(row);
                    default:
                        throw new InvalidOperationException("Tag components carry no data.");
                }
            }
        }

        /// <summary>Writable ref to the component data; bumps the revision on access.</summary>
        /// <exception cref="NullReferenceException">Thrown when the ref is invalid.</exception>
        public ref T RW
        {
            get
            {
                var structure = RequireStructure();
                Core.ChangeRevision();
                var row = Core.Location.Row;
                switch (Core.Kind)
                {
                    case ComponentKind.Dense:
                        return ref structure.GetDenseRef<T>(row);
                    case ComponentKind.Discrete:
                        return ref structure.GetDiscreteRef<T>(row);
                    default:
                        throw new InvalidOperationException("Tag components carry no data.");
                }
            }
        }

        /// <summary>Converts to the typeless ref.</summary>
        /// <exception cref="NullReferenceException">Thrown when the ref is invalid.</exception>
        public ComponentRef Untyped()
        {
            if (Core == null || !Core.NotNull) throw new NullReferenceException("Component Reference is cut.");
            return new ComponentRef(Core);
        }

        private Structure RequireStructure()
        {
            if (Core == null || !Core.NotNull) throw new NullReferenceException("Component Reference is cut.");
            return Core.Location.Structure;
        }

        /// <inheritdoc />
        public bool Equals(ComponentRef<T> other) => ComponentRefCoreComparer.Equals(Core, other.Core);

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (obj is null) return Core is null;
            return obj is ComponentRef<T> other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode() => ComponentRefCoreComparer.GetHashCode(Core);

        public static bool operator ==(ComponentRef<T> left, ComponentRef<T> right) => left.Equals(right);

        public static bool operator !=(ComponentRef<T> left, ComponentRef<T> right) => !left.Equals(right);

        public static implicit operator ComponentRef(ComponentRef<T> obj) => obj.Untyped();

        public static explicit operator ComponentRef<T>(ComponentRef obj) => obj.Typed<T>();
    }
```

- [ ] **Step 3: 重写 `ECS/Entity.cs`**

整体替换为：

```csharp
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CoreECS.Defines;
using CoreECS.Managers;
using CoreECS.Structures;
using CoreECS.Utils;
using ComponentRefCore = CoreECS.Structures.ComponentRefCore;

namespace CoreECS
{
    /// <summary>
    /// Handle to an entity inside a world. Holds the shared pooled
    /// <see cref="EntityLocation"/>, so references follow migrations and
    /// swap-removes automatically.
    /// </summary>
    public readonly struct Entity : IEquatable<Entity>
    {
        private readonly IWorld m_world;
        private readonly ulong m_entityId;
        private readonly EntityLocation m_location;
        private readonly uint m_generation;
        private readonly ComponentManager m_componentManager;

        /// <summary>Internal constructor used by the world integration layer.</summary>
        internal Entity(IWorld world, ulong entityId, EntityLocation location, uint generation)
        {
            m_world = world;
            m_entityId = entityId;
            m_location = location;
            m_generation = generation;
            m_componentManager = world?.GetManager<ComponentManager>();
        }

        /// <summary>World this entity belongs to.</summary>
        /// <exception cref="InvalidOperationException">Thrown for a default entity.</exception>
        public IWorld World => m_world ?? throw new InvalidOperationException("Entity is not associated with any world.");

        /// <summary>Unique id inside its world; safe to copy without the location being alive.</summary>
        public ulong EntityId => m_entityId;

        /// <summary>True while the location is alive and its generation matches this handle.</summary>
        public bool IsValid => m_location != null
                               && m_location.Structure != null
                               && m_location.Generation == m_generation;

        /// <summary>Component mask of the structure currently owning the entity.</summary>
        /// <exception cref="InvalidOperationException">Thrown when the entity is no longer alive.</exception>
        public ulong Mask => RequireLocation().Structure.Mask;

        /// <summary>Creates a default-initialized component of type <typeparamref name="T"/>.</summary>
        public ComponentRef<T> CreateComponent<T>() where T : struct, IComponent<T> => CreateComponent(default(T));

        /// <summary>
        /// Creates a component of type <typeparamref name="T"/> with the given value.
        /// Dense components may migrate the entity to another structure; discrete
        /// components overwrite an existing instance; tags ignore the value and return
        /// <c>default</c> (tags carry no data).
        /// </summary>
        public ComponentRef<T> CreateComponent<T>(T component) where T : struct, IComponent<T>
        {
            RequireLocation();
            var orchestrator = Orchestrator;
            var info = ComponentTypeRegistry.GetOrRegister<T>();
            switch (info.Kind)
            {
                case ComponentKind.Dense:
                    return new ComponentRef<T>(orchestrator.AddDenseComponent(m_entityId, component));
                case ComponentKind.Discrete:
                    return new ComponentRef<T>(orchestrator.AddDiscreteComponent(m_entityId, component));
                case ComponentKind.Tag:
                    orchestrator.AddTagComponent<T>(m_entityId);
                    return default;
                default:
                    throw new InvalidOperationException($"Unsupported component kind: {info.Kind}.");
            }
        }

        /// <summary>Destroys a component referenced by a typed handle.</summary>
        public void DestroyComponent<T>(ComponentRef<T> comp) where T : struct, IComponent<T>
        {
            Assertion.ArgumentNotNull(comp.NotNull ? this : null, "Component is null.");
            Assertion.AreEqual(comp.EntityId, m_entityId, "Component does not belong to this entity.");
            RequireLocation();
            Orchestrator.RemoveComponent(m_entityId, comp.Core.TypeId, comp.Core.Kind);
        }

        /// <summary>Destroys a component referenced by a typeless handle.</summary>
        public void DestroyComponent(ComponentRef comp)
        {
            Assertion.ArgumentNotNull(comp.NotNull ? this : null, "Component is null.");
            Assertion.AreEqual(comp.EntityId, m_entityId, "Component does not belong to this entity.");
            RequireLocation();
            Orchestrator.RemoveComponent(m_entityId, comp.Core.TypeId, comp.Core.Kind);
        }

        /// <summary>Destroys the component of type <typeparamref name="T"/>; throws when absent.</summary>
        public void DestroyComponent<T>() where T : struct, IComponent<T>
        {
            Assertion.IsTrue(HasComponent<T>(), "Entity does not have a component of type T.");
            var orchestrator = Orchestrator;
            var info = ComponentTypeRegistry.GetOrRegister<T>();
            switch (info.Kind)
            {
                case ComponentKind.Dense:
                    orchestrator.RemoveDenseComponent<T>(m_entityId);
                    return;
                case ComponentKind.Discrete:
                    orchestrator.RemoveDiscreteComponent<T>(m_entityId);
                    return;
                case ComponentKind.Tag:
                    orchestrator.RemoveTagComponent<T>(m_entityId);
                    return;
                default:
                    throw new InvalidOperationException($"Unsupported component kind: {info.Kind}.");
            }
        }

        /// <summary>
        /// Gets a reference to the component of type <typeparamref name="TComp"/>.
        /// Returns <c>default</c> when the component is absent or when
        /// <typeparamref name="TComp"/> is a tag (tags carry no refs).
        /// </summary>
        public ComponentRef<TComp> GetComponent<TComp>() where TComp : struct, IComponent<TComp>
        {
            RequireLocation();
            var info = ComponentTypeRegistry.GetOrRegister<TComp>();
            if (info.Kind == ComponentKind.Tag) return default;

            var core = Orchestrator.GetComponentRef<TComp>(m_entityId);
            return core == null ? default : new ComponentRef<TComp>(core);
        }

        /// <summary>
        /// Gets refs for all dense and discrete components on the entity. Tags are omitted
        /// (no data). Order is dense (type id ascending) then discrete (store enumeration
        /// order) and must not be relied on.
        /// </summary>
        public ComponentRef[] GetComponents()
        {
            var location = RequireLocation();
            var results = new List<ComponentRef>();
            CollectComponents(location, location.Structure, location.Row, results);
            return results.ToArray();
        }

        /// <summary>Adds all dense and discrete refs to <paramref name="results"/> and returns the count.</summary>
        public int GetComponents(ICollection<ComponentRef> results)
        {
            var location = RequireLocation();
            var before = results.Count;
            CollectComponents(location, location.Structure, location.Row, results);
            return results.Count - before;
        }

        /// <summary>Gets the refs of type <typeparamref name="TComp"/>; tags yield an empty array.</summary>
        public ComponentRef<TComp>[] GetComponents<TComp>() where TComp : struct, IComponent<TComp>
        {
            RequireLocation();
            var info = ComponentTypeRegistry.GetOrRegister<TComp>();
            if (info.Kind == ComponentKind.Tag) return Array.Empty<ComponentRef<TComp>>();

            var core = Orchestrator.GetComponentRef<TComp>(m_entityId);
            return core == null
                ? Array.Empty<ComponentRef<TComp>>()
                : new[] { new ComponentRef<TComp>(core) };
        }

        /// <summary>Adds the refs of type <typeparamref name="TComp"/> and returns the count.</summary>
        public int GetComponents<TComp>(ICollection<ComponentRef<TComp>> results)
            where TComp : struct, IComponent<TComp>
        {
            RequireLocation();
            var info = ComponentTypeRegistry.GetOrRegister<TComp>();
            if (info.Kind == ComponentKind.Tag) return 0;

            var core = Orchestrator.GetComponentRef<TComp>(m_entityId);
            if (core == null) return 0;

            results.Add(new ComponentRef<TComp>(core));
            return 1;
        }

        /// <summary>Checks whether the entity carries the component (all three kinds).</summary>
        public bool HasComponent<T>() where T : struct, IComponent<T>
        {
            RequireLocation();
            return Orchestrator.HasComponent<T>(m_entityId);
        }

        private static void CollectComponents(
            EntityLocation location, Structure structure, int row, ICollection<ComponentRef> results)
        {
            var denseTypeIds = structure.DenseTypeIds;
            for (var i = 0; i < denseTypeIds.Count; i++)
            {
                var typeId = denseTypeIds[i];
                results.Add(new ComponentRef(new ComponentRefCore(
                    location, location.Generation, typeId, ComponentKind.Dense,
                    structure.GetDenseVersion(typeId, row))));
            }

            var spareSet = structure.SpareSetOrNull;
            if (spareSet == null) return;

            foreach (var typeId in spareSet.TypeIds)
            {
                if (!structure.HasDiscrete(typeId, row)) continue;
                results.Add(new ComponentRef(new ComponentRefCore(
                    location, location.Generation, typeId, ComponentKind.Discrete,
                    structure.GetDiscreteVersion(typeId, row))));
            }
        }

        private EntityLocation RequireLocation()
        {
            if (!IsValid) throw new InvalidOperationException("Entity has already been destroyed.");
            return m_location;
        }

        private ComponentOrchestrator Orchestrator
        {
            get
            {
                var orchestrator = m_componentManager?.Orchestrator;
                if (orchestrator == null)
                {
                    throw new InvalidOperationException("Entity is not associated with a component kernel.");
                }

                return orchestrator;
            }
        }

        #region Equality

        /// <summary>Same world, entity id and generation.</summary>
        public bool Equals(Entity other)
        {
            return ReferenceEquals(m_world, other.m_world)
                   && m_entityId == other.m_entityId
                   && m_generation == other.m_generation;
        }

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is Entity other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode()
        {
            var worldHash = m_world == null ? 0 : RuntimeHelpers.GetHashCode(m_world);
            return HashCode.Combine(worldHash, m_entityId, m_generation);
        }

        public static bool operator ==(Entity left, Entity right) => left.Equals(right);

        public static bool operator !=(Entity left, Entity right) => !left.Equals(right);

        #endregion
    }
}
```

- [ ] **Step 4: 重写 `ECS/EntityExtension.cs`**

整体替换为（公开签名与 v1 完全一致，`GetOrCreateComponent` 复用 `TryGetComponent`）：

```csharp
using CoreECS.Defines;

namespace CoreECS
{
    /// <summary>
    /// Entity component access helpers built on the public Entity API.
    /// Tag components carry no refs: TryGetComponent returns true with a default ref
    /// for a present tag, and GetOrCreateComponent adds the tag then returns a default ref.
    /// </summary>
    public static class EntityExtension
    {
        /// <summary>Tries to get the component of type <typeparamref name="TComp"/>.</summary>
        /// <param name="entity">Entity to get the component from.</param>
        /// <param name="componentRef">Component reference; default when absent.</param>
        /// <returns>True when the component exists.</returns>
        public static bool TryGetComponent<TComp>(this Entity entity, out ComponentRef<TComp> componentRef)
            where TComp : struct, IComponent<TComp>
        {
            if (entity.HasComponent<TComp>())
            {
                componentRef = entity.GetComponent<TComp>();
                return true;
            }

            componentRef = default;
            return false;
        }

        /// <summary>Gets the component of type <typeparamref name="TComp"/>, creating a default one when absent.</summary>
        /// <param name="entity">Entity to get the component from.</param>
        /// <param name="componentRef">Component reference.</param>
        /// <returns>True when the component already existed.</returns>
        public static bool GetOrCreateComponent<TComp>(this Entity entity, out ComponentRef<TComp> componentRef)
            where TComp : struct, IComponent<TComp>
        {
            if (entity.TryGetComponent(out componentRef)) return true;

            componentRef = entity.CreateComponent<TComp>();
            return false;
        }

        /// <summary>Gets the component of type <typeparamref name="TComp"/>, creating one with <paramref name="initialValue"/> when absent.</summary>
        /// <param name="entity">Entity to get the component from.</param>
        /// <param name="componentRef">Component reference.</param>
        /// <param name="initialValue">Initial value used when the component is created.</param>
        /// <returns>True when the component already existed.</returns>
        public static bool GetOrCreateComponent<TComp>(
            this Entity entity, out ComponentRef<TComp> componentRef, in TComp initialValue)
            where TComp : struct, IComponent<TComp>
        {
            if (entity.TryGetComponent(out componentRef)) return true;

            componentRef = entity.CreateComponent(initialValue);
            return false;
        }
    }
}
```

- [ ] **Step 5: 编译验证（预期失败，错误面有界）**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet build ECS/ECS.csproj`

Expected: **FAIL**，错误只出现在 Task 2 将重写/删除的 v1 集成文件，共 8 处（多目标构建可能每处报两次）：

| 文件 | 位置 | 错误 |
|---|---|---|
| `ECS/EntityGraph.cs` | 62、74、85、103、127 | `ComponentRef` / `ComponentRef<T>` 构造函数只接受 `CoreECS.Structures.ComponentRefCore`，传入 v1 `IComponentRefCore` → CS1503 |
| `ECS/World.cs` | 152、171、256 | `Entity` 构造函数签名变为 `(IWorld, ulong, EntityLocation, uint)`，传入 5 参旧签名 → CS1729 |

若出现上述列表之外的错误（尤其 `ECS/Entity.cs` / `ECS/Defines/ComponentRef.cs`），说明 Step 1 的内核前向适配有遗漏，必须修复后再提交。

- [ ] **Step 6: 全量测试说明**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj`

Expected: **无法运行**。Test 项目仍引用 v1 内部 API（`Core.RefLocator` / `Core.Offset` / `Core.Version`、`new Entity(world, id, generation)`、`EntityGraph`、v1 `ComponentRefCore`、`EntityManager.GetEntity` 等），编译失败属预期。Task 2 恢复 `ECS` 编译后，以下测试文件在 Task 3 迁移前预期为红：

- 需重写（v1 存储/管理器语义）：`ComponentManagerTestUnit`、`EntityGraphTestUnit`、`EntityManagerTestUnit`
- 需机械适配（断言 v1 ref 内部结构或 v1 语义）：`ComponentTestUnit`（`Core.RefLocator/Offset/Version`）、`EntityTestUnit`（`Core.RefLocator`、旧 `Entity` 构造、同一实体重复添加同类型 dense 组件）、`EntityMatcherTestUnit`（把 `GetComponents().Select(x => x.Core)` 传给 v1 `ComponentFilter`）、`WorldTestUnit`（`EntityManager.GetEntity`）
- 预期保持通过（Task 2 接线后）：`EntityCollectorTestUnit`、`IntegrationTestUnit`、`StressTestUnit`，以及所有 Plan 1a/1b 内核套件

- [ ] **Step 7: 提交**

```bash
git add ECS/Entity.cs ECS/EntityExtension.cs ECS/Defines/ComponentRef.cs \
        ECS/Structures/ComponentOrchestrator.cs ECS/Structures/ComponentHookDispatcher.cs \
        ECS/Structures/Structure.cs ECS/Structures/SpareSetComponentContainer.cs \
        ECS/Structures/DiscreteStore.cs ECS/Managers/ComponentManager.cs
git commit -m "refactor(core): swap public entity and component ref to v2 kernel"
```

---

## Self-Review 记录

1. **handoff 覆盖**：约束 2（Entity v2）与约束 3（ComponentRef v2）→ Task 1；约束 6（EntityMatchManager 接线）、约束 7/9（World/MinimalWorld + `CreateEntity(mask)`）、约束 8（删 v1 存储）→ Task 2；内部测试迁移 → Task 3（见"本计划范围边界"）。
2. **占位符扫描**：Task 1 三个公开文件均为完整代码；内核适配为精确 diff；无 TBD/TODO/"类似 Task N"。
3. **类型一致性核对**：Entity 调用的内核 API 均来自 Plan 1b 或本任务 Step 1——`ComponentOrchestrator.{CreateEntity, DestroyEntity, AddDenseComponent, RemoveDenseComponent, AddDiscreteComponent, RemoveDiscreteComponent, AddTagComponent, RemoveTagComponent, HasComponent, GetComponentRef, RemoveComponent}`；`Structure.{Mask, DenseTypeIds, SpareSetOrNull, HasDiscrete, GetDenseVersion(uint,int), GetDiscreteVersion(uint,int), GetDenseRef, GetDiscreteRef, SetDiscrete}`；`EntityTable.{TryGetLocation, Create, Destroy}`；`ComponentRefCore.{NotNull, EntityId, Revision, ChangeRevision, Location, Generation, TypeId, Kind, Version}`。命名与 Plan 1b 计划逐字一致。
4. **CS0314 约束问题**：见设计决策 1；discrete 完整调用链已逐环放宽（`Entity` → `AddDiscreteComponent` → `RegisterDiscrete` / `SetDiscrete` → `GetOrCreateStore` → `DiscreteStore<T>`；读路径 `ComponentRef<T>.RO/RW` → `GetDiscreteRef`），tag 路径只放宽编排层两个方法，无遗漏。
5. **Tag 语义**：`GetComponent<Tag>` 返回 `default` 与 spec 4.2（第 124 行）一致；`CreateComponent<Tag>` 返回 `default` 是本计划按"Tag 无 ref"（handoff 约束 3）的补充决策，Task 3 应为 Tag / Discrete 增加行为测试。
6. **失效实体语义**：保持 v1 抛异常，避免 Task 3 无谓改写 `Entity_InvalidAccessThrowsException` / `Entity_DestroyEntityThenAccess_ThrowsException`。
7. **相等性**：结构相等保证 v1 的 `Create` / `Get` 引用相等语义（`EntityTestUnit.ComponentRef_EqualsOperator_DifferentReferencesSameComponent_ReturnsTrue` 与 `ComponentRef_EqualityOperator_SameComponent_ReturnsTrue`）。
8. **预期红状态有界**：Task 1 后 `ECS/ECS.csproj` 仅 `EntityGraph.cs`（5 处）与 `World.cs`（3 处）编译失败；Step 5 给出了精确错误表，超出即为实现缺陷。
9. **前向依赖显式化**：`ComponentManager.Orchestrator`（Task 2 注入）与内核约束放宽（Task 1 落地）已在本任务声明；Task 2 计划不得重复定义或回退约束。
10. **遗留记录**：spec 2.1 要求三种 kind 均调用 `OnCreate` / `OnDestroy`，Plan 1b 内核有意跳过 Tag 的 hook（Tag 默认空实现，行为等价）。若后续要求 Tag 自定义 hook，需在 Plan 1b 内核补 `InvokeTagCreate/InvokeTagDestroy`，本任务不处理。
11. **验证命令统一**：全部带 `PATH="$HOME/.dotnet:$PATH"`；提交信息按用户指定 `refactor(core): swap public entity and component ref to v2 kernel`。
