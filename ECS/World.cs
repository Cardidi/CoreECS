using System;
using CoreECS.Defines;
using CoreECS.Managers;
using CoreECS.Utils;

namespace CoreECS
{
    /// <summary>
    /// A basic usable world which contains minimal managers to run the ECS system.
    /// This class extends MinimalWorld and provides the core functionality for managing
    /// entities, components, and systems in the ECS framework.
    /// </summary>
    public class World : MinimalWorld
    {
        /// <summary>
        /// Gets the entity match manager responsible for creating entity collectors.
        /// </summary>
        protected EntityMatchManager EntityMatch { get; private set; }
        
        /// <summary>
        /// Gets the entity manager responsible for creating and managing entities.
        /// </summary>
        protected EntityManager Entity { get; private set; }

        /// <summary>
        /// Gets the component manager responsible for managing components.
        /// </summary>
        protected ComponentManager Component { get; private set; }

        /// <summary>
        /// Gets the system manager responsible for managing and executing systems.
        /// </summary>
        protected SystemManager System { get; private set; }
        
        /// <summary>
        /// Registers the core managers required for the ECS system.
        /// </summary>
        /// <param name="register">The manager register interface</param>
        protected override void OnRegisterManager(IManagerRegister register)
        {
            register.RegisterManager<EntityMatchManager>();
            register.RegisterManager<EntityManager>();
            register.RegisterManager<ComponentManager>();
            register.RegisterManager<SystemManager>();
        }

        /// <summary>
        /// Called after all managers have been constructed.
        /// </summary>
        protected override void OnConstruct()
        {}

        /// <summary>
        /// Called after all managers have been started and before OnStart called. Only called for once
        /// Initializes the core managers.
        /// </summary>
        protected override void OnFirstStart()
        {}

        /// <summary>
        /// Called after all managers have been started.
        /// Initializes references to the core managers.
        /// </summary>
        protected override void OnStart()
        {
            EntityMatch = GetManager<EntityMatchManager>();
            Entity = GetManager<EntityManager>();
            Component = GetManager<ComponentManager>();
            System = GetManager<SystemManager>();
        }

        /// <summary>
        /// Called at the beginning of each tick.
        /// Tears down systems to prepare for the new tick.
        /// </summary>
        protected override void OnTickBegin()
        {
            System.TeardownSystems();
        }

        /// <summary>
        /// Called during each tick.
        /// Executes systems based on the tick mask.
        /// </summary>
        /// <param name="tickMask">The tick mask determining which systems to execute</param>
        protected override void OnTick(ulong tickMask)
        {
            System.ExecuteSystems(tickMask);
        }

        /// <summary>
        /// Called at the end of each tick.
        /// Cleans up systems after execution.
        /// </summary>
        protected override void OnTickEnd()
        {
            Component.CleanupComponents();
            System.CleanupSystems();
        }

        /// <summary>
        /// Called when the world is shutting down.
        /// </summary>
        protected override void OnShutdown()
        {}
        
        /// <summary>
        /// Finds a system of the specified type.
        /// </summary>
        /// <typeparam name="T">The type of system to find, must implement ISystem</typeparam>
        /// <returns>The system instance if found, otherwise null</returns>
        public T FindSystem<T>() where T : class, ISystem
        {
            Assertion.IsTrue(Ready, "World is not ready");
            
            if (System != null && System.SystemTransformer.TryGetValue(typeof(T), out var system))
            {
                return (T)system;
            }
            
            return null;
        }

        #region PublicAPI

        /// <summary>
        /// Gets an entity by its ID.
        /// </summary>
        /// <param name="entityId">The ID of the entity to retrieve</param>
        /// <returns>The Entity instance if found, otherwise default(Entity)</returns>
        public Entity GetEntity(ulong entityId)
        {
            if (Entity == null || Component == null)
                throw new InvalidOperationException("Core ECS managers are not available");
            
            var entityGraph = Entity.GetEntity(entityId);
            if (entityGraph != null)
                return new Entity(this, entityId, Entity, Component);

            return default;
        }

        /// <summary>
        /// Creates a new entity in the world.
        /// </summary>
        /// <param name="mask">Optional mask for the entity, defaults to ulong.MaxValue</param>
        /// <returns>A new Entity instance</returns>
        /// <exception cref="InvalidOperationException">Thrown when core ECS managers are not available</exception>
        public Entity CreateEntity(ulong mask = ulong.MaxValue)
        {
            Assertion.IsTrue(Ready, "World is not ready");

            if (Entity == null || Component == null)
                throw new InvalidOperationException("Core ECS managers are not available");
            
            var entityGraph = Entity.CreateEntity(mask); // Default mask
            return new Entity(this, entityGraph.EntityId, Entity, Component);
        }
        
        /// <summary>
        /// Destroys an entity by its ID.
        /// </summary>
        /// <param name="entityId">The ID of the entity to destroy</param>
        /// <exception cref="InvalidOperationException">Thrown when core ECS managers are not available</exception>
        public void DestroyEntity(ulong entityId)
        {
            Assertion.IsTrue(Ready, "World is not ready");

            if (Entity == null || Component == null)
                throw new InvalidOperationException("Core ECS managers are not available");
            
            Entity.DestroyEntity(entityId);
        }
        
        /// <summary>
        /// Destroys an entity.
        /// </summary>
        /// <param name="entity">The entity to destroy</param>
        public void DestroyEntity(Entity entity)
        {
            Assertion.IsTrue(Ready, "World is not ready");
            
            if (entity.IsValid)
            {
                DestroyEntity(entity.EntityId);
            }
        }

        /// <summary>
        /// Registers a system with the world.
        /// </summary>
        /// <param name="systemType">The type of system to register</param>
        /// <exception cref="InvalidOperationException">Thrown when system manager is not available</exception>
        public void RegisterSystem(Type systemType)
        {
            Assertion.IsTrue(Ready, "World is not ready");

            if (System == null)
                throw new InvalidOperationException("Core ECS managers are not available");
            
            System.RegisterSystem(systemType);
            
        }

        /// <summary>
        /// Registers a system with the world.
        /// </summary>
        /// <typeparam name="T">The type of system to register, must implement ISystem</typeparam>
        /// <exception cref="InvalidOperationException">Thrown when system manager is not available</exception>
        public void RegisterSystem<T>() where T : class, ISystem
        {
            Assertion.IsTrue(Ready, "World is not ready");

            if (System == null)
                throw new InvalidOperationException("Core ECS managers are not available");
            
            System.RegisterSystem(typeof(T));
            
        }

        /// <summary>
        /// Unregisters a system from the world.
        /// </summary>
        /// <param name="systemType">The type of system to unregister</param>
        /// <exception cref="InvalidOperationException">Thrown when system manager is not available</exception>
        public void UnregisterSystem(Type systemType)
        {
            Assertion.IsTrue(Ready, "World is not ready");

            if (System == null)
                throw new InvalidOperationException("Core ECS managers are not available");
            
            System.UnregisterSystem(systemType);
        }
        
        /// <summary>
        /// Unregisters a system from the world.
        /// </summary>
        /// <typeparam name="T">The type of system to unregister, must implement ISystem</typeparam>
        /// <exception cref="InvalidOperationException">Thrown when system manager is not available</exception>
        public void UnregisterSystem<T>() where T : class, ISystem
        {
            Assertion.IsTrue(Ready, "World is not ready");

            if (System == null)
                throw new InvalidOperationException("Core ECS managers are not available");
            
            System.UnregisterSystem(typeof(T));
        }

        /// <summary>
        /// Creates a new entity collector with the specified matcher and flag.
        /// </summary>
        /// <param name="matcher">The entity matcher to use for filtering entities</param>
        /// <param name="flag">The flag to use for the collector</param>
        /// <returns>A new IEntityCollector instance</returns>
        /// <exception cref="InvalidOperationException">Thrown when EntityMatch manager is not available</exception>
        public IEntityCollector CreateCollector(IEntityMatcher matcher,
            EntityCollectorFlag flag = EntityCollectorFlag.Lazy)
        {
            Assertion.IsTrue(Ready, "World is not ready");

            if (EntityMatch == null)
                throw new InvalidOperationException("Core ECS managers are not available");
            
            return EntityMatch.MakeCollector(flag, matcher);
        }

        #endregion
    }
}