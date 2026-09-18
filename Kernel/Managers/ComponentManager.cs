using System;
using CoreECS.Defines;
using CoreECS.Structures;
using CoreECS.Utils;

namespace CoreECS.Managers
{
    /// <summary>
    /// Delegate for component creation events.
    /// </summary>
    /// <param name="entityId">The ID of the entity that owns the component</param>
    /// <param name="compType">The type of the component that was created</param>
    public delegate void ComponentCreated(ulong entityId, Type compType);

    /// <summary>
    /// Delegate for component destruction events.
    /// </summary>
    /// <param name="entityId">The ID of the entity that owned the component</param>
    /// <param name="compType">The type of the component that was destroyed</param>
    public delegate void ComponentDestroyed(ulong entityId, Type compType);

    /// <summary>
    /// Delegate for component revision change events.
    /// </summary>
    /// <param name="entityId">The ID of the entity that owns the component</param>
    /// <param name="compType">The type of the component that changed</param>
    public delegate void ComponentChanged(ulong entityId, Type compType);
    
    /// <summary>
    /// Delegate for component revision change events.
    /// </summary>
    /// <param name="entityId">The ID of the entity that owns the component</param>
    /// <param name="compType">The type of the component that changed</param>
    internal delegate void ComponentChangedSink(ulong entityId, uint typeId, EntityLocation location);
    
    /// <summary>
    /// Owns the v2 component kernel for one world: the structure registry, the
    /// orchestrator and the observer bridge that forwards structure events to the
    /// component-level signals.
    /// </summary>
    public sealed class ComponentManager : IWorldManager
    {
        /// <summary>
        /// Translates structure observer events into component-level signals.
        /// The entity id is read from the emitting structure row, so events carry the
        /// post-migration structure and row.
        /// </summary>
        private sealed class KernelObserver : IStructureObserver
        {
            private readonly ComponentManager m_manager;

            public KernelObserver(ComponentManager manager)
            {
                m_manager = manager;
            }

            public void OnComponentAdded(Structure structure, int row, uint typeId)
            {
                structure.HasChangeInterest = m_manager.m_hasChangeInterest;
                structure.HasMutatingChangeHandlers = m_manager.m_hasMutatingChangeHandlers;
                m_manager.EmitComponentCreated(structure.Entities[row], ComponentTypeRegistry.GetById(typeId).Type);
            }

            public void OnComponentRemoved(Structure structure, int row, uint typeId)
            {
                m_manager.EmitComponentRemoved(structure.Entities[row], ComponentTypeRegistry.GetById(typeId).Type);
            }

            public void OnComponentChanged(Structure structure, int row, uint typeId)
            {
                var entityId = structure.Entities[row];
                // Capture the location before public handlers run: a migrating handler
                // swap-removes this row, so reading it afterwards would anchor the
                // journal marker to the row's new owner.
                var location = structure.GetLocationAt(row);

                m_manager.EmitComponentChanged(entityId, ComponentTypeRegistry.GetById(typeId).Type);
                m_manager.OnComponentChangeSink.Invoke(entityId, typeId, location);
            }
        }

        /// <summary>Archetype registry owned by this manager.</summary>
        internal StructureRegistry Structures { get; } = new();

        /// <summary>Observer sink handed to the orchestrator.</summary>
        internal IStructureObserver Observer { get; }

        /// <summary>
        /// v2 component kernel orchestrator. Created and injected by <see cref="EntityManager"/>
        /// (which owns the entity table the orchestrator needs).
        /// </summary>
        internal ComponentOrchestrator Orchestrator { get; set; }

        /// <summary>
        /// Event triggered when a component is created. Payload is the owning entity id and
        /// the component type; the v1 component-ref-core payload was removed with v1 storage.
        /// </summary>
        private ComponentCreated m_onComponentCreated;
        public event ComponentCreated OnComponentCreated
        {
            add => m_onComponentCreated += value;
            remove => m_onComponentCreated -= value;
        }

        /// <summary>
        /// Event triggered when a component is removed.
        /// </summary>
        private ComponentDestroyed m_onComponentRemoved;
        public event ComponentDestroyed OnComponentRemoved
        {
            add => m_onComponentRemoved += value;
            remove => m_onComponentRemoved -= value;
        }

        /// <summary>
        /// Event triggered when a component revision changes.
        /// </summary>
        private ComponentChanged m_onComponentChanged;
        public event ComponentChanged OnComponentChanged
        {
            add { m_onComponentChanged += value; _refreshChangeInterest(); }
            remove { m_onComponentChanged -= value; _refreshChangeInterest(); }
        }

        private readonly EventDispatchState m_createdDispatch = new();
        private readonly EventDispatchState m_removedDispatch = new();
        private readonly EventDispatchState m_changedDispatch = new();

        /// <summary>
        /// Internal fast path used to forward revision changes without going through the
        /// public event.
        /// </summary>
        internal event ComponentChangedSink OnComponentChangeSink;

        private bool m_sinkInterest;
        private bool m_sinkMutatingInterest;
        private bool m_hasChangeInterest;
        private bool m_hasMutatingChangeHandlers;

        /// <summary>
        /// True when any revision-change listener (public signal, entity signal or a
        /// revision-tracking collector) needs the observer chain to run.
        /// </summary>
        internal bool HasChangeInterest => m_hasChangeInterest;

        /// <summary>
        /// True when a public change handler (component or entity level) can run during
        /// notification and therefore may migrate or destroy the entity mid-write.
        /// </summary>
        internal bool HasMutatingChangeHandlers => m_hasMutatingChangeHandlers;

        /// <summary>
        /// Updates the interest reported by the entity manager's revision relay and
        /// republishes the cached flags to every live structure.
        /// </summary>
        internal void SetSinkInterest(bool any, bool mutating)
        {
            if (m_sinkInterest == any && m_sinkMutatingInterest == mutating) return;

            m_sinkInterest = any;
            m_sinkMutatingInterest = mutating;
            _refreshChangeInterest();
        }

        private void _refreshChangeInterest()
        {
            m_hasChangeInterest = m_onComponentChanged != null || m_sinkInterest;
            m_hasMutatingChangeHandlers = m_onComponentChanged != null || m_sinkMutatingInterest;

            foreach (var structure in Structures.Structures)
            {
                structure.HasChangeInterest = m_hasChangeInterest;
                structure.HasMutatingChangeHandlers = m_hasMutatingChangeHandlers;
            }
        }

        private void EmitComponentCreated(ulong entityId, Type type)
        {
            var handlers = m_onComponentCreated;
            if (handlers == null) return;

            using (m_createdDispatch.Using)
                handlers(entityId, type);
        }

        private void EmitComponentRemoved(ulong entityId, Type type)
        {
            var handlers = m_onComponentRemoved;
            if (handlers == null) return;

            using (m_removedDispatch.Using)
                handlers(entityId, type);
        }

        private void EmitComponentChanged(ulong entityId, Type type)
        {
            var handlers = m_onComponentChanged;
            if (handlers == null) return;

            using (m_changedDispatch.Using)
                handlers(entityId, type);
        }

        /// <summary>
        /// Initializes a new instance of the ComponentManager class.
        /// </summary>
        public ComponentManager()
        {
            Observer = new KernelObserver(this);
        }

        /// <summary>Called when the manager is created.</summary>
        public void OnManagerCreated() {}

        /// <summary>Called when the world starts.</summary>
        public void OnWorldStarted() {}

        /// <summary>Called when the world ends.</summary>
        public void OnWorldEnded() {}

        /// <summary>Called when the manager is destroyed.</summary>
        public void OnManagerDestroyed() {}
    }
}
