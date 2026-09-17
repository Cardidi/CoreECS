using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CoreECS.Defines;
using CoreECS.Managers;
using CoreECS.Structures;
using CoreECS.Utils;

namespace CoreECS
{
    /// <summary>
    /// Handle to an entity inside a world. Holds the shared pooled
    /// <see cref="EntityLocation"/>, so references follow migrations and
    /// swap-removes automatically.
    /// </summary>
    public readonly struct Entity : IEquatable<Entity>
    {
        private readonly IWorld m_world;
        private readonly ulong m_entityId;
        private readonly EntityLocation m_location;
        private readonly uint m_generation;
        private readonly ComponentManager m_componentManager;

        /// <summary>Internal constructor used by the world integration layer.</summary>
        internal Entity(IWorld world, ulong entityId, EntityLocation location, uint generation)
        {
            m_world = world;
            m_entityId = entityId;
            m_location = location;
            m_generation = generation;
            m_componentManager = world?.GetManager<ComponentManager>();
        }

        /// <summary>World this entity belongs to.</summary>
        /// <exception cref="InvalidOperationException">Thrown for a default entity.</exception>
        public IWorld World => m_world ?? throw new InvalidOperationException("Entity is not associated with any world.");

        /// <summary>Unique id inside its world; safe to copy without the location being alive.</summary>
        public ulong EntityId => m_entityId;

        /// <summary>True while the location is alive and its generation matches this handle.</summary>
        public bool IsValid => m_location != null
                               && m_location.Structure != null
                               && m_location.Generation == m_generation;

        /// <summary>Component mask of the structure currently owning the entity.</summary>
        /// <exception cref="InvalidOperationException">Thrown when the entity is no longer alive.</exception>
        public ulong Mask => RequireLocation().Structure.Mask;

        /// <summary>
        /// Changes the entity mask, migrating the entity into the structure with the same
        /// dense composition and the new mask. Dense data, sparse components and tags are
        /// preserved; no component lifecycle hook runs. Setting the current mask is a no-op.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when the entity is no longer alive or is busy.</exception>
        public void SetMask(ulong mask)
        {
            RequireLocation();
            Orchestrator.SetMask(m_entityId, mask);
        }

        /// <summary>Creates a default-initialized component of type <typeparamref name="T"/>.</summary>
        public ComponentRef<T> CreateComponent<T>() where T : struct, IComponent<T> => CreateComponent(default(T));

        /// <summary>
        /// Creates a component of type <typeparamref name="T"/> with the given value.
        /// Dense components may migrate the entity to another structure; sparse
        /// components overwrite an existing instance; tags ignore the value and return
        /// <c>default</c> (tags carry no data).
        /// </summary>
        public ComponentRef<T> CreateComponent<T>(T component) where T : struct, IComponent<T>
        {
            RequireLocation();
            var orchestrator = Orchestrator;
            var info = ComponentTypeRegistry.GetOrRegister<T>();
            switch (info.Kind)
            {
                case ComponentKind.Dense:
                    return new ComponentRef<T>(orchestrator.AddDenseComponent(m_entityId, component));
                case ComponentKind.Sparse:
                    return new ComponentRef<T>(orchestrator.AddSparseComponent(m_entityId, component));
                case ComponentKind.Tag:
                    orchestrator.AddTagComponent<T>(m_entityId);
                    return default;
                default:
                    throw new InvalidOperationException($"Unsupported component kind: {info.Kind}.");
            }
        }

        /// <summary>Destroys a component referenced by a typed handle.</summary>
        public void DestroyComponent<T>(ComponentRef<T> comp) where T : struct, IComponent<T>
        {
            Assertion.ArgumentNotNull(comp.NotNull ? this : null, "Component is null.");
            Assertion.AreEqual(comp.EntityId, m_entityId, "Component does not belong to this entity.");
            RequireLocation();
            Orchestrator.RemoveComponent(m_entityId, comp.Core.TypeId, comp.Core.Kind);
        }

        /// <summary>Destroys a component referenced by a typeless handle.</summary>
        public void DestroyComponent(ComponentRef comp)
        {
            Assertion.ArgumentNotNull(comp.NotNull ? this : null, "Component is null.");
            Assertion.AreEqual(comp.EntityId, m_entityId, "Component does not belong to this entity.");
            RequireLocation();
            Orchestrator.RemoveComponent(m_entityId, comp.Core.TypeId, comp.Core.Kind);
        }

        /// <summary>Destroys the component of type <typeparamref name="T"/>; throws when absent.</summary>
        public void DestroyComponent<T>() where T : struct, IComponent<T>
        {
            Assertion.IsTrue(HasComponent<T>(), "Entity does not have a component of type T.");
            var orchestrator = Orchestrator;
            var info = ComponentTypeRegistry.GetOrRegister<T>();
            switch (info.Kind)
            {
                case ComponentKind.Dense:
                    orchestrator.RemoveDenseComponent<T>(m_entityId);
                    return;
                case ComponentKind.Sparse:
                    orchestrator.RemoveSparseComponent<T>(m_entityId);
                    return;
                case ComponentKind.Tag:
                    orchestrator.RemoveTagComponent<T>(m_entityId);
                    return;
                default:
                    throw new InvalidOperationException($"Unsupported component kind: {info.Kind}.");
            }
        }

        /// <summary>
        /// Gets a reference to the component of type <typeparamref name="TComp"/>.
        /// Returns <c>default</c> when the component is absent or when
        /// <typeparamref name="TComp"/> is a tag (tags carry no refs).
        /// </summary>
        public ComponentRef<TComp> GetComponent<TComp>() where TComp : struct, IComponent<TComp>
        {
            RequireLocation();
            var info = ComponentTypeRegistry.GetOrRegister<TComp>();
            if (info.Kind == ComponentKind.Tag) return default;

            var core = Orchestrator.GetComponentRef<TComp>(m_entityId);
            return core == null ? default : new ComponentRef<TComp>(core);
        }

        /// <summary>
        /// Gets refs for all dense and sparse components on the entity. Tags are omitted
        /// (no data). Order is dense (type id ascending) then sparse (store enumeration
        /// order) and must not be relied on.
        /// </summary>
        public ComponentRef[] GetComponents()
        {
            var location = RequireLocation();
            var results = new List<ComponentRef>();
            CollectComponents(location, location.Structure, location.Row, results);
            return results.ToArray();
        }

        /// <summary>Adds all dense and sparse refs to <paramref name="results"/> and returns the count.</summary>
        public int GetComponents(ICollection<ComponentRef> results)
        {
            var location = RequireLocation();
            var before = results.Count;
            CollectComponents(location, location.Structure, location.Row, results);
            return results.Count - before;
        }

        /// <summary>Gets the refs of type <typeparamref name="TComp"/>; tags yield an empty array.</summary>
        public ComponentRef<TComp>[] GetComponents<TComp>() where TComp : struct, IComponent<TComp>
        {
            RequireLocation();
            var info = ComponentTypeRegistry.GetOrRegister<TComp>();
            if (info.Kind == ComponentKind.Tag) return Array.Empty<ComponentRef<TComp>>();

            var core = Orchestrator.GetComponentRef<TComp>(m_entityId);
            return core == null
                ? Array.Empty<ComponentRef<TComp>>()
                : new[] { new ComponentRef<TComp>(core) };
        }

        /// <summary>Adds the refs of type <typeparamref name="TComp"/> and returns the count.</summary>
        public int GetComponents<TComp>(ICollection<ComponentRef<TComp>> results)
            where TComp : struct, IComponent<TComp>
        {
            RequireLocation();
            var info = ComponentTypeRegistry.GetOrRegister<TComp>();
            if (info.Kind == ComponentKind.Tag) return 0;

            var core = Orchestrator.GetComponentRef<TComp>(m_entityId);
            if (core == null) return 0;

            results.Add(new ComponentRef<TComp>(core));
            return 1;
        }

        /// <summary>Checks whether the entity carries the component (all three kinds).</summary>
        public bool HasComponent<T>() where T : struct, IComponent<T>
        {
            RequireLocation();
            return Orchestrator.HasComponent<T>(m_entityId);
        }

        private void CollectComponents(
            EntityLocation location, Structure structure, int row, ICollection<ComponentRef> results)
        {
            var orchestrator = Orchestrator;
            var denseTypeIds = structure.DenseTypeIds;
            for (var i = 0; i < denseTypeIds.Count; i++)
            {
                var core = orchestrator.GetComponentRef(m_entityId, denseTypeIds[i], ComponentKind.Dense);
                if (core != null) results.Add(new ComponentRef(core));
            }

            var sparse = structure.SparseOrNull;
            if (sparse == null) return;

            foreach (var typeId in sparse.TypeIds)
            {
                if (!structure.HasSparse(typeId, row)) continue;
                var core = orchestrator.GetComponentRef(m_entityId, typeId, ComponentKind.Sparse);
                if (core != null) results.Add(new ComponentRef(core));
            }
        }

        private EntityLocation RequireLocation()
        {
            if (!IsValid) throw new InvalidOperationException("Entity has already been destroyed.");
            return m_location;
        }

        private ComponentOrchestrator Orchestrator
        {
            get
            {
                var orchestrator = m_componentManager?.Orchestrator;
                if (orchestrator == null)
                {
                    throw new InvalidOperationException("Entity is not associated with a component kernel.");
                }

                return orchestrator;
            }
        }

        #region Equality

        /// <summary>Same world, entity id and generation.</summary>
        public bool Equals(Entity other)
        {
            return ReferenceEquals(m_world, other.m_world)
                   && m_entityId == other.m_entityId
                   && m_generation == other.m_generation;
        }

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is Entity other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode()
        {
            var worldHash = m_world == null ? 0 : RuntimeHelpers.GetHashCode(m_world);
            return HashCode.Combine(worldHash, m_entityId, m_generation);
        }

        public static bool operator ==(Entity left, Entity right) => left.Equals(right);

        public static bool operator !=(Entity left, Entity right) => !left.Equals(right);

        #endregion
    }
}
