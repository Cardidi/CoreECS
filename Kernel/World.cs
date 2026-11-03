using System;
using System.Collections.Generic;
using CoreECS.Defines;
using CoreECS.Managers;
using CoreECS.Utils;
using Microsoft.Extensions.DependencyInjection;

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

        protected override IInjectionProxyFactory GetInjectionProxyFactory()
        {
            return BuiltinInjectionProxyFactory.Instance;
        }

        /// <summary>
        /// Registers the core managers required for the ECS system.
        /// </summary>
        /// <param name="register">The manager register interface</param>
        protected override void OnRegisterManager(IManagerRegister register)
        {
            register.RegisterManager<ComponentManager>();
            register.RegisterManager<EntityManager>();
            register.RegisterManager<EntityMatchManager>();
            register.RegisterManager<SystemManager>();
        }

        /// <summary>
        /// Registers additional services after <see cref="OnRegisterManager"/>.
        /// </summary>
        /// <param name="services">The service collection</param>
        protected override void RegisterServices(IServiceCollection services)
        {}

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
                return new Entity(this, entityId, entityGraph.Generation, Entity, Component);

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
            
            var entityGraph = Entity.CreateEntity(mask);
            return new Entity(this, entityGraph.EntityId, entityGraph.Generation, Entity, Component);
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
        /// Appends entity IDs that match the specified matcher to <paramref name="result"/>.
        /// Existing items in <paramref name="result"/> are preserved.
        /// </summary>
        /// <param name="matcher">Matcher that defines the query conditions.</param>
        /// <param name="result">Target collection used as non-alloc output.</param>
        /// <returns>The number of matched entities appended to <paramref name="result"/>.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the world is not ready or the entity manager is unavailable.</exception>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="matcher"/> or <paramref name="result"/> is null.</exception>
        public int Query(IEntityMatcher matcher, ICollection<ulong> result)
        {
            Assertion.IsTrue(Ready, "World is not ready");
            Assertion.ArgumentNotNull(matcher, nameof(matcher));
            Assertion.ArgumentNotNull(result, nameof(result));
            
            if (Entity == null)
                throw new InvalidOperationException("Core ECS managers are not available");

            var added = 0;
            foreach (var entityGraph in Entity.EntityCaches.Values)
            {
                if (!_isMatched(entityGraph, matcher)) continue;
                
                result.Add(entityGraph.EntityId);
                added += 1;
            }

            return added;
        }

        /// <summary>
        /// Appends entity handles that match the specified matcher to <paramref name="result"/>.
        /// Existing items in <paramref name="result"/> are preserved.
        /// </summary>
        /// <param name="matcher">Matcher that defines the query conditions.</param>
        /// <param name="result">Target collection used as non-alloc output.</param>
        /// <returns>The number of matched entities appended to <paramref name="result"/>.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the world is not ready or required managers are unavailable.</exception>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="matcher"/> or <paramref name="result"/> is null.</exception>
        public int Query(IEntityMatcher matcher, ICollection<Entity> result)
        {
            Assertion.IsTrue(Ready, "World is not ready");
            Assertion.ArgumentNotNull(matcher, nameof(matcher));
            Assertion.ArgumentNotNull(result, nameof(result));
            
            if (Entity == null || Component == null)
                throw new InvalidOperationException("Core ECS managers are not available");

            var added = 0;
            foreach (var entityGraph in Entity.EntityCaches.Values)
            {
                if (!_isMatched(entityGraph, matcher)) continue;
                
                result.Add(new Entity(this, entityGraph.EntityId, entityGraph.Generation, Entity, Component));
                added += 1;
            }

            return added;
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
        /// <exception cref="InvalidOperationException">Thrown when the world is not ready, system manager is not available, or the system is not registered</exception>
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
        /// <exception cref="InvalidOperationException">Thrown when the world is not ready, system manager is not available, or the system is not registered</exception>
        public void UnregisterSystem<T>() where T : class, ISystem
        {
            Assertion.IsTrue(Ready, "World is not ready");

            if (System == null)
                throw new InvalidOperationException("Core ECS managers are not available");
            
            System.UnregisterSystem(typeof(T));
        }

        /// <summary>
        /// Creates a structural-change entity collector for the specified matcher.
        /// </summary>
        /// <param name="matcher">The entity matcher to use for filtering entities</param>
        /// <param name="flag">Flags controlling which events are mirrored into <see cref="IEntityCollector.Changed"/>; defaults to <see cref="EntityCollectorFlag.Default"/></param>
        /// <returns>A new IEntityCollector instance</returns>
        /// <exception cref="InvalidOperationException">Thrown when EntityMatch manager is not available</exception>
        public IEntityCollector CreateCollector(IEntityMatcher matcher,
            EntityCollectorFlag flag = EntityCollectorFlag.Default)
        {
            Assertion.IsTrue(Ready, "World is not ready");

            if (EntityMatch == null)
                throw new InvalidOperationException("Core ECS managers are not available");
            
            return EntityMatch.MakeCollector(flag, matcher);
        }

        /// <summary>
        /// Shared matcher gate for world-level non-alloc entity queries.
        /// </summary>
        private static bool _isMatched(EntityGraph entityGraph, IEntityMatcher matcher)
        {
            if ((matcher.EntityMask & entityGraph.Mask) == 0) return false;
            if (entityGraph.WishDestroy) return false;
            return matcher.ComponentFilter(entityGraph.RwComponents);
        }

        #endregion
    }
}