using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
        /// Flag indicating whether managers have been constructed.
        /// </summary>
        private bool m_constructed;

        private readonly List<IWorldManager> m_managers;
        private readonly List<IWorldManager> m_createdManagers;
        private readonly List<IWorldManager> m_startedManagers;

        /// <summary>
        /// Initializes a new instance of the ManagerMediator class.
        /// </summary>
        /// <param name="world">The world this mediator belongs to</param>
        public ManagerMediator(IWorld world)
        {
            Assertion.IsNotNull(world);

            m_registeredManagers = new List<(Type, Type)>();
            m_managers = new List<IWorldManager>();
            m_createdManagers = new List<IWorldManager>();
            m_startedManagers = new List<IWorldManager>();
            
            // Initialize with empty manager map initially
            m_managerMap = new Dictionary<Type, IWorldManager>();
        }

        /// <summary>
        /// Gets manager type pairs registered before construction.
        /// </summary>
        internal IReadOnlyList<(Type interfaceType, Type implementationType)> RegisteredManagers => m_registeredManagers;
        
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
        /// <param name="injectionProxy">Proxy used to create manager instances</param>
        public void Construct(IInjectionProxy injectionProxy)
        {
            if (m_constructed) return;
            Assertion.ArgumentNotNull(injectionProxy);

#if NET6_0_OR_GREATER
            var built = ImmutableDictionary.CreateBuilder<Type, IWorldManager>();
#else
            var built = new Dictionary<Type, IWorldManager>();
#endif
            var generated = new List<IWorldManager>();
            var seen = new HashSet<IWorldManager>(ReferenceComparer.Instance);

            foreach (var (interfaceType, implementationType) in m_registeredManagers)
            {
                if (implementationType == null)
                {
                    throw new InvalidOperationException("A registered manager type is null.");
                }

                if (!ParentType.IsAssignableFrom(implementationType))
                {
                    throw new InvalidOperationException(
                        $"Not a valid manager type {implementationType}.");
                }

                if (built.ContainsKey(implementationType) &&
                    (implementationType == interfaceType || built.ContainsKey(interfaceType)))
                {
                    throw new InvalidOperationException(
                        $"Duplicated manager type {interfaceType} or {implementationType}.");
                }

                object? resolved = injectionProxy.ServiceProvider.GetService(implementationType);
                if (!(resolved is IWorldManager manager))
                {
                    throw new InvalidOperationException(
                        $"Failed to create manager instance for {implementationType}.");
                }

                if (interfaceType != implementationType)
                    built.Add(interfaceType, manager);
                built.Add(implementationType, manager);
                if (seen.Add(manager))
                    generated.Add(manager);
            }

#if NET6_0_OR_GREATER
            m_managerMap = built.ToImmutable();
#else
            m_managerMap = built;
#endif
            m_managers.Clear();
            m_managers.AddRange(generated);
            m_constructed = true;
        }

        /// <summary>
        /// Boots up all registered managers by calling their lifecycle methods.
        /// Calls OnManagerCreated and OnWorldStarted for all managers.
        /// </summary>
        public void Boot()
        {
            if (Booted) return;

            foreach (IWorldManager manager in m_managers)
            {
                manager.OnManagerCreated();
                m_createdManagers.Add(manager);
            }

            foreach (IWorldManager manager in m_managers)
            {
                manager.OnWorldStarted();
                m_startedManagers.Add(manager);
            }

            Booted = true;
        }

        /// <summary>
        /// Unwinds every manager lifecycle phase that completed during startup.
        /// Rollback failures are logged and do not interrupt the remaining unwind.
        /// </summary>
        internal void RollbackStartup()
        {
            RollbackCompletedManagers();
        }

        /// <summary>
        /// Shuts down all registered managers by calling their lifecycle methods.
        /// Calls OnWorldEnded and OnManagerDestroyed for all managers.
        /// </summary>
        public void Shutdown()
        {
            if (!Booted) return;

            RollbackCompletedManagers();
        }

        private void RollbackCompletedManagers()
        {
            Booted = false;

            for (int index = m_startedManagers.Count - 1; index >= 0; index--)
            {
                try
                {
                    m_startedManagers[index].OnWorldEnded();
                }
                catch (Exception exception)
                {
                    SafeLog(exception);
                }
            }
            m_startedManagers.Clear();

            for (int index = m_createdManagers.Count - 1; index >= 0; index--)
            {
                try
                {
                    m_createdManagers[index].OnManagerDestroyed();
                }
                catch (Exception exception)
                {
                    SafeLog(exception);
                }
            }
            m_createdManagers.Clear();
        }

        private static void SafeLog(Exception exception)
        {
            try
            {
                Log.Exp(exception);
            }
            catch
            {
                // Logging a rollback failure must never replace the startup failure.
            }
        }

        private sealed class ReferenceComparer : IEqualityComparer<IWorldManager>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();

            public bool Equals(IWorldManager? left, IWorldManager? right)
            {
                return ReferenceEquals(left, right);
            }

            public int GetHashCode(IWorldManager value)
            {
                return RuntimeHelpers.GetHashCode(value);
            }
        }
    }
}
