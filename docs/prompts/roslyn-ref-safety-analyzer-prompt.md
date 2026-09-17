# 任务：为 CoreECS 实现 ref/span 失效静态检查的 Roslyn 分析器

> 把本文件整份交给实现 AI。仓库为 CoreECS（纯 C# ECS 库），本任务只新增分析器项目与测试，不修改 `Kernel/` 运行时行为。

## 1. 背景

CoreECS v2 使用 archetype（`Structure`）行式 SoA 存储。以下 API 返回指向结构内部数组的**裸引用**：

- `ComponentRef<T>.RO` → `ref readonly T`
- `ComponentRef<T>.RW` → `ref T`
- `Structure.GetReadOnlyDenseColumn<T>()` → `ReadOnlySpan<T>`
- `Structure.GetReadWriteDenseColumn<T>()` → `Span<T>`

（`ComponentRef<T>` 定义见 `Kernel/Defines/ComponentRef.cs`；`Structure` 见 `Kernel/Structures/Structure.cs`。）

任何**结构变更**都可能移动/重建底层数组，使已发出的裸 ref/span 失效，之后继续读写会指向别的实体数据、过期数据或越界：

- `Entity.CreateComponent<T>()` / `CreateComponent<T>(T)`（dense 迁移：行在 Structure 间移动 + `SwapRemove`）
- `Entity.DestroyComponent<T>()` / `DestroyComponent<T>(ComponentRef<T>)` / `DestroyComponent(ComponentRef)`
- `Entity.SetMask(ulong)`
- `World.CreateEntity(...)`（向结构 `Append`，可能触发数组 `Grow`）
- `World.DestroyEntity(ulong)` / `DestroyEntity(Entity)`
- `CommandBuffer.Playback()`（录制阶段不修改存储，只有 Playback 应用）
- 同一 `Structure` 上任何 `Append` / `SwapRemove` / `Grow`（含通过其他实体触发的间接变更）

目前这些失效只能靠运行期表现为错写/异常；目标是在编译期给出诊断，引导用户"要么先复制值，要么在结构变更前用完 ref"。

## 2. 目标

新增一个 Roslyn 分析器：当**从上述来源获得的 ref 本地变量或 span 仍处于存活状态**时，同一方法体内调用了可能触发结构变更的 API，则报告诊断。

## 3. 诊断规格

| 项 | 值 |
|---|---|
| ID | `ECS0001` |
| 名称 | `RefInvalidatedByStructuralChange` |
| 默认级别 | Warning（可通过 `.editorconfig` 提升为 error） |
| 消息 | `ref/span obtained from '{0}' may be invalidated by '{1}'; copy the value or finish using it before structural changes` |

`.editorconfig` 建议片段（写进 README）：

```ini
dotnet_diagnostic.ECS0001.severity = warning # 或 error
```

## 4. 检测规则（方法体内，保守启发式）

1. **识别 ref/span 来源**
   - 属性访问 `.RO` / `.RW`，其接收者类型为 `CoreECS.Defines.ComponentRef<T>`（`T : struct, CoreECS.Defines.IComponent<T>`）。
   - 方法调用 `GetReadOnlyDenseColumn<T>()` / `GetReadWriteDenseColumn<T>()`，接收者类型为 `CoreECS.Structures.Structure`。
   - 由上述来源派生的 `ref` / `ref readonly` 本地变量，以及 `Span<T>` / `ReadOnlySpan<T>` 本地变量。
2. **存活区间**：从声明点到该符号在方法体内的**最后一次引用**；无法确定时保守视为到方法结束。重新赋值会结束旧值的存活区间。
3. **触发 API**（在存活区间内出现即报告一次，位置指向触发调用）
   - `Entity.CreateComponent`、`Entity.DestroyComponent`
   - `Entity.SetMask`
   - `World.CreateEntity`、`World.DestroyEntity`
   - `CommandBuffer.Playback`
4. **不报告**
   - 直接按值读取后立即复制（如 `var x = position.RO.X;`，没有产生 ref 本地变量）
   - 结构变更调用之后才声明的 ref/span
   - ref/span 的最后一次使用早于结构变更调用
5. **已知限制（必须写进 README）**
   - 仅方法内分析，不做跨方法/跨字段/跨 lambda 的数据流；把 ref 传给其他方法或存进闭包后，不再跟踪。
   - 同一实体上的判定使用语法启发式：只要存活区间内出现触发 API，就报告（不区分实体是否同一）。误报可通过注释或 `#pragma warning disable ECS0001` 抑制。
   - `ComponentRef<T>` 句柄本身（`GetComponent<T>()` 的返回值）跨结构变更是安全的，**不要**报告；只有 `.RO` / `.RW` 产生的裸 ref 才报告。

## 5. 正反例（用于测试用例）

错误：

```csharp
ref var position = ref entity.GetComponent<PositionComponent>().RW;
position.X = 1;
entity.CreateComponent<VelocityComponent>();   // ECS0001
position.Y = 2;                                // 已经指向过期行
```

正确（结构变更前用完）：

```csharp
ref var position = ref entity.GetComponent<PositionComponent>().RW;
position.X = 1;
position.Y = 2;                                // 最后一次使用
entity.CreateComponent<VelocityComponent>();
```

正确（结构变更后才取 ref）：

```csharp
entity.CreateComponent<VelocityComponent>();
ref var position = ref entity.GetComponent<PositionComponent>().RW;
position.X = 1;
```

错误（span）：

```csharp
var positions = structure.GetReadWriteDenseColumn<PositionComponent>();
world.CreateEntity();                          // ECS0001：Append 可能 Grow 该结构
var first = positions[0];
```

正确（只读句柄不受影响）：

```csharp
var positionRef = entity.GetComponent<PositionComponent>(); // ComponentRef<T> 句柄
entity.CreateComponent<VelocityComponent>();
positionRef.RW.X = 1;                          // 句柄每次访问重新解析，安全
```

## 6. 交付物

1. 分析器项目 `Analyzers/CoreECS.Analyzers/CoreECS.Analyzers.csproj`
   - `netstandard2.0`；`Microsoft.CodeAnalysis.CSharp`（4.8+，`PrivateAssets=all`）
   - `IsRoslynComponent=true`、`EnforceExtendedAnalyzerRules=true`、`IncludeBuildOutput=false`
   - 实现 `[DiagnosticAnalyzer(LanguageNames.CSharp)]`，使用语法/语义模型做方法体内跟踪；注意增量性能，避免整编译单元扫描
2. 测试项目 `Analyzers/CoreECS.Analyzers.Tests/`
   - NUnit + `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing`（或 xUnit 等价包）
   - 覆盖第 5 节全部正反例，另补：`ref readonly`（`RO`）、嵌套块、重新赋值、方法末尾使用、`#pragma` 抑制
   - 测试源码内联 CoreECS API 存根（stub），不依赖运行时项目
3. 两个项目加入 `CoreECS.sln`
4. README（`Analyzers/README.md`）：引用方式、配置、限制说明
   - 项目引用方式示例：`<ProjectReference Include="...CoreECS.Analyzers.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />`
5. 可选（第二阶段，非必须）：CodeFix 把"结构变更前持有 ref"改写为"按次访问 `x.RW`"或"先复制到局部变量"

## 7. 验收标准

- `dotnet build` / `dotnet test` 全绿
- 所有错误示例各报且只报一条 `ECS0001`，位置在触发调用上；所有正确示例零报告
- 对 `Kernel/` 现有源码运行分析器零误报（若有真实命中，先与需求方确认是否为真问题，不得直接改运行时代码）
- 分析器对单文件增量分析耗时可忽略（大文件 < 100ms）

## 8. 非目标

- 不做跨方法/过程间分析
- 不引入运行时检查或修改 `Kernel/` 行为
- 不检测 `ComponentRef<T>` 句柄本身的有效性
