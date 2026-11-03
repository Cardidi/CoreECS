using System;
using System.Collections.Generic;
using CoreECS.Defines;
using CoreECS.Structures;
using CoreECS.Utils;

#pragma warning disable CS0618

#if COREECS_ULONG_ENTITY
using EntityValue = System.UInt64;
#else
using EntityValue = System.UInt32;
#endif

namespace CoreECS.Managers
{
    [Obsolete("Temporary v2 migration bridge")]
    internal delegate void EntityGetComponent(EntityGraph entityGraph, Type componentType);

    [Obsolete("Temporary v2 migration bridge")]
    internal delegate void EntityLoseComponent(EntityGraph entityGraph, Type componentType);

    [Obsolete("Temporary v2 migration bridge")]
    internal delegate void EntityChangeComponent(EntityGraph entityGraph, Type componentType);

    public sealed class EntityManager : IWorldManager
    {
        private static readonly Emitter<EntityGetComponent, EntityGraph, Type> s_getEmitter =
            (handler, graph, componentType) => handler(graph, componentType);
        private static readonly Emitter<EntityLoseComponent, EntityGraph, Type> s_loseEmitter =
            (handler, graph, componentType) => handler(graph, componentType);
        private static readonly Emitter<EntityChangeComponent, EntityGraph, Type> s_changeEmitter =
            (handler, graph, componentType) => handler(graph, componentType);

        private readonly ComponentManager m_componentManager;
        private readonly EntityTable m_entityTable;
        private readonly Dictionary<EntityId, EntityGraph> m_nativeEntityCaches =
            new Dictionary<EntityId, EntityGraph>();
        private readonly Dictionary<ulong, EntityGraph> m_entityCaches =
            new Dictionary<ulong, EntityGraph>();
        private bool m_init;
        private bool m_shutdown;

        public IWorld World { get; }

        [Obsolete("Temporary v2 migration bridge")]
        internal Signal<EntityGetComponent> OnEntityGotComp { get; } = new Signal<EntityGetComponent>();

        [Obsolete("Temporary v2 migration bridge")]
        internal Signal<EntityLoseComponent> OnEntityLoseComp { get; } = new Signal<EntityLoseComponent>();

        [Obsolete("Temporary v2 migration bridge")]
        internal Signal<EntityChangeComponent> OnEntityChangeComp { get; } = new Signal<EntityChangeComponent>();

        [Obsolete("Temporary v2 migration bridge")]
        internal IReadOnlyDictionary<ulong, EntityGraph> EntityCaches => m_entityCaches;

        public EntityManager(IWorld world, ComponentManager compManager)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            m_componentManager = compManager ?? throw new ArgumentNullException(nameof(compManager));
            m_entityTable = compManager.EntityTable;
            m_componentManager.HandlerAttachmentChanged += OnHandlerAttachmentChanged;
            m_componentManager.OnComponentChanged.Add(OnComponentChanged);
        }

        internal EntityId Create(ulong mask)
        {
            m_componentManager.StructuralMutationGuard.ThrowIfMutationForbidden();
            EnsureAvailable();
            EntityId entityId = m_entityTable.Create(mask);
            EntityGraph graph = EntityGraph.Pool.Get();
            graph.NativeEntityId = entityId;
            graph.EntityId = Convert.ToUInt64(entityId.Index);
            graph.Mask = mask;
            graph.WishDestroy = false;
            m_nativeEntityCaches.Add(entityId, graph);
            m_entityCaches.Add(graph.EntityId, graph);
            return entityId;
        }

        internal EntityGraph Get(EntityId entityId)
        {
            return m_entityTable.IsAlive(entityId) && m_nativeEntityCaches.TryGetValue(entityId, out EntityGraph graph)
                ? graph
                : null;
        }

        internal bool IsAlive(EntityId entityId)
        {
            return m_entityTable.IsAlive(entityId);
        }

        internal ulong GetMask(EntityId entityId)
        {
            return m_entityTable.GetMask(entityId);
        }

        internal bool Destroy(EntityId entityId)
        {
            m_componentManager.StructuralMutationGuard.ThrowIfMutationForbidden();
            EnsureAvailable();
            if (!m_entityTable.IsAlive(entityId))
            {
                return false;
            }

            EntityGraph graph = m_nativeEntityCaches[entityId];
            graph.WishDestroy = true;
            ComponentHandler handler;
            while ((handler = m_entityTable.GetComponentHead(entityId)) != null)
            {
                m_componentManager.Destroy(new ComponentRef(handler), entityId);
            }

            if (!m_entityTable.MarkDestroyed(entityId))
            {
                return false;
            }

            m_nativeEntityCaches.Remove(entityId);
            m_entityCaches.Remove(Convert.ToUInt64(entityId.Index));
            OnEntityLoseComp.Emit(graph, null, s_loseEmitter);
            EntityGraph.Pool.Release(graph);
            return true;
        }

        internal void CommitReleases()
        {
            m_componentManager.StructuralMutationGuard.ThrowIfMutationForbidden();
            m_entityTable.CommitReleases();
        }

        internal void AcquireRevisionInterest()
        {
            m_componentManager.AcquireRevisionInterest();
        }

        internal void ReleaseRevisionInterest()
        {
            m_componentManager.ReleaseRevisionInterest();
        }

        internal void AdvanceChangeEpoch()
        {
            m_componentManager.AdvanceChangeEpoch();
        }

        [Obsolete("Temporary v2 migration bridge")]
        internal EntityGraph CreateEntity(ulong mask)
        {
            EntityId entityId = Create(mask);
            return m_nativeEntityCaches[entityId];
        }

        [Obsolete("Temporary v2 migration bridge")]
        internal EntityGraph GetEntity(ulong entityId)
        {
            if (!TryResolveLegacy(entityId, out EntityId nativeId))
            {
                return null;
            }

            return Get(nativeId);
        }

        [Obsolete("Temporary v2 migration bridge")]
        internal void DestroyEntity(ulong entityId)
        {
            if (TryResolveLegacy(entityId, out EntityId nativeId))
            {
                Destroy(nativeId);
            }
        }

        public void OnManagerCreated()
        {
            m_componentManager.StructuralMutationGuard.ThrowIfMutationForbidden();
            m_init = true;
            m_shutdown = false;
        }

        public void OnWorldStarted()
        {
            m_componentManager.StructuralMutationGuard.ThrowIfMutationForbidden();
        }

        public void OnWorldEnded()
        {
            m_componentManager.StructuralMutationGuard.ThrowIfMutationForbidden();
        }

        public void OnManagerDestroyed()
        {
            m_componentManager.StructuralMutationGuard.ThrowIfMutationForbidden();
            m_shutdown = true;
            m_componentManager.HandlerAttachmentChanged -= OnHandlerAttachmentChanged;
            m_componentManager.OnComponentChanged.Remove(OnComponentChanged);
            foreach (EntityGraph graph in m_nativeEntityCaches.Values)
            {
                graph.RwComponents.Clear();
                EntityGraph.Pool.Release(graph);
            }
            m_nativeEntityCaches.Clear();
            m_entityCaches.Clear();
        }

        private void OnHandlerAttachmentChanged(ComponentHandler handler, bool attached)
        {
            if (!m_nativeEntityCaches.TryGetValue(handler.EntityId, out EntityGraph graph))
            {
                return;
            }

            Type componentType = handler.Storage.ComponentType;
            if (attached)
            {
                graph.RwComponents.Add(new HandlerComponentRefCore(handler, handler.BindGeneration));
                OnEntityGotComp.Emit(graph, componentType, s_getEmitter);
                return;
            }

            for (int i = 0; i < graph.RwComponents.Count; i++)
            {
                if (graph.RwComponents[i] is IHandlerComponentRefCore core &&
                    ReferenceEquals(core.Handler, handler) &&
                    core.BindGeneration == handler.BindGeneration)
                {
                    graph.RwComponents.RemoveAt(i);
                    break;
                }
            }

            OnEntityLoseComp.Emit(graph, componentType, s_loseEmitter);
        }

        private void OnComponentChanged(IComponentRefCore core, ulong entityId, Type componentType)
        {
            if (OnEntityChangeComp.HasReceivers && m_entityCaches.TryGetValue(entityId, out EntityGraph graph))
            {
                OnEntityChangeComp.Emit(graph, componentType, s_changeEmitter);
            }
        }

        private bool TryResolveLegacy(ulong entityIndex, out EntityId entityId)
        {
            if (entityIndex == 0 || entityIndex > Convert.ToUInt64(EntityId.MaxIndex))
            {
                entityId = default(EntityId);
                return false;
            }

            return m_entityTable.TryGetCurrent((EntityValue)entityIndex, out entityId);
        }

        private void EnsureAvailable()
        {
            Assertion.IsTrue(m_init);
            Assertion.IsFalse(m_shutdown);
        }
    }
}
#pragma warning restore CS0618
