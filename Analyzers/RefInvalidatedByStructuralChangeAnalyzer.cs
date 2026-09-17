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

        public const string RefParameterDiagnosticId = "ECS0002";

        private const string ComponentRefDefinitionName = "CoreECS.Defines.ComponentRef<T>";
        private const string ComponentRefTypeName = "CoreECS.Defines.ComponentRef";
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

        private static readonly DiagnosticDescriptor RefParameterRule = new DiagnosticDescriptor(
            RefParameterDiagnosticId,
            "Ref/span parameter may be invalidated by a structural change",
            "ref/span parameter '{0}' may be invalidated by '{1}'; re-acquire the ref/span after the structural change",
            "CoreECS",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            helpLinkUri: "https://github.com/Cardidi/CoreECS/blob/main/Analyzers/README.md#ecs0002");

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

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(Rule, RefParameterRule);

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
            var refLikeParameters = GetRefLikeParameters(model.GetDeclaredSymbol(body) as IMethodSymbol);
            var refLikeParameterSet = new HashSet<ISymbol>(refLikeParameters, SymbolEqualityComparer.Default);
            var parameterLastUse = new Dictionary<ISymbol, int>(SymbolEqualityComparer.Default);

            foreach (var parameter in refLikeParameters)
            {
                candidateNames.Add(parameter.Name);
            }

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
                    UpdateParameterLastUse(identifier, model, refLikeParameterSet, parameterLastUse);
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

                if (source != null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        Rule,
                        trigger.Node.GetLocation(),
                        source.Origin,
                        trigger.DisplayName));
                }

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
            }
        }

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
