using System;
using System.Collections.Concurrent;
using System.Threading;
using CoreECS.Defines;

namespace CoreECS.Structures
{
    /// <summary>
    /// Immutable metadata describing a registered component type.
    /// </summary>
    public readonly struct ComponentTypeInfo
    {
        /// <summary>The component struct type.</summary>
        public readonly Type Type;

        /// <summary>Stable id assigned on first registration; starts at 1.</summary>
        public readonly uint TypeId;

        /// <summary>Storage category of the component type.</summary>
        public readonly ComponentKind Kind;

        internal ComponentTypeInfo(Type type, uint typeId, ComponentKind kind)
        {
            Type = type;
            TypeId = typeId;
            Kind = kind;
        }
    }

    /// <summary>
    /// Global registry mapping component types to stable ids and storage kinds.
    /// Registration is append-only: ids are never reused or reassigned.
    /// </summary>
    public static class ComponentTypeRegistry
    {
        private static readonly ConcurrentDictionary<Type, ComponentTypeInfo> s_byType = new();
        private static readonly ConcurrentDictionary<uint, ComponentTypeInfo> s_byId = new();
        private static readonly object s_lock = new();
        private static int s_nextId = 0;

        /// <summary>Number of registered type entries (test hook).</summary>
        internal static int RegisteredTypeCount => s_byType.Count;

        /// <summary>Number of registered id entries (test hook); always equals <see cref="RegisteredTypeCount"/>.</summary>
        internal static int RegisteredIdCount => s_byId.Count;

        /// <summary>
        /// Gets metadata for a component type, registering it on first use.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="type"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="type"/> is not a component type.</exception>
        public static ComponentTypeInfo GetOrRegister<T>() where T : struct, IComponent<T>
        {
            return GetOrRegister(typeof(T));
        }

        /// <summary>
        /// Gets metadata for a component type, registering it on first use.
        /// Registration is serialized under a lock so a losing concurrent registration
        /// can never publish an orphan id into the id map.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="type"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="type"/> is not a component type.</exception>
        public static ComponentTypeInfo GetOrRegister(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (s_byType.TryGetValue(type, out var registered)) return registered;

            lock (s_lock)
            {
                if (s_byType.TryGetValue(type, out registered)) return registered;

                var kind = ResolveKind(type);
                var id = (uint)Interlocked.Increment(ref s_nextId);
                var info = new ComponentTypeInfo(type, id, kind);
                s_byId[id] = info;
                s_byType[type] = info;
                return info;
            }
        }

        /// <summary>
        /// Tries to get metadata without registering the type.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="type"/> is null.</exception>
        public static bool TryGet(Type type, out ComponentTypeInfo info)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            return s_byType.TryGetValue(type, out info);
        }

        /// <summary>
        /// Gets metadata for a registered type id.
        /// </summary>
        public static ComponentTypeInfo GetById(uint typeId)
        {
            if (s_byId.TryGetValue(typeId, out var info)) return info;

            throw new ArgumentOutOfRangeException(
                nameof(typeId), $"Component type id {typeId} is not registered.");
        }

        /// <summary>
        /// Resolves the storage kind of a component type by its most derived interface
        /// (Tag &gt; Discrete &gt; Dense).
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="type"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="type"/> is not a component struct type.</exception>
        public static ComponentKind ResolveKind(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (!type.IsValueType)
            {
                throw new ArgumentException(
                    $"{type.FullName} is not a CoreECS component type; components must be structs.",
                    nameof(type));
            }

            if (ImplementsOpenGeneric(type, typeof(ITagComponent<>))) return ComponentKind.Tag;
            if (ImplementsOpenGeneric(type, typeof(IDiscreteComponent<>))) return ComponentKind.Discrete;
            if (ImplementsOpenGeneric(type, typeof(IComponent<>))) return ComponentKind.Dense;

            throw new ArgumentException(
                $"{type.FullName} is not a CoreECS component type.", nameof(type));
        }

        private static bool ImplementsOpenGeneric(Type type, Type openGeneric)
        {
            foreach (var implemented in type.GetInterfaces())
            {
                if (implemented.IsGenericType &&
                    implemented.GetGenericTypeDefinition() == openGeneric)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
