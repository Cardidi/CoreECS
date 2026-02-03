using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using CoreECS.Defines;
using CoreECS.Utils;

namespace CoreECS.Managers
{
    /// <summary>
    /// Delegate for system teardown events.
    /// </summary>
    /// <param name="world">The world in which the system is being torn down</param>
    public delegate void SystemTeardown(IWorld world);
    
    /// <summary>
    /// Delegate for system begin execution events.
    /// </summary>
    /// <param name="world">The world in which the system is executing</param>
    /// <param name="system">The system that is about to execute</param>
    public delegate void SystemBeginExecute(IWorld world, ISystem system);
    
    /// <summary>
    /// Delegate for system end execution events.
    /// </summary>
    /// <param name="world">The world in which the system is executing</param>
    /// <param name="system">The system that has finished executing</param>
    public delegate void SystemEndExecute(IWorld world, ISystem system);

    /// <summary>
    /// Delegate for system cleanup events.
    /// </summary>
    /// <param name="world">The world in which the system is being cleaned up</param>
    public delegate void SystemCleanup(IWorld world);
    
    /// <summary>
    /// Manages system scheduling and execution in the world.
    /// This class is responsible for registering, unregistering, and executing systems in the correct order.
    /// </summary>
    public sealed class SystemManager : IWorldManager
    {
        /// <summary>
        /// Gets the world this manager belongs to.
        /// </summary>
        public IWorld World { get; }
        
        /// <summary>
        /// Gets all registered systems in the manager.
        /// </summary>
        public IReadOnlyList<ISystem> Systems => m_systems;

        /// <summary>
        /// Gets all registered systems in the manager by type.
        /// </summary>
        public IReadOnlyDictionary<Type, ISystem> SystemTransformer => m_systemTransformer;
        
        /// <summary>
        /// Event triggered when systems are being torn down.
        /// </summary>
        public Signal<SystemTeardown> OnSystemTeardown { get; } = new();
        
        /// <summary>
        /// Event triggered when a system begins execution.
        /// </summary>
        public Signal<SystemBeginExecute> OnSystemBeginExecute { get; } = new();
        
        /// <summary>
        /// Event triggered when a system ends execution.
        /// </summary>
        public Signal<SystemEndExecute> OnSystemEndExecute { get; } = new();
        
        /// <summary>
        /// Event triggered when systems are being cleaned up.
        /// </summary>
        public Signal<SystemCleanup> OnSystemCleanup { get; } = new();

        /// <summary>
        /// List of all registered systems.
        /// </summary>
        private readonly List<ISystem> m_systems = new();

        /// <summary>
        /// Dictionary mapping system types to their instances.
        /// </summary>
        private readonly Dictionary<Type, ISystem> m_systemTransformer = new();
        
        /// <summary>
        /// Queue of system types to be removed.
        /// </summary>
        private readonly Queue<Type> m_delSystems = new();
        
        /// <summary>
        /// Queue of system types to be added.
        /// </summary>
        private readonly Queue<Type> m_addSystems = new();

        /// <summary>
        /// Dependency injector for systems.
        /// </summary>
        private readonly Injector m_injector;

        /// <summary>
        /// Indicates whether the manager has been initialized.
        /// </summary>
        private bool m_init = false;

        /// <summary>
        /// Indicates whether the manager is shutting down.
        /// </summary>
        private bool m_shutdown = false;

        /// <summary>
        /// Indicates whether systems can be added or removed.
        /// </summary>
        private bool m_changable = true;

        /// <summary>
        /// Executes a system if it matches the system mask.
        /// </summary>
        /// <param name="system">The system to potentially execute</param>
        /// <param name="systemMask">The mask that determines which systems should execute</param>
        private void _systemPoll(ISystem system, ulong systemMask)
        {
            var selected = (system.TickGroup & systemMask) > 0;
            if (selected)
            {
                OnSystemBeginExecute.Emit(World, system, static (h, w, s) => h(w, s));
                try
                {
                    system.OnTick(systemMask);
                }
                catch (Exception e)
                {
                    Log.Exp(e);
                }
                OnSystemEndExecute.Emit(World, system, static (h, w, s) => h(w, s));
            }
        }

        /// <summary>
        /// Initializes a system by calling its OnCreate method.
        /// </summary>
        /// <param name="system">The system to initialize</param>
        private void _createSystem(ISystem system)
        {
            try
            {
                system.OnCreate();
            }
            catch (Exception e)
            {
                Log.Exp(e);
            }
        }

        /// <summary>
        /// Destroys a system by calling its OnDestroy method.
        /// </summary>
        /// <param name="system">The system to destroy</param>
        private void _destroySystem(ISystem system)
        {
            try
            {
                system.OnDestroy();
            }
            catch (Exception e)
            {
                Log.Exp(e);
            }
        } 

        /// <summary>
        /// Instantiates a system of the specified type.
        /// </summary>
        /// <param name="systemType">The type of system to instantiate</param>
        /// <returns>The instantiated system</returns>
        private ISystem _instantSystem(Type systemType)
        {
            Assertion.IsParentTypeTo<ISystem>(systemType);
            Assertion.IsFalse(m_systemTransformer.ContainsKey(systemType));
            
            var sys = (ISystem) FormatterServices.GetUninitializedObject(systemType);
            if (m_injector != null) m_injector.InjectConstructor(sys);
            return sys;
        }

        /// <summary>
        /// Sets up all queued systems for execution.
        /// </summary>
        public void TeardownSystems()
        {
            Assertion.IsTrue(m_init, "SystemManager is not initialized yet.");
            Assertion.IsFalse(m_shutdown, "SystemManager has already shutdown.");
            
            m_changable = false;

            for (var i = m_addSystems.Count; i > 0; i--)
            {
                var systemType = m_addSystems.Dequeue();
                var sys = _instantSystem(systemType);
                m_systemTransformer.Add(systemType, sys);
                m_systems.Add(sys);
                _createSystem(sys);
            }
            
            OnSystemTeardown.Emit(World, static (h, w) => h(w));
        }
        
        /// <summary>
        /// Executes all systems that match the specified system mask.
        /// </summary>
        /// <param name="systemMask">The mask that determines which systems should execute</param>
        public void ExecuteSystems(ulong systemMask)
        {
            Assertion.IsTrue(m_init, "SystemManager is not initialized yet.");
            Assertion.IsFalse(m_shutdown, "SystemManager has already shutdown.");
            
            for (var i = 0; i < Systems.Count; i++)
            {
                var system = Systems[i];
                _systemPoll(system, systemMask);
            }
        }

        /// <summary>
        /// Cleans up all queued systems for removal.
        /// </summary>
        public void CleanupSystems()
        {
            Assertion.IsTrue(m_init, "SystemManager is not initialized yet.");
            Assertion.IsFalse(m_shutdown, "SystemManager has already shutdown.");
            
            while (m_delSystems.TryDequeue(out var type))
            {
                var sys = m_systemTransformer[type];
                m_systemTransformer.Remove(type);
                m_systems.Remove(sys);
                _destroySystem(sys);
            }
            
            m_changable = true;
            
            OnSystemCleanup.Emit(World, static (h, w) => h(w));
        }
        
        /// <summary>
        /// Registers a system type with the manager.
        /// </summary>
        /// <param name="systemType">The type of system to register</param>
        public void RegisterSystem(Type systemType)
        {
            Assertion.IsFalse(m_shutdown, "SystemManager has already shutdown.");
            Assertion.IsNotNull(systemType);
            Assertion.IsParentTypeTo<ISystem>(systemType);

            if (m_changable)
            {
                var sys = _instantSystem(systemType);
                m_systemTransformer.Add(systemType, sys);
                m_systems.Add(sys);
                _createSystem(sys);
            }
            else
            {
                if (!m_systemTransformer.ContainsKey(systemType) && !m_addSystems.Contains(systemType))
                {
                    m_addSystems.Enqueue(systemType);
                }
            }
        }

        /// <summary>
        /// Unregisters a system type from the manager.
        /// </summary>
        /// <param name="systemType">The type of system to unregister</param>
        public void UnregisterSystem(Type systemType)
        {
            Assertion.IsFalse(m_shutdown, "SystemManager has already shutdown.");
            Assertion.IsNotNull(systemType);
            Assertion.IsParentTypeTo<ISystem>(systemType);
            
            var sys = m_systemTransformer[systemType];

            if (m_changable)
            {
                m_systemTransformer.Remove(systemType);
                m_systems.Remove(sys);
                _destroySystem(sys);
            }
            else
            {
                if (m_systemTransformer.ContainsKey(systemType) && !m_delSystems.Contains(systemType))
                    m_delSystems.Enqueue(systemType);
            }
        }

        #region EventHandlers

        /// <summary>
        /// Called when the manager is created.
        /// </summary>
        public void OnManagerCreated()
        {
            m_init = false;
            m_shutdown = false;
        }

        /// <summary>
        /// Called when the world starts.
        /// </summary>
        public void OnWorldStarted()
        {
            m_init = true;
            if (m_addSystems.TryDequeue(out var type))
            {
                var sys = _instantSystem(type);
                m_systemTransformer.Add(type, sys);
                m_systems.Add(sys);
                _createSystem(sys);
            }
        }

        /// <summary>
        /// Called when the world ends.
        /// </summary>
        public void OnWorldEnded()
        {
            m_shutdown = true;
            
            m_addSystems.Clear();
            m_delSystems.Clear();
            foreach (var system in m_systems) m_delSystems.Enqueue(system.GetType());
            
            while (m_delSystems.TryDequeue(out var type))
            {
                var sys = m_systemTransformer[type];
                m_systemTransformer.Remove(type);
                m_systems.Remove(sys);
                _destroySystem(sys);
            }
        }

        /// <summary>
        /// Called when the manager is destroyed.
        /// </summary>
        public void OnManagerDestroyed()
        {
        }

        #endregion

        /// <summary>
        /// Initializes a new instance of the SystemManager class.
        /// </summary>
        /// <param name="world">The world this manager belongs to</param>
        /// <param name="injector">The dependency injector for systems</param>
        public SystemManager(IWorld world, Injector injector)
        {
            World = world;
            m_injector = injector;
        }
    }
}