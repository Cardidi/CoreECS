using System;
using System.Collections.Generic;
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
        private readonly HashSet<ulong> m_destroying = new();
        private readonly HashSet<ulong> m_mutating = new();

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
        /// Re-entrant destroys of the same entity from a hook are no-ops; hooks that destroy
        /// other entities or create discrete stores are tolerated (store ids are snapshotted
        /// and the final row is re-read from the location binding).
        /// </summary>
        public void DestroyEntity(ulong entityId)
        {
            if (!m_table.TryGetLocation(entityId, out var location)) return;
            if (m_mutating.Contains(entityId))
            {
                throw new InvalidOperationException($"Entity {entityId} is being mutated.");
            }
            if (!m_destroying.Add(entityId)) return;

            try
            {
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
                        // Snapshot the store ids: a hook may create new stores on this structure.
                        var discreteTypeIds = new List<uint>(spareSet.TypeIds);
                        for (var i = 0; i < discreteTypeIds.Count; i++)
                        {
                            var typeId = discreteTypeIds[i];
                            if (!structure.HasDiscrete(typeId, row)) continue;
                            ComponentHookDispatcher.InvokeDiscreteDestroy(structure, row, typeId, entityId);
                        }
                    }
                }

                // Re-read the binding: re-entrant destroys keep the location's row current.
                location.Structure?.SwapRemove(location.Row);
            }
            finally
            {
                m_destroying.Remove(entityId);
                m_table.Destroy(entityId);
            }
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
        /// Adding over an existing instance overwrites the value with a fresh version and
        /// fires <c>OnCreate</c> again; no implicit <c>OnDestroy</c> is raised.
        /// </summary>
        public ComponentRefCore AddDiscreteComponent<T>(ulong entityId, in T value)
            where T : struct, IComponent<T>
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
        public ComponentRefCore AddTagComponent<T>(ulong entityId) where T : struct, IComponent<T>
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
        public void RemoveDiscreteComponent<T>(ulong entityId) where T : struct, IComponent<T>
        {
            var typeId = ComponentTypeRegistry.GetOrRegister<T>().TypeId;
            ComponentHookDispatcher.RegisterDiscrete<T>();
            RemoveDiscreteComponentCore(entityId, typeId);
        }

        /// <summary>
        /// Removes a discrete component by type id: runs <c>OnDestroy</c> under the mutation
        /// guard (re-entrant mutation or destroy of the entity from the hook is rejected),
        /// re-reads the row after the hook (other entities may have shifted it) and removes
        /// the instance when it is still present.
        /// </summary>
        private void RemoveDiscreteComponentCore(ulong entityId, uint typeId)
        {
            var location = RequireLocation(entityId);
            var structure = location.Structure;
            if (!structure.HasDiscrete(typeId, location.Row)) return;

            m_mutating.Add(entityId);
            try
            {
                ComponentHookDispatcher.InvokeDiscreteDestroy(structure, location.Row, typeId, entityId);
            }
            finally
            {
                m_mutating.Remove(entityId);
            }

            structure = location.Structure;
            if (structure == null || !structure.HasDiscrete(typeId, location.Row)) return;
            structure.RemoveDiscrete(typeId, location.Row);
        }

        /// <summary>Removes a tag from the entity row, notifying the observer through the structure.</summary>
        public void RemoveTagComponent<T>(ulong entityId) where T : struct, IComponent<T>
        {
            var location = RequireLocation(entityId);
            var structure = location.Structure;
            var info = ComponentTypeRegistry.GetOrRegister<T>();

            structure.RemoveTag(info.TypeId, location.Row);
        }

        /// <summary>
        /// Adds a dense component to a live entity by migrating its row into the structure
        /// whose key gains <typeparamref name="T"/>: copies dense data shared with the target,
        /// tags and discrete components, writes the value with a fresh version, swap-removes
        /// the source row, reports the addition to the observer sink with the target row and
        /// invokes <c>OnCreate</c> on the stored instance.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the entity is not alive, or already carries the dense component.
        /// v1's create-or-get path checked presence before creating; the archetype model
        /// cannot represent two instances of the same dense type, so a duplicate add is
        /// an explicit error rather than a silent second instance.
        /// </exception>
        public ComponentRefCore AddDenseComponent<T>(ulong entityId, in T value)
            where T : struct, IComponent<T>
        {
            var location = RequireLocation(entityId);
            var current = location.Structure;
            var info = ComponentTypeRegistry.GetOrRegister<T>();
            if (current.HasDense(info.TypeId))
            {
                throw new InvalidOperationException(
                    $"Entity {entityId} already has dense component {typeof(T).Name}.");
            }

            var targetKey = new StructureKey(
                StructureKey.AddType(current.Key.ToArray(), info.TypeId), current.Mask);
            var target = m_registry.GetOrCreate(targetKey);
            if (m_observer != null) target.Observer = m_observer;

            var sourceRow = location.Row;
            var targetRow = target.Append(entityId, location);
            current.CopyDenseTo(target, sourceRow, targetRow);
            current.CopyTagsTo(target, sourceRow, targetRow);
            current.MoveDiscreteTo(target, sourceRow, targetRow);

            var version = ComponentVersion.Next();
            target.SetDenseValue(targetRow, value, version);
            current.SwapRemove(sourceRow);

            ComponentHookDispatcher.RegisterDense<T>();
            m_observer?.OnComponentAdded(target, targetRow, info.TypeId);
            ComponentHookDispatcher.InvokeDenseCreate(target, targetRow, info.TypeId, entityId);

            return new ComponentRefCore(location, location.Generation, info.TypeId, ComponentKind.Dense, version);
        }

        /// <summary>
        /// Removes a dense component by type id: invokes <c>OnDestroy</c> while the old
        /// value is still stored, under the mutation guard so the hook cannot destroy or
        /// migrate the entity; re-reads the row after the hook (other entities may have
        /// shifted it) and migrates the row into the structure without the type,
        /// reporting the removal to the observer sink with the target row.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the entity is not alive, or does not carry the dense component.
        /// </exception>
        private void RemoveDenseComponentCore(ulong entityId, uint typeId)
        {
            var location = RequireLocation(entityId);
            var current = location.Structure;
            if (!current.HasDense(typeId))
            {
                throw new InvalidOperationException(
                    $"Entity {entityId} does not have dense component type id {typeId}.");
            }

            m_mutating.Add(entityId);
            try
            {
                ComponentHookDispatcher.InvokeDenseDestroy(current, location.Row, typeId, entityId);
            }
            finally
            {
                m_mutating.Remove(entityId);
            }

            current = location.Structure;
            if (current == null || !current.HasDense(typeId)) return;

            var sourceRow = location.Row;
            var targetKey = new StructureKey(
                StructureKey.RemoveType(current.Key.ToArray(), typeId), current.Mask);
            var target = m_registry.GetOrCreate(targetKey);
            if (m_observer != null) target.Observer = m_observer;

            var targetRow = target.Append(entityId, location);
            current.CopyDenseTo(target, sourceRow, targetRow);
            current.CopyTagsTo(target, sourceRow, targetRow);
            current.MoveDiscreteTo(target, sourceRow, targetRow);
            current.SwapRemove(sourceRow);

            m_observer?.OnComponentRemoved(target, targetRow, typeId);
        }

        /// <summary>
        /// Removes a dense component from a live entity. The registered hook is invoked
        /// while the old value is still stored; see <see cref="RemoveDenseComponentCore"/>.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the entity is not alive, or does not carry the dense component;
        /// v1 <c>DestroyComponent&lt;T&gt;()</c> asserted presence the same way.
        /// </exception>
        public void RemoveDenseComponent<T>(ulong entityId) where T : struct, IComponent<T>
        {
            var typeId = ComponentTypeRegistry.GetOrRegister<T>().TypeId;
            ComponentHookDispatcher.RegisterDense<T>();
            RemoveDenseComponentCore(entityId, typeId);
        }

        /// <summary>
        /// Removes a component by type id and kind. Dense and discrete removals run their
        /// lifecycle hooks under the entity mutation guard and re-read the row afterwards
        /// (see the core helpers); tag removal clears the bit and ignores absent tags.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when a dense component is absent, matching <see cref="RemoveDenseComponent{T}"/>.
        /// </exception>
        public void RemoveComponent(ulong entityId, uint typeId, ComponentKind kind)
        {
            switch (kind)
            {
                case ComponentKind.Dense:
                    RemoveDenseComponentCore(entityId, typeId);
                    return;

                case ComponentKind.Discrete:
                    RemoveDiscreteComponentCore(entityId, typeId);
                    return;

                case ComponentKind.Tag:
                    var location = RequireLocation(entityId);
                    location.Structure.RemoveTag(typeId, location.Row);
                    return;
            }
        }

        private EntityLocation RequireLocation(ulong entityId)
        {
            if (m_destroying.Contains(entityId) || m_mutating.Contains(entityId))
            {
                throw new InvalidOperationException($"Entity {entityId} is busy.");
            }

            if (!m_table.TryGetLocation(entityId, out var location) || location.Structure == null)
            {
                throw new InvalidOperationException($"Entity {entityId} is not alive.");
            }

            return location;
        }
    }
}
