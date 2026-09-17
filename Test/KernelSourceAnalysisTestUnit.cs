using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CoreECS.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Testing.Verifiers;

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
                .Where(path => !IsBuildOutputPath(path))
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), parseOptions, path))
                .ToArray();

            var references = AnalyzerHarness.GetRuntimeReferences()
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
            Assert.That(compileErrors, Is.Empty,
                "Kernel 源码应在测试编译中零错误：" + string.Join(Environment.NewLine, compileErrors.Select(d => d.ToString())));

            var analyzerDiagnostics = await GetAnalyzerDiagnosticsAsync(compilation);
            var ecsDiagnostics = analyzerDiagnostics.Where(d => d.Id is "ECS0001" or "ECS0002").ToArray();
            Assert.That(ecsDiagnostics, Is.Empty,
                "Kernel 源码出现 ECS 诊断：" + string.Join(Environment.NewLine, ecsDiagnostics.Select(d => d.ToString())));
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

            var stopwatch = Stopwatch.StartNew();
            var diagnostics = await AnalyzerHarness.GetDiagnosticsAsync(AnalyzerTestSource.Wrap(builder.ToString()));
            stopwatch.Stop();

            Assert.That(diagnostics.Count(diagnostic => diagnostic.Id == "ECS0001"), Is.EqualTo(500));
            Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(5000),
                "大方法分析（含索引构建）应低于冒烟预算；spec 目标见第 7 节");
        }

        private static bool IsBuildOutputPath(string path)
        {
            var separator = Path.DirectorySeparatorChar;
            return path.Contains($"{separator}obj{separator}") || path.Contains($"{separator}bin{separator}");
        }

        private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(CSharpCompilation compilation)
        {
            var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new RefInvalidatedByStructuralChangeAnalyzer());
            return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync(CancellationToken.None);
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
