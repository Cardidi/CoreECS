using System;
using System.Collections.Generic;
using CoreECS.Defines;
using CoreECS.Managers;
using CoreECS.Utils;

namespace CoreECS
{
    /// <summary>
    /// Represents an entity in the ECS world.
    /// An entity is essentially a container for components and serves as an identifier
    /// for game objects. It doesn't contain data itself but holds references to components.
    /// </summary>
    public readonly struct Entity
    {

        #region Internals

        /// <summary>
        /// Reference to the world this entity belongs to.
        /// </summary>
        private readonly IWorld m_world;
        
        /// <summary>
        /// Unique identifier for this entity within its world.
        /// </summary>
        private readonly ulong m_entityId;

        /// <summary>
        /// The generation of the EntityGraph at the time this Entity was created.
        /// Used to detect if the EntityGraph has been recycled.
        /// </summary>
        private readonly uint m_generation;

        // Cache entity manager and component manager to avoid querying world multiple times.
        
        /// <summary>
        /// Cached reference to the entity manager for faster access.
        /// </summary>
        private readonly EntityManager m_entityManager;

        /// <summary>
        /// Cached reference to the component manager for faster access.
        /// </summary>
        private readonly ComponentManager m_componentManager;

        /// <summary>
        /// Helper method to access the entity graph for this entity with generation validation.
        /// The entity graph tracks the entity's components and their relationships.
        /// </summary>
        /// <returns>The entity graph for this entity</returns>
        /// <exception cref="InvalidOperationException">Thrown when the entity has been destroyed or the EntityGraph has been recycled</exception>
        private EntityGraph _accessGraph()
        {
            if (m_entityManager?.EntityCaches.TryGetValue(m_entityId, out var graph) ?? false)
            {
                if (graph.Generation != m_generation)
                {
                    throw new InvalidOperationException(
                        $"EntityGraph has been recycled. Entity {m_entityId} no longer references the original EntityGraph instance. " +
                        $"Expected generation {m_generation}, but current is {graph.Generation}.");
                }
                return graph;
            }
            
            throw new InvalidOperationException("Entity has already been destroyed.");
        }
        
        /// <summary>
        /// Helper method to access the component manager for this entity with generation validation.
        /// </summary>
        /// <returns>The component manager</returns>
        /// <exception cref="InvalidOperationException">Thrown when the entity is not associated with any world or EntityGraph has been recycled</exception>
        private ComponentManager _accessComponentManager()
        {
            if (m_componentManager != null && (m_entityManager?.EntityCaches.TryGetValue(m_entityId, out var graph) ?? false))
            {
                if (graph.Generation != m_generation)
                {
                    throw new InvalidOperationException(
                        $"EntityGraph has been recycled. Entity {m_entityId} no longer references the original EntityGraph instance. " +
                        $"Expected generation {m_generation}, but current is {graph.Generation}.");
                }
                return m_componentManager;
            }
            
            throw new InvalidOperationException("Entity is not associated with any world.");
        }

        #endregion

        /// <summary>
        /// Gets the world this entity belongs to.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when the entity is not associated with any world</exception>
        public IWorld World => m_world ?? throw new InvalidOperationException("Entity is not associated with any world.");

        /// <summary>
        /// Gets a value indicating whether this entity is still valid (not destroyed and not recycled).
        /// </summary>
        public bool IsValid
        {
            get
            {
                if (m_world == null) return false;
                var entityManager = m_world.GetManager<EntityManager>();
                if (!entityManager.EntityCaches.TryGetValue(m_entityId, out var graph)) return false;
                return graph.Generation == m_generation;
            }
        }

        /// <summary>
        /// Gets the unique identifier for this entity.
        /// </summary>
        public ulong EntityId => m_entityId;

        /// <summary>
        /// Gets the component mask for this entity.
        /// The mask is a bitmask that represents which component types this entity has.
        /// </summary>
        public ulong Mask => _accessGraph().Mask;
        
        /// <summary>
        /// Creates a new component of type T and attaches it to this entity.
        /// </summary>
        /// <typeparam name="T">Component type to create, must be a struct implementing IComponent&lt;T&gt;</typeparam>
        /// <returns>A reference to the newly created component</returns>
        public ComponentRef<T> CreateComponent<T>() where T : struct, IComponent<T>
        {
            var compRef = _accessComponentManager().CreateComponent<T>(m_entityId);
            return new ComponentRef<T>(compRef);
        }
        
        /// <summary>
        /// Creates a new component of type T and attaches it to this entity.
        /// </summary>
        /// <param name="component">The initial value for the component</param>
        /// <typeparam name="T">Component type to create, must be a struct implementing IComponent&lt;T&gt;</typeparam>
        /// <returns>A reference to the newly created component</returns>
        public ComponentRef<T> CreateComponent<T>(T component) where T : struct, IComponent<T>
        {
            var compRef = _accessComponentManager().CreateComponent<T>(m_entityId, component);
            return new ComponentRef<T>(compRef);
        }
        
        /// <summary>
        /// Destroys a component of type T attached to this entity.
        /// </summary>
        /// <typeparam name="T">Component type to destroy, must be a struct implementing IComponent&lt;T&gt;</typeparam>
        /// <param name="comp">Reference to the component to destroy</param>
        public void DestroyComponent<T>(ComponentRef<T> comp) where T : struct, IComponent<T>
        {
            Assertion.ArgumentNotNull(comp.NotNull ? this : null, "Component is null.");
            Assertion.AreEqual(comp.EntityId, m_entityId, "Component does not belong to this entity.");
            _accessComponentManager().DestroyComponent(comp.Core);
        }
        
        /// <summary>
        /// Destroys a component attached to this entity.
        /// </summary>
        /// <param name="comp">Typeless reference to the component to destroy</param>
        public void DestroyComponent(ComponentRef comp)
        {
            Assertion.ArgumentNotNull(comp.NotNull ? this : null, "Component is null.");
            Assertion.AreEqual(comp.EntityId, m_entityId, "Component does not belong to this entity.");
            _accessComponentManager().DestroyComponent(comp.Core);
        }

        /// <summary>
        /// Destroys a component of type T attached to this entity.
        /// </summary>
        /// <typeparam name="T">Component type to destroy, must be a struct implementing IComponent&lt;T&gt;</typeparam>
        public void DestroyComponent<T>() where T : struct, IComponent<T>
        {
            var component = _accessGraph().GetComponent<T>();
            Assertion.IsTrue(component.NotNull, "Entity does not have a component of type T.");
            _accessComponentManager().DestroyComponent(component.Core);
        }
        
        /// <summary>
        /// Gets a reference to a component of type T attached to this entity.
        /// </summary>
        /// <typeparam name="TComp">Component type to retrieve, must be a struct implementing IComponent&lt;TComp&gt;</typeparam>
        /// <returns>A typed reference to the component</returns>
        public ComponentRef<TComp> GetComponent<TComp>() where TComp : struct, IComponent<TComp>
        {
            return _accessGraph().GetComponent<TComp>();
        }

        /// <summary>
        /// Gets all components attached to this entity.
        /// </summary>
        /// <returns>An array of typeless component references</returns>
        public ComponentRef[] GetComponents()
        {
            return _accessGraph().GetComponents();
        }

        /// <summary>
        /// Gets all components attached to this entity and adds them to the specified collection.
        /// </summary>
        /// <param name="results">Collection to add component references to</param>
        /// <returns>The number of components added to the collection</returns>
        public int GetComponents(ICollection<ComponentRef> results)
        {
            return _accessGraph().GetComponents(results);
        }

        /// <summary>
        /// Gets all components of type TComp attached to this entity.
        /// </summary>
        /// <typeparam name="TComp">Component type to retrieve, must be a struct implementing IComponent&lt;TComp&gt;</typeparam>
        /// <returns>An array of typed component references</returns>
        public ComponentRef<TComp>[] GetComponents<TComp>() where TComp : struct, IComponent<TComp>
        {
            return _accessGraph().GetComponents<TComp>();
        }

        /// <summary>
        /// Gets all components of type TComp attached to this entity and adds them to the specified collection.
        /// </summary>
        /// <typeparam name="TComp">Component type to retrieve, must be a struct implementing IComponent&lt;TComp&gt;</typeparam>
        /// <param name="results">Collection to add component references to</param>
        /// <returns>The number of components added to the collection</returns>
        public int GetComponents<TComp>(ICollection<ComponentRef<TComp>> results) where TComp : struct, IComponent<TComp>
        {
            return _accessGraph().GetComponents(results);
        }
        
        /// <summary>
        /// Checks if this entity has a component of type T.
        /// </summary>
        /// <typeparam name="T">Component type to check for, must be a struct implementing IComponent&lt;T&gt;</typeparam>
        /// <returns>True if the entity has the component, false otherwise</returns>
        public bool HasComponent<T>() where T : struct, IComponent<T>
        {
            return _accessGraph().HasComponent<T>();
        }

        /// <summary>
        /// Internal constructor used by the ECS framework to create an entity.
        /// </summary>
        /// <param name="world">The world this entity belongs to</param>
        /// <param name="entityId">Unique identifier for this entity</param>
        /// <param name="generation">The generation of the EntityGraph at creation time</param>
        /// <param name="entityManager">Optional cached entity manager</param>
        /// <param name="componentManager">Optional cached component manager</param>
        public Entity(IWorld world, ulong entityId, uint generation, EntityManager entityManager = null, ComponentManager componentManager = null)
        {
            m_world = world;
            m_entityId = entityId;
            m_generation = generation;
            m_entityManager = entityManager ?? world.GetManager<EntityManager>();
            m_componentManager = componentManager ?? world.GetManager<ComponentManager>();
        }
    }
}