using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
#if NET6_0_OR_GREATER
using System.Collections.Immutable;
#endif
using CoreECS.Defines;
using CoreECS.Utils;

namespace CoreECS
{
    /// <summary>
    /// Interface for registering world managers with the ECS framework.
    /// Provides methods to register managers by interface and implementation types.
    /// </summary>
    public interface IManagerRegister
    {
        /// <summary>
        /// Register a manager implementation with its interface type.
        /// TImp is the interface type and TMgr is the implementation that inherits from TImp.
        /// </summary>
        /// <typeparam name="TImp">The interface type of the manager</typeparam>
        /// <typeparam name="TMgr">The implementation type of the manager, which should inherit from TImp</typeparam>
        public void RegisterManager<TImp, TMgr>() where TMgr : TImp where TImp : IWorldManager;
        
        /// <summary>
        /// Register a manager directly by its concrete type.
        /// The same type is used as both interface and implementation.
        /// </summary>
        /// <typeparam name="T">The concrete manager type that implements IWorldManager</typeparam>
        public void RegisterManager<T>() where T : IWorldManager;
        
        /// <summary>
        /// Check if a manager type (either interface or implementation) is already registered.
        /// </summary>
        /// <typeparam name="T">The type to check for registration</typeparam>
        /// <returns>True if the type is already registered, false otherwise</returns>
        public bool AlreadyRegistered<T>();
    }
    
    /// <summary>
    /// Mediator class that manages the lifecycle and registration of world managers.
    /// Handles manager registration, construction, dependency injection, and lifecycle events.
    /// </summary>
    public sealed class ManagerMediator : IManagerRegister
    {
        /// <summary>
        /// Reference to the parent type for all managers.
        /// </summary>
        private static readonly Type ParentType = typeof(IWorldManager);
        
        /// <summary>
        /// Immutable dictionary mapping manager types to their instances.
        /// </summary>
        private IReadOnlyDictionary<Type, IWorldManager> m_managerMap;
        
        /// <summary>
        /// List of registered manager types before construction.
        /// </summary>
        private readonly List<(Type interfaceType, Type implementationType)> m_registeredManagers;
        
        /// <summary>
        /// Reference to the world this mediator belongs to.
        /// </summary>
        private readonly IWorld m_world;
        
        /// <summary>
        /// Dependency injector for manager instances.
        /// </summary>
        private readonly Injector m_injector;
        
        /// <summary>
        /// Flag indicating whether managers have been constructed.
        /// </summary>
        private bool m_constructed = false;

        /// <summary>
        /// Initializes a new instance of the ManagerMediator class.
        /// </summary>
        /// <param name="world">The world this mediator belongs to</param>
        /// <param name="injector">Dependency injector for manager instances</param>
        public ManagerMediator(IWorld world, Injector injector)
        {
            Assertion.IsNotNull(world);

            m_world = world;
            m_injector = injector;
            m_registeredManagers = new List<(Type, Type)>();
            
            // Initialize with empty manager map initially
            m_managerMap = new Dictionary<Type, IWorldManager>();
        }
        
        /// <summary>
        /// Gets a value indicating whether the managers have been booted (initialized).
        /// </summary>
        public bool Booted { get; private set; }
        
        /// <summary>
        /// Gets a read-only dictionary of all constructed managers.
        /// </summary>
        public IReadOnlyDictionary<Type, IWorldManager> Managers => m_managerMap;

        /// <summary>
        /// Registers a manager implementation with its interface type.
        /// </summary>
        /// <typeparam name="TImp">The interface type of the manager</typeparam>
        /// <typeparam name="TMgr">The implementation type of the manager, which should inherit from TImp</typeparam>
        public void RegisterManager<TImp, TMgr>() where TMgr : TImp where TImp : IWorldManager
        {
            Assertion.IsFalse(m_constructed, "Cannot register manager after construction");
            
            // Check if either type is already registered
            foreach (var (interfaceType, implementationType) in m_registeredManagers)
            {
                if (interfaceType == typeof(TImp) || implementationType == typeof(TMgr))
                {
                    Log.Err($"Manager type already registered: Interface={interfaceType}, Implementation={implementationType}");
                    return;
                }
            }
            
            m_registeredManagers.Add((typeof(TImp), typeof(TMgr)));
        }

        /// <summary>
        /// Registers a manager directly by its concrete type.
        /// The same type is used as both interface and implementation.
        /// </summary>
        /// <typeparam name="T">The concrete manager type that implements IWorldManager</typeparam>
        public void RegisterManager<T>() where T : IWorldManager
        {
            Assertion.IsFalse(m_constructed, "Cannot register manager after construction");
            
            var type = typeof(T);
            
            // Check if the type is already registered
            foreach (var (interfaceType, implementationType) in m_registeredManagers)
            {
                if (interfaceType == type || implementationType == type)
                {
                    Log.Err($"Manager type already registered: {type}");
                    return;
                }
            }
            
            // Register the same type as both interface and implementation
            m_registeredManagers.Add((type, type));
        }

        /// <summary>
        /// Checks if a manager type (either interface or implementation) is already registered.
        /// </summary>
        /// <typeparam name="T">The type to check for registration</typeparam>
        /// <returns>True if the type is already registered, false otherwise</returns>
        public bool AlreadyRegistered<T>()
        {
            var type = typeof(T);
            
            // Check in registered list (before construction)
            foreach (var (interfaceType, implementationType) in m_registeredManagers)
            {
                if (interfaceType == type || implementationType == type)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Constructs all registered managers and initializes the manager map.
        /// This method should be called once after all managers have been registered.
        /// </summary>
        public void Construct()
        {
            if (m_constructed) return; // Prevent double construction
            
            using (ListPool<IWorldManager>.Get(out var generated))
            {
#if NET6_0_OR_GREATER
                var built = ImmutableDictionary.CreateBuilder<Type, IWorldManager>();
#else
                var built = new Dictionary<Type, IWorldManager>();
#endif

                foreach (var (interfaceType, implementationType) in m_registeredManagers)
                {
                    if (implementationType == null)
                    {
                        Log.Err($"Null registered manager type found!");
                        continue;
                    }

                    if (!ParentType.IsAssignableFrom(implementationType))
                    {
                        Log.Err($"Not a valid manager type {implementationType}");
                        continue;
                    }

                    if (built.ContainsKey(implementationType) &&
                        (implementationType == interfaceType || built.ContainsKey(interfaceType)))
                    {
                        Log.Err($"Duplicated manager type {interfaceType} or {implementationType}");
                        continue;
                    }

                    try
                    {
                        // Create manager instance without calling constructor
                        var manager = (IWorldManager) FormatterServices.GetUninitializedObject(implementationType);
                        
                        // Register manager into injector if possible
                        if (m_injector != null) m_injector.Register(manager);

                        // Register both the interface type and implementation type to the same manager instance
                        if (interfaceType != implementationType) built.Add(interfaceType, manager);
                        built.Add(implementationType, manager);
                        generated.Add(manager);
                    }
                    catch (Exception e)
                    {
                        Log.Err($"Failed to create manager instance for {implementationType}: {e.Message}");
                        continue;
                    }
                }
                
#if NET6_0_OR_GREATER
                m_managerMap = built.ToImmutable();
#else
                m_managerMap = built;
#endif

                // Now inject dependencies for all managers
                if (m_injector != null)
                {
                    foreach (var mgr in generated) m_injector.InjectConstructor(mgr);
                }
            }
            
            m_constructed = true;
        }

        /// <summary>
        /// Boots up all registered managers by calling their lifecycle methods.
        /// Calls OnManagerCreated and OnWorldStarted for all managers.
        /// </summary>
        public void Boot()
        {
            if (Booted) return;
            Booted = true;
            
            var managersToUse = m_managerMap;
            
            // First, call OnManagerCreated for all managers
            foreach (var mgr in managersToUse.Values)
            {
                try
                {
                    mgr.OnManagerCreated();
                }
                catch (Exception e)
                {
                    Log.Exp(e);
                }
            }
            
            // Then, call OnWorldStarted for all managers
            foreach (var mgr in managersToUse.Values)
            {
                try
                {
                    mgr.OnWorldStarted();
                }
                catch (Exception e)
                {
                    Log.Exp(e);
                }
            }
        }

        /// <summary>
        /// Shuts down all registered managers by calling their lifecycle methods.
        /// Calls OnWorldEnded and OnManagerDestroyed for all managers.
        /// </summary>
        public void Shutdown()
        {
            if (!Booted) return;
            Booted = false;
            
            var managersToUse = m_managerMap;
            
            // First, call OnWorldEnded for all managers
            foreach (var mgr in managersToUse.Values)
            {
                try
                {
                    mgr.OnWorldEnded();
                }
                catch (Exception e)
                {
                    Log.Exp(e);
                }
            }

            // Then, call OnManagerDestroyed for all managers
            foreach (var mgr in managersToUse.Values)
            {
                try
                {
                    mgr.OnManagerDestroyed();
                }
                catch (Exception e)
                {
                    Log.Exp(e);
                }
            }
        }
    }
}