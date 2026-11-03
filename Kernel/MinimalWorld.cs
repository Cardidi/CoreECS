using System;
using CoreECS.Defines;
using CoreECS.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace CoreECS
{
    /// <summary>
    /// A minimal implementation of IWorld. It provides a basic world structure with essential managers.
    /// This abstract class serves as a foundation for implementing ECS worlds, handling the core
    /// lifecycle of managers, ticks, and dependency injection. You can extend this class to create
    /// your own world with custom managers.
    /// </summary>
    public abstract class MinimalWorld : IWorld
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
        /// Starts up the world, initializing all managers and systems.
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

                try
                {
                    OnRegisterManager(m_mediator);
                }
                catch (Exception e)
                {
                    Log.Exp(e, nameof(OnRegisterManager));
                }
                
                RegisterRequiredServices(collection);
                
                try
                {
                    RegisterServices(collection);
                }
                catch (Exception e)
                {
                    Log.Exp(e, nameof(RegisterServices));
                }
                
                
                InjectionProxy = factory.CreateProxy(collection);
                m_mediator.Construct(InjectionProxy);
                
                try
                {
                    OnConstruct();
                }
                catch (Exception e)
                {
                    Log.Exp(e, nameof(OnConstruct));
                }
            }

            // Boot the mediator
            m_mediator.Boot();

            // Finalize initialization
            m_init = true;

            if (firstStart)
            {
                try
                {
                    OnFirstStart();
                }
                catch (Exception e)
                {
                    Log.Exp(e, nameof(OnFirstStart));
                }
            }
            
            try
            {
                OnStart();
            }
            catch (Exception e)
            {
                Log.Exp(e, nameof(OnStart));
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

            // Call user-defined shutdown logic
            try
            {
                OnShutdown();
            }
            catch (Exception e)
            {
                Log.Exp(e, nameof(OnShutdown));
            }

            // Shutdown mediator
            m_mediator.Shutdown();

            // Update state
            m_init = false;
            m_shutdown = true;
        }

        /// <summary>
        /// Begins a new tick, incrementing the tick count.
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
                OnTickBegin();
            }
            catch (Exception e)
            {
                Log.Exp(e, nameof(OnTickBegin));
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
                OnTick(tickMask);
            }
            catch (Exception e)
            {
                Log.Exp(e, nameof(OnTick));
            }
        }

        /// <summary>
        /// Ends the current tick, completing the tick cycle.
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
                OnTickEnd();
            }
            catch (Exception e)
            {
                Log.Exp(e, nameof(OnTickEnd));
            }
            m_ticking = false;
        }

        #region Dependency Injection

        /// <summary>
        /// Returns the factory used to build <see cref="InjectionProxy"/>.
        /// </summary>
        protected virtual IInjectionProxyFactory GetInjectionProxyFactory()
        {
            throw new NotImplementedException($"{GetType().Name} must override {nameof(GetInjectionProxyFactory)} to provide an {nameof(IInjectionProxyFactory)} implementation.");
        }

        /// <summary>
        /// Registers framework-required services into the collection before the proxy is built.
        /// </summary>
        /// <param name="services">The service collection</param>
        protected internal virtual void RegisterRequiredServices(IServiceCollection services)
        {
            services.AddSingleton<IWorld>(this);
            services.AddSingleton(GetType(), this);

            // Register managers.
            foreach (var (_, implementationType) in m_mediator.RegisteredManagers)
            {
                services.AddSingleton(implementationType);
            }
        }

        #endregion

        #region Lifecycle Events

        /// <summary>
        /// Called during world startup to register managers.
        /// Implementations should register all required managers with the provided register.
        /// </summary>
        /// <param name="register">The manager register interface</param>
        protected abstract void OnRegisterManager(IManagerRegister register);

        /// <summary>
        /// Registers additional services after <see cref="OnRegisterManager"/>.
        /// </summary>
        /// <param name="services">The service collection</param>
        protected abstract void RegisterServices(IServiceCollection services);

        /// <summary>
        /// Called after all managers have been constructed.
        /// Implementations can perform additional initialization here.
        /// </summary>
        protected abstract void OnConstruct();

        /// <summary>
        /// Called after the world has been fully initialized for the first time.
        /// Implementations can perform startup logic here.
        /// </summary>
        protected abstract void OnFirstStart();
        
        /// <summary>
        /// Called after the world has been fully initialized.
        /// Implementations can perform startup logic here.
        /// </summary>
        protected abstract void OnStart();
        
        /// <summary>
        /// Called at the beginning of each tick.
        /// Implementations can prepare for tick processing here.
        /// </summary>
        protected abstract void OnTickBegin();
        
        /// <summary>
        /// Called during each tick to process systems.
        /// Implementations should execute systems based on the tick mask.
        /// </summary>
        /// <param name="tickMask">The tick mask determining which systems to execute</param>
        protected abstract void OnTick(ulong tickMask);
        
        /// <summary>
        /// Called at the end of each tick.
        /// Implementations can perform cleanup after tick processing.
        /// </summary>
        protected abstract void OnTickEnd();
        
        /// <summary>
        /// Called during world shutdown.
        /// Implementations should release resources here.
        /// </summary>
        protected abstract void OnShutdown();

        #endregion
    }
}