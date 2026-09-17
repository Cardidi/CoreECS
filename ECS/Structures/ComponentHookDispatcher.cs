using System;
using System.Collections.Concurrent;
using CoreECS.Defines;

namespace CoreECS.Structures
{
    /// <summary>
    /// Non-generic dispatch of component lifecycle hooks (<c>OnCreate</c> / <c>OnDestroy</c>).
    /// Generic callers register the per-type hook pair; non-generic orchestration code that
    /// only knows type ids invokes the cached delegates.
    /// </summary>
    internal static class ComponentHookDispatcher
    {
        private static readonly ConcurrentDictionary<uint, ComponentHookPair> s_dense = new();
        private static readonly ConcurrentDictionary<uint, ComponentHookPair> s_discrete = new();

        /// <summary>Registers the dense component hooks for <typeparamref name="T"/>.</summary>
        public static void RegisterDense<T>() where T : struct, IComponent<T>
        {
            var typeId = ComponentTypeRegistry.GetOrRegister<T>().TypeId;
            s_dense[typeId] = new ComponentHookPair(
                (structure, row, entityId) => structure.GetDenseRef<T>(row).OnCreate(entityId),
                (structure, row, entityId) => structure.GetDenseRef<T>(row).OnDestroy(entityId));
        }

        /// <summary>Registers the discrete component hooks for <typeparamref name="T"/>.</summary>
        public static void RegisterDiscrete<T>() where T : struct, IDiscreteComponent<T>
        {
            var typeId = ComponentTypeRegistry.GetOrRegister<T>().TypeId;
            s_discrete[typeId] = new ComponentHookPair(
                (structure, row, entityId) => structure.GetDiscreteRef<T>(row).OnCreate(entityId),
                (structure, row, entityId) => structure.GetDiscreteRef<T>(row).OnDestroy(entityId));
        }

        /// <summary>Invokes <c>OnCreate</c> on the dense component at the row; no-op when unregistered.</summary>
        public static void InvokeDenseCreate(Structure structure, int row, uint typeId, ulong entityId)
        {
            if (s_dense.TryGetValue(typeId, out var hooks)) hooks.Create(structure, row, entityId);
        }

        /// <summary>Invokes <c>OnDestroy</c> on the dense component at the row; no-op when unregistered.</summary>
        public static void InvokeDenseDestroy(Structure structure, int row, uint typeId, ulong entityId)
        {
            if (s_dense.TryGetValue(typeId, out var hooks)) hooks.Destroy(structure, row, entityId);
        }

        /// <summary>Invokes <c>OnCreate</c> on the discrete component at the row; no-op when unregistered.</summary>
        public static void InvokeDiscreteCreate(Structure structure, int row, uint typeId, ulong entityId)
        {
            if (s_discrete.TryGetValue(typeId, out var hooks)) hooks.Create(structure, row, entityId);
        }

        /// <summary>Invokes <c>OnDestroy</c> on the discrete component at the row; no-op when unregistered.</summary>
        public static void InvokeDiscreteDestroy(Structure structure, int row, uint typeId, ulong entityId)
        {
            if (s_discrete.TryGetValue(typeId, out var hooks)) hooks.Destroy(structure, row, entityId);
        }
    }

    /// <summary>
    /// Create/destroy hook delegates for one component type. Delegates receive the structure,
    /// the live row and the owning entity id so callers need no generic type information.
    /// </summary>
    internal readonly struct ComponentHookPair
    {
        /// <summary>Invokes <c>OnCreate(entityId)</c> on the component instance at the row.</summary>
        public readonly Action<Structure, int, ulong> Create;

        /// <summary>Invokes <c>OnDestroy(entityId)</c> on the component instance at the row.</summary>
        public readonly Action<Structure, int, ulong> Destroy;

        /// <summary>Creates a hook pair from create/destroy delegates.</summary>
        public ComponentHookPair(Action<Structure, int, ulong> create, Action<Structure, int, ulong> destroy)
        {
            Create = create;
            Destroy = destroy;
        }
    }
}
