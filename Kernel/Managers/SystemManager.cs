using System;
using System.Collections.Generic;
using CoreECS.Defines;
using CoreECS.Structures;
using CoreECS.Utils;

namespace CoreECS.Managers
{
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
    /// Manages system scheduling and execution in the world.
    /// This class is responsible for registering, unregistering, and executing systems in the correct order.
    /// </summary>
    public sealed class SystemManager : IWorldManager
    {
        private StructuralMutationGuard m_structuralMutationGuard;

        private readonly SystemSchedule m_schedule = new SystemSchedule();
        private int m_registrationGeneration;
        private int m_executionEventDispatchDepth;

        internal bool HasInjectedGuardForTesting => m_structuralMutationGuard != null;
        internal SystemSchedule Schedule => m_schedule;
        internal int ExecutionCacheCopyCount { get; private set; }
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
        /// Event triggered when a system begins execution.
        /// </summary>
        public Signal<SystemBeginExecute> OnSystemBeginExecute { get; } = new();
        
        /// <summary>
        /// Event triggered when a system ends execution.
        /// </summary>
        public Signal<SystemEndExecute> OnSystemEndExecute { get; } = new();
        
        /// <summary>
        /// List of all registered systems.
        /// </summary>
        private readonly List<ISystem> m_systems = new();

        /// <summary>
        /// Dictionary mapping system types to their instances.
        /// </summary>
        private readonly Dictionary<Type, ISystem> m_systemTransformer = new();
        
        /// <summary>
        /// System references captured from the resolved DAG at the latest dirty BeginTick.
        /// </summary>
        private List<ISystem> m_executionCache = new();

        /// <summary>
        /// Injection proxy for resolving system constructor dependencies.
        /// </summary>
        private readonly IInjectionProxy m_injectionProxy;

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
                EmitSystemBeginExecute(system);
                try
                {
                    system.OnTick(systemMask);
                }
                catch (Exception e)
                {
                    Log.Exp(e);
                }
                EmitSystemEndExecute(system);
            }
        }

        private void EmitSystemBeginExecute(ISystem system)
        {
            if (!OnSystemBeginExecute.HasReceivers) return;

            m_executionEventDispatchDepth++;
            try
            {
                OnSystemBeginExecute.Emit(World, system, static (h, w, s) => h(w, s));
            }
            finally
            {
                m_executionEventDispatchDepth--;
            }
        }

        private void EmitSystemEndExecute(ISystem system)
        {
            if (!OnSystemEndExecute.HasReceivers) return;

            m_executionEventDispatchDepth++;
            try
            {
                OnSystemEndExecute.Emit(World, system, static (h, w, s) => h(w, s));
            }
            finally
            {
                m_executionEventDispatchDepth--;
            }
        }

        private void ThrowIfExecutionEventDispatching()
        {
            if (m_executionEventDispatchDepth != 0)
            {
                throw new InvalidOperationException(
                    "System DAG cannot be changed from a system execution observation callback.");
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
            
            return (ISystem) m_injectionProxy.CreateObject(systemType);
        }

        /// <summary>
        /// Sets up all queued systems for execution.
        /// </summary>
        public void TeardownSystems()
        {
            Assertion.IsTrue(m_init, "SystemManager is not initialized yet.");
            Assertion.IsFalse(m_shutdown, "SystemManager has already shutdown.");
            
            m_changable = false;

            if (m_schedule.IsDirty)
            {
                IReadOnlyList<Type> executionOrder = m_schedule.BuildExecutionOrder();
                var nextExecutionCache = new List<ISystem>(executionOrder.Count);
                var activatedSystems = new List<KeyValuePair<Type, ISystem>>();

                try
                {
                    for (int i = 0; i < executionOrder.Count; i++)
                    {
                        Type systemType = executionOrder[i];
                        if (!m_systemTransformer.TryGetValue(systemType, out ISystem system))
                        {
                            system = _instantSystem(systemType);
                            activatedSystems.Add(
                                new KeyValuePair<Type, ISystem>(systemType, system));
                            _createSystem(system);
                        }

                        nextExecutionCache.Add(system);
                    }

                    for (int i = 0; i < activatedSystems.Count; i++)
                    {
                        KeyValuePair<Type, ISystem> activated = activatedSystems[i];
                        m_systemTransformer.Add(activated.Key, activated.Value);
                        m_systems.Add(activated.Value);
                    }

                    m_executionCache = nextExecutionCache;
                    unchecked { ExecutionCacheCopyCount++; }
                }
                catch (Exception e)
                {
                    for (int i = activatedSystems.Count - 1; i >= 0; i--)
                    {
                        KeyValuePair<Type, ISystem> activated = activatedSystems[i];
                        if (m_systemTransformer.TryGetValue(activated.Key,
                                out ISystem published) &&
                            ReferenceEquals(published, activated.Value))
                        {
                            m_systemTransformer.Remove(activated.Key);
                            m_systems.Remove(activated.Value);
                        }

                        _destroySystem(activated.Value);
                    }

                    m_schedule.MarkDirty();
                    Log.Exp(e);
                }
            }
            
        }
        
        /// <summary>
        /// Executes all systems that match the specified system mask.
        /// </summary>
        /// <param name="systemMask">The mask that determines which systems should execute</param>
        public void ExecuteSystems(ulong systemMask)
        {
            Assertion.IsTrue(m_init, "SystemManager is not initialized yet.");
            Assertion.IsFalse(m_shutdown, "SystemManager has already shutdown.");
            
            for (var i = 0; i < m_executionCache.Count; i++)
            {
                var system = m_executionCache[i];
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
            
            var removedTypes = new List<Type>();
            foreach (KeyValuePair<Type, ISystem> pair in m_systemTransformer)
            {
                if (m_schedule.FindSystem(pair.Key) == null)
                    removedTypes.Add(pair.Key);
            }

            for (int i = 0; i < removedTypes.Count; i++)
            {
                Type systemType = removedTypes[i];
                ISystem system = m_systemTransformer[systemType];
                m_systemTransformer.Remove(systemType);
                m_systems.Remove(system);
                _destroySystem(system);
            }
            
            m_changable = true;
            
        }
        
        /// <summary>
        /// Registers a system type with the manager at the root of the schedule tree.
        /// </summary>
        /// <param name="systemType">The type of system to register</param>
        /// <returns>A handle for declaring ordering anchors.</returns>
        public SystemRegistration RegisterSystem(Type systemType)
        {
            return RegisterSystem(systemType, null);
        }

        /// <summary>
        /// Registers a system type with the manager in a named group.
        /// </summary>
        /// <param name="systemType">The type of system to register.</param>
        /// <param name="groupName">The group name, or null for the root.</param>
        /// <returns>A handle for declaring ordering anchors.</returns>
        public SystemRegistration RegisterSystem(Type systemType, string groupName)
        {
            ThrowIfExecutionEventDispatching();
            Assertion.IsFalse(m_shutdown, "SystemManager has already shutdown.");
            Assertion.IsNotNull(systemType);
            Assertion.IsParentTypeTo<ISystem>(systemType);

            SystemGroupNode group = ResolveGroup(groupName);
            if (m_schedule.FindSystem(systemType) != null)
                throw new InvalidOperationException($"System {systemType.FullName} is already registered.");

            ISystem sys = null;
            if (m_changable)
            {
                sys = _instantSystem(systemType);
                m_systemTransformer.Add(systemType, sys);
                m_systems.Add(sys);
            }

            SystemEntryNode node = m_schedule.AddSystem(systemType, group);
            if (sys != null) _createSystem(sys);

            return new SystemRegistration(this, node, m_registrationGeneration);
        }

        /// <summary>Registers a root-level group.</summary>
        public GroupRegistration RegisterGroup(string name,
            GroupInsertMode mode = GroupInsertMode.Later)
        {
            return RegisterGroupCore(name, null, mode);
        }

        /// <summary>Registers a nested group.</summary>
        public GroupRegistration RegisterGroup(string name, string parentName,
            GroupInsertMode mode = GroupInsertMode.Later)
        {
            Assertion.ArgumentNotNull(parentName, nameof(parentName));
            return RegisterGroupCore(name, parentName, mode);
        }

        internal void AddSystemAnchor(SystemEntryNode node, SystemAnchor anchor, int generation)
        {
            ThrowIfExecutionEventDispatching();
            EnsureRegistrationHandleValid(generation);
            if (!ReferenceEquals(m_schedule.FindSystem(node.SystemType), node))
                throw new InvalidOperationException("Registration handle is no longer valid.");

            ValidateAnchor(anchor);
            node.Anchors.Add(anchor);
            m_schedule.MarkDirty();
        }

        internal void AddGroupAnchor(SystemGroupNode node, SystemAnchor anchor, int generation)
        {
            ThrowIfExecutionEventDispatching();
            EnsureRegistrationHandleValid(generation);
            if (!ReferenceEquals(m_schedule.FindGroup(node.Name), node))
                throw new InvalidOperationException("Registration handle is no longer valid.");

            ValidateAnchor(anchor);
            node.Anchors.Add(anchor);
            m_schedule.MarkDirty();
        }

        private GroupRegistration RegisterGroupCore(string name, string parentName,
            GroupInsertMode mode)
        {
            ThrowIfExecutionEventDispatching();
            Assertion.IsFalse(m_shutdown, "SystemManager has already shutdown.");
            Assertion.ArgumentNotNull(name, nameof(name));
            Assertion.IsFalse(string.IsNullOrEmpty(name), "Group name must not be empty.");

            if (mode != GroupInsertMode.Early && mode != GroupInsertMode.Later)
                throw new ArgumentOutOfRangeException(nameof(mode));
            if (m_schedule.FindGroup(name) != null)
                throw new InvalidOperationException($"Group '{name}' is already registered.");

            SystemGroupNode parent = ResolveGroup(parentName);
            SystemGroupNode node = m_schedule.AddGroup(name, parent, mode);
            return new GroupRegistration(this, node, m_registrationGeneration);
        }

        private SystemGroupNode ResolveGroup(string groupName)
        {
            if (groupName == null) return m_schedule.Root;

            SystemGroupNode group = m_schedule.FindGroup(groupName);
            if (group == null)
                throw new InvalidOperationException($"Group '{groupName}' is not registered.");

            return group;
        }

        private void EnsureRegistrationHandleValid(int generation)
        {
            if (m_shutdown || generation != m_registrationGeneration)
                throw new InvalidOperationException("Registration handle is no longer valid.");
        }

        private static void ValidateAnchor(SystemAnchor anchor)
        {
            if (anchor.SystemType != null)
            {
                Assertion.IsParentTypeTo<ISystem>(anchor.SystemType,
                    $"Type {anchor.SystemType.FullName} is not a system type.");
                return;
            }

            Assertion.ArgumentNotNull(anchor.GroupName, nameof(anchor.GroupName));
            Assertion.IsFalse(string.IsNullOrEmpty(anchor.GroupName),
                "Group name must not be empty.");
        }

        /// <summary>
        /// Unregisters a system type from the manager.
        /// </summary>
        /// <param name="systemType">The type of system to unregister</param>
        /// <exception cref="InvalidOperationException">Thrown when the system is not registered</exception>
        public void UnregisterSystem(Type systemType)
        {
            ThrowIfExecutionEventDispatching();
            Assertion.IsFalse(m_shutdown, "SystemManager has already shutdown.");
            Assertion.IsNotNull(systemType);
            Assertion.IsParentTypeTo<ISystem>(systemType);
            
            if (m_schedule.FindSystem(systemType) == null)
                throw new InvalidOperationException($"System {systemType.FullName} is not registered.");

            m_schedule.RemoveSystem(systemType);

            if (m_changable)
            {
                if (m_systemTransformer.TryGetValue(systemType, out ISystem system))
                {
                    m_systemTransformer.Remove(systemType);
                    m_systems.Remove(system);
                    _destroySystem(system);
                }
            }
        }

        #region EventHandlers

        /// <summary>
        /// Called when the manager is created.
        /// </summary>
        public void OnManagerCreated()
        {
            ThrowIfStructuralMutationForbidden();
            unchecked { m_registrationGeneration++; }
            m_init = false;
            m_shutdown = false;
        }

        /// <summary>
        /// Called when the world starts.
        /// </summary>
        public void OnWorldStarted()
        {
            ThrowIfStructuralMutationForbidden();
            m_init = true;
        }

        /// <summary>
        /// Called when the world ends.
        /// </summary>
        public void OnWorldEnded()
        {
            ThrowIfStructuralMutationForbidden();
            m_shutdown = true;
            unchecked { m_registrationGeneration++; }
            
            for (int i = 0; i < m_systems.Count; i++)
            {
                ISystem system = m_systems[i];
                m_systemTransformer.Remove(system.GetType());
                _destroySystem(system);
            }

            m_systems.Clear();
            m_executionCache.Clear();
            m_schedule.ClearSystems();
        }

        /// <summary>
        /// Called when the manager is destroyed.
        /// </summary>
        public void OnManagerDestroyed()
        {
            ThrowIfStructuralMutationForbidden();
        }

        private void ThrowIfStructuralMutationForbidden()
        {
            if (m_structuralMutationGuard == null)
            {
                try
                {
                    m_structuralMutationGuard =
                        World.GetManager<ComponentManager>().StructuralMutationGuard;
                }
                catch (InvalidOperationException)
                {
                    return;
                }
            }

            m_structuralMutationGuard.ThrowIfMutationForbidden();
        }

        #endregion

        /// <summary>
        /// Initializes a new instance of the SystemManager class.
        /// </summary>
        /// <param name="world">The world this manager belongs to</param>
        /// <param name="injectionProxy">Injection proxy for constructor dependency resolution</param>
        public SystemManager(IWorld world, IInjectionProxy injectionProxy)
            : this(world, injectionProxy, null)
        {
        }

        /// <summary>
        /// Initializes a new instance with an explicitly supplied component manager.
        /// </summary>
        /// <param name="world">The world this manager belongs to</param>
        /// <param name="injectionProxy">Injection proxy for constructor dependency resolution</param>
        /// <param name="componentManager">The component manager that owns the World mutation guard</param>
        public SystemManager(IWorld world, IInjectionProxy injectionProxy, ComponentManager componentManager)
        {
            World = world;
            m_injectionProxy = injectionProxy;
            m_structuralMutationGuard = componentManager?.StructuralMutationGuard;
        }
    }
}
