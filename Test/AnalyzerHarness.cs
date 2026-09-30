using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreECS.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CoreECS.Test.Analyzers
{
    internal static class AnalyzerHarness
    {
        public static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string source)
        {
            var tree = CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.CSharp12));
            var compilation = CSharpCompilation.Create(
                "AnalyzerTest",
                new[] { tree },
                GetRuntimeReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new RefInvalidatedByStructuralChangeAnalyzer());
            return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync(CancellationToken.None);
        }

        public static IEnumerable<MetadataReference> GetRuntimeReferences()
        {
            var trustedPlatformAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
            return trustedPlatformAssemblies
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path));
        }
    }
}
