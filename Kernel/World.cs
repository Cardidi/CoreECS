using System;
using CoreECS.Defines;
using CoreECS.Managers;
using CoreECS.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CoreECS
{
    /// <summary>
    /// The only ECS world implementation and the single entry point of the framework.
    /// It owns the built-in core managers (components, entities, entity matching and
    /// systems) and handles the core lifecycle of managers, ticks and dependency
    /// injection. Subclasses can register additional managers and services by overriding
    /// <see cref="OnRegister"/> and hook into startup and cleanup through
    /// <see cref="OnSetup"/> and <see cref="OnCleanup"/>.
    /// </summary>
    public class World : IWorld
    {
        #region Private Area

        /// <summary>
        /// Mediator for managing world managers.
        /// </summary>
        private ManagerMediator m_mediator = null;

        /// <summary>
        /// Flag indicating whether the world has been initialized.
        /// </summary>
        private bool m_init = false;

        /// <summary>
        /// Flag indicating whether the world is currently in a tick.
        /// </summary>
        private bool m_ticking = false;

        /// <summary>
        /// Flag indicating whether the world has been shut down.
        /// </summary>
        private bool m_shutdown = false;

        #endregion

        /// <summary>
        /// Gets the current tick count of the world.
        /// This value increments at the beginning of each tick.
        /// </summary>
        public uint TickCount { get; private set; } = 0;

        /// <summary>
        /// Gets the injection proxy built on first startup. Null before <see cref="Startup"/>.
        /// </summary>
        public IInjectionProxy InjectionProxy { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the world is ready for operation.
        /// The world is ready after initialization and before shutdown.
        /// </summary>
        public bool Ready => m_init && !m_shutdown;

        /// <summary>
        /// Gets a value indicating whether the world is currently in a tick.
        /// </summary>
        public bool Ticking => m_ticking;

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
        /// Gets a manager by its type.
        /// </summary>
        /// <typeparam name="TMgr">The type of manager to retrieve</typeparam>
        /// <returns>The manager instance</returns>
        /// <exception cref="InvalidOperationException">Thrown when the world is not initialized, shut down, or the manager is not found</exception>
        public TMgr GetManager<TMgr>() where TMgr : IWorldManager
        {
            Assertion.IsTrue(m_init, "World is not initialized");
            Assertion.IsFalse(m_shutdown, "World is shutdown");

            if (m_mediator.Managers.TryGetValue(typeof(TMgr), out var manager))
                return (TMgr)manager;

            throw new InvalidOperationException($"Manager of type {typeof(TMgr).Name} not found.");
        }

        /// <summary>
        /// Starts up the world, initializing the built-in core managers and all custom
        /// managers registered by <see cref="OnRegister"/>, then calls <see cref="OnSetup"/>.
        /// This method should be called once before any ticks are processed.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when the world is already initialized or shut down</exception>
        public void Startup()
        {
            Assertion.IsFalse(m_init, "World is already initialized");
            Assertion.IsFalse(m_shutdown, "World is shutdown");

            // Initialize state
            var firstStart = m_mediator == null;
            TickCount = 0;
            m_ticking = false;

            // Create mediator if not exists
            if (firstStart)
            {
                var factory = GetInjectionProxyFactory();
                var collection = factory.CreateServiceCollection();

                m_mediator = new ManagerMediator(this);

                // Built-in core managers; subclasses add custom managers in OnRegister.
                m_mediator.RegisterManager<ComponentManager>();
                m_mediator.RegisterManager<EntityManager>();
                m_mediator.RegisterManager<EntityMatchManager>();
                m_mediator.RegisterManager<SystemManager>();

                try
                {
                    OnRegister(m_mediator, collection);
                }
                catch (Exception e)
                {
                    Log.Exp(e, nameof(OnRegister));
                }

                RegisterRequiredServices(collection);

                InjectionProxy = factory.CreateProxy(collection);
                m_mediator.Construct(InjectionProxy);
            }

            // Boot the mediator
            m_mediator.Boot();

            // Finalize initialization
            m_init = true;

            // Wire the built-in core manager references before OnSetup so that
            // overriding OnSetup can never break tick processing.
            EntityMatch = GetManager<EntityMatchManager>();
            Entity = GetManager<EntityManager>();
            Component = GetManager<ComponentManager>();
            System = GetManager<SystemManager>();

            // Flatten the revision-change relay: the component manager forwards directly
            // to the entity manager, which forwards directly to the match manager.
            Entity.ConnectMatchManager(EntityMatch);
            Component.ChangeSink = Entity.OnRevisionChanged;

            try
            {
                OnSetup();
            }
            catch (Exception e)
            {
                Log.Exp(e, nameof(OnSetup));
            }
        }

        /// <summary>
        /// Shuts down the world, releasing all resources.
        /// This method should be called when the world is no longer needed.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when the world is not initialized, already shut down, or currently ticking</exception>
        public void Shutdown()
        {
            Assertion.IsTrue(m_init, "World is not initialized");
            Assertion.IsFalse(m_shutdown, "World is already shutdown");
            Assertion.IsFalse(m_ticking, "World is ticking and should not be shutdown");

            // Call user-defined cleanup logic
            try
            {
                OnCleanup();
            }
            catch (Exception e)
            {
                Log.Exp(e, nameof(OnCleanup));
            }

            // Shutdown mediator
            m_mediator.Shutdown();

            // Update state
            m_init = false;
            m_shutdown = true;
        }

        /// <summary>
        /// Begins a new tick, incrementing the tick count and tearing down the system
        /// schedule so pending registration changes are applied.
        /// This method should be called before processing any systems.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when the world is not initialized, shut down, or already ticking</exception>
        public void BeginTick()
        {
            Assertion.IsTrue(m_init, "World is not initialized");
            Assertion.IsFalse(m_shutdown, "World is shutdown");
            Assertion.IsFalse(m_ticking, "World is already ticking");

            // Increment tick count at the beginning of each tick
            TickCount++;
            m_ticking = true;
            try
            {
                System.TeardownSystems();
            }
            catch (Exception e)
            {
                Log.Exp(e, nameof(BeginTick));
            }
        }

        /// <summary>
        /// Executes the tick, processing systems based on the tick mask.
        /// This method should be called after BeginTick and before EndTick.
        /// </summary>
        /// <param name="tickMask">Optional mask to filter which systems should execute</param>
        /// <exception cref="InvalidOperationException">Thrown when the world is not initialized, shut down, or not in ticking state</exception>
        public void Tick(ulong tickMask = ulong.MaxValue)
        {
            Assertion.IsTrue(m_init, "World is not initialized");
            Assertion.IsFalse(m_shutdown, "World is shutdown");
            Assertion.IsTrue(m_ticking, "World must enter ticking first");

            try
            {
                System.ExecuteSystems(tickMask);
            }
            catch (Exception e)
            {
                Log.Exp(e, nameof(Tick));
            }
        }

        /// <summary>
        /// Ends the current tick, cleaning up systems after execution.
        /// This method should be called after all systems have been processed.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when the world is not initialized, shut down, or not in ticking state</exception>
        public void EndTick()
        {
            Assertion.IsTrue(m_init, "World is not initialized");
            Assertion.IsFalse(m_shutdown, "World is shutdown");
            Assertion.IsTrue(m_ticking, "World must be in ticking state");

            try
            {
                System.CleanupSystems();
            }
            catch (Exception e)
            {
                Log.Exp(e, nameof(EndTick));
            }
            m_ticking = false;
        }

        #region Dependency Injection

        /// <summary>
        /// Returns the factory used to build <see cref="InjectionProxy"/>.
        /// The default implementation uses the built-in dependency injection factory.
        /// </summary>
        protected virtual IInjectionProxyFactory GetInjectionProxyFactory()
        {
            return BuiltinInjectionProxyFactory.Instance;
        }

        /// <summary>
        /// Registers framework-required services into the collection before the proxy is built.
        /// Services registered by <see cref="OnRegister"/> take precedence for the same service type.
        /// </summary>
        /// <param name="services">The service collection</param>
        protected internal virtual void RegisterRequiredServices(IServiceCollection services)
        {
            services.TryAddSingleton<IWorld>(this);
            services.TryAdd(new ServiceDescriptor(GetType(), this));

            // Register managers.
            foreach (var (_, implementationType) in m_mediator.RegisteredManagers)
            {
                services.TryAddSingleton(implementationType);
            }
        }

        #endregion

        #region Lifecycle Events

        /// <summary>
        /// Called during the first startup, after the built-in core managers have been
        /// registered and before framework services are added. Subclasses can register
        /// additional managers and services here.
        /// </summary>
        /// <param name="register">The manager register interface</param>
        /// <param name="services">The service collection used to build the injection proxy</param>
        protected virtual void OnRegister(IManagerRegister register, IServiceCollection services)
        {
        }

        /// <summary>
        /// Called after the world has been fully initialized on every startup.
        /// The built-in core manager references are already wired when this runs.
        /// Implementations can track their own state to detect the first startup.
        /// </summary>
        protected virtual void OnSetup()
        {
        }

        /// <summary>
        /// Called on every shutdown before the managers are shut down.
        /// Implementations should release resources here.
        /// </summary>
        protected virtual void OnCleanup()
        {
        }

        #endregion

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
        
        #region PublicAPI - Resource Requestion
        
        /// <summary>
        /// Creates a command buffer bound to this world. Record entity and component
        /// commands and apply them in one explicit playback; disposing without playback
        /// discards the pending records. Recording performs no structural change.
        /// </summary>
        /// <returns>A new command buffer bound to this world.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the world is not ready.</exception>
        public CommandBuffer CreateCommandBuffer()
        {
            Assertion.IsTrue(Ready, "World is not ready");
            return new CommandBuffer(this);
        }

        /// <summary>
        /// Creates a non-pooled query over the entities matching the specified matcher.
        /// The returned query owns an empty snapshot until <see cref="IEntityQuery.Refresh"/> is called.
        /// </summary>
        /// <param name="matcher">Matcher that defines the query conditions.</param>
        /// <returns>A new query bound to this world.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the world is not ready or the entity manager is unavailable.</exception>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="matcher"/> is null.</exception>
        public IEntityQuery CreateQuery(IEntityMatcher matcher)
        {
            Assertion.IsTrue(Ready, "World is not ready");
            Assertion.ArgumentNotNull(matcher, nameof(matcher));

            if (Entity == null)
                throw new InvalidOperationException("Core ECS managers are not available");

            return new EntityQuery(matcher, Entity);
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
        
        #endregion

        #region PublicAPI - Entity Management

        /// <summary>
        /// Gets an entity by its ID.
        /// </summary>
        /// <param name="entityId">The ID of the entity to retrieve</param>
        /// <returns>The Entity instance if found, otherwise default(Entity)</returns>
        public Entity GetEntity(ulong entityId)
        {
            if (Entity == null || Component == null)
                throw new InvalidOperationException("Core ECS managers are not available");

            return Entity.GetEntity(entityId);
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

            return Entity.CreateEntity(mask);
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
        
        #endregion
        
        #region PublicAPI - System Management

        /// <summary>
        /// Registers a system with the world at the root level of the schedule.
        /// </summary>
        /// <param name="systemType">The type of system to register</param>
        /// <returns>A registration handle used to declare Before / After anchors</returns>
        /// <exception cref="InvalidOperationException">Thrown when the world is not ready or system manager is not available</exception>
        public SystemRegistration RegisterSystem(Type systemType)
        {
            Assertion.IsTrue(Ready, "World is not ready");

            if (System == null)
                throw new InvalidOperationException("Core ECS managers are not available");

            return System.RegisterSystem(systemType);
        }

        /// <summary>
        /// Registers a system with the world, optionally inside a registered group.
        /// </summary>
        /// <typeparam name="T">The type of system to register, must implement ISystem</typeparam>
        /// <param name="groupName">Name of the group to place the system in; null places it at the root level</param>
        /// <returns>A registration handle used to declare Before / After anchors</returns>
        /// <exception cref="InvalidOperationException">Thrown when the world is not ready, system manager is not available or the group is not registered</exception>
        public SystemRegistration RegisterSystem<T>(string groupName = null) where T : class, ISystem
        {
            Assertion.IsTrue(Ready, "World is not ready");

            if (System == null)
                throw new InvalidOperationException("Core ECS managers are not available");

            return System.RegisterSystem(typeof(T), groupName);
        }

        /// <summary>
        /// Registers a group at the root level of the system schedule.
        /// </summary>
        /// <param name="name">Unique group name</param>
        /// <param name="mode">Insertion position among the root level; defaults to Later (append)</param>
        /// <returns>A registration handle used to declare Before / After anchors</returns>
        /// <exception cref="InvalidOperationException">Thrown when the world is not ready, system manager is not available or the name is already registered</exception>
        public GroupRegistration RegisterGroup(string name, GroupInsertMode mode = GroupInsertMode.Later)
        {
            Assertion.IsTrue(Ready, "World is not ready");

            if (System == null)
                throw new InvalidOperationException("Core ECS managers are not available");

            return System.RegisterGroup(name, mode);
        }

        /// <summary>
        /// Registers a nested group inside another registered group.
        /// </summary>
        /// <param name="name">Unique group name</param>
        /// <param name="parentName">Name of the already registered parent group</param>
        /// <param name="mode">Insertion position among the parent level; defaults to Later (append)</param>
        /// <returns>A registration handle used to declare Before / After anchors</returns>
        /// <exception cref="InvalidOperationException">Thrown when the world is not ready, system manager is not available, the name is already registered or the parent is not registered</exception>
        public GroupRegistration RegisterGroup(string name, string parentName, GroupInsertMode mode = GroupInsertMode.Later)
        {
            Assertion.IsTrue(Ready, "World is not ready");

            if (System == null)
                throw new InvalidOperationException("Core ECS managers are not available");

            return System.RegisterGroup(name, parentName, mode);
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


        #endregion
    }
}
