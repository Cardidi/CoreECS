# CoreECS ref/span 失效静态检查（Roslyn 分析器）Spec

> 状态：待评审。实现前请先确认第 3 节诊断消息与第 7 节性能指标。

## 1. 背景

CoreECS v2 使用 archetype（`Structure`）行式 SoA 存储。以下 API 返回指向结构内部数组的**裸引用**：

- `ComponentRef<T>.RO` → `ref readonly T`
- `ComponentRef<T>.RW` → `ref T`
- `Structure.GetReadOnlyDenseColumn<T>()` → `ReadOnlySpan<T>`
- `Structure.GetReadWriteDenseColumn<T>()` → `Span<T>`

（`ComponentRef<T>` 定义见 `Kernel/Defines/ComponentRef.cs`；`Structure` 见 `Kernel/Structures/Structure.cs`。）

任何**结构变更**都可能移动/重建底层数组，使已发出的裸 ref/span 失效，之后继续读写会指向别的实体数据、过期数据或越界。目标是在编译期给出诊断，引导用户"要么先复制值，要么在结构变更前用完 ref"。

此外，结构变更可能被用户代码**封装**：只要某个函数（传递地）调用了结构变更 API，调用该函数同样会让裸 ref/span 失效。分析器需要实现**不安全传递性**，并在"ref/span 作为函数参数传入、函数内部又执行结构变更"时提示调用方与函数实现。

## 2. 诊断

| 项 | ECS0001 | ECS0002 |
|---|---|---|
| 名称 | `RefInvalidatedByStructuralChange` | `RefParameterInvalidatedByStructuralChange` |
| 默认级别 | Warning（可经 `.editorconfig` 提升为 error） | Warning（同左） |
| 消息 | `ref/span obtained from '{0}' may be invalidated by '{1}'; copy the value or finish using it before structural changes` | `ref/span parameter '{0}' may be invalidated by '{1}'; re-acquire the ref/span after the structural change` |
| `{0}` | 来源表达式（如 `ComponentRef<PositionComponent>.RW`、`Structure.GetReadWriteDenseColumn<PositionComponent>`） | 逗号分隔的参数名（如 `value`、`left, right`） |
| `{1}` | 触发调用显示名（如 `CreateComponent<VelocityComponent>`、`Fill`） | 同左 |
| 位置 | 触发调用（Invocation / ObjectCreation / 属性访问） | 函数体内触发调用处 |

`.editorconfig`：

```ini
dotnet_diagnostic.ECS0001.severity = warning # 或 error
dotnet_diagnostic.ECS0002.severity = warning # 或 error
```

## 3. 检测规则

### 3.1 ECS0001：来源

1. 属性访问 `.RO` / `.RW`，接收者类型为 `CoreECS.Defines.ComponentRef<T>`。
2. 方法调用 `GetReadOnlyDenseColumn<T>()` / `GetReadWriteDenseColumn<T>()`，接收者类型为 `CoreECS.Structures.Structure`。
3. 由上述来源派生的 `ref` / `ref readonly` 本地变量，以及 `Span<T>` / `ReadOnlySpan<T>` 本地变量（含 `var` 推断、切片、赋值传递）。

`ComponentRef<T>` 句柄本身（`GetComponent<T>()` 的返回值）跨结构变更是安全的，**不**作为来源。

### 3.2 ECS0001：存活区间

从声明点到该符号在方法体内的**最后一次引用**；重新赋值结束旧值的存活区间（重新从新来源赋值则开启新区间）；以 `ref` / `out` / `in` 实参传入其他方法、或无法确定最后一次引用时，保守视为存活到方法结束。

### 3.3 触发调用（ECS0001 / ECS0002 共用）

在存活区间（ECS0001）或函数体（ECS0002）内出现即报告一次，位置指向触发调用：

1. 固定结构变更 API：
   - `Entity.CreateComponent`、`Entity.DestroyComponent`
   - `Entity.SetMask`
   - `World.CreateEntity`、`World.DestroyEntity`
   - `CommandBuffer.Playback`
2. **传递不安全的用户调用**（见第 4 节）：目标方法（含属性访问器、索引器访问器、构造函数、运算符）经不安全传递闭包判定为不安全。

触发调用的形态：方法调用、对象创建（`new`）、属性/索引器读取（getter）、属性/索引器赋值（setter）、复合赋值与自增自减的目标访问器。

### 3.4 ECS0002：ref/span 参数

函数（方法、构造函数、访问器、局部函数）满足以下条件时，函数体内每个不安全调用各报一条 ECS0002：

1. 至少有一个"可能指向 Structure 内部存储"的参数：
   - `ref` / `out` / `in` 参数（任意类型），但参数类型为 `ComponentRef` / `ComponentRef<T>` 时排除（句柄安全）；
   - 非 `ref` 的 `Span<T>` / `ReadOnlySpan<T>` 参数；
   - 排除隐式 `this` 参数。
2. 函数体内出现第 3.3 节的触发调用（固定 API 或传递不安全调用），且至少一个 ref/span 参数在该触发调用**之后仍被使用**（触发调用自身实参中的使用不算：实参在结构变更前已按值读取）。

调用方传入的 ref/span 在调用返回后继续使用，由 ECS0001 在调用点捕获；ECS0002 只针对函数自身在结构变更后继续使用参数（读/写）的场景。

### 3.5 不报告

- 直接按值读取后立即复制（如 `var x = position.RO.X;`，没有产生 ref/span 本地变量）。
- 结构变更调用之后才声明的 ref/span。
- ref/span 的最后一次使用早于结构变更调用。
- `ComponentRef<T>` 句柄本身作为参数（ECS0002 排除）。

## 4. 不安全方法与传递性

### 4.1 定义

- **直接不安全**：函数体的操作树中出现固定结构变更 API（第 3.3.1 节）。嵌套 lambda 体视为函数体的一部分；嵌套局部函数是独立节点，不计入外层函数体。
- **传递不安全**：函数调用了不安全函数（工作列表不动点；递归/环安全）。
- **抽象成员闭包**：若某实现/重写不安全，则其 `override` 链上的基方法、显式接口实现、隐式接口实现对应的接口成员也标记为不安全。因此通过基类/接口调用的点会被判定为不安全。

### 4.2 覆盖的函数形态

方法、构造函数、析构函数、运算符/转换运算符、属性/事件访问器、局部函数。

### 4.3 已知限制（写入 README）

- 不安全传递仅限**同一编译单元**；来自 metadata 的方法视为安全。
- 委托变量、反射、`foreach`/`using`/`await`/LINQ 等隐式协议调用不跟踪。
- 嵌套 lambda 体保守计入外层函数；`ComponentRef<T>` 句柄参数不视为可能失效。
- ref/span 的**来源**追踪仍仅限方法内（不跨方法传播来源）；跨方法只传播"不安全"标记。

## 5. 正反例

ECS0001（与原始 spec 相同，新增传递性示例）：

```csharp
ref var position = ref entity.GetComponent<PositionComponent>().RW;
position.X = 1;
entity.CreateComponent<VelocityComponent>();   // ECS0001
position.Y = 2;

var positions = structure.GetReadWriteDenseColumn<PositionComponent>();
world.CreateEntity();                          // ECS0001
var first = positions[0];
```

```csharp
void Wrapper() => entity.CreateComponent<VelocityComponent>(); // Wrapper 不安全

ref var position = ref entity.GetComponent<PositionComponent>().RW;
Wrapper();                                     // ECS0001（传递不安全）
position.X = 1;
```

ECS0002：

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

正确：

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

## 6. 交付物

目录结构与 `Kernel/`、`Test/` 保持一致（顶层文件夹 = 一个项目）：

1. 分析器项目 `Analyzers/Analyzers.csproj`
   - `AssemblyName` / `RootNamespace` = `CoreECS.Analyzers`（与 `Kernel/Kernel.csproj` 的 `AssemblyName=CoreECS` 命名方式一致）
   - `netstandard2.0`；`Microsoft.CodeAnalysis.CSharp`（4.8+，`PrivateAssets=all`）
   - `IsRoslynComponent=true`、`EnforceExtendedAnalyzerRules=true`、`IncludeBuildOutput=false`
   - `RefInvalidatedByStructuralChangeAnalyzer`（ECS0001 + ECS0002）+ `UnsafeMethodIndex`（编译单元级不动点）
2. 测试加入现有 `Test/Test.csproj`
   - 新增包引用：`Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.NUnit`、`Microsoft.CodeAnalysis.CSharp`、`Microsoft.CodeAnalysis.CSharp.Workspaces`
   - 新增项目引用：`..\Analyzers\Analyzers.csproj`
   - 测试文件放在 `Test/`，命名空间 `CoreECS.Test.Analyzers`（可用 `dotnet test --filter "FullyQualifiedName~CoreECS.Test.Analyzers"` 单独运行）
   - 覆盖第 5 节全部正反例、`ref readonly`、嵌套块、重赋值、方法末尾使用、`#pragma` 抑制、传递性、虚方法/接口分派、参数范围、递归环
   - 内联 CoreECS API stub，不依赖运行时项目
3. `Analyzers` 项目加入 `CoreECS.sln`（`Test` 已在解决方案中）
4. `Analyzers/README.md`：引用方式、配置、检测规则、限制说明
5. 可选（第二阶段）：CodeFix

## 7. 验收标准

- `dotnet build` / `dotnet test` 全绿。
- ECS0001 错误示例各报且只报一条，位置在触发调用上；正确示例零报告。
- ECS0002 示例在函数体内每个不安全调用处各报一条（仅当 ref/span 参数在该调用之后仍被使用）；安全参数（`ComponentRef<T>`）与调用后不再使用的参数零报告。
- 传递性示例：包装函数/虚方法/接口调用处报 ECS0001；递归环正确收敛。
- 对 `Kernel/` 现有源码运行分析器，ECS0001 与 ECS0002 均零误报（若有真实命中，先与需求方确认，不得直接改运行时代码）。
- 性能（已按跨方法传递调整，待评审确认数值）：
  - 不安全索引按编译单元构建一次；Kernel 全量（约 60 文件）索引构建 + 分析 < 2s；
  - 单方法 ECS0001/ECS0002 分析为 O(方法体)；含索引的大文件（约 2000 行）< 1s。
  - IDE 增量场景每次编译重建索引，属已知成本，README 说明。

## 8. 非目标

- ref/span 的**来源与存活**不做跨方法/跨字段/跨 lambda 数据流（仅方法内）。
- 不引入运行时检查或修改 `Kernel/` 行为。
- 不检测 `ComponentRef<T>` 句柄本身的有效性。
- 不跟踪委托/反射/隐式协议调用；metadata 方法视为安全。
