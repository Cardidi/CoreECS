using System;
using System.Collections.Generic;
using CoreECS.Defines;
using CoreECS.Structures;
using CoreECS.Utils;

#pragma warning disable CS0618

namespace CoreECS.Managers
{
    /// <summary>
    /// Owns collector subscriptions and pumps the shared component revision journal.
    /// </summary>
    public sealed class EntityMatchManager : IWorldManager
    {
        private readonly CollectorSubscriptions m_subscriptions = new CollectorSubscriptions();
        private readonly List<EntityCollector> m_collectors = new List<EntityCollector>();
        private EntityManager m_entityManager;
        private ComponentManager m_componentManager;
        private ComponentChangeJournal m_changeJournal;
        private StructuralMutationGuard m_structuralMutationGuard;
        private int m_revisionTrackingCollectorCount;
        private bool m_isSubscribedToEntitySignals;

        /// <summary>Gets the World that owns this manager.</summary>
        public IWorld World { get; }

        internal bool HasInjectedGuardForTesting => m_structuralMutationGuard != null;
        internal int LastDeliveredCountForTesting { get; private set; }
        internal int LastUnrelatedCountForTesting { get; private set; }
        internal int PumpCountForTesting { get; private set; }
        internal int PumpedEntryCountForTesting { get; private set; }
        internal int RegisteredCollectorCountForTesting =>
            m_subscriptions.RegisteredCountForTesting;
        internal int SubscriptionLinkCountForTesting =>
            m_subscriptions.SubscriptionLinkCountForTesting;

        /// <summary>Creates a collector with default flags.</summary>
        public EntityCollector MakeCollector(EntityMatcher matcher)
        {
            return MakeCollector(EntityCollectorFlag.Default, matcher);
        }

        /// <summary>Creates and registers a collector.</summary>
        public EntityCollector MakeCollector(EntityCollectorFlag flag, EntityMatcher matcher)
        {
            Assertion.IsNotNull(matcher);
            EntityMatcherPlan plan = matcher.Bind();

            EnsureInfrastructure();
            m_changeJournal.AdvanceEpoch();
            ulong createdSequence = m_changeJournal.NextSequence;
            EnsureEntitySignalSubscriptions();

            var collector = new EntityCollector(matcher, flag, this, createdSequence);
            m_collectors.Add(collector);
            m_subscriptions.Register(collector, plan, flag);
            if (collector.TrackRevisionChanged)
            {
                m_revisionTrackingCollectorCount++;
                m_entityManager.AcquireRevisionInterest();
            }

            foreach (EntityGraph graph in m_entityManager.EntityCaches.Values)
            {
                collector.MarkPending(graph.NativeEntityId, CollectorPendingReason.Lifecycle);
            }

            return collector;
        }

        /// <summary>
        /// Consumes every journal entry not previously pumped and distributes it to all eligible
        /// collector pending sets.
        /// </summary>
        internal void PumpChanges()
        {
            EnsureInfrastructure();
            PumpCountForTesting++;
            LastDeliveredCountForTesting = 0;
            LastUnrelatedCountForTesting = 0;
            m_changeJournal.Pump(entry =>
            {
                PumpedEntryCountForTesting++;
                m_subscriptions.DispatchRevision(entry);
                LastDeliveredCountForTesting += m_subscriptions.LastDeliveredCountForTesting;
                LastUnrelatedCountForTesting += m_subscriptions.LastUnrelatedCountForTesting;
            });
        }

        internal void MarkEntityLifecycle(EntityId entityId)
        {
            m_subscriptions.DispatchLifecycle(entityId);
            LastDeliveredCountForTesting = m_subscriptions.LastDeliveredCountForTesting;
            LastUnrelatedCountForTesting = m_subscriptions.LastUnrelatedCountForTesting;
        }

        internal EntityGraph GetEntityGraph(EntityId entityId)
        {
            return m_entityManager.Get(entityId);
        }

        internal void OnCollectorDisposed(EntityCollector collector)
        {
            m_subscriptions.Unregister(collector);

            if (collector.TrackRevisionChanged)
            {
                m_revisionTrackingCollectorCount--;
                m_entityManager.ReleaseRevisionInterest();
            }

            if (m_collectors.Remove(collector))
            {
                ReleaseEntitySignalSubscriptionsIfUnused();
            }
        }

        internal ulong GetCreatedSequenceForTesting(EntityCollector collector)
        {
            return collector.CreatedSequence;
        }

        internal int PendingCountForTesting(EntityCollector collector)
        {
            return collector.Pending.Count;
        }

        internal bool HasPendingReasonForTesting(
            EntityCollector collector,
            EntityId entityId,
            CollectorPendingReason reason)
        {
            return collector.Pending.TryGet(entityId, out CollectorPendingReason actual) &&
                   (actual & reason) == reason;
        }

        private void EnsureEntitySignalSubscriptions()
        {
            if (m_isSubscribedToEntitySignals) return;
            m_entityManager.OnEntityGotComp.Add(OnComponentAdded);
            m_entityManager.OnEntityLoseComp.Add(OnComponentRemoved);
            m_isSubscribedToEntitySignals = true;
        }

        private void ReleaseEntitySignalSubscriptionsIfUnused()
        {
            if (!m_isSubscribedToEntitySignals || m_collectors.Count != 0) return;
            m_entityManager.OnEntityGotComp.Remove(OnComponentAdded);
            m_entityManager.OnEntityLoseComp.Remove(OnComponentRemoved);
            m_isSubscribedToEntitySignals = false;
        }

        private void OnComponentAdded(EntityGraph entityGraph, Type componentType)
        {
            MarkStructuralPending(entityGraph, componentType, true);
        }

        private void OnComponentRemoved(EntityGraph entityGraph, Type componentType)
        {
            if (componentType == null)
            {
                MarkEntityLifecycle(entityGraph.NativeEntityId);
                return;
            }

            MarkStructuralPending(entityGraph, componentType, false);
        }

        private void MarkStructuralPending(
            EntityGraph entityGraph,
            Type componentType,
            bool added)
        {
            uint typeId = ComponentTypeRegistry.Register(componentType).TypeId;
            CollectorPendingReason reason = CollectorPendingReason.Structural |
                (added
                    ? CollectorPendingReason.StructuralAdded
                    : CollectorPendingReason.StructuralRemoved);
            m_subscriptions.DispatchStructural(typeId, entityGraph.NativeEntityId, reason,
                entityGraph.RwComponents);
            LastDeliveredCountForTesting = m_subscriptions.LastDeliveredCountForTesting;
            LastUnrelatedCountForTesting = m_subscriptions.LastUnrelatedCountForTesting;
        }

        private void EnsureInfrastructure()
        {
            if (m_componentManager == null)
            {
                m_componentManager = World.GetManager<ComponentManager>();
            }

            m_changeJournal = m_componentManager.ChangeJournal;
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

        public void OnManagerCreated()
        {
            ThrowIfStructuralMutationForbidden();
        }

        public void OnWorldStarted()
        {
            ThrowIfStructuralMutationForbidden();
        }

        public void OnWorldEnded()
        {
            ThrowIfStructuralMutationForbidden();
        }

        public void OnManagerDestroyed()
        {
            ThrowIfStructuralMutationForbidden();
            for (int i = 0; i < m_collectors.Count; i++)
            {
                EntityCollector collector = m_collectors[i];
                m_subscriptions.Unregister(collector);
                collector.InvalidateFromManager();
            }

            for (int i = 0; i < m_revisionTrackingCollectorCount; i++)
            {
                m_entityManager.ReleaseRevisionInterest();
            }

            m_collectors.Clear();
            m_subscriptions.Clear();
            m_revisionTrackingCollectorCount = 0;
            ReleaseEntitySignalSubscriptionsIfUnused();
            if (m_isSubscribedToEntitySignals)
            {
                m_entityManager.OnEntityGotComp.Remove(OnComponentAdded);
                m_entityManager.OnEntityLoseComp.Remove(OnComponentRemoved);
                m_isSubscribedToEntitySignals = false;
            }
        }

        public EntityMatchManager(IWorld world, EntityManager entityManager)
            : this(world, entityManager, null)
        {
        }

        public EntityMatchManager(
            IWorld world,
            EntityManager entityManager,
            ComponentManager componentManager)
        {
            World = world;
            m_entityManager = entityManager;
            m_componentManager = componentManager;
            m_changeJournal = componentManager?.ChangeJournal;
            m_structuralMutationGuard = componentManager?.StructuralMutationGuard;
        }
    }
}

#pragma warning restore CS0618
