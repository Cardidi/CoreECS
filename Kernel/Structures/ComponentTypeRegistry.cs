using System;
using System.Collections.Generic;
using System.Reflection;
using CoreECS.Defines;

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

                ComponentKind kind = ResolveKind(componentType);

                if (s_lastTypeId == uint.MaxValue)
                {
                    throw new InvalidOperationException("The process has exhausted component TypeIds.");
                }

                metadata = new ComponentTypeMetadata(componentType, ++s_lastTypeId, kind);
                s_metadataByType.Add(componentType, metadata);
                return metadata;
            }
        }

        private static ComponentKind ResolveKind(Type componentType)
        {
            bool isTag = false;
            Type[] implementedInterfaces = componentType.GetInterfaces();
            for (int i = 0; i < implementedInterfaces.Length; i++)
            {
                Type implementedInterface = implementedInterfaces[i];
                if (implementedInterface.IsGenericType &&
                    implementedInterface.GetGenericTypeDefinition() == typeof(ITagComponent<>) &&
                    implementedInterface.GetGenericArguments()[0] == componentType)
                {
                    isTag = true;
                    break;
                }
            }

            if (!isTag)
            {
                return ComponentKind.Data;
            }

            if (componentType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Length != 0)
            {
                throw new InvalidOperationException(
                    $"Tag component {componentType.FullName} cannot declare instance fields.");
            }

            return ComponentKind.Tag;
        }
    }
}
