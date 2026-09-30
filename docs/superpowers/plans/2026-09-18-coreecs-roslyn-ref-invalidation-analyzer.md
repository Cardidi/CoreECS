# CoreECS Roslyn 分析器 ECS0001 + ECS0002 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 交付两个 Roslyn 诊断：`ECS0001`（存活 ref/span 跨结构变更/传递不安全调用继续使用）与 `ECS0002`（函数以 ref/out/in 或 Span 参数接收裸 ref/span，函数体内执行结构变更），并实现跨方法**不安全传递性**（含虚方法/接口分派）。

**Architecture:** `CompilationStartAction` 中先构建编译单元级 `UnsafeMethodIndex`：扫描所有源方法（方法/构造/运算符/访问器/局部函数）的操作树，收集"直接调用六个结构 API"的种子与调用边，经工作列表不动点向上传播，并把 override 链与接口实现闭包到基类/接口成员；随后每个函数体注册 `SyntaxNodeAction`，用语义模型做方法体内遍历：识别 ref/span 来源与存活区间（ECS0001），识别触发调用（固定 API 或 `index.IsUnsafe` 的用户调用/属性访问/对象创建），在触发点报告 ECS0001 或 ECS0002。ref/span 的**来源与存活**仍只做方法内分析。

**Tech Stack:** C#（分析器 `LangVersion 12` / `netstandard2.0`；测试 `net8.0`）、Microsoft.CodeAnalysis.CSharp 4.8.0、NUnit 3.14、Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.NUnit 1.1.2、`~/.dotnet/dotnet`（8.0.425；系统 PATH 的 10.x SDK 不满足 `global.json` 的 8.x 要求）。

**Spec:** `docs/superpowers/specs/2026-09-18-coreecs-roslyn-ref-invalidation-analyzer.md`

**基线:** `PATH="$HOME/.dotnet:$PATH" dotnet build CoreECS.sln` 0 错误；`dotnet test CoreECS.sln` 全绿。

---

## File Structure

| 文件 | 职责 |
|---|---|
| `Analyzers/Analyzers.csproj` | 分析器项目（netstandard2.0、IsRoslynComponent、IncludeBuildOutput=false） |
| `Analyzers/AnalyzerReleases.Unshipped.md` / `.Shipped.md` | RS2008 发布跟踪（ECS0001、ECS0002） |
| `Analyzers/StructuralChangeApis.cs` | 固定结构变更 API 表（Entity/World/CommandBuffer 六个方法） |
| `Analyzers/UnsafeMethodIndex.cs` | 编译单元级不安全方法不动点（直接 + 传递 + override/接口闭包） |
| `Analyzers/RefInvalidatedByStructuralChangeAnalyzer.cs` | ECS0001 + ECS0002 的语法/语义方法体分析 |
| `Test/Test.csproj`（修改） | 加入 Microsoft.CodeAnalysis 测试包与 `Analyzers` 项目引用；测试文件与现有 `TestUnit` 并列 |
| `Test/AnalyzerTestSource.cs` | 内联 CoreECS API stub + `Wrap` / `WrapWith` 模板 |
| `Test/AnalyzerHarness.cs` | 自定义 harness：`CSharpCompilation + WithAnalyzers`，用于消息断言与 Kernel 分析 |
| `Test/RefInvalidationAnalyzerTestUnit.cs` | ECS0001 正反例（spec 第 5 节） |
| `Test/RefParameterAnalyzerTestUnit.cs` | ECS0002 参数用例 |
| `Test/UnsafeTransitivityAnalyzerTestUnit.cs` | 传递性、虚方法/接口、递归环、属性/构造触发 |
| `Test/KernelSourceAnalysisTestUnit.cs` | Kernel 全源码零误报 + 大文件性能冒烟 |
| `Analyzers/README.md` | 引用方式、配置、检测规则、限制 |
| `CoreECS.sln` | 加入 `Analyzers` 项目（`Test` 已在） |

**不做：** CodeFix（spec 第 6.5 节可选）、ref/span 来源跨方法传播、委托/反射/隐式协议调用跟踪、运行时改动、`Kernel/` 任何文件改动。

---

## Task 1: 项目骨架 + ECS0001 最小垂直切片（RW ref × CreateComponent）

**Files:**
- Create: `Analyzers/Analyzers.csproj`
- Create: `Analyzers/AnalyzerReleases.Shipped.md`
- Create: `Analyzers/AnalyzerReleases.Unshipped.md`
- Create: `Analyzers/RefInvalidatedByStructuralChangeAnalyzer.cs`
- Modify: `Test/Test.csproj`
- Create: `Test/AnalyzerTestSource.cs`
- Create: `Test/AnalyzerHarness.cs`
- Create: `Test/RefInvalidationAnalyzerTestUnit.cs`
- Modify: `CoreECS.sln`

- [ ] **Step 1: 创建分析器项目文件**

`Analyzers/Analyzers.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFramework>netstandard2.0</TargetFramework>
        <LangVersion>12</LangVersion>
        <Nullable>enable</Nullable>
        <ImplicitUsings>disable</ImplicitUsings>
        <AssemblyName>CoreECS.Analyzers</AssemblyName>
        <RootNamespace>CoreECS.Analyzers</RootNamespace>
        <IsRoslynComponent>true</IsRoslynComponent>
        <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
        <IncludeBuildOutput>false</IncludeBuildOutput>
        <IsPackable>false</IsPackable>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.8.0" PrivateAssets="all" />
        <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="3.3.4" PrivateAssets="all" />
    </ItemGroup>

    <ItemGroup>
        <AdditionalFiles Include="AnalyzerReleases.Shipped.md" />
        <AdditionalFiles Include="AnalyzerReleases.Unshipped.md" />
    </ItemGroup>

</Project>
```

`AnalyzerReleases.Shipped.md`：

```markdown
; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md
```

`AnalyzerReleases.Unshipped.md`：

```markdown
; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
ECS0001 | CoreECS | Warning | Ref/span invalidated by structural change
ECS0002 | CoreECS | Warning | Ref/span parameter invalidated by structural change
```

- [ ] **Step 2: 写 ECS0001 v1（规则 + 注册 + 空分析）让测试可编译**

`Analyzers/RefInvalidatedByStructuralChangeAnalyzer.cs`（v1：只含 ECS0001 规则，`AnalyzeBody` 为空；Task 5 会整体替换为最终实现）：

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CoreECS.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class RefInvalidatedByStructuralChangeAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "ECS0001";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            "Ref/span may be invalidated by a structural change",
            "ref/span obtained from '{0}' may be invalidated by '{1}'; copy the value or finish using it before structural changes",
            "CoreECS",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            helpLinkUri: "https://github.com/Cardidi/CoreECS/blob/main/Analyzers/README.md#ecs0001");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(
                AnalyzeBody,
                SyntaxKind.MethodDeclaration,
                SyntaxKind.ConstructorDeclaration,
                SyntaxKind.DestructorDeclaration,
                SyntaxKind.OperatorDeclaration,
                SyntaxKind.ConversionOperatorDeclaration,
                SyntaxKind.GetAccessorDeclaration,
                SyntaxKind.SetAccessorDeclaration,
                SyntaxKind.AddAccessorDeclaration,
                SyntaxKind.RemoveAccessorDeclaration,
                SyntaxKind.LocalFunctionStatement);
        }

        private static void AnalyzeBody(SyntaxNodeAnalysisContext context)
        {
        }
    }
}
```

注意：`LocalFunctionStatement` 单独注册，方法体遍历时必须**跳过**嵌套局部函数（`EnumerateBodyNodes` 的 `descendIntoChildren` 谓词），避免同一函数体被分析两次；局部函数体作为独立 body 分析（ref/span 无法被局部函数捕获，语义上安全）。

- [ ] **Step 3: 修改现有 `Test/Test.csproj`（加入分析器测试包与项目引用）**

在 `PropertyGroup` 末尾加入 `<NoWarn>$(NoWarn);NU1701;CS0618</NoWarn>`（测试包自带的已知过时/兼容警告）；在第一个 `ItemGroup` 末尾追加三个包；在 `ProjectReference` ItemGroup 追加 `Analyzers` 引用。修改后的完整文件：

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFramework>net8.0</TargetFramework>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>

        <IsPackable>false</IsPackable>
        <IsTestProject>true</IsTestProject>
        <RootNamespace>CoreECS.Test</RootNamespace>
        <NoWarn>$(NoWarn);NU1701;CS0618</NoWarn>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="coverlet.collector" Version="6.0.0"/>
        <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0"/>
        <PackageReference Include="NUnit" Version="3.14.0"/>
        <PackageReference Include="NUnit.Analyzers" Version="3.9.0"/>
        <PackageReference Include="NUnit3TestAdapter" Version="4.5.0"/>
        <PackageReference Include="Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.NUnit" Version="1.1.2"/>
        <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.8.0"/>
        <PackageReference Include="Microsoft.CodeAnalysis.CSharp.Workspaces" Version="4.8.0"/>
    </ItemGroup>

    <ItemGroup>
        <Using Include="NUnit.Framework"/>
    </ItemGroup>

    <ItemGroup>
      <ProjectReference Include="..\Kernel\Kernel.csproj" />
      <ProjectReference Include="..\Analyzers\Analyzers.csproj" />
    </ItemGroup>

</Project>
```

说明：`Microsoft.Extensions.DependencyInjection` 经 `Kernel` 的 ProjectReference 传递可用，无需显式添加；分析器测试命名空间为 `CoreECS.Test.Analyzers`，与现有 `CoreECS.Test` 测试隔离，可用 `--filter "FullyQualifiedName~CoreECS.Test.Analyzers"` 单独运行。

- [ ] **Step 4: 写测试 stub 与第一个失败测试**

`Test/AnalyzerTestSource.cs`：

```csharp
namespace CoreECS.Test.Analyzers
{
    internal static class AnalyzerTestSource
    {
        public const string Stubs = @"
using System;
using CoreECS;
using CoreECS.Defines;
using CoreECS.Structures;

namespace CoreECS.Defines
{
    public interface IComponent<T> where T : struct, IComponent<T> { }

    public readonly struct ComponentRef<T> where T : struct, IComponent<T>
    {
        private static T s_value;
        public ref readonly T RO => ref s_value;
        public ref T RW => ref s_value;
    }
}

namespace CoreECS.Structures
{
    public sealed class Structure
    {
        public ReadOnlySpan<T> GetReadOnlyDenseColumn<T>() where T : struct, IComponent<T> => default;
        public Span<T> GetReadWriteDenseColumn<T>() where T : struct, IComponent<T> => default;
    }
}

namespace CoreECS
{
    public sealed class Entity
    {
        public ComponentRef<T> GetComponent<T>() where T : struct, IComponent<T> => default;
        public ComponentRef<T> CreateComponent<T>() where T : struct, IComponent<T> => default;
        public ComponentRef<T> CreateComponent<T>(T value) where T : struct, IComponent<T> => default;
        public void DestroyComponent<T>() where T : struct, IComponent<T> { }
        public void SetMask(ulong mask) { }
    }

    public sealed class World
    {
        public Entity CreateEntity(ulong mask = ulong.MaxValue) => default;
        public void DestroyEntity(ulong entityId) { }
        public void DestroyEntity(Entity entity) { }
    }

    public sealed class CommandBuffer
    {
        public void Playback() { }
    }
}

public struct PositionComponent : IComponent<PositionComponent>
{
    public float X;
    public float Y;
}

public struct VelocityComponent : IComponent<VelocityComponent>
{
    public float X;
    public float Y;
}

public static class Helpers
{
    public static void Mutate(ref PositionComponent value) { }
}

public static class TestWorld
{
    public static World Instance;
}
";

        public static string Wrap(string body) => WrapWith(string.Empty, body);

        public static string WrapWith(string declarations, string body) => Stubs + declarations + @"

public static class Scenario
{
    public static void Run(Entity entity, World world, CommandBuffer commands, Structure structure)
    {
" + body + @"
    }
}
";
    }
}
```

`Test/RefInvalidationAnalyzerTestUnit.cs`：

```csharp
using System.Threading.Tasks;
using CoreECS.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Testing.Verifiers;

namespace CoreECS.Test.Analyzers
{
    [TestFixture]
    public class RefInvalidationAnalyzerTestUnit
    {
        private static Task VerifyAsync(string body)
        {
            var test = new CSharpAnalyzerTest<RefInvalidatedByStructuralChangeAnalyzer, NUnitVerifier>
            {
                TestCode = AnalyzerTestSource.Wrap(body),
                ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            };
            return test.RunAsync();
        }

        [Test]
        public async Task RwRef_UsedAfterCreateComponent_Reports()
        {
            await VerifyAsync(@"
                ref var position = ref entity.GetComponent<PositionComponent>().RW;
                position.X = 1;
                {|ECS0001:entity.CreateComponent<VelocityComponent>()|};
                position.Y = 2;
");
        }

        [Test]
        public async Task RwRef_FinishedBeforeCreateComponent_DoesNotReport()
        {
            await VerifyAsync(@"
                ref var position = ref entity.GetComponent<PositionComponent>().RW;
                position.X = 1;
                position.Y = 2;
                entity.CreateComponent<VelocityComponent>();
");
        }

        [Test]
        public async Task RwRef_AcquiredAfterCreateComponent_DoesNotReport()
        {
            await VerifyAsync(@"
                entity.CreateComponent<VelocityComponent>();
                ref var position = ref entity.GetComponent<PositionComponent>().RW;
                position.X = 1;
");
        }
    }
}
```

- [ ] **Step 5: 加入解决方案并运行 RED**

```bash
PATH="$HOME/.dotnet:$PATH" dotnet sln CoreECS.sln add Analyzers/Analyzers.csproj
PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --nologo --filter "FullyQualifiedName~CoreECS.Test.Analyzers"
```

Expected: `RwRef_UsedAfterCreateComponent_Reports` 失败（"Mismatch between number of diagnostics returned, expected \"1\" actual \"0\""），另外两个通过。

- [ ] **Step 6: 实现 ECS0001 v1（GREEN）**

用以下内容替换 `RefInvalidatedByStructuralChangeAnalyzer.cs`。v1 覆盖：`.RO`/`.RW` ref 本地变量来源、六个固定触发 API、最后一次引用存活区间、方法体遍历（跳过嵌套局部函数）、报告。

```csharp
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CoreECS.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class RefInvalidatedByStructuralChangeAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "ECS0001";

        private const string ComponentRefDefinitionName = "CoreECS.Defines.ComponentRef<T>";
        private const string StructureTypeName = "CoreECS.Structures.Structure";
        private const string EntityTypeName = "CoreECS.Entity";
        private const string WorldTypeName = "CoreECS.World";
        private const string CommandBufferTypeName = "CoreECS.CommandBuffer";
        private const string SpanDefinitionName = "System.Span<T>";
        private const string ReadOnlySpanDefinitionName = "System.ReadOnlySpan<T>";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            "Ref/span may be invalidated by a structural change",
            "ref/span obtained from '{0}' may be invalidated by '{1}'; copy the value or finish using it before structural changes",
            "CoreECS",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            helpLinkUri: "https://github.com/Cardidi/CoreECS/blob/main/Analyzers/README.md#ecs0001");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(
                AnalyzeBody,
                SyntaxKind.MethodDeclaration,
                SyntaxKind.ConstructorDeclaration,
                SyntaxKind.DestructorDeclaration,
                SyntaxKind.OperatorDeclaration,
                SyntaxKind.ConversionOperatorDeclaration,
                SyntaxKind.GetAccessorDeclaration,
                SyntaxKind.SetAccessorDeclaration,
                SyntaxKind.AddAccessorDeclaration,
                SyntaxKind.RemoveAccessorDeclaration,
                SyntaxKind.LocalFunctionStatement);
        }

        private static void AnalyzeBody(SyntaxNodeAnalysisContext context)
        {
            var body = context.Node;
            var model = context.SemanticModel;
            var tracked = new Dictionary<ISymbol, TrackedRef>(SymbolEqualityComparer.Default);
            var candidateNames = new HashSet<string>(StringComparer.Ordinal);
            var triggers = new List<TriggerCall>();

            foreach (var node in EnumerateBodyNodes(body))
            {
                switch (node)
                {
                    case LocalDeclarationStatementSyntax declaration:
                        RegisterDeclaration(declaration, model, tracked, candidateNames);
                        break;
                    case AssignmentExpressionSyntax assignment when assignment.IsKind(SyntaxKind.SimpleAssignmentExpression):
                        HandleAssignment(assignment, model, tracked, candidateNames);
                        break;
                    case InvocationExpressionSyntax invocation when TryGetTrigger(invocation, model, out var trigger):
                        triggers.Add(trigger);
                        break;
                }

                if (node is IdentifierNameSyntax identifier
                    && candidateNames.Contains(identifier.Identifier.ValueText))
                {
                    UpdateLastUse(identifier, model, tracked);
                }
            }

            foreach (var trigger in triggers)
            {
                TrackedRef? source = null;
                foreach (var candidate in tracked.Values)
                {
                    if (!candidate.IsLiveAt(trigger.Position))
                    {
                        continue;
                    }

                    if (source == null || candidate.DeclarationPosition > source.DeclarationPosition)
                    {
                        source = candidate;
                    }
                }

                if (source == null)
                {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    Rule,
                    trigger.Node.GetLocation(),
                    source.Origin,
                    trigger.DisplayName));
            }
        }

        private static IEnumerable<SyntaxNode> EnumerateBodyNodes(SyntaxNode body)
        {
            return body.DescendantNodesAndSelf(
                node => node == body || node is not LocalFunctionStatementSyntax);
        }

        private static void RegisterDeclaration(
            LocalDeclarationStatementSyntax declaration,
            SemanticModel model,
            Dictionary<ISymbol, TrackedRef> tracked,
            HashSet<string> candidateNames)
        {
            foreach (var declarator in declaration.Declaration.Variables)
            {
                if (model.GetDeclaredSymbol(declarator) is not ILocalSymbol symbol)
                {
                    continue;
                }

                candidateNames.Add(symbol.Name);

                if (declarator.Initializer == null)
                {
                    continue;
                }

                var isRefLocal = declaration.Declaration.Type is RefTypeSyntax
                                 || declarator.Initializer.Value is RefExpressionSyntax;
                if (!isRefLocal && !IsSpanType(model.GetTypeInfo(declaration.Declaration.Type).Type))
                {
                    continue;
                }

                if (TryResolveSource(declarator.Initializer.Value, model, tracked, out var origin))
                {
                    tracked[symbol] = new TrackedRef(origin!, declarator.SpanStart);
                }
            }
        }

        private static void HandleAssignment(
            AssignmentExpressionSyntax assignment,
            SemanticModel model,
            Dictionary<ISymbol, TrackedRef> tracked,
            HashSet<string> candidateNames)
        {
            if (assignment.Left is not IdentifierNameSyntax left)
            {
                return;
            }

            var symbol = model.GetSymbolInfo(left).Symbol;
            if (symbol == null)
            {
                return;
            }

            if (TryResolveSource(assignment.Right, model, tracked, out var origin))
            {
                candidateNames.Add(symbol.Name);
                tracked[symbol] = new TrackedRef(origin!, assignment.SpanStart);
            }
            else
            {
                tracked.Remove(symbol);
            }
        }

        private static void UpdateLastUse(
            IdentifierNameSyntax identifier,
            SemanticModel model,
            Dictionary<ISymbol, TrackedRef> tracked)
        {
            var symbol = model.GetSymbolInfo(identifier).Symbol;
            if (symbol == null || !tracked.TryGetValue(symbol, out var info))
            {
                return;
            }

            if (identifier.SpanStart > info.LastUsePosition)
            {
                info.LastUsePosition = identifier.SpanStart;
            }

            if (IsPassedByRefOrOut(identifier))
            {
                info.Escaped = true;
            }
        }

        private static bool IsPassedByRefOrOut(IdentifierNameSyntax identifier)
        {
            var current = identifier.Parent;
            while (current is ExpressionSyntax)
            {
                current = current.Parent;
            }

            return current is ArgumentSyntax argument
                   && argument.RefKindKeyword.Kind() is SyntaxKind.RefKeyword
                       or SyntaxKind.OutKeyword
                       or SyntaxKind.InKeyword;
        }

        private static bool TryResolveSource(
            ExpressionSyntax expression,
            SemanticModel model,
            Dictionary<ISymbol, TrackedRef> tracked,
            out string? origin)
        {
            origin = null;
            if (expression is RefExpressionSyntax refExpression)
            {
                expression = refExpression.Expression;
            }

            while (expression is ParenthesizedExpressionSyntax parenthesized)
            {
                expression = parenthesized.Expression;
            }

            switch (expression)
            {
                case MemberAccessExpressionSyntax memberAccess
                    when model.GetSymbolInfo(memberAccess).Symbol is IPropertySymbol property
                         && IsComponentRefAccessor(property):
                    origin = GetComponentRefAccessorName(property, model, memberAccess.SpanStart);
                    return true;
                case InvocationExpressionSyntax invocation
                    when model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method
                         && IsStructureColumnAccessor(method):
                    origin = GetStructureColumnAccessorName(method, model, invocation.SpanStart);
                    return true;
            }

            foreach (var identifier in expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
            {
                var symbol = model.GetSymbolInfo(identifier).Symbol;
                if (symbol != null && tracked.TryGetValue(symbol, out var source))
                {
                    origin = source.Origin;
                    return true;
                }
            }

            return false;
        }

        private static bool IsComponentRefAccessor(IPropertySymbol property)
        {
            if (property.Name != "RO" && property.Name != "RW")
            {
                return false;
            }

            var containingType = property.ContainingType;
            return containingType != null
                   && containingType.IsGenericType
                   && containingType.OriginalDefinition.ToDisplayString() == ComponentRefDefinitionName;
        }

        private static bool IsStructureColumnAccessor(IMethodSymbol method)
        {
            if (method.Name != "GetReadOnlyDenseColumn" && method.Name != "GetReadWriteDenseColumn")
            {
                return false;
            }

            return method.ContainingType?.ToDisplayString() == StructureTypeName;
        }

        private static string GetComponentRefAccessorName(
            IPropertySymbol property,
            SemanticModel model,
            int position)
        {
            return property.ContainingType.ToMinimalDisplayString(model, position) + "." + property.Name;
        }

        private static string GetStructureColumnAccessorName(
            IMethodSymbol method,
            SemanticModel model,
            int position)
        {
            return method.ContainingType.ToMinimalDisplayString(model, position)
                   + "." + method.Name
                   + FormatTypeArguments(method, model, position);
        }

        private static string FormatTypeArguments(
            IMethodSymbol method,
            SemanticModel model,
            int position)
        {
            if (method.TypeArguments.Length == 0)
            {
                return string.Empty;
            }

            return "<" + string.Join(", ", method.TypeArguments.Select(
                argument => argument.ToMinimalDisplayString(model, position))) + ">";
        }

        private static bool IsSpanType(ITypeSymbol? type)
        {
            if (type is not INamedTypeSymbol namedType || !namedType.IsGenericType)
            {
                return false;
            }

            var definitionName = namedType.OriginalDefinition.ToDisplayString();
            return definitionName == SpanDefinitionName || definitionName == ReadOnlySpanDefinitionName;
        }

        private static bool TryGetTrigger(
            InvocationExpressionSyntax invocation,
            SemanticModel model,
            out TriggerCall trigger)
        {
            trigger = default;
            if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
            {
                return false;
            }

            var typeName = method.ContainingType?.ToDisplayString();
            var isTrigger = typeName switch
            {
                EntityTypeName => method.Name == "CreateComponent"
                                  || method.Name == "DestroyComponent"
                                  || method.Name == "SetMask",
                WorldTypeName => method.Name == "CreateEntity"
                                 || method.Name == "DestroyEntity",
                CommandBufferTypeName => method.Name == "Playback",
                _ => false,
            };

            if (!isTrigger)
            {
                return false;
            }

            var displayName = method.Name + FormatTypeArguments(method, model, invocation.SpanStart);
            trigger = new TriggerCall(invocation, displayName, invocation.SpanStart);
            return true;
        }

        private sealed class TrackedRef
        {
            public TrackedRef(string origin, int declarationPosition)
            {
                Origin = origin;
                DeclarationPosition = declarationPosition;
                LastUsePosition = declarationPosition;
            }

            public string Origin { get; }

            public int DeclarationPosition { get; }

            public int LastUsePosition { get; set; }

            public bool Escaped { get; set; }

            public bool IsLiveAt(int position)
            {
                if (position <= DeclarationPosition)
                {
                    return false;
                }

                return Escaped || LastUsePosition > position;
            }
        }

        private readonly struct TriggerCall
        {
            public TriggerCall(SyntaxNode node, string displayName, int position)
            {
                Node = node;
                DisplayName = displayName;
                Position = position;
            }

            public SyntaxNode Node { get; }

            public string DisplayName { get; }

            public int Position { get; }
        }
    }
}
```

- [ ] **Step 7: 运行测试确认 GREEN**

```bash
PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --nologo --filter "FullyQualifiedName~CoreECS.Test.Analyzers"
```

Expected: 3 passed / 0 failed。

---

## Task 2: 全部固定触发 API 覆盖 + 句柄/值拷贝安全

**Files:**
- Modify: `Test/RefInvalidationAnalyzerTestUnit.cs`

- [ ] **Step 1: 追加测试**

```csharp
        [TestCase("entity.CreateComponent<VelocityComponent>()")]
        [TestCase("entity.CreateComponent<VelocityComponent>(new VelocityComponent())")]
        [TestCase("entity.DestroyComponent<PositionComponent>()")]
        [TestCase("entity.SetMask(1UL)")]
        [TestCase("world.CreateEntity()")]
        [TestCase("world.CreateEntity(0UL)")]
        [TestCase("world.DestroyEntity(0UL)")]
        [TestCase("world.DestroyEntity(entity)")]
        [TestCase("commands.Playback()")]
        public async Task AllTriggerApis_InvalidateLiveRwRef(string trigger)
        {
            await VerifyAsync($@"
                ref var position = ref entity.GetComponent<PositionComponent>().RW;
                {{|ECS0001:{trigger}|}};
                position.X = 1;
");
        }

        [Test]
        public async Task ComponentRefHandle_IsSafeAcrossStructuralChange()
        {
            await VerifyAsync(@"
                var positionRef = entity.GetComponent<PositionComponent>();
                entity.CreateComponent<VelocityComponent>();
                positionRef.RW.X = 1;
");
        }

        [Test]
        public async Task ValueCopyOfRwAccess_DoesNotReport()
        {
            await VerifyAsync(@"
                var x = entity.GetComponent<PositionComponent>().RW.X;
                entity.CreateComponent<VelocityComponent>();
");
        }
```

- [ ] **Step 2: 运行确认 GREEN**

```bash
PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --nologo --filter "FullyQualifiedName~CoreECS.Test.Analyzers"
```

Expected: 14 passed / 0 failed。

---

## Task 3: `RO`（ref readonly）与 span 来源

**Files:**
- Modify: `Test/RefInvalidationAnalyzerTestUnit.cs`

- [ ] **Step 1: 追加测试**

```csharp
        [Test]
        public async Task ReadOnlyRef_UsedAfterStructuralChange_Reports()
        {
            await VerifyAsync(@"
                ref readonly var position = ref entity.GetComponent<PositionComponent>().RO;
                var x = position.X;
                {|ECS0001:entity.DestroyComponent<PositionComponent>()|};
                var y = position.Y;
");
        }

        [Test]
        public async Task ReadWriteSpan_UsedAfterCreateEntity_Reports()
        {
            await VerifyAsync(@"
                var positions = structure.GetReadWriteDenseColumn<PositionComponent>();
                {|ECS0001:world.CreateEntity()|};
                var first = positions[0];
");
        }

        [Test]
        public async Task ReadOnlySpan_UsedAfterCreateEntity_Reports()
        {
            await VerifyAsync(@"
                var positions = structure.GetReadOnlyDenseColumn<PositionComponent>();
                {|ECS0001:world.CreateEntity()|};
                var first = positions[0];
");
        }

        [Test]
        public async Task Span_FinishedBeforeStructuralChange_DoesNotReport()
        {
            await VerifyAsync(@"
                var positions = structure.GetReadWriteDenseColumn<PositionComponent>();
                var first = positions[0];
                world.CreateEntity();
");
        }

        [Test]
        public async Task ExplicitSpanTypedLocal_Reports()
        {
            await VerifyAsync(@"
                Span<PositionComponent> positions = structure.GetReadWriteDenseColumn<PositionComponent>();
                {|ECS0001:world.CreateEntity()|};
                positions[0].X = 1;
");
        }

        [Test]
        public async Task UnrelatedSpan_DoesNotReport()
        {
            await VerifyAsync(@"
                Span<PositionComponent> positions = stackalloc PositionComponent[4];
                world.CreateEntity();
                positions[0].X = 1;
");
        }
```

- [ ] **Step 2: 运行确认 GREEN**

Expected: 20 passed / 0 failed。

---

## Task 4: 存活区间边界（嵌套块、方法末尾、重赋值、派生、escape、局部函数、多触发）

**Files:**
- Modify: `Test/RefInvalidationAnalyzerTestUnit.cs`

- [ ] **Step 1: 追加测试**

```csharp
        [Test]
        public async Task RefInOuterBlock_TriggerInNestedBlock_Reports()
        {
            await VerifyAsync(@"
                ref var position = ref entity.GetComponent<PositionComponent>().RW;
                if (position.X > 0)
                {
                    {|ECS0001:world.CreateEntity()|};
                }
                position.Y = 1;
");
        }

        [Test]
        public async Task RefUsedAtEndOfMethodAfterTrigger_Reports()
        {
            await VerifyAsync(@"
                ref var position = ref entity.GetComponent<PositionComponent>().RW;
                {|ECS0001:world.DestroyEntity(0UL)|};
                position.X = 1;
");
        }

        [Test]
        public async Task ReassignmentToDefault_EndsLiveness()
        {
            await VerifyAsync(@"
                var positions = structure.GetReadWriteDenseColumn<PositionComponent>();
                positions = default;
                world.CreateEntity();
                var first = positions[0];
");
        }

        [Test]
        public async Task ReassignmentToNewSource_RestartsLiveness()
        {
            await VerifyAsync(@"
                ReadOnlySpan<PositionComponent> positions = structure.GetReadWriteDenseColumn<PositionComponent>();
                positions = default;
                positions = structure.GetReadOnlyDenseColumn<PositionComponent>();
                {|ECS0001:world.CreateEntity()|};
                var first = positions[0];
");
        }

        [Test]
        public async Task DerivedSpan_Reports()
        {
            await VerifyAsync(@"
                var positions = structure.GetReadWriteDenseColumn<PositionComponent>();
                var slice = positions.Slice(0, 1);
                {|ECS0001:world.CreateEntity()|};
                var first = slice[0];
");
        }

        [Test]
        public async Task RefPassedByRef_EscapesAndReports()
        {
            await VerifyAsync(@"
                ref var position = ref entity.GetComponent<PositionComponent>().RW;
                Helpers.Mutate(ref position);
                {|ECS0001:world.CreateEntity()|};
");
        }

        [Test]
        public async Task RefInsideLocalFunction_Reports()
        {
            await VerifyAsync(@"
                void Local()
                {
                    ref var position = ref entity.GetComponent<PositionComponent>().RW;
                    {|ECS0001:world.CreateEntity()|};
                    position.X = 1;
                }
                Local();
");
        }

        [Test]
        public async Task TwoTriggersDuringLiveness_ReportTwice()
        {
            await VerifyAsync(@"
                ref var position = ref entity.GetComponent<PositionComponent>().RW;
                {|ECS0001:world.CreateEntity()|};
                {|ECS0001:world.CreateEntity()|};
                position.X = 1;
");
        }
```

- [ ] **Step 2: 运行确认 GREEN**

Expected: 28 passed / 0 failed。`RefInsideLocalFunction_Reports` 依赖 Task 1 的局部函数独立注册 + 跳过嵌套；若重复报告说明 `EnumerateBodyNodes` 谓词错误。

---

## Task 5: `UnsafeMethodIndex`（直接 + 传递 + 虚方法/接口）+ ECS0001 触发扩展

**Files:**
- Create: `Analyzers/StructuralChangeApis.cs`
- Create: `Analyzers/UnsafeMethodIndex.cs`
- Modify: `Analyzers/RefInvalidatedByStructuralChangeAnalyzer.cs`（整体替换为最终 ECS0001 实现：CompilationStartAction + index）
- Create: `Test/UnsafeTransitivityAnalyzerTestUnit.cs`

- [ ] **Step 1: 写失败测试（传递性、虚方法/接口、递归环、安全调用）**

`Test/UnsafeTransitivityAnalyzerTestUnit.cs`：

```csharp
using System.Threading.Tasks;
using CoreECS.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Testing.Verifiers;

namespace CoreECS.Test.Analyzers
{
    [TestFixture]
    public class UnsafeTransitivityAnalyzerTestUnit
    {
        private static Task VerifyAsync(string body)
        {
            var test = new CSharpAnalyzerTest<RefInvalidatedByStructuralChangeAnalyzer, NUnitVerifier>
            {
                TestCode = AnalyzerTestSource.Wrap(body),
                ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            };
            return test.RunAsync();
        }

        private static Task VerifyWithDeclarationsAsync(string declarations, string body)
        {
            var test = new CSharpAnalyzerTest<RefInvalidatedByStructuralChangeAnalyzer, NUnitVerifier>
            {
                TestCode = AnalyzerTestSource.WrapWith(declarations, body),
                ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            };
            return test.RunAsync();
        }

        [Test]
        public async Task DirectWrapperCall_InvalidatesLiveRef_Reports()
        {
            await VerifyAsync(@"
                void Wrapper() => entity.CreateComponent<VelocityComponent>();
                ref var position = ref entity.GetComponent<PositionComponent>().RW;
                {|ECS0001:Wrapper()|};
                position.X = 1;
");
        }

        [Test]
        public async Task TransitiveWrapperCall_InvalidatesLiveRef_Reports()
        {
            await VerifyAsync(@"
                void Inner() { world.CreateEntity(); }
                void Outer() { Inner(); }
                ref var position = ref entity.GetComponent<PositionComponent>().RW;
                {|ECS0001:Outer()|};
                position.X = 1;
");
        }

        [Test]
        public async Task RecursiveUnsafeMethods_ConvergeAndReport()
        {
            await VerifyAsync(@"
                void A() { B(); }
                void B() { A(); world.CreateEntity(); }
                ref var position = ref entity.GetComponent<PositionComponent>().RW;
                {|ECS0001:A()|};
                position.X = 1;
");
        }

        [Test]
        public async Task SafeMethodCall_DoesNotInvalidate()
        {
            await VerifyAsync(@"
                void Safe() { var x = 1; }
                ref var position = ref entity.GetComponent<PositionComponent>().RW;
                Safe();
                position.X = 1;
");
        }

        [Test]
        public async Task VirtualDispatch_ToUnsafeOverride_InvalidatesLiveRef()
        {
            await VerifyWithDeclarationsAsync(@"
public class BaseAction
{
    public virtual void Run() { }
}

public class DerivedAction : BaseAction
{
    public override void Run() { TestWorld.Instance.CreateEntity(); }
}
", @"
                TestWorld.Instance = world;
                BaseAction action = new DerivedAction();
                ref var position = ref entity.GetComponent<PositionComponent>().RW;
                {|ECS0001:action.Run()|};
                position.X = 1;
");
        }

        [Test]
        public async Task InterfaceDispatch_ToUnsafeImplementation_InvalidatesLiveRef()
        {
            await VerifyWithDeclarationsAsync(@"
public interface IAction
{
    void Run();
}

public sealed class ActionImpl : IAction
{
    public void Run() { TestWorld.Instance.CreateEntity(); }
}
", @"
                TestWorld.Instance = world;
                IAction action = new ActionImpl();
                ref var position = ref entity.GetComponent<PositionComponent>().RW;
                {|ECS0001:action.Run()|};
                position.X = 1;
");
        }

        [Test]
        public async Task LambdaContainingStructuralChange_MakesMethodUnsafe()
        {
            await VerifyAsync(@"
                void Outer()
                {
                    Action action = () => world.CreateEntity();
                }
                ref var position = ref entity.GetComponent<PositionComponent>().RW;
                {|ECS0001:Outer()|};
                position.X = 1;
");
        }

        [Test]
        public async Task UnsafePropertyGetter_InvalidatesLiveRef()
        {
            await VerifyWithDeclarationsAsync(@"
public class UnsafeAccessor
{
    public int Value
    {
        get
        {
            TestWorld.Instance.CreateEntity();
            return 0;
        }
    }
}
", @"
                TestWorld.Instance = world;
                var accessor = new UnsafeAccessor();
                ref var position = ref entity.GetComponent<PositionComponent>().RW;
                var value = {|ECS0001:accessor.Value|};
                position.X = 1;
");
        }

        [Test]
        public async Task UnsafeConstructor_InvalidatesLiveRef()
        {
            await VerifyWithDeclarationsAsync(@"
public class UnsafeCtor
{
    public UnsafeCtor()
    {
        TestWorld.Instance.CreateEntity();
    }
}
", @"
                TestWorld.Instance = world;
                ref var position = ref entity.GetComponent<PositionComponent>().RW;
                {|ECS0001:new UnsafeCtor()|};
                position.X = 1;
");
        }
    }
}
```

- [ ] **Step 2: 运行确认 RED**

```bash
PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --nologo --filter "FullyQualifiedName~CoreECS.Test.Analyzers"
```

Expected: 新增测试失败（用户调用未被识别为触发；`SafeMethodCall_DoesNotInvalidate` 可能已通过）。

- [ ] **Step 3: 新建 `StructuralChangeApis.cs`**

```csharp
using Microsoft.CodeAnalysis;

namespace CoreECS.Analyzers
{
    internal static class StructuralChangeApis
    {
        private const string EntityTypeName = "CoreECS.Entity";
        private const string WorldTypeName = "CoreECS.World";
        private const string CommandBufferTypeName = "CoreECS.CommandBuffer";

        public static bool IsStructuralChangeApi(IMethodSymbol method)
        {
            var typeName = method.ContainingType?.ToDisplayString();
            return typeName switch
            {
                EntityTypeName => method.Name == "CreateComponent"
                                  || method.Name == "DestroyComponent"
                                  || method.Name == "SetMask",
                WorldTypeName => method.Name == "CreateEntity"
                                 || method.Name == "DestroyEntity",
                CommandBufferTypeName => method.Name == "Playback",
                _ => false,
            };
        }
    }
}
```

- [ ] **Step 4: 新建 `UnsafeMethodIndex.cs`（完整实现）**

```csharp
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace CoreECS.Analyzers
{
    internal sealed class UnsafeMethodIndex
    {
        private readonly HashSet<IMethodSymbol> m_unsafeMethods;

        private UnsafeMethodIndex(HashSet<IMethodSymbol> unsafeMethods)
        {
            m_unsafeMethods = unsafeMethods;
        }

        public bool IsUnsafe(IMethodSymbol? method)
        {
            if (method == null)
            {
                return false;
            }

            return m_unsafeMethods.Contains(method.OriginalDefinition);
        }

        public static UnsafeMethodIndex Build(Compilation compilation)
        {
            var callers = new Dictionary<IMethodSymbol, HashSet<IMethodSymbol>>(SymbolEqualityComparer.Default);
            var unsafeMethods = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            var queue = new Queue<IMethodSymbol>();

            foreach (var tree in compilation.SyntaxTrees)
            {
                var model = compilation.GetSemanticModel(tree);
                foreach (var node in tree.GetRoot().DescendantNodes())
                {
                    if (!IsMethodLikeNode(node))
                    {
                        continue;
                    }

                    if (model.GetDeclaredSymbol(node) is not IMethodSymbol declaredMethod)
                    {
                        continue;
                    }

                    var bodyNode = GetBodyNode(node);
                    if (bodyNode == null)
                    {
                        continue;
                    }

                    var operation = model.GetOperation(bodyNode);
                    if (operation == null)
                    {
                        continue;
                    }

                    var declaringMethod = declaredMethod.OriginalDefinition;
                    foreach (var descendant in DescendantsSkippingLocalFunctions(operation))
                    {
                        var target = GetCalledMethod(descendant);
                        if (target == null)
                        {
                            continue;
                        }

                        if (StructuralChangeApis.IsStructuralChangeApi(target))
                        {
                            if (unsafeMethods.Add(declaringMethod))
                            {
                                queue.Enqueue(declaringMethod);
                            }

                            continue;
                        }

                        var callee = target.OriginalDefinition;
                        if (!callers.TryGetValue(callee, out var callerSet))
                        {
                            callers[callee] = callerSet = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
                        }

                        callerSet.Add(declaringMethod);
                    }
                }
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();

                if (callers.TryGetValue(current, out var callerSet))
                {
                    foreach (var caller in callerSet)
                    {
                        if (unsafeMethods.Add(caller))
                        {
                            queue.Enqueue(caller);
                        }
                    }
                }

                foreach (var abstraction in GetAbstractions(current))
                {
                    if (unsafeMethods.Add(abstraction))
                    {
                        queue.Enqueue(abstraction);
                    }
                }
            }

            return new UnsafeMethodIndex(unsafeMethods);
        }

        private static bool IsMethodLikeNode(SyntaxNode node)
        {
            return node is MethodDeclarationSyntax
                or ConstructorDeclarationSyntax
                or DestructorDeclarationSyntax
                or OperatorDeclarationSyntax
                or ConversionOperatorDeclarationSyntax
                or AccessorDeclarationSyntax
                or LocalFunctionStatementSyntax;
        }

        private static SyntaxNode? GetBodyNode(SyntaxNode node)
        {
            return node switch
            {
                BaseMethodDeclarationSyntax method => (SyntaxNode?)method.Body ?? method.ExpressionBody?.Expression,
                AccessorDeclarationSyntax accessor => (SyntaxNode?)accessor.Body ?? accessor.ExpressionBody?.Expression,
                LocalFunctionStatementSyntax localFunction => (SyntaxNode?)localFunction.Body ?? localFunction.ExpressionBody?.Expression,
                _ => null,
            };
        }

        private static IEnumerable<IOperation> DescendantsSkippingLocalFunctions(IOperation root)
        {
            var stack = new Stack<IOperation>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (current is ILocalFunctionOperation)
                {
                    continue;
                }

                yield return current;

                foreach (var child in current.ChildOperations)
                {
                    stack.Push(child);
                }
            }
        }

        private static IMethodSymbol? GetCalledMethod(IOperation operation)
        {
            return operation switch
            {
                IInvocationOperation invocation => invocation.TargetMethod,
                IObjectCreationOperation creation => creation.Constructor,
                IPropertyReferenceOperation propertyReference => GetAccessedAccessor(propertyReference),
                IEventAssignmentOperation eventAssignment => GetEventAccessor(eventAssignment),
                IIncrementOrDecrementOperation increment => increment.OperatorMethod,
                ICompoundAssignmentOperation compound => compound.OperatorMethod,
                IBinaryOperation binary => binary.OperatorMethod,
                IUnaryOperation unary => unary.OperatorMethod,
                IConversionOperation conversion => conversion.OperatorMethod,
                _ => null,
            };
        }

        private static IMethodSymbol? GetAccessedAccessor(IPropertyReferenceOperation propertyReference)
        {
            var property = propertyReference.Property;
            var parent = propertyReference.Parent;
            var isWrite = parent is ISimpleAssignmentOperation simpleAssignment && simpleAssignment.Target == propertyReference
                          || parent is ICompoundAssignmentOperation compoundAssignment && compoundAssignment.Target == propertyReference
                          || parent is IIncrementOrDecrementOperation increment && increment.Target == propertyReference;

            return isWrite
                ? property.SetMethod ?? property.GetMethod
                : property.GetMethod ?? property.SetMethod;
        }

        private static IMethodSymbol? GetEventAccessor(IEventAssignmentOperation eventAssignment)
        {
            if (eventAssignment.EventReference is not IEventReferenceOperation eventReference)
            {
                return null;
            }

            return eventAssignment.Adds
                ? eventReference.Event.AddMethod
                : eventReference.Event.RemoveMethod;
        }

        private static IEnumerable<IMethodSymbol> GetAbstractions(IMethodSymbol method)
        {
            var abstractions = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

            for (var overridden = method.OverriddenMethod; overridden != null; overridden = overridden.OverriddenMethod)
            {
                abstractions.Add(overridden.OriginalDefinition);
            }

            foreach (var explicitImplementation in method.ExplicitInterfaceImplementations)
            {
                abstractions.Add(explicitImplementation.OriginalDefinition);
            }

            var containingType = method.ContainingType;
            if (containingType != null && containingType.TypeKind != TypeKind.Interface)
            {
                foreach (var interfaceType in containingType.AllInterfaces)
                {
                    foreach (var member in interfaceType.GetMembers())
                    {
                        if (member is not IMethodSymbol interfaceMethod)
                        {
                            continue;
                        }

                        var implementation = containingType.FindImplementationForInterfaceMember(interfaceMethod);
                        if (implementation is IMethodSymbol implementationMethod
                            && SymbolEqualityComparer.Default.Equals(implementationMethod.OriginalDefinition, method))
                        {
                            abstractions.Add(interfaceMethod.OriginalDefinition);
                        }
                    }
                }
            }

            return abstractions;
        }
    }
}
```

- [ ] **Step 5: 用最终 ECS0001 实现替换分析器**

`RefInvalidatedByStructuralChangeAnalyzer.cs` 完整替换为：

```csharp
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CoreECS.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class RefInvalidatedByStructuralChangeAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "ECS0001";

        private const string ComponentRefDefinitionName = "CoreECS.Defines.ComponentRef<T>";
        private const string StructureTypeName = "CoreECS.Structures.Structure";
        private const string SpanDefinitionName = "System.Span<T>";
        private const string ReadOnlySpanDefinitionName = "System.ReadOnlySpan<T>";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            "Ref/span may be invalidated by a structural change",
            "ref/span obtained from '{0}' may be invalidated by '{1}'; copy the value or finish using it before structural changes",
            "CoreECS",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            helpLinkUri: "https://github.com/Cardidi/CoreECS/blob/main/Analyzers/README.md#ecs0001");

        private static readonly SyntaxKind[] BodySyntaxKinds =
        {
            SyntaxKind.MethodDeclaration,
            SyntaxKind.ConstructorDeclaration,
            SyntaxKind.DestructorDeclaration,
            SyntaxKind.OperatorDeclaration,
            SyntaxKind.ConversionOperatorDeclaration,
            SyntaxKind.GetAccessorDeclaration,
            SyntaxKind.SetAccessorDeclaration,
            SyntaxKind.AddAccessorDeclaration,
            SyntaxKind.RemoveAccessorDeclaration,
            SyntaxKind.LocalFunctionStatement,
        };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(startContext =>
            {
                var index = UnsafeMethodIndex.Build(startContext.Compilation);
                startContext.RegisterSyntaxNodeAction(
                    nodeContext => AnalyzeBody(nodeContext, index),
                    BodySyntaxKinds);
            });
        }

        private static void AnalyzeBody(SyntaxNodeAnalysisContext context, UnsafeMethodIndex index)
        {
            var body = context.Node;
            var model = context.SemanticModel;
            var tracked = new Dictionary<ISymbol, TrackedRef>(SymbolEqualityComparer.Default);
            var candidateNames = new HashSet<string>(StringComparer.Ordinal);
            var triggers = new List<TriggerCall>();

            foreach (var node in EnumerateBodyNodes(body))
            {
                switch (node)
                {
                    case LocalDeclarationStatementSyntax declaration:
                        RegisterDeclaration(declaration, model, tracked, candidateNames);
                        break;
                    case AssignmentExpressionSyntax assignment when assignment.IsKind(SyntaxKind.SimpleAssignmentExpression):
                        HandleAssignment(assignment, model, tracked, candidateNames);
                        break;
                    default:
                        if (TryGetUnsafeCall(node, model, index, out var trigger))
                        {
                            triggers.Add(trigger);
                        }

                        break;
                }

                if (node is IdentifierNameSyntax identifier
                    && candidateNames.Contains(identifier.Identifier.ValueText))
                {
                    UpdateLastUse(identifier, model, tracked);
                }
            }

            foreach (var trigger in triggers)
            {
                TrackedRef? source = null;
                foreach (var candidate in tracked.Values)
                {
                    if (!candidate.IsLiveAt(trigger.Position))
                    {
                        continue;
                    }

                    if (source == null || candidate.DeclarationPosition > source.DeclarationPosition)
                    {
                        source = candidate;
                    }
                }

                if (source == null)
                {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    Rule,
                    trigger.Node.GetLocation(),
                    source.Origin,
                    trigger.DisplayName));
            }
        }

        private static bool TryGetUnsafeCall(
            SyntaxNode node,
            SemanticModel model,
            UnsafeMethodIndex index,
            out TriggerCall trigger)
        {
            trigger = default;

            switch (node)
            {
                case InvocationExpressionSyntax invocation:
                    return TryCreateTrigger(
                        invocation,
                        model.GetSymbolInfo(invocation).Symbol as IMethodSymbol,
                        model,
                        index,
                        out trigger);
                case ObjectCreationExpressionSyntax creation:
                    return TryCreateTrigger(
                        creation,
                        model.GetSymbolInfo(creation).Symbol as IMethodSymbol,
                        model,
                        index,
                        out trigger);
                case MemberAccessExpressionSyntax memberAccess when memberAccess.Parent is not InvocationExpressionSyntax:
                    if (model.GetSymbolInfo(memberAccess).Symbol is not IPropertySymbol property)
                    {
                        return false;
                    }

                    return TryCreateTrigger(
                        memberAccess,
                        GetAccessedAccessor(memberAccess, property),
                        model,
                        index,
                        out trigger);
                case ElementAccessExpressionSyntax elementAccess when elementAccess.Parent is not InvocationExpressionSyntax:
                    if (model.GetSymbolInfo(elementAccess).Symbol is not IPropertySymbol indexer)
                    {
                        return false;
                    }

                    return TryCreateTrigger(
                        elementAccess,
                        GetAccessedAccessor(elementAccess, indexer),
                        model,
                        index,
                        out trigger);
                default:
                    return false;
            }
        }

        private static bool TryCreateTrigger(
            SyntaxNode node,
            IMethodSymbol? method,
            SemanticModel model,
            UnsafeMethodIndex index,
            out TriggerCall trigger)
        {
            trigger = default;
            if (method == null)
            {
                return false;
            }

            if (!StructuralChangeApis.IsStructuralChangeApi(method) && !index.IsUnsafe(method))
            {
                return false;
            }

            var name = method.AssociatedSymbol?.Name ?? method.Name;
            trigger = new TriggerCall(
                node,
                name + FormatTypeArguments(method, model, node.SpanStart),
                node.SpanStart);
            return true;
        }

        private static IMethodSymbol? GetAccessedAccessor(
            ExpressionSyntax expression,
            IPropertySymbol property)
        {
            var isWrite = expression.Parent is AssignmentExpressionSyntax assignment && assignment.Left == expression
                          || expression.Parent is PrefixUnaryExpressionSyntax
                          || expression.Parent is PostfixUnaryExpressionSyntax;

            return isWrite
                ? property.SetMethod ?? property.GetMethod
                : property.GetMethod ?? property.SetMethod;
        }

        private static IEnumerable<SyntaxNode> EnumerateBodyNodes(SyntaxNode body)
        {
            return body.DescendantNodesAndSelf(
                node => node == body || node is not LocalFunctionStatementSyntax);
        }

        private static void RegisterDeclaration(
            LocalDeclarationStatementSyntax declaration,
            SemanticModel model,
            Dictionary<ISymbol, TrackedRef> tracked,
            HashSet<string> candidateNames)
        {
            foreach (var declarator in declaration.Declaration.Variables)
            {
                if (model.GetDeclaredSymbol(declarator) is not ILocalSymbol symbol)
                {
                    continue;
                }

                candidateNames.Add(symbol.Name);

                if (declarator.Initializer == null)
                {
                    continue;
                }

                var isRefLocal = declaration.Declaration.Type is RefTypeSyntax
                                 || declarator.Initializer.Value is RefExpressionSyntax;
                if (!isRefLocal && !IsSpanType(model.GetTypeInfo(declaration.Declaration.Type).Type))
                {
                    continue;
                }

                if (TryResolveSource(declarator.Initializer.Value, model, tracked, out var origin))
                {
                    tracked[symbol] = new TrackedRef(origin!, declarator.SpanStart);
                }
            }
        }

        private static void HandleAssignment(
            AssignmentExpressionSyntax assignment,
            SemanticModel model,
            Dictionary<ISymbol, TrackedRef> tracked,
            HashSet<string> candidateNames)
        {
            if (assignment.Left is not IdentifierNameSyntax left)
            {
                return;
            }

            var symbol = model.GetSymbolInfo(left).Symbol;
            if (symbol == null)
            {
                return;
            }

            if (TryResolveSource(assignment.Right, model, tracked, out var origin))
            {
                candidateNames.Add(symbol.Name);
                tracked[symbol] = new TrackedRef(origin!, assignment.SpanStart);
            }
            else
            {
                tracked.Remove(symbol);
            }
        }

        private static void UpdateLastUse(
            IdentifierNameSyntax identifier,
            SemanticModel model,
            Dictionary<ISymbol, TrackedRef> tracked)
        {
            var symbol = model.GetSymbolInfo(identifier).Symbol;
            if (symbol == null || !tracked.TryGetValue(symbol, out var info))
            {
                return;
            }

            if (identifier.SpanStart > info.LastUsePosition)
            {
                info.LastUsePosition = identifier.SpanStart;
            }

            if (IsPassedByRefOrOut(identifier))
            {
                info.Escaped = true;
            }
        }

        private static bool IsPassedByRefOrOut(IdentifierNameSyntax identifier)
        {
            var current = identifier.Parent;
            while (current is ExpressionSyntax)
            {
                current = current.Parent;
            }

            return current is ArgumentSyntax argument
                   && argument.RefKindKeyword.Kind() is SyntaxKind.RefKeyword
                       or SyntaxKind.OutKeyword
                       or SyntaxKind.InKeyword;
        }

        private static bool TryResolveSource(
            ExpressionSyntax expression,
            SemanticModel model,
            Dictionary<ISymbol, TrackedRef> tracked,
            out string? origin)
        {
            origin = null;
            if (expression is RefExpressionSyntax refExpression)
            {
                expression = refExpression.Expression;
            }

            while (expression is ParenthesizedExpressionSyntax parenthesized)
            {
                expression = parenthesized.Expression;
            }

            switch (expression)
            {
                case MemberAccessExpressionSyntax memberAccess
                    when model.GetSymbolInfo(memberAccess).Symbol is IPropertySymbol property
                         && IsComponentRefAccessor(property):
                    origin = GetComponentRefAccessorName(property, model, memberAccess.SpanStart);
                    return true;
                case InvocationExpressionSyntax invocation
                    when model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method
                         && IsStructureColumnAccessor(method):
                    origin = GetStructureColumnAccessorName(method, model, invocation.SpanStart);
                    return true;
            }

            foreach (var identifier in expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
            {
                var symbol = model.GetSymbolInfo(identifier).Symbol;
                if (symbol != null && tracked.TryGetValue(symbol, out var source))
                {
                    origin = source.Origin;
                    return true;
                }
            }

            return false;
        }

        private static bool IsComponentRefAccessor(IPropertySymbol property)
        {
            if (property.Name != "RO" && property.Name != "RW")
            {
                return false;
            }

            var containingType = property.ContainingType;
            return containingType != null
                   && containingType.IsGenericType
                   && containingType.OriginalDefinition.ToDisplayString() == ComponentRefDefinitionName;
        }

        private static bool IsStructureColumnAccessor(IMethodSymbol method)
        {
            if (method.Name != "GetReadOnlyDenseColumn" && method.Name != "GetReadWriteDenseColumn")
            {
                return false;
            }

            return method.ContainingType?.ToDisplayString() == StructureTypeName;
        }

        private static string GetComponentRefAccessorName(
            IPropertySymbol property,
            SemanticModel model,
            int position)
        {
            return property.ContainingType.ToMinimalDisplayString(model, position) + "." + property.Name;
        }

        private static string GetStructureColumnAccessorName(
            IMethodSymbol method,
            SemanticModel model,
            int position)
        {
            return method.ContainingType.ToMinimalDisplayString(model, position)
                   + "." + method.Name
                   + FormatTypeArguments(method, model, position);
        }

        private static string FormatTypeArguments(
            IMethodSymbol method,
            SemanticModel model,
            int position)
        {
            if (method.TypeArguments.Length == 0)
            {
                return string.Empty;
            }

            return "<" + string.Join(", ", method.TypeArguments.Select(
                argument => argument.ToMinimalDisplayString(model, position))) + ">";
        }

        private static bool IsSpanType(ITypeSymbol? type)
        {
            if (type is not INamedTypeSymbol namedType || !namedType.IsGenericType)
            {
                return false;
            }

            var definitionName = namedType.OriginalDefinition.ToDisplayString();
            return definitionName == SpanDefinitionName || definitionName == ReadOnlySpanDefinitionName;
        }

        private sealed class TrackedRef
        {
            public TrackedRef(string origin, int declarationPosition)
            {
                Origin = origin;
                DeclarationPosition = declarationPosition;
                LastUsePosition = declarationPosition;
            }

            public string Origin { get; }

            public int DeclarationPosition { get; }

            public int LastUsePosition { get; set; }

            public bool Escaped { get; set; }

            public bool IsLiveAt(int position)
            {
                if (position <= DeclarationPosition)
                {
                    return false;
                }

                return Escaped || LastUsePosition > position;
            }
        }

        private readonly struct TriggerCall
        {
            public TriggerCall(SyntaxNode node, string displayName, int position)
            {
                Node = node;
                DisplayName = displayName;
                Position = position;
            }

            public SyntaxNode Node { get; }

            public string DisplayName { get; }

            public int Position { get; }
        }
    }
}
```

- [ ] **Step 6: 运行确认 GREEN**

```bash
PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --nologo --filter "FullyQualifiedName~CoreECS.Test.Analyzers"
```

Expected: 37 passed / 0 failed。重点核查：
- `TransitiveWrapperCall_InvalidatesLiveRef_Reports`：`Outer` 经 `Inner` 传递不安全。
- `RecursiveUnsafeMethods_ConvergeAndReport`：环内不动点收敛。
- `VirtualDispatch_...` / `InterfaceDispatch_...`：抽象闭包生效。
- `LambdaContainingStructuralChange_...`：lambda 体计入外层函数。
- `UnsafePropertyGetter_...` / `UnsafeConstructor_...`：属性访问/对象创建触发。

---

## Task 6: ECS0002（ref/span 参数 + 不安全调用）

**Files:**
- Modify: `Analyzers/RefInvalidatedByStructuralChangeAnalyzer.cs`
- Create: `Test/RefParameterAnalyzerTestUnit.cs`

- [ ] **Step 1: 写失败测试**

`Test/RefParameterAnalyzerTestUnit.cs`：

```csharp
using System.Threading.Tasks;
using CoreECS.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Testing.Verifiers;

namespace CoreECS.Test.Analyzers
{
    [TestFixture]
    public class RefParameterAnalyzerTestUnit
    {
        private static Task VerifyAsync(string body)
        {
            var test = new CSharpAnalyzerTest<RefInvalidatedByStructuralChangeAnalyzer, NUnitVerifier>
            {
                TestCode = AnalyzerTestSource.Wrap(body),
                ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            };
            return test.RunAsync();
        }

        [Test]
        public async Task RefParameter_UsedAfterStructuralChange_ReportsEcs0002()
        {
            await VerifyAsync(@"
                void Fill(ref PositionComponent value)
                {
                    {|ECS0002:entity.CreateComponent<VelocityComponent>()|};
                    value.X = 1;
                }
                Fill(ref entity.GetComponent<PositionComponent>().RW);
");
        }

        [Test]
        public async Task RefParameter_StructuralChangeWithoutLaterUse_DoesNotReport()
        {
            await VerifyAsync(@"
                void Fill(ref PositionComponent value)
                {
                    entity.CreateComponent<VelocityComponent>();
                }
                Fill(ref entity.GetComponent<PositionComponent>().RW);
");
        }

        [Test]
        public async Task SpanParameter_UsedAfterStructuralChange_ReportsEcs0002()
        {
            await VerifyAsync(@"
                void Fill(Span<PositionComponent> values)
                {
                    {|ECS0002:world.CreateEntity()|};
                    var first = values[0];
                }
                Fill(structure.GetReadWriteDenseColumn<PositionComponent>());
");
        }

        [Test]
        public async Task OutParameter_UsedAfterStructuralChange_ReportsEcs0002()
        {
            await VerifyAsync(@"
                void Reset(out int value)
                {
                    value = 0;
                    {|ECS0002:entity.SetMask(1UL)|};
                    value = 1;
                }
                Reset(out var value);
");
        }

        [Test]
        public async Task InParameter_ReadBeforeStructuralChange_DoesNotReport()
        {
            await VerifyAsync(@"
                void Fill(in PositionComponent value)
                {
                    var x = value.X;
                    entity.CreateComponent<VelocityComponent>();
                }
                Fill(in entity.GetComponent<PositionComponent>().RW);
");
        }

        [Test]
        public async Task InParameter_ReadAfterStructuralChange_ReportsEcs0002()
        {
            await VerifyAsync(@"
                void Fill(in PositionComponent value)
                {
                    {|ECS0002:entity.CreateComponent<VelocityComponent>()|};
                    var x = value.X;
                }
                Fill(in entity.GetComponent<PositionComponent>().RW);
");
        }

        [Test]
        public async Task MultipleRefParameters_AllNamesInMessage()
        {
            var diagnostics = await AnalyzerHarness.GetDiagnosticsAsync(AnalyzerTestSource.Wrap(@"
                void Combine(ref PositionComponent left, Span<PositionComponent> right)
                {
                    world.CreateEntity();
                    left.X = 1;
                    var first = right[0];
                }
                Combine(ref entity.GetComponent<PositionComponent>().RW, structure.GetReadWriteDenseColumn<PositionComponent>());
"));
            var ecs0002 = diagnostics.Single(diagnostic => diagnostic.Id == "ECS0002");
            Assert.That(ecs0002.GetMessage(), Does.Contain("'left, right'"));
            Assert.That(ecs0002.GetMessage(), Does.Contain("'CreateEntity'"));
        }

        [Test]
        public async Task ComponentRefParameter_StructuralChange_DoesNotReportEcs0002()
        {
            await VerifyAsync(@"
                void Touch(ComponentRef<PositionComponent> handle)
                {
                    entity.SetMask(1UL);
                    handle.RW.X = 1;
                }
                Touch(default);
");
        }

        [Test]
        public async Task RefParameter_TransitiveUnsafeCall_ReportsEcs0002()
        {
            await VerifyAsync(@"
                void Inner() { world.CreateEntity(); }
                void Forward(ref PositionComponent value)
                {
                    {|ECS0002:Inner()|};
                    value.X = 1;
                }
                Forward(ref entity.GetComponent<PositionComponent>().RW);
");
        }

        [Test]
        public async Task RefParameter_ForwardedToUnsafeMethod_ReportsBothEcs0002()
        {
            await VerifyAsync(@"
                void Mutate(ref PositionComponent value)
                {
                    {|ECS0002:entity.SetMask(1UL)|};
                    value.X = 1;
                }
                void Forward(ref PositionComponent value)
                {
                    {|ECS0002:Mutate(ref value)|};
                    value.X = 2;
                }
                Forward(ref entity.GetComponent<PositionComponent>().RW);
");
        }

        [Test]
        public async Task SafeRefParameterMethod_DoesNotReport()
        {
            await VerifyAsync(@"
                void Fill(ref PositionComponent value)
                {
                    value.X = 1;
                }
                Fill(ref entity.GetComponent<PositionComponent>().RW);
");
        }

        [Test]
        public async Task RefPassedToUnsafeMethod_ReportsEcs0001AtCall()
        {
            await VerifyAsync(@"
                void Mutate(ref PositionComponent value)
                {
                    {|ECS0002:entity.SetMask(1UL)|};
                    value.X = 1;
                }
                ref var position = ref entity.GetComponent<PositionComponent>().RW;
                {|ECS0001:Mutate(ref position)|};
                position.X = 1;
");
        }
    }
}
```

- [ ] **Step 2: 运行确认 RED**

```bash
PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --nologo --filter "FullyQualifiedName~CoreECS.Test.Analyzers"
```

Expected: “参数在调用后仍使用”的 ECS0002 用例失败；`RefParameter_StructuralChangeWithoutLaterUse_DoesNotReport`、`InParameter_ReadBeforeStructuralChange_DoesNotReport`、`ComponentRefParameter_...`、`SafeRefParameterMethod_...` 通过。

- [ ] **Step 3: 给分析器加入 ECS0002**

对 `RefInvalidatedByStructuralChangeAnalyzer.cs` 做以下修改（其余不动）：

1) 常量与规则：

```csharp
        public const string RefParameterDiagnosticId = "ECS0002";

        private const string ComponentRefTypeName = "CoreECS.Defines.ComponentRef";
```

2) 在 `Rule` 之后新增 `RefParameterRule`：

```csharp
        private static readonly DiagnosticDescriptor RefParameterRule = new DiagnosticDescriptor(
            RefParameterDiagnosticId,
            "Ref/span parameter may be invalidated by a structural change",
            "ref/span parameter '{0}' may be invalidated by '{1}'; re-acquire the ref/span after the structural change",
            "CoreECS",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            helpLinkUri: "https://github.com/Cardidi/CoreECS/blob/main/Analyzers/README.md#ecs0002");
```

3) `SupportedDiagnostics`：

```csharp
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(Rule, RefParameterRule);
```

4) `AnalyzeBody` 顶部取 ref/span 参数集合并登记名字（供标识符快速过滤），并在标识符分支更新参数的最后一次使用：

```csharp
            var refLikeParameters = GetRefLikeParameters(model.GetDeclaredSymbol(body) as IMethodSymbol);
            var refLikeParameterSet = new HashSet<ISymbol>(refLikeParameters, SymbolEqualityComparer.Default);
            var parameterLastUse = new Dictionary<ISymbol, int>(SymbolEqualityComparer.Default);

            foreach (var parameter in refLikeParameters)
            {
                candidateNames.Add(parameter.Name);
            }
```

```csharp
                if (node is IdentifierNameSyntax identifier
                    && candidateNames.Contains(identifier.Identifier.ValueText))
                {
                    UpdateLastUse(identifier, model, tracked);
                    UpdateParameterLastUse(identifier, model, refLikeParameterSet, parameterLastUse);
                }
```

5) 报告循环内（ECS0001 报告之后）追加 ECS0002——仅当参数在触发调用**之后**仍被使用（实参位置在 `trigger.Node.Span.End` 之内不算）：

```csharp
                var staleParameters = new List<IParameterSymbol>();
                foreach (var parameter in refLikeParameters)
                {
                    if (parameterLastUse.TryGetValue(parameter, out var lastUse)
                        && lastUse > trigger.Node.Span.End)
                    {
                        staleParameters.Add(parameter);
                    }
                }

                if (staleParameters.Count > 0)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        RefParameterRule,
                        trigger.Node.GetLocation(),
                        string.Join(", ", staleParameters.Select(parameter => parameter.Name)),
                        trigger.DisplayName));
                }
```

6) 新增成员：

```csharp
        private static void UpdateParameterLastUse(
            IdentifierNameSyntax identifier,
            SemanticModel model,
            HashSet<ISymbol> refLikeParameters,
            Dictionary<ISymbol, int> lastUses)
        {
            if (refLikeParameters.Count == 0)
            {
                return;
            }

            var symbol = model.GetSymbolInfo(identifier).Symbol;
            if (symbol == null || !refLikeParameters.Contains(symbol))
            {
                return;
            }

            if (!lastUses.TryGetValue(symbol, out var lastUse) || identifier.SpanStart > lastUse)
            {
                lastUses[symbol] = identifier.SpanStart;
            }
        }

        private static List<IParameterSymbol> GetRefLikeParameters(IMethodSymbol? method)
        {
            var result = new List<IParameterSymbol>();
            if (method == null)
            {
                return result;
            }

            foreach (var parameter in method.Parameters)
            {
                if (parameter.IsThis || IsComponentRefType(parameter.Type))
                {
                    continue;
                }

                if (parameter.RefKind != RefKind.None || IsSpanType(parameter.Type))
                {
                    result.Add(parameter);
                }
            }

            return result;
        }

        private static bool IsComponentRefType(ITypeSymbol type)
        {
            if (type is not INamedTypeSymbol namedType)
            {
                return false;
            }

            var definitionName = namedType.OriginalDefinition.ToDisplayString();
            return definitionName == ComponentRefTypeName || definitionName == ComponentRefDefinitionName;
        }
```

- [ ] **Step 4: 运行确认 GREEN**

```bash
PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --nologo --filter "FullyQualifiedName~CoreECS.Test.Analyzers"
```

Expected: 49 passed / 0 failed。若 `RefPassedToUnsafeMethod_ReportsEcs0001AtCall` 失败，检查 `Mutate(ref position)` 是否被 `TryGetUnsafeCall` 识别为不安全调用（`index.IsUnsafe(Mutate)`），以及存活区间是否因 `ref` 实参标记 `Escaped`。

## Task 7: `#pragma` 抑制、Kernel 零误报、性能冒烟、全解决方案

**Files:**
- Modify: `Test/RefInvalidationAnalyzerTestUnit.cs`（追加抑制测试）
- Create: `Test/KernelSourceAnalysisTestUnit.cs`

- [ ] **Step 1: 追加 `#pragma` 抑制测试**

```csharp
        [Test]
        public async Task PragmaDisable_SuppressesEcs0001()
        {
            await VerifyAsync(@"
                ref var position = ref entity.GetComponent<PositionComponent>().RW;
#pragma warning disable ECS0001
                world.CreateEntity();
#pragma warning restore ECS0001
                position.X = 1;
");
        }
```

（ECS0002 的 pragma 抑制同理，可在 `RefParameterAnalyzerTestUnit` 追加同型用例。）

- [ ] **Step 2: 运行确认 GREEN**

Expected: 50 passed / 0 failed。

- [ ] **Step 3: 写 Kernel 零误报 + 性能冒烟测试**

`Test/KernelSourceAnalysisTestUnit.cs`：

```csharp
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CoreECS.Test.Analyzers
{
    [TestFixture]
    public class KernelSourceAnalysisTestUnit
    {
        [Test]
        public async Task KernelSources_ProduceNoEcsDiagnostics()
        {
            var kernelDirectory = FindKernelDirectory();
            var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp12)
                .WithPreprocessorSymbols(
                    "NET", "NET8_0", "NET8_0_OR_GREATER", "NET6_0_OR_GREATER",
                    "NETCOREAPP", "NETCOREAPP3_1_OR_GREATER");
            var trees = Directory
                .GetFiles(kernelDirectory, "*.cs", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), parseOptions, path))
                .ToArray();

            var references = GetRuntimeReferences()
                .Append(MetadataReference.CreateFromFile(
                    typeof(Microsoft.Extensions.DependencyInjection.ServiceCollection).Assembly.Location))
                .ToArray();

            var compilation = CSharpCompilation.Create(
                "CoreECS.Kernel",
                trees,
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    allowUnsafe: true,
                    nullableContextOptions: NullableContextOptions.Disable));

            var compileErrors = compilation.GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToArray();
            Assert.That(compileErrors, Is.Empty, "Kernel 源码应在测试编译中零错误：" + string.Join(Environment.NewLine, compileErrors.Select(d => d.ToString())));

            var analyzerDiagnostics = await GetAnalyzerDiagnosticsAsync(compilation);
            var ecsDiagnostics = analyzerDiagnostics.Where(d => d.Id is "ECS0001" or "ECS0002").ToArray();
            Assert.That(ecsDiagnostics, Is.Empty, "Kernel 源码出现 ECS 诊断：" + string.Join(Environment.NewLine, ecsDiagnostics.Select(d => d.ToString())));
        }

        [Test]
        public async Task LargeMethod_AnalyzesWithinSmokeBudget()
        {
            var builder = new StringBuilder();
            for (var i = 0; i < 500; i++)
            {
                builder.AppendLine($"ref var p{i} = ref entity.GetComponent<PositionComponent>().RW;");
                builder.AppendLine($"p{i}.X = {i};");
                builder.AppendLine("world.CreateEntity();");
                builder.AppendLine($"p{i}.Y = {i};");
            }

            var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<RefInvalidatedByStructuralChangeAnalyzer, Microsoft.CodeAnalysis.Testing.Verifiers.NUnitVerifier>
            {
                TestCode = AnalyzerTestSource.Wrap(builder.ToString()),
                ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net80,
                CompilerDiagnostics = Microsoft.CodeAnalysis.Testing.CompilerDiagnostics.None,
            };

            var stopwatch = Stopwatch.StartNew();
            await test.RunAsync();
            stopwatch.Stop();

            Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(5000),
                "大方法分析（含索引构建）应低于冒烟预算；spec 目标见第 7 节");
        }

        private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(CSharpCompilation compilation)
        {
            var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new RefInvalidatedByStructuralChangeAnalyzer());
            return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync(CancellationToken.None);
        }

        private static IEnumerable<MetadataReference> GetRuntimeReferences()
        {
            var trustedPlatformAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
            return trustedPlatformAssemblies
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path));
        }

        private static string FindKernelDirectory()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, "Kernel", "Kernel.csproj");
                if (File.Exists(candidate))
                {
                    return Path.Combine(directory.FullName, "Kernel");
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("未找到 Kernel/Kernel.csproj，测试需从仓库内运行。");
        }
    }
}
```

- [ ] **Step 4: 运行确认 GREEN**

```bash
PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --nologo --filter "FullyQualifiedName~CoreECS.Test.Analyzers"
```

Expected: 52 passed / 0 failed。

若 Kernel 出现 ECS0001/ECS0002：**不得修改 Kernel 运行时代码**，先把命中点报告给需求方确认；若确认误报，收紧规则后重跑（例如 ECS0002 排除 `out ComponentRef<T>` 已覆盖 `EntityExtension.GetOrCreateComponent`，若仍有命中需逐条分析）。

- [ ] **Step 5: 全解决方案构建 + 测试**

```bash
PATH="$HOME/.dotnet:$PATH" dotnet build CoreECS.sln --nologo
PATH="$HOME/.dotnet:$PATH" dotnet test CoreECS.sln --nologo
```

Expected: 0 错误；Kernel/Test 原有测试与新增分析器测试全绿。

---

## Task 8: README

**Files:**
- Create: `Analyzers/README.md`

- [ ] **Step 1: 写 `Analyzers/README.md`**

完整内容（与仓库实际文件一致）：

````markdown
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
````

- [ ] **Step 2: 最终验证**

```bash
PATH="$HOME/.dotnet:$PATH" dotnet build CoreECS.sln --nologo
PATH="$HOME/.dotnet:$PATH" dotnet test CoreECS.sln --nologo
```

Expected: 构建 0 错误；全部测试通过（含分析器 52 个用例与 Kernel 零误报）。

- [ ] **Step 3: 向用户汇报（不要自行 commit）**

汇总：新增文件、测试数量、Kernel 零误报结果、索引构建/大方法实测耗时；询问是否需要提交（建议 `feat(core): add ECS0001/ECS0002 ref invalidation analyzer with unsafe transitivity`）。

---

## Self-Review

**Spec 覆盖核对：**
- ECS0001 来源/存活/固定触发/不报告 → Task 1-4；传递不安全触发 → Task 5。
- ECS0002 参数范围（ref/out/in + Span，排除 ComponentRef、this）→ Task 6；直接与传递不安全调用 → Task 6；仅当参数在触发调用之后仍被使用时报告（Kernel `EntityExtension.cs:53` 的 `in initialValue` 只在调用前读取，因此零误报；该决策在执行中经需求方确认）。
- 不安全传递（直接、不动点、override 链、接口实现、lambda、局部函数独立）→ Task 5。
- 触发形态（方法调用、new、属性/索引器访问、运算符）→ Task 5（运算符经 `GetCalledMethod` 覆盖，未单测，属实现细节）。
- 交付物（项目/sln/README/测试）→ Task 1、7、8。
- 验收（全绿、Kernel 零误报、性能、pragma）→ Task 7。
- 非目标（来源不跨方法、无运行时改动、不检测句柄）→ 设计保持。

**占位符扫描：** 无 TBD/TODO；关键代码步骤给出完整代码。

**类型/命名一致性：** `UnsafeMethodIndex.IsUnsafe`、`StructuralChangeApis.IsStructuralChangeApi`、`TriggerCall.Node`、`RefParameterRule`、`RefParameterDiagnosticId`、`AnalyzerTestSource.WrapWith`、`TestWorld.Instance` 全计划一致。

**执行注意：**
- 必须 `PATH="$HOME/.dotnet:$PATH" dotnet`（系统 PATH 的 SDK 10.0.301 不满足 `global.json`）。
- `Microsoft.CodeAnalysis.Testing.Verifiers.NUnitVerifier` 在命名空间 `Microsoft.CodeAnalysis.Testing.Verifiers`；`ReferenceAssemblies.Net.Net80` 在 1.1.2 可用（已 spike 验证）。
- 每个 Task 结束跑分析器测试项目；Task 7 结束跑全解决方案。
- 不在本计划内 commit；等用户确认。
