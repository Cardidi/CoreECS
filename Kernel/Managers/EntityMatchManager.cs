using System;
using System.Collections.Generic;
using CoreECS.Defines;
using CoreECS.Structures;
using CoreECS.Utils;

namespace CoreECS.Managers
{
    /// <summary>
    /// Owns collector subscriptions and pumps the shared component revision journal.
    /// </summary>
    public sealed class EntityMatchManager : IWorldManager
    {
        private readonly CollectorSubscriptions m_subscriptions = new CollectorSubscriptions();
        private readonly List<EntityCollector> m_collectors = new List<EntityCollector>();
        private readonly List<EntityId> m_initialEntities = new List<EntityId>();
        private readonly EntityManager m_entityManager;
        private ComponentManager? m_componentManager;
        private ComponentChangeJournal? m_changeJournal;
        private EntityTable? m_entityTable;
        private StructuralMutationGuard? m_structuralMutationGuard;
        private int m_revisionTrackingCollectorCount;
        private readonly Action<ComponentChangeJournal.Entry> m_pumpVisitor;

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
        internal EntityCollector MakeCollector(EntityMatcher matcher)
        {
            return MakeCollector(EntityCollectorFlag.Default, matcher);
        }

        /// <summary>Creates and registers a collector.</summary>
        internal EntityCollector MakeCollector(EntityCollectorFlag flag, EntityMatcher matcher)
        {
            Assertion.IsNotNull(matcher);
            EntityMatcherPlan plan = matcher.Bind();

            EnsureInfrastructure();
            m_changeJournal!.AdvanceEpoch();
            ulong createdSequence = m_changeJournal!.NextSequence;

            var collector = new EntityCollector(matcher, flag, this, createdSequence);
            m_collectors.Add(collector);
            m_subscriptions.Register(collector, plan, flag);
            if (collector.TrackRevisionChanged)
            {
                m_revisionTrackingCollectorCount++;
                m_entityManager.AcquireRevisionInterest();
            }

            m_initialEntities.Clear();
            m_entityTable!.AppendLiveEntityIds(m_initialEntities);
            for (int i = 0; i < m_initialEntities.Count; i++)
            {
                collector.MarkPending(m_initialEntities[i], CollectorPendingReason.Lifecycle);
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
            m_changeJournal!.Pump(m_pumpVisitor);
        }

        private void DispatchPumpEntry(ComponentChangeJournal.Entry entry)
        {
            PumpedEntryCountForTesting++;
            m_subscriptions.DispatchRevision(entry);
            LastDeliveredCountForTesting += m_subscriptions.LastDeliveredCountForTesting;
            LastUnrelatedCountForTesting += m_subscriptions.LastUnrelatedCountForTesting;
        }

        internal void MarkEntityLifecycle(EntityId entityId)
        {
            m_subscriptions.DispatchLifecycle(entityId);
            LastDeliveredCountForTesting = m_subscriptions.LastDeliveredCountForTesting;
            LastUnrelatedCountForTesting = m_subscriptions.LastUnrelatedCountForTesting;
        }

        internal void MarkComponentStructural(
            EntityId entityId,
            uint typeId)
        {
            m_subscriptions.DispatchStructural(typeId, entityId);
            LastDeliveredCountForTesting = m_subscriptions.LastDeliveredCountForTesting;
            LastUnrelatedCountForTesting = m_subscriptions.LastUnrelatedCountForTesting;
        }

        internal void MarkComponentRevision(EntityId entityId, uint typeId)
        {
            m_changeJournal!.Append(entityId, typeId);
        }

        internal bool Matches(EntityId entityId, EntityMatcherPlan plan)
        {
            return m_entityTable!.IsAlive(entityId) &&
                   (plan.EntityMask & m_entityTable!.GetMask(entityId)) != 0 &&
                   plan.Matches(m_entityTable!.GetComponentHead(entityId));
        }

        internal void OnCollectorDisposed(EntityCollector collector)
        {
            m_subscriptions.Unregister(collector);

            if (collector.TrackRevisionChanged)
            {
                m_revisionTrackingCollectorCount--;
                m_entityManager.ReleaseRevisionInterest();
            }

            m_collectors.Remove(collector);
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

        private void EnsureInfrastructure()
        {
            if (m_componentManager == null)
            {
                m_componentManager = World.GetManager<ComponentManager>();
                m_componentManager.AttachEntityMatchManager(this);
            }

            m_changeJournal = m_componentManager.ChangeJournal;
            m_entityTable = m_componentManager.EntityTable;
            m_entityManager.AttachEntityMatchManager(this);
            m_componentManager.AttachEntityMatchManager(this);
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

            m_structuralMutationGuard!.ThrowIfMutationForbidden();
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
            m_initialEntities.Clear();
        }

        public EntityMatchManager(IWorld world, EntityManager entityManager)
            : this(world, entityManager, null)
        {
        }

        public EntityMatchManager(
            IWorld world,
            EntityManager entityManager,
            ComponentManager? componentManager)
        {
            World = world;
            m_entityManager = entityManager;
            m_componentManager = componentManager;
            m_pumpVisitor = DispatchPumpEntry;
            m_changeJournal = componentManager?.ChangeJournal;
            m_entityTable = componentManager?.EntityTable;
            m_structuralMutationGuard = componentManager?.StructuralMutationGuard;
        }
    }
}
