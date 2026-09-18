# CoreECS.Analyzers

CoreECS v2 的 Roslyn 静态检查，用于在编译期发现「裸 ref/span 跨结构变更继续使用」的隐患。

## ECS0001 · RefInvalidatedByStructuralChange

```
ref/span obtained from '{0}' may be invalidated by '{1}'; copy the value or finish using it before structural changes
```

CoreECS 的结构变更（组件增删、mask 迁移、实体创建/销毁、CommandBuffer.Playback，以及任何**传递调用**到这些 API 的函数）可能移动或重建 Structure 内部数组。以下 API 返回指向内部存储的裸引用：

- `ComponentRef<T>.RO` → `ref readonly T`
- `ComponentRef<T>.RW` → `ref T`
- `Structure.GetReadOnlyDenseColumn<T>()` → `ReadOnlySpan<T>`
- `Structure.GetReadWriteDenseColumn<T>()` → `Span<T>`

若这些 ref/span 仍处于存活状态（声明之后、最后一次引用之前）时调用了结构变更，ECS0001 会在触发调用处报告一次 Warning。

### 错误

```csharp
ref var position = ref entity.GetComponent<PositionComponent>().RW;
position.X = 1;
entity.CreateComponent<VelocityComponent>();   // ECS0001
position.Y = 2;                                // 已指向过期行

var positions = structure.GetReadWriteDenseColumn<PositionComponent>();
world.CreateEntity();                          // ECS0001：Append 可能 Grow 该结构
var first = positions[0];
```

```csharp
void Wrapper() => entity.CreateComponent<VelocityComponent>();

ref var position = ref entity.GetComponent<PositionComponent>().RW;
Wrapper();                                     // ECS0001：Wrapper 传递不安全
position.X = 1;
```

### 正确

```csharp
// 结构变更前用完
ref var position = ref entity.GetComponent<PositionComponent>().RW;
position.X = 1;
position.Y = 2;
entity.CreateComponent<VelocityComponent>();

// 结构变更后再取
entity.CreateComponent<VelocityComponent>();
ref var position = ref entity.GetComponent<PositionComponent>().RW;
position.X = 1;

// ComponentRef<T> 句柄本身安全：每次 .RW 访问重新解析
var positionRef = entity.GetComponent<PositionComponent>();
entity.CreateComponent<VelocityComponent>();
positionRef.RW.X = 1;
```

## ECS0002 · RefParameterInvalidatedByStructuralChange

```
ref/span parameter '{0}' may be invalidated by '{1}'; re-acquire the ref/span after the structural change
```

函数（方法、构造函数、访问器、局部函数）以 `ref` / `out` / `in` 参数，或 `Span<T>` / `ReadOnlySpan<T>` 值参数接收裸 ref/span 时，函数体内的结构变更（含传递不安全调用）会使该参数指向的存储失效。若函数在该触发调用**之后仍继续使用**该参数（读或写），ECS0002 会在触发调用处报告；触发调用自身实参中的读取（按值复制发生在结构变更之前）不算。

调用方传入的 ref/span 在调用返回后继续使用，由 ECS0001 在调用点捕获。

`ComponentRef` / `ComponentRef<T>` 句柄参数不受影响（句柄每次访问重新解析）。

### 错误

```csharp
void Fill(ref PositionComponent value)
{
    entity.CreateComponent<VelocityComponent>();  // ECS0002
    value.X = 1;                                  // 调用后继续使用已失效的 ref
}

void Consume(Span<PositionComponent> values)
{
    world.CreateEntity();                         // ECS0002
    var first = values[0];
}

void Forward(ref PositionComponent value)
{
    Wrapper();                                    // ECS0002（Wrapper 传递不安全）
    value.X = 1;
}
```

### 正确

```csharp
void Fill(ref PositionComponent value)
{
    value.X = 1;                                  // 无结构变更
}

void ReadOnly(in PositionComponent value)
{
    var x = value.X;
    entity.CreateComponent<VelocityComponent>();  // 之后不再使用 value → 不报
}
```

## 不安全传递性

若函数体内（含嵌套 lambda）调用了结构变更 API，则该函数**不安全**；调用不安全函数的函数同样不安全。该标记沿调用图传播，并覆盖虚方法 override 链与接口实现（任一实现不安全，则通过基类/接口的调用视为不安全）。因此：

- 调用传递不安全的函数，会像直接调用结构 API 一样使存活 ref/span 失效（ECS0001）；
- 带 ref/span 参数的函数调用不安全函数，且调用后继续使用该参数，会在该调用处报 ECS0002。

## 引用方式

项目引用（源码内使用）：

```xml
<ItemGroup>
  <ProjectReference Include="..\Analyzers\Analyzers.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

## 配置

`.editorconfig`：

```ini
dotnet_diagnostic.ECS0001.severity = warning # 或 error
dotnet_diagnostic.ECS0002.severity = warning # 或 error
```

单点抑制：

```csharp
#pragma warning disable ECS0001
entity.CreateComponent<VelocityComponent>();
#pragma warning restore ECS0001
```

## 检测规则

1. 来源（ECS0001）：`.RO` / `.RW` 属性访问（接收者 `CoreECS.Defines.ComponentRef<T>`）；`Structure.GetReadOnlyDenseColumn<T>()` / `GetReadWriteDenseColumn<T>()`；由这些来源派生的 `ref` / `ref readonly` 本地变量与 `Span<T>` / `ReadOnlySpan<T>` 本地变量。
2. 存活区间：从声明点到方法体内最后一次引用；重新赋值结束旧值的存活区间；以 `ref`/`out`/`in` 传给其他方法时保守视为存活到方法结束。
3. 触发：六个固定 API（`Entity.CreateComponent` / `DestroyComponent` / `SetMask`、`World.CreateEntity` / `DestroyEntity`、`CommandBuffer.Playback`）；传递不安全的用户调用、属性/索引器访问器、构造函数、运算符。
4. ECS0002 参数范围：`ref` / `out` / `in` 任意类型（`ComponentRef` / `ComponentRef<T>` 除外）+ `Span<T>` / `ReadOnlySpan<T>` 值参数；排除隐式 `this`。仅当参数在触发调用之后仍被使用时报告。
5. 只分析方法体/函数体内；结构变更之后才声明的 ref/span、最后一次引用早于结构变更的 ref/span、按值复制（如 `var x = position.RO.X;`）都不报告。

## 已知限制

- ref/span 的**来源与存活**仅方法内分析，不跨方法/字段/lambda；跨方法只传播"不安全"标记。
- 不安全传递仅限**同一编译单元**；来自外部程序集（metadata）的方法视为安全。
- 委托变量、反射、`foreach` / `using` / `await` / LINQ 等隐式协议调用不跟踪；字段初始化器中的 lambda 不计入任何函数。
- 嵌套 lambda 体保守计入外层函数（lambda 内调用结构 API 会使外层函数不安全）。
- ECS0002 只检查函数自身在结构变更后对参数的使用；调用方在调用后使用传入的 ref/span 由 ECS0001 在调用点报告。
- 触发 API 与 ref 来源是否属于同一实体不做区分：只要存活区间内出现触发就报告。误报可用 `#pragma warning disable ECS0001` / `ECS0002` 抑制。
- 不检测结构内部 API（`Structure.Append` / `SwapRemove` / `Grow` / `GetDenseRef`）与第三方包装。
- 性能：不安全索引按编译单元构建一次（IDE 增量编辑时随编译重建），大型项目有一次性成本。
