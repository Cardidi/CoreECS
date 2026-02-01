using System;
using System.Collections.Generic;
using CoreECS.Defines;
using CoreECS.Utils;

namespace CoreECS.Managers
{
    /// <summary>
    /// Delegate for entity component acquisition events.
    /// </summary>
    /// <param name="entityGraph">The entity graph that acquired a component</param>
    public delegate void EntityGetComponent(EntityGraph entityGraph);
    
    /// <summary>
    /// Delegate for entity component loss events.
    /// </summary>
    /// <param name="entityGraph">The entity graph that lost a component</param>
    public delegate void EntityLoseComponent(EntityGraph entityGraph);
    
    /// <summary>
    /// Manages entities in the world.
    /// This class is responsible for creating, destroying, and tracking entities in the ECS system.
    /// </summary>
    public sealed class EntityManager : IWorldManager
    {
        /// <summary>
        /// Gets the world this manager belongs to.
        /// </summary>
        public IWorld World { get; }
        
        /// <summary>
        /// Event triggered when an entity gets a component.
        /// </summary>
        public Signal<EntityGetComponent> OnEntityGotComp { get; } = new();
        
        /// <summary>
        /// Event triggered when an entity loses a component.
        /// </summary>
        public Signal<EntityLoseComponent> OnEntityLoseComp { get; } = new();
        
        /// <summary>
        /// The next available entity ID.
        /// </summary>
        private ulong m_allocatedId = 0;

        /// <summary>
        /// Indicates whether the manager has been initialized.
        /// </summary>
        private bool m_init = false;

        /// <summary>
        /// Indicates whether the manager is shutting down.
        /// </summary>
        private bool m_shutdown = false;
        
        /// <summary>
        /// Reference to the component manager for handling component events.
        /// </summary>
        private ComponentManager m_compManager;
        
        /// <summary>
        /// Dictionary mapping entity IDs to their entity graphs.
        /// </summary>
        private readonly Dictionary<ulong, EntityGraph> m_entityCaches = new();

        /// <summary>
        /// Gets a read-only view of the entity caches.
        /// </summary>
        public IReadOnlyDictionary<ulong, EntityGraph> EntityCaches => m_entityCaches;
        
        /// <summary>
        /// Creates a new entity with the specified mask.
        /// </summary>
        /// <param name="mask">The component mask for the new entity</param>
        /// <returns>The entity graph for the newly created entity</returns>
        /// <exception cref="ApplicationException">Thrown when the maximum number of entities has been reached</exception>
        public EntityGraph CreateEntity(ulong mask)
        {
            Assertion.IsTrue(m_init);
            Assertion.IsFalse(m_shutdown);
            
            if (m_allocatedId == ulong.MaxValue) throw new ApplicationException(
                "No more entities can being allocated! Please consider restart application...");
            
            var id = ++m_allocatedId;
            var graph = EntityGraph.Pool.Get();
            m_entityCaches.Add(id, graph);
            graph.Mask = mask;
            graph.EntityId = id;
            graph.WishDestroy = false;
            
            return graph;
        }
        
        /// <summary>
        /// Gets the entity graph for the specified entity ID.
        /// </summary>
        /// <param name="entityId">The ID of the entity to retrieve</param>
        /// <returns>The entity graph for the specified entity, or null if not found</returns>
        public EntityGraph GetEntity(ulong entityId)
        {
            Assertion.IsTrue(m_init);
            Assertion.IsFalse(m_shutdown);
            
            if (m_entityCaches.TryGetValue(entityId, out var graph))
                return graph;
            
            return null;
        }
        
        /// <summary>
        /// Destroys the entity with the specified ID.
        /// </summary>
        /// <param name="entityId">The ID of the entity to destroy</param>
        public void DestroyEntity(ulong entityId)
        {
            Assertion.IsTrue(m_init);
            Assertion.IsFalse(m_shutdown);
            
            if (m_entityCaches.Remove(entityId, out var graph))
            {
                graph.RwComponents.Clear();
                graph.WishDestroy = true;
                OnEntityLoseComp.Emit(in graph, static (h, c) => h(c));
                EntityGraph.Pool.Release(graph);
            }
        }

        /// <summary>
        /// Handles component addition events.
        /// </summary>
        /// <param name="component">The component that was added</param>
        /// <param name="entityId">The ID of the entity that received the component</param>
        private void _onComponentAdded(IComponentRefCore component, ulong entityId)
        {
            var gs = GetEntity(entityId);
            if (gs == null) return;
            
            gs.RwComponents.Add(component);
            
            OnEntityGotComp.Emit(in gs, static (h, c) => h(c));
        }
        
        /// <summary>
        /// Handles component removal events.
        /// </summary>
        /// <param name="component">The component that was removed</param>
        /// <param name="entityId">The ID of the entity that lost the component</param>
        private void _onComponentRemoved(IComponentRefCore component, ulong entityId)
        {
            var gs = GetEntity(entityId);
            if (gs == null) return;
            gs.RwComponents.Remove(component);
            
            OnEntityLoseComp.Emit(in gs, static (h, c) => h(c));
        }

        /// <summary>
        /// Called when the manager is created.
        /// </summary>
        public void OnManagerCreated()
        {
            m_compManager.OnComponentCreated.Add(_onComponentAdded);
            m_compManager.OnComponentRemoved.Add(_onComponentRemoved);

            m_init = true;
        }

        /// <summary>
        /// Called when the world starts.
        /// </summary>
        public void OnWorldStarted() {}

        /// <summary>
        /// Called when the world ends.
        /// </summary>
        public void OnWorldEnded() {}

        /// <summary>
        /// Called when the manager is destroyed.
        /// </summary>
        public void OnManagerDestroyed()
        {
            m_shutdown = true;
            
            m_compManager.OnComponentCreated.Remove(_onComponentAdded);
            m_compManager.OnComponentRemoved.Remove(_onComponentRemoved);
            
            foreach (var ec in m_entityCaches.Values)
            {
                ec.RwComponents.Clear();
                EntityGraph.Pool.Release(ec);
            }
            
            m_entityCaches.Clear();
        }

        /// <summary>
        /// Initializes a new instance of the EntityManager class.
        /// </summary>
        /// <param name="world">The world this manager belongs to</param>
        /// <param name="compManager">The component manager for handling component events</param>
        public EntityManager(IWorld world, ComponentManager compManager)
        {
            World = world;
            m_compManager = compManager;
        }
    }
}