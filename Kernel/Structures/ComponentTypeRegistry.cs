using System;
using System.Collections.Generic;

namespace CoreECS.Structures
{
    internal sealed class ComponentTypeMetadata
    {
        internal Type ComponentType { get; }

        internal uint TypeId { get; }

        internal ComponentKind Kind { get; }

        internal ComponentTypeMetadata(Type componentType, uint typeId, ComponentKind kind)
        {
            ComponentType = componentType;
            TypeId = typeId;
            Kind = kind;
        }
    }

    internal static class ComponentType<T>
    {
        internal static readonly ComponentTypeMetadata Metadata =
            ComponentTypeRegistry.Register(typeof(T));
    }

    internal static class ComponentTypeRegistry
    {
        private static readonly object s_registrationLock = new object();
        private static readonly Dictionary<Type, ComponentTypeMetadata> s_metadataByType =
            new Dictionary<Type, ComponentTypeMetadata>();

        private static uint s_lastTypeId;

        internal static ComponentTypeMetadata GetOrRegister<T>()
        {
            return ComponentType<T>.Metadata;
        }

        internal static ComponentTypeMetadata Register(Type componentType)
        {
            lock (s_registrationLock)
            {
                if (s_metadataByType.TryGetValue(componentType, out ComponentTypeMetadata metadata))
                {
                    return metadata;
                }

                if (s_lastTypeId == uint.MaxValue)
                {
                    throw new InvalidOperationException("The process has exhausted component TypeIds.");
                }

                metadata = new ComponentTypeMetadata(componentType, ++s_lastTypeId, ComponentKind.Data);
                s_metadataByType.Add(componentType, metadata);
                return metadata;
            }
        }
    }
}
