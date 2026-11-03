using System;
using CoreECS.Defines;
using CoreECS.Managers;
using CoreECS.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CoreECS
{
    /// <summary>
    /// Owns the entities, components, systems, managers, and lifecycle of an ECS world.
    /// </summary>
    public class World : IWorld
    {
        private ManagerMediator m_mediator;
        private bool m_initialized;
        private bool m_ticking;
        private bool m_shutdown;

        /// <summary>Gets the number of ticks begun by this world.</summary>
        public uint TickCount { get; private set; }

        /// <summary>Gets whether the world has started and has not shut down.</summary>
        public bool Ready => m_initialized && !m_shutdown;

        /// <summary>Gets whether a tick is currently in progress.</summary>
        public bool Ticking => m_ticking;

        /// <summary>Gets the injection proxy built during startup.</summary>
        public IInjectionProxy InjectionProxy { get; private set; }

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

        /// <summary>Returns the factory used to create this world's service provider.</summary>
        protected virtual IInjectionProxyFactory GetInjectionProxyFactory()
        {
            return BuiltinInjectionProxyFactory.Instance;
        }

        /// <summary>
        /// Registers custom managers and services while the world is being constructed.
        /// </summary>
        /// <param name="managers">The manager register.</param>
        /// <param name="services">The service collection.</param>
        protected virtual void OnRegister(IManagerRegister managers,
            IServiceCollection services)
        {
        }

        /// <summary>Runs after all managers have started.</summary>
        protected virtual void OnSetup()
        {
        }

        /// <summary>Runs before managers are shut down.</summary>
        protected virtual void OnCleanup()
        {
        }

        private static void RegisterCoreManagers(IManagerRegister register)
        {
            register.RegisterManager<ComponentManager>();
            register.RegisterManager<EntityManager>();
            register.RegisterManager<EntityMatchManager>();
            register.RegisterManager<SystemManager>();
        }

        /// <summary>
        /// Registers services required by the framework and all registered managers.
        /// </summary>
        protected internal virtual void RegisterRequiredServices(IServiceCollection services)
        {
            EnsureFrameworkServicesAreNotReplaced(services);
            services.AddSingleton<IWorld>(this);
            services.AddSingleton(GetType(), this);

            foreach (var (_, implementationType) in m_mediator.RegisteredManagers)
            {
                services.TryAdd(ServiceDescriptor.Singleton(
                    implementationType, implementationType));
            }
        }

        private void EnsureFrameworkServicesAreNotReplaced(
            IServiceCollection services)
        {
            foreach (ServiceDescriptor descriptor in services)
            {
                if (descriptor.ServiceType == typeof(IWorld) ||
                    descriptor.ServiceType == GetType() ||
                    IsCoreManagerType(descriptor.ServiceType))
                {
                    throw new InvalidOperationException(
                        $"Framework service {descriptor.ServiceType.Name} cannot be replaced.");
                }
            }
        }

        private static bool IsCoreManagerType(Type type)
        {
            return type == typeof(ComponentManager) ||
                   type == typeof(EntityManager) ||
                   type == typeof(EntityMatchManager) ||
                   type == typeof(SystemManager);
        }

        /// <summary>
        /// Starts this world and constructs its manager graph.
        /// </summary>
        public void Startup()
        {
            ThrowIfStructuralMutationForbidden();
            Assertion.IsFalse(m_initialized, "World is already initialized");
            Assertion.IsFalse(m_shutdown, "World is shutdown");

            TickCount = 0;
            m_ticking = false;

            var mediator = new ManagerMediator(this);
            m_mediator = mediator;

            try
            {
                IInjectionProxyFactory factory = GetInjectionProxyFactory();
                IServiceCollection services = factory.CreateServiceCollection();
                RegisterCoreManagers(mediator);
                OnRegister(m_mediator, services);
                RegisterRequiredServices(services);
                InjectionProxy = factory.CreateProxy(services);
                mediator.Construct(InjectionProxy);
                mediator.Boot();

                EntityMatch = GetConstructedManager<EntityMatchManager>(mediator);
                Entity = GetConstructedManager<EntityManager>(mediator);
                Component = GetConstructedManager<ComponentManager>(mediator);
                System = GetConstructedManager<SystemManager>(mediator);
                m_initialized = true;
                OnSetup();
            }
            catch
            {
                SafeRollback(mediator);
                DisposeFailedInjectionGraph(InjectionProxy);
                ResetFailedStartup();
                throw;
            }
        }

        private static void SafeRollback(ManagerMediator mediator)
        {
            try
            {
                mediator.RollbackStartup();
            }
            catch (Exception exception)
            {
                SafeLog(exception);
            }
        }

        private static void DisposeFailedInjectionGraph(IInjectionProxy proxy)
        {
            if (proxy == null) return;

            try
            {
                if (proxy is IDisposable disposableProxy)
                {
                    disposableProxy.Dispose();
                }
                else if (proxy.ServiceProvider is IDisposable disposableProvider)
                {
                    disposableProvider.Dispose();
                }
            }
            catch (Exception exception)
            {
                SafeLog(exception);
            }
        }

        private static void SafeLog(Exception exception)
        {
            try
            {
                Log.Exp(exception);
            }
            catch
            {
                // Cleanup failures must never replace the startup failure.
            }
        }

        private static TManager GetConstructedManager<TManager>(ManagerMediator mediator)
            where TManager : IWorldManager
        {
            if (mediator.Managers.TryGetValue(typeof(TManager), out IWorldManager manager))
                return (TManager)manager;

            throw new InvalidOperationException(
                $"Manager of type {typeof(TManager).Name} not found.");
        }

        private void ResetFailedStartup()
        {
            m_initialized = false;
            m_ticking = false;
            m_mediator = null;
            InjectionProxy = null;
            EntityMatch = null;
            Entity = null;
            Component = null;
            System = null;
        }

        /// <summary>
        /// Shuts down this world after a completed tick.
        /// </summary>
        public void Shutdown()
        {
            ThrowIfStructuralMutationForbidden();
            Assertion.IsTrue(m_initialized, "World is not initialized");
            Assertion.IsFalse(m_shutdown, "World is already shutdown");
            Assertion.IsFalse(m_ticking, "World is ticking and should not be shutdown");

            try
            {
                OnCleanup();
            }
            catch (Exception exception)
            {
                Log.Exp(exception, nameof(OnCleanup));
            }

            m_mediator.Shutdown();
            m_initialized = false;
            m_shutdown = true;
        }

        /// <summary>
        /// Begins a tick and applies pending system DAG changes.
        /// </summary>
        public void BeginTick()
        {
            ThrowIfStructuralMutationForbidden();
            Assertion.IsTrue(m_initialized, "World is not initialized");
            Assertion.IsFalse(m_shutdown, "World is shutdown");
            Assertion.IsFalse(m_ticking, "World is already ticking");

            TickCount++;
            m_ticking = true;
            System.TeardownSystems();
        }

        /// <summary>
        /// Executes the system snapshot captured by <see cref="BeginTick"/>.
        /// </summary>
        public void Tick(ulong tickMask = ulong.MaxValue)
        {
            ThrowIfStructuralMutationForbidden();
            Assertion.IsTrue(m_initialized, "World is not initialized");
            Assertion.IsFalse(m_shutdown, "World is shutdown");
            Assertion.IsTrue(m_ticking, "World must enter ticking first");

            System.ExecuteSystems(tickMask);
        }

        /// <summary>
        /// Finalizes pending systems, compacts component storage, and releases entity IDs.
        /// </summary>
        public void EndTick()
        {
            ThrowIfStructuralMutationForbidden();
            Assertion.IsTrue(m_initialized, "World is not initialized");
            Assertion.IsFalse(m_shutdown, "World is shutdown");
            Assertion.IsTrue(m_ticking, "World must be in ticking state");

            System.CleanupSystems();
            Component.CompactDirtyStores();
            Entity.CommitReleases();
            m_ticking = false;
        }

        /// <summary>Gets a manager by its registered type.</summary>
        public TMgr GetManager<TMgr>() where TMgr : IWorldManager
        {
            Assertion.IsTrue(m_initialized, "World is not initialized");
            Assertion.IsFalse(m_shutdown, "World is shutdown");

            if (m_mediator.Managers.TryGetValue(typeof(TMgr), out IWorldManager manager))
                return (TMgr)manager;

            throw new InvalidOperationException(
                $"Manager of type {typeof(TMgr).Name} not found.");
        }

        private void ThrowIfStructuralMutationForbidden()
        {
            if (m_mediator != null &&
                m_mediator.Managers.TryGetValue(typeof(ComponentManager),
                    out IWorldManager manager))
            {
                ((ComponentManager)manager).StructuralMutationGuard.ThrowIfMutationForbidden();
            }
        }
        
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
        public Entity GetEntity(EntityId entityId)
        {
            if (Entity == null || Component == null)
                throw new InvalidOperationException("Core ECS managers are not available");
            
            if (Entity.IsAlive(entityId))
                return new Entity(this, entityId);

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

            Component.StructuralMutationGuard.ThrowIfMutationForbidden();
            
            EntityId entityId = Entity.Create(mask);
            return new Entity(this, entityId);
        }
        
        /// <summary>
        /// Destroys an entity by its ID.
        /// </summary>
        /// <param name="entityId">The ID of the entity to destroy</param>
        /// <exception cref="InvalidOperationException">Thrown when core ECS managers are not available</exception>
        public void DestroyEntity(EntityId entityId)
        {
            Assertion.IsTrue(Ready, "World is not ready");

            if (Entity == null || Component == null)
                throw new InvalidOperationException("Core ECS managers are not available");

            Component.StructuralMutationGuard.ThrowIfMutationForbidden();
            
            Entity.Destroy(entityId);
        }
        
        /// <summary>
        /// Destroys an entity.
        /// </summary>
        /// <param name="entity">The entity to destroy</param>
        public void DestroyEntity(Entity entity)
        {
            Assertion.IsTrue(Ready, "World is not ready");
            
            Component.StructuralMutationGuard.ThrowIfMutationForbidden();

            if (ReferenceEquals(entity.WorldOrNull, this))
            {
                DestroyEntity(entity.EntityId);
            }
        }

        /// <summary>
        /// Creates a reusable materialized EntityId query snapshot.
        /// </summary>
        /// <param name="matcher">Matcher that defines the query conditions.</param>
        /// <param name="refreshImmediately">Whether to materialize the current World state immediately.</param>
        /// <returns>A concrete query owned by this World.</returns>
        public EntityQuery CreateQuery(EntityMatcher matcher, bool refreshImmediately = true)
        {
            Assertion.IsTrue(Ready, "World is not ready");
            Assertion.ArgumentNotNull(matcher, nameof(matcher));

            if (Component == null)
                throw new InvalidOperationException("Core ECS managers are not available");

            return new EntityQuery(this, matcher, Component.EntityTable,
                Component.StorageRegistry, refreshImmediately);
        }

        /// <summary>
        /// Registers a system with the world.
        /// </summary>
        /// <param name="systemType">The type of system to register</param>
        /// <exception cref="InvalidOperationException">Thrown when system manager is not available</exception>
        public SystemRegistration RegisterSystem(Type systemType)
        {
            Assertion.IsTrue(Ready, "World is not ready");

            if (System == null)
                throw new InvalidOperationException("Core ECS managers are not available");
            
            return System.RegisterSystem(systemType);
        }

        /// <summary>
        /// Registers a system in a named schedule group.
        /// </summary>
        /// <param name="systemType">The concrete system type.</param>
        /// <param name="groupName">The already registered group name.</param>
        /// <returns>A handle for declaring ordering anchors.</returns>
        public SystemRegistration RegisterSystem(Type systemType, string groupName)
        {
            Assertion.IsTrue(Ready, "World is not ready");

            if (System == null)
                throw new InvalidOperationException("Core ECS managers are not available");

            return System.RegisterSystem(systemType, groupName);
        }

        /// <summary>
        /// Registers a system with the world at the root of the schedule.
        /// </summary>
        /// <typeparam name="T">The type of system to register, must implement ISystem</typeparam>
        /// <exception cref="InvalidOperationException">Thrown when system manager is not available</exception>
        public SystemRegistration RegisterSystem<T>() where T : class, ISystem
        {
            return RegisterSystem<T>(null);
        }

        /// <summary>
        /// Registers a system with the world inside a named group.
        /// </summary>
        /// <typeparam name="T">The type of system to register, must implement ISystem</typeparam>
        /// <param name="groupName">The already registered group name.</param>
        /// <exception cref="InvalidOperationException">Thrown when system manager is not available</exception>
        public SystemRegistration RegisterSystem<T>(string groupName) where T : class, ISystem
        {
            Assertion.IsTrue(Ready, "World is not ready");

            if (System == null)
                throw new InvalidOperationException("Core ECS managers are not available");
            
            return System.RegisterSystem(typeof(T), groupName);
        }

        /// <summary>Registers a root-level system schedule group.</summary>
        public GroupRegistration RegisterGroup(string name,
            GroupInsertMode mode = GroupInsertMode.Later)
        {
            Assertion.IsTrue(Ready, "World is not ready");

            if (System == null)
                throw new InvalidOperationException("Core ECS managers are not available");

            return System.RegisterGroup(name, mode);
        }

        /// <summary>Registers a nested system schedule group.</summary>
        public GroupRegistration RegisterGroup(string name, string parentName,
            GroupInsertMode mode = GroupInsertMode.Later)
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

        /// <summary>
        /// Creates a structural-change entity collector for the specified matcher.
        /// </summary>
        /// <param name="matcher">The entity matcher to use for filtering entities</param>
        /// <param name="flag">Flags controlling which events are mirrored into <see cref="EntityCollector.Changed"/>; defaults to <see cref="EntityCollectorFlag.Default"/></param>
        /// <returns>A concrete collector owned by this World.</returns>
        /// <exception cref="InvalidOperationException">Thrown when EntityMatch manager is not available</exception>
        public EntityCollector CreateCollector(EntityMatcher matcher,
            EntityCollectorFlag flag = EntityCollectorFlag.Default)
        {
            Assertion.IsTrue(Ready, "World is not ready");

            if (EntityMatch == null)
                throw new InvalidOperationException("Core ECS managers are not available");
            
            return EntityMatch.MakeCollector(flag, matcher);
        }

        #endregion
    }
}
