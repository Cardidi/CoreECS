using System;
using System.Collections.Generic;
using CoreECS.Defines;
using CoreECS.Structures;
using CoreECS.Utils;

namespace CoreECS.Managers
{
    /// <summary>
    /// Delegate for entity component acquisition events.
    /// </summary>
    /// <param name="entityId">The ID of the entity that acquired a component</param>
    /// <param name="componentType">The type of the component that was added</param>
    public delegate void EntityGetComponent(ulong entityId, Type componentType);

    /// <summary>
    /// Delegate for entity component loss events. <paramref name="componentType"/> is null
    /// when the entity itself was destroyed (the kernel raises no per-component events then).
    /// </summary>
    /// <param name="entityId">The ID of the entity that lost a component</param>
    /// <param name="componentType">The type of the component that was removed</param>
    public delegate void EntityLoseComponent(ulong entityId, Type componentType);

    /// <summary>
    /// Delegate for entity component revision change events.
    /// </summary>
    /// <param name="entityId">The ID of the entity whose component changed</param>
    /// <param name="componentType">The type of the component that changed</param>
    public delegate void EntityChangeComponent(ulong entityId, Type componentType);

    /// <summary>
    /// Manages entities in the world over the v2 kernel: owns the entity table, creates and
    /// destroys entities through the orchestrator, and re-emits component-level signals as
    /// entity-level signals. The v1 entity-graph payload was removed with v1 storage; signals
    /// now carry the entity id instead of the pooled graph.
    /// </summary>
    public sealed class EntityManager : IWorldManager
    {
        private static readonly Emitter<EntityGetComponent, ulong, Type> s_gotEmitter =
            static (h, entityId, componentType) => h(entityId, componentType);

        private static readonly Emitter<EntityLoseComponent, ulong, Type> s_loseEmitter =
            static (h, entityId, componentType) => h(entityId, componentType);

        private static readonly Emitter<EntityChangeComponent, ulong, Type> s_changeEmitter =
            static (h, entityId, componentType) => h(entityId, componentType);

        /// <summary>Gets the world this manager belongs to.</summary>
        public IWorld World { get; }

        /// <summary>Event triggered when an entity gets a component.</summary>
        public Signal<EntityGetComponent> OnEntityGotComp { get; } = new();

        /// <summary>Event triggered when an entity loses a component or is destroyed.</summary>
        public Signal<EntityLoseComponent> OnEntityLoseComp { get; } = new();

        /// <summary>Event triggered when one of an entity's components changes revision.</summary>
        public Signal<EntityChangeComponent> OnEntityChangeComp { get; } = new();

        private readonly ComponentManager m_compManager;
        private readonly EntityTable m_table = new();
        private readonly HashSet<ulong> m_destroying = new();
        private EntityMatchManager m_matchManager;
        private bool m_init;
        private bool m_shutdown;

        /// <summary>
        /// Connects the match manager so revision changes bypass the public
        /// <see cref="OnEntityChangeComp"/> signal when nobody subscribes to it.
        /// Wired by <see cref="World.Startup"/>.
        /// </summary>
        /// <param name="matchManager">The match manager owned by the same world</param>
        internal void ConnectMatchManager(EntityMatchManager matchManager)
        {
            m_matchManager = matchManager;
            matchManager.RevisionInterestChanged = _refreshChangeInterest;
            _refreshChangeInterest();
        }

        /// <summary>
        /// Republishes this manager's revision-change interest to the component manager,
        /// which caches it on the structures so writers can skip the observer chain when
        /// nothing listens.
        /// </summary>
        private void _refreshChangeInterest()
        {
            var any = OnEntityChangeComp.HasReceivers || (m_matchManager?.HasRevisionInterest ?? false);
            var mutating = OnEntityChangeComp.HasReceivers;
            m_compManager?.SetSinkInterest(any, mutating);
        }

        /// <summary>
        /// Handles a component revision change forwarded by the component manager
        /// (bypassing the public signal chain).
        /// </summary>
        /// <param name="entityId">The entity that owns the component</param>
        /// <param name="typeId">The id of the component type that changed</param>
        /// <param name="location">The owning entity's pooled location</param>
        internal void OnRevisionChanged(ulong entityId, uint typeId, EntityLocation location)
        {
            if (OnEntityChangeComp.HasReceivers)
            {
                OnEntityChangeComp.Emit(entityId, ComponentTypeRegistry.GetById(typeId).Type, s_changeEmitter);
            }

            m_matchManager?.OnRevisionChanged(entityId, typeId, location);
        }

        /// <summary>Kernel entity registry (internal test/debug access).</summary>
        internal EntityTable Table => m_table;

        private ComponentOrchestrator Orchestrator => m_compManager.Orchestrator;

        /// <summary>
        /// Creates a new entity with the specified mask.
        /// </summary>
        /// <param name="mask">The component mask for the new entity</param>
        /// <returns>The entity handle for the newly created entity</returns>
        public Entity CreateEntity(ulong mask = ulong.MaxValue)
        {
            Assertion.IsTrue(m_init);
            Assertion.IsFalse(m_shutdown);

            var (entityId, location) = Orchestrator.CreateEntity(mask);
            return new Entity(World, entityId, location, location.Generation);
        }

        /// <summary>
        /// Gets the entity handle for a live entity id.
        /// </summary>
        /// <param name="entityId">The ID of the entity to retrieve</param>
        /// <returns>The entity handle, or default when the id is not live</returns>
        public Entity GetEntity(ulong entityId)
        {
            Assertion.IsTrue(m_init);
            Assertion.IsFalse(m_shutdown);

            if (!m_table.TryGetLocation(entityId, out var location) || location.Structure == null) return default;

            return new Entity(World, entityId, location, location.Generation);
        }

        /// <summary>
        /// Destroys the entity with the specified ID. Unknown ids are ignored. Component
        /// lifecycle hooks run inside the orchestrator; a single entity-lost event with a
        /// null component type is emitted afterwards.
        /// </summary>
        /// <param name="entityId">The ID of the entity to destroy</param>
        public void DestroyEntity(ulong entityId)
        {
            Assertion.IsTrue(m_init);
            Assertion.IsFalse(m_shutdown);

            if (!m_table.TryGetLocation(entityId, out _)) return;
            if (!m_destroying.Add(entityId)) return;

            try
            {
                Orchestrator.DestroyEntity(entityId);
            }
            finally
            {
                m_destroying.Remove(entityId);
            }

            OnEntityLoseComp.Emit(entityId, null, s_loseEmitter);
        }

        /// <summary>Handles component addition events.</summary>
        private void _onComponentAdded(ulong entityId, Type compType)
        {
            OnEntityGotComp.Emit(entityId, compType, s_gotEmitter);
        }

        /// <summary>Handles component removal events.</summary>
        private void _onComponentRemoved(ulong entityId, Type compType)
        {
            OnEntityLoseComp.Emit(entityId, compType, s_loseEmitter);
        }

        /// <summary>Called when the manager is created.</summary>
        public void OnManagerCreated()
        {
            m_compManager.OnComponentCreated.Add(_onComponentAdded);
            m_compManager.OnComponentRemoved.Add(_onComponentRemoved);

            m_init = true;
        }

        /// <summary>Called when the world starts.</summary>
        public void OnWorldStarted() {}

        /// <summary>Called when the world ends.</summary>
        public void OnWorldEnded() {}

        /// <summary>Called when the manager is destroyed.</summary>
        public void OnManagerDestroyed()
        {
            m_shutdown = true;
            m_table.Clear();

            m_compManager.OnComponentCreated.Remove(_onComponentAdded);
            m_compManager.OnComponentRemoved.Remove(_onComponentRemoved);
            OnEntityChangeComp.ReceiversChanged = null;
            if (m_matchManager != null) m_matchManager.RevisionInterestChanged = null;
            m_compManager.SetSinkInterest(false, false);
            m_matchManager = null;
        }

        /// <summary>
        /// Initializes a new instance of the EntityManager class and injects the orchestrator
        /// (over the shared structure registry and this manager's table) into
        /// <paramref name="compManager"/>.
        /// </summary>
        /// <param name="world">The world this manager belongs to</param>
        /// <param name="compManager">The component manager owning the kernel</param>
        public EntityManager(IWorld world, ComponentManager compManager)
        {
            World = world;
            m_compManager = compManager;
            compManager.Orchestrator = new ComponentOrchestrator(compManager.Structures, m_table, compManager.Observer);
            OnEntityChangeComp.ReceiversChanged = _refreshChangeInterest;
        }
    }
}
