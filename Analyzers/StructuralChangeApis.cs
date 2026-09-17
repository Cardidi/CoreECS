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
