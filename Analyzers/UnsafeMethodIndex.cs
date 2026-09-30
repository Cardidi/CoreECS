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
