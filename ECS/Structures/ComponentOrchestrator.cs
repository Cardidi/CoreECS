using System;
using CoreECS.Defines;

namespace CoreECS.Structures
{
    /// <summary>
    /// Coordinates entity lifecycle and non-dense component operations over the kernel:
    /// allocates entities through the structure registry and entity table, dispatches
    /// component lifecycle hooks and forwards structure observer events to the injected sink.
    /// </summary>
    internal sealed class ComponentOrchestrator
    {
        private static readonly uint[] s_noDenseTypes = Array.Empty<uint>();

        private readonly StructureRegistry m_registry;
        private readonly EntityTable m_table;
        private readonly IStructureObserver m_observer;

        /// <summary>
        /// Creates an orchestrator over the given kernel registry and entity table.
        /// <paramref name="observer"/> may be null; when set it is attached to every
        /// structure the orchestrator selects.
        /// </summary>
        public ComponentOrchestrator(StructureRegistry registry, EntityTable table, IStructureObserver observer = null)
        {
            m_registry = registry ?? throw new ArgumentNullException(nameof(registry));
            m_table = table ?? throw new ArgumentNullException(nameof(table));
            m_observer = observer;
        }

        /// <summary>
        /// Creates an entity with the given component mask in the (empty dense) structure
        /// selected by that mask, appends its row and returns the id/location pair.
        /// </summary>
        public (ulong EntityId, EntityLocation Location) CreateEntity(ulong mask = ulong.MaxValue)
        {
            var structure = m_registry.GetOrCreate(s_noDenseTypes, mask);
            if (m_observer != null) structure.Observer = m_observer;

            var (entityId, location) = m_table.Create();
            structure.Append(entityId, location);
            return (entityId, location);
        }

        /// <summary>
        /// Destroys a live entity: invokes <c>OnDestroy</c> on every dense and discrete
        /// component instance at its row (tags carry no lifecycle hooks), swap-removes the
        /// row and releases the entity location back to the pool. Unknown ids are ignored.
        /// </summary>
        public void DestroyEntity(ulong entityId)
        {
            if (!m_table.TryGetLocation(entityId, out var location)) return;

            var structure = location.Structure;
            if (structure != null)
            {
                var row = location.Row;
                var denseTypeIds = structure.DenseTypeIds;
                for (var i = 0; i < denseTypeIds.Count; i++)
                {
                    ComponentHookDispatcher.InvokeDenseDestroy(structure, row, denseTypeIds[i], entityId);
                }

                var spareSet = structure.SpareSetOrNull;
                if (spareSet != null)
                {
                    foreach (var typeId in spareSet.TypeIds)
                    {
                        if (!structure.HasDiscrete(typeId, row)) continue;
                        ComponentHookDispatcher.InvokeDiscreteDestroy(structure, row, typeId, entityId);
                    }
                }

                structure.SwapRemove(row);
            }

            m_table.Destroy(entityId);
        }

        /// <summary>Checks whether a live entity carries the component, by storage kind.</summary>
        public bool HasComponent<T>(ulong entityId) where T : struct, IComponent<T>
        {
            var info = ComponentTypeRegistry.GetOrRegister<T>();
            if (!m_table.TryGetLocation(entityId, out var location)) return false;

            var structure = location.Structure;
            if (structure == null) return false;

            switch (info.Kind)
            {
                case ComponentKind.Dense:
                    return structure.HasDense(info.TypeId);
                case ComponentKind.Discrete:
                    return structure.HasDiscrete(info.TypeId, location.Row);
                case ComponentKind.Tag:
                    return structure.HasTag(info.TypeId, location.Row);
                default:
                    return false;
            }
        }

        /// <summary>
        /// Gets a reference core for the component when present. Dense and discrete refs carry
        /// the instance version; tags return a presence-only core (version 0). Returns null
        /// when the entity is unknown or the component is absent.
        /// </summary>
        public ComponentRefCore GetComponentRef<T>(ulong entityId) where T : struct, IComponent<T>
        {
            var info = ComponentTypeRegistry.GetOrRegister<T>();
            if (!m_table.TryGetLocation(entityId, out var location)) return null;

            var structure = location.Structure;
            if (structure == null) return null;

            switch (info.Kind)
            {
                case ComponentKind.Dense:
                    if (!structure.HasDense(info.TypeId)) return null;
                    return new ComponentRefCore(location, location.Generation, info.TypeId,
                        ComponentKind.Dense, structure.GetDenseVersion(info.TypeId, location.Row));
                case ComponentKind.Discrete:
                    if (!structure.HasDiscrete(info.TypeId, location.Row)) return null;
                    return new ComponentRefCore(location, location.Generation, info.TypeId,
                        ComponentKind.Discrete, structure.GetDiscreteVersion(info.TypeId, location.Row));
                case ComponentKind.Tag:
                    if (!structure.HasTag(info.TypeId, location.Row)) return null;
                    return new ComponentRefCore(location, location.Generation, info.TypeId, ComponentKind.Tag, 0u);
                default:
                    return null;
            }
        }

        /// <summary>
        /// Writes a discrete component at the entity row with a fresh version, notifies the
        /// observer through the structure and invokes <c>OnCreate</c> on the stored instance.
        /// </summary>
        public ComponentRefCore AddDiscreteComponent<T>(ulong entityId, in T value)
            where T : struct, IDiscreteComponent<T>
        {
            var location = RequireLocation(entityId);
            var structure = location.Structure;
            var info = ComponentTypeRegistry.GetOrRegister<T>();
            var version = ComponentVersion.Next();

            ComponentHookDispatcher.RegisterDiscrete<T>();
            structure.SetDiscrete(location.Row, value, version);
            ComponentHookDispatcher.InvokeDiscreteCreate(structure, location.Row, info.TypeId, entityId);
            return new ComponentRefCore(location, location.Generation, info.TypeId, ComponentKind.Discrete, version);
        }

        /// <summary>
        /// Adds a tag to the entity row, notifying the observer through the structure.
        /// Tags carry no data and no lifecycle hooks.
        /// </summary>
        public ComponentRefCore AddTagComponent<T>(ulong entityId) where T : struct, ITagComponent<T>
        {
            var location = RequireLocation(entityId);
            var structure = location.Structure;
            var info = ComponentTypeRegistry.GetOrRegister<T>();

            structure.AddTag(info.TypeId, location.Row);
            return new ComponentRefCore(location, location.Generation, info.TypeId, ComponentKind.Tag, 0u);
        }

        /// <summary>
        /// Invokes <c>OnDestroy</c> on the discrete component instance when present, then
        /// removes it from the entity row and notifies the observer through the structure.
        /// </summary>
        public void RemoveDiscreteComponent<T>(ulong entityId) where T : struct, IDiscreteComponent<T>
        {
            var location = RequireLocation(entityId);
            var structure = location.Structure;
            var info = ComponentTypeRegistry.GetOrRegister<T>();
            if (!structure.HasDiscrete(info.TypeId, location.Row)) return;

            ComponentHookDispatcher.RegisterDiscrete<T>();
            ComponentHookDispatcher.InvokeDiscreteDestroy(structure, location.Row, info.TypeId, entityId);
            structure.RemoveDiscrete(info.TypeId, location.Row);
        }

        /// <summary>Removes a tag from the entity row, notifying the observer through the structure.</summary>
        public void RemoveTagComponent<T>(ulong entityId) where T : struct, ITagComponent<T>
        {
            var location = RequireLocation(entityId);
            var structure = location.Structure;
            var info = ComponentTypeRegistry.GetOrRegister<T>();

            structure.RemoveTag(info.TypeId, location.Row);
        }

        private EntityLocation RequireLocation(ulong entityId)
        {
            if (!m_table.TryGetLocation(entityId, out var location) || location.Structure == null)
            {
                throw new InvalidOperationException($"Entity {entityId} is not alive.");
            }

            return location;
        }
    }
}
