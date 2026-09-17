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
    /// Owns the v2 component kernel for one world: the structure registry, the
    /// orchestrator and the observer bridge that forwards structure events to the
    /// component-level signals.
    /// </summary>
    public sealed class ComponentManager : IWorldManager
    {
        private static readonly Emitter<ComponentCreated, ulong, Type> s_addEmitter =
            static (h, entityId, compType) => h(entityId, compType);

        private static readonly Emitter<ComponentDestroyed, ulong, Type> s_rmEmitter =
            static (h, entityId, compType) => h(entityId, compType);

        private static readonly Emitter<ComponentChanged, ulong, Type> s_changeEmitter =
            static (h, entityId, compType) => h(entityId, compType);

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
                m_manager.OnComponentCreated.Emit(
                    structure.Entities[row], ComponentTypeRegistry.GetById(typeId).Type, s_addEmitter);
            }

            public void OnComponentRemoved(Structure structure, int row, uint typeId)
            {
                m_manager.OnComponentRemoved.Emit(
                    structure.Entities[row], ComponentTypeRegistry.GetById(typeId).Type, s_rmEmitter);
            }

            public void OnComponentChanged(Structure structure, int row, uint typeId)
            {
                var entityId = structure.Entities[row];
                if (m_manager.OnComponentChanged.HasReceivers)
                {
                    m_manager.OnComponentChanged.Emit(
                        entityId, ComponentTypeRegistry.GetById(typeId).Type, s_changeEmitter);
                }

                m_manager.ChangeSink?.OnRevisionChanged(entityId, typeId);
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
        public Signal<ComponentCreated> OnComponentCreated { get; } = new();

        /// <summary>
        /// Event triggered when a component is removed.
        /// </summary>
        public Signal<ComponentDestroyed> OnComponentRemoved { get; } = new();

        /// <summary>
        /// Event triggered when a component revision changes.
        /// </summary>
        public Signal<ComponentChanged> OnComponentChanged { get; } = new();

        /// <summary>
        /// Internal fast path used to forward revision changes without going through the
        /// public signal. Wired by <see cref="World.Startup"/> to the entity manager.
        /// </summary>
        internal IComponentChangeSink ChangeSink { get; set; }

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
