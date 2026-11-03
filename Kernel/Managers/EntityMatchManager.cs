using System;
using System.Collections.Generic;
using CoreECS.Defines;
using CoreECS.Structures;
using CoreECS.Utils;

#pragma warning disable CS0618

namespace CoreECS.Managers
{

    /// <summary>
    /// Manages entity collectors and matching logic.
    /// This class is responsible for creating and updating collectors based on entity changes.
    /// </summary>
    public sealed class EntityMatchManager : IWorldManager
    {
        private StructuralMutationGuard m_structuralMutationGuard;

        internal bool HasInjectedGuardForTesting => m_structuralMutationGuard != null;
        
        private const int COLLECTED_BUFFER_INDEX = 0;
        private const int MATCHING_BUFFER_INDEX = 1;
        private const int CLASHING_BUFFER_INDEX = 2;
        private const int CHANGED_BUFFER_INDEX = 3;
        private const int CHANGE_MATCHING_BUFFER_INDEX = 4;
        private const int CHANGE_CLASHING_BUFFER_INDEX = 5;
        private const int CHANGE_CHANGED_BUFFER_INDEX = 6;
        
        /// <summary>
        /// Internal implementation of IEntityCollector that manages multiple buffers for efficient entity tracking.
        /// </summary>
        private class Collector : IEntityCollector, ICollectorSubscriptionTarget
        {

            /// <summary>
            /// Ordered storage for the front and back buffers used by the collector.
            /// Index map:
            /// [0] = collected,
            /// [1] = matching,
            /// [2] = clashing,
            /// [3] = changed,
            /// [4] = change matching,
            /// [5] = change clashing,
            /// [6] = change changed.
            /// </summary>
            public readonly List<EntityId>[] Buffers = new[]
            {
                new List<EntityId>(),
                new List<EntityId>(),
                new List<EntityId>(),
                new List<EntityId>(),
                new List<EntityId>(),
                new List<EntityId>(),
                new List<EntityId>(),
            };

            /// <summary>
            /// Membership indexes kept in sync with <see cref="Buffers"/> so hot-path lookups
            /// can avoid repeated linear scans over the exposed lists.
            /// </summary>
            internal readonly HashSet<EntityId>[] BufferSets = new[]
            {
                new HashSet<EntityId>(),
                new HashSet<EntityId>(),
                new HashSet<EntityId>(),
                new HashSet<EntityId>(),
                new HashSet<EntityId>(),
                new HashSet<EntityId>(),
                new HashSet<EntityId>(),
            };

            /// <summary>
            /// Gets the flags for this collector.
            /// </summary>
            public EntityCollectorFlag Flag { get; }

            /// <summary>
            /// Gets the matcher for this collector.
            /// </summary>
            public EntityMatcher Matcher { get; }

            /// <summary>
            /// Gets the collected entities buffer.
            /// </summary>
            public IReadOnlyList<EntityId> Collected => Buffers[COLLECTED_BUFFER_INDEX];

            /// <summary>
            /// Gets the matching entities buffer.
            /// </summary>
            public IReadOnlyList<EntityId> Matching => Buffers[MATCHING_BUFFER_INDEX];

            /// <summary>
            /// Gets the clashing entities buffer.
            /// </summary>
            public IReadOnlyList<EntityId> Clashing => Buffers[CLASHING_BUFFER_INDEX];

            /// <summary>
            /// Gets the published changed-entity buffer for the current flush phase.
            /// </summary>
            public IReadOnlyList<EntityId> Changed => Buffers[CHANGED_BUFFER_INDEX];

            /// <summary>
            /// Gets a value indicating whether this collector has been destroyed.
            /// </summary>
            public bool Destroyed { get; private set; } = false;

            internal ulong CreatedSequence { get; }

            ulong ICollectorSubscriptionTarget.CreatedSequence => CreatedSequence;

            internal CollectorPendingSet Pending { get; } = new CollectorPendingSet();

            /// <summary>
            /// Gets a value indicating whether component data revisions are mirrored into
            /// <see cref="Changed"/>.
            /// </summary>
            public readonly bool TrackRevisionChanged;

            /// <summary>
            /// Gets a value indicating whether entities entering the collector are mirrored into
            /// <see cref="Changed"/>.
            /// </summary>
            public readonly bool TrackMatchChanged;

            /// <summary>
            /// Gets a value indicating whether entities leaving the collector are mirrored into
            /// <see cref="Changed"/>.
            /// </summary>
            public readonly bool TrackClashChanged;

            /// <summary>
            /// Gets a value indicating whether component-driven <see cref="Changed"/> entries are
            /// limited to matcher-relevant component types.
            /// </summary>
            public readonly bool HasChangeComponent;

            /// <summary>
            /// Summarizes previous changes and starts a new collecting phase.
            /// </summary>
            public void Flush()
            {
                m_manager._prepareCollectorFlush(this);
                // Swap both the ordered buffers and their membership indexes together,
                // otherwise the hash sets would describe the wrong side of the double buffer.
                (Buffers[1], Buffers[2], Buffers[3], Buffers[4], Buffers[5], Buffers[6]) =
                    (Buffers[4], Buffers[5], Buffers[6], Buffers[1], Buffers[2], Buffers[3]);
                (BufferSets[1], BufferSets[2], BufferSets[3], BufferSets[4], BufferSets[5], BufferSets[6]) =
                    (BufferSets[4], BufferSets[5], BufferSets[6], BufferSets[1], BufferSets[2], BufferSets[3]);
                
                // Clear previous change buffers
                ClearBuffer(CHANGE_MATCHING_BUFFER_INDEX);
                ClearBuffer(CHANGE_CLASHING_BUFFER_INDEX);
                ClearBuffer(CHANGE_CHANGED_BUFFER_INDEX);
                
                // Copy data from back to front
                var collected = Buffers[COLLECTED_BUFFER_INDEX];
                var collectedSet = BufferSets[COLLECTED_BUFFER_INDEX];
                var changedMatch = Buffers[MATCHING_BUFFER_INDEX];
                var changedClash = Buffers[CLASHING_BUFFER_INDEX];
                var changedClashSet = BufferSets[CLASHING_BUFFER_INDEX];

                // Must do a removal at the end of match and start of change
                var newLength = collected.Count;
                
                if (changedClash.Count > 0)
                {
                    
                    // Phantom entities are entities that are in clashing buffer but not in collected buffer
                    // We need to remove them from clashing buffer
                    var phantom = 0;
                    var changed = 0;

                    using (DictionaryPool<EntityId, int>.Get(out var memo))
                    {
                        // Cache those collected entities to speed up removal
                        memo.EnsureCapacity(collected.Count);
                        for (var i = 0; i < collected.Count; i++)
                            memo.Add(collected[i], i);

                        // Do removal operations
                        for (var i = changedClash.Count - 1; i >= 0; i--)
                        {
                            var entityId = changedClash[i];
                            if (memo.TryGetValue(entityId, out var removalIdx))
                            {
                                changed += 1;
                                collectedSet.Remove(entityId);
                                memo[collected[^changed]] = removalIdx;
                                memo.Remove(entityId);
                                (collected[removalIdx], collected[^changed]) = (collected[^changed], collected[removalIdx]);
                            }
                            else
                            {
                                phantom += 1;
                                changedClashSet.Remove(entityId);
                                (changedClash[i], changedClash[^phantom]) = (changedClash[^phantom], changedClash[i]);
                            }
                        }
                    }


                    changedClash.RemoveRange(changedClash.Count - phantom, phantom);
                    newLength -= changed;
                }

                // Update back buffer to ensure alignment with front buffer
                if (changedMatch.Count > 0)
                {
                    var startAt = newLength;
                    var appended = 0;

#if NET6_0_OR_GREATER
                    // Ensure collection capacity to reduce reallocation
                    collected.EnsureCapacity(Math.Max(newLength + changedMatch.Count, collected.Count));
#endif
                    
                    for (var i = 0; i < changedMatch.Count; i++)
                    {
                        var entityId = changedMatch[i];
                        if (!collectedSet.Add(entityId)) continue;
                        
                        var finPos = startAt + appended;
                        if (finPos < collected.Count) collected[finPos] = entityId;
                        else collected.Add(entityId);
                        
                        appended += 1;
                    }

                    newLength += appended;
                }
                
                // Shrink array if necessary
                if (newLength < collected.Count)
                {
                    collected.RemoveRange(newLength, collected.Count - newLength);
                }
            }

            /// <summary>
            /// Summarizes previous changes and starts a new collecting phase.
            /// </summary>
            [Obsolete("Use Flush() instead.")]
            public void Change()
            {
                Flush();
            }

            /// <summary>
            /// Releases all resources used by the collector.
            /// Clears all buffers and removes the collector from the EntityMatchManager.
            /// </summary>
            public void Dispose()
            {
                if (Destroyed) return;
                Destroyed = true;
                
                // Clear all buffers
                for (var i = 0; i < Buffers.Length; i++)
                {
                    ClearBuffer(i);
                }
                
                // Remove this collector from the manager's list
                m_manager._onDisposeCollector(this);
            }

            /// <summary>
            /// Initializes a new instance of the Collector class.
            /// </summary>
            /// <param name="matcher">The matcher to use for filtering entities</param>
            /// <param name="flag">The flags that control collector behavior</param>
            /// <param name="manager">The manager that created this collector</param>
            public Collector(
                EntityMatcher matcher,
                EntityCollectorFlag flag,
                EntityMatchManager manager,
                ulong createdSequence)
            {
                Matcher = matcher;
                Flag = flag;
                CreatedSequence = createdSequence;
                TrackRevisionChanged = (flag & EntityCollectorFlag.RevisionAsChange) > 0;
                TrackMatchChanged = (flag & EntityCollectorFlag.MatchAsChange) > 0;
                TrackClashChanged = (flag & EntityCollectorFlag.ClashAsChange) > 0;
                HasChangeComponent = (flag & EntityCollectorFlag.RelatedComponentOnly) > 0;
                m_manager = manager;
            }

            void ICollectorSubscriptionTarget.MarkPending(
                EntityId entityId,
                CollectorPendingReason reason)
            {
                Pending.Mark(entityId, reason);
            }

            /// <summary>
            /// Reference to the manager that created this collector.
            /// </summary>
            private readonly EntityMatchManager m_manager;

            /// <summary>
            /// Checks whether the specified entity is already present in the target buffer.
            /// </summary>
            /// <param name="bufferIndex">Index of the buffer to inspect.</param>
            /// <param name="entityId">Entity identifier to look up.</param>
            /// <returns>True if the entity is tracked by the specified buffer; otherwise false.</returns>
            public bool ContainsInBuffer(int bufferIndex, EntityId entityId)
            {
                return BufferSets[bufferIndex].Contains(entityId);
            }

            /// <summary>
            /// Adds the entity to the target buffer if it is not already tracked there.
            /// </summary>
            /// <param name="bufferIndex">Index of the buffer to update.</param>
            /// <param name="entityId">Entity identifier to add.</param>
            /// <returns>True if the entity was newly added; otherwise false.</returns>
            public bool AddUniqueToBuffer(int bufferIndex, EntityId entityId)
            {
                if (!BufferSets[bufferIndex].Add(entityId)) return false;
                Buffers[bufferIndex].Add(entityId);
                return true;
            }

            /// <summary>
            /// Queues an entity for the next <see cref="Flush"/> <see cref="Changed"/> publish.
            /// </summary>
            /// <param name="entityId">Entity identifier to mark as changed.</param>
            public void MarkChanged(EntityId entityId)
            {
                AddUniqueToBuffer(CHANGE_CHANGED_BUFFER_INDEX, entityId);
            }

            /// <summary>
            /// Removes the entity from the target buffer when it is currently tracked there.
            /// </summary>
            /// <param name="bufferIndex">Index of the buffer to update.</param>
            /// <param name="entityId">Entity identifier to remove.</param>
            /// <returns>True if the entity was removed; otherwise false.</returns>
            public bool RemoveFromBuffer(int bufferIndex, EntityId entityId)
            {
                if (!BufferSets[bufferIndex].Remove(entityId)) return false;
                Buffers[bufferIndex].Remove(entityId);
                return true;
            }

            /// <summary>
            /// Clears both the ordered buffer and its membership index.
            /// </summary>
            /// <param name="bufferIndex">Index of the buffer to reset.</param>
            public void ClearBuffer(int bufferIndex)
            {
                Buffers[bufferIndex].Clear();
                BufferSets[bufferIndex].Clear();
            }

        }

        /// <summary>
        /// Gets the world this manager belongs to.
        /// </summary>
        public IWorld World { get; }

        /// <summary>
        /// Reference to the entity manager for tracking entity changes.
        /// </summary>
        private EntityManager m_entityManager;

        private ComponentManager m_componentManager;

        private ComponentChangeJournal m_changeJournal;

        private readonly CollectorSubscriptions m_subscriptions = new CollectorSubscriptions();

        /// <summary>
        /// List of all collectors managed by this manager.
        /// </summary>
        private readonly List<Collector> m_collectors = new();

        /// <summary>
        /// Number of collectors that care about revision-only changes.
        /// </summary>
        private int m_revisionTrackingCollectorCount;

        /// <summary>
        /// Indicates whether entity change signals are currently subscribed.
        /// </summary>
        private bool m_isSubscribedToEntitySignals;

        /// <summary>
        /// Ensures this manager is subscribed to entity change signals when collectors exist.
        /// </summary>
        private void _ensureEntitySignalSubscriptions()
        {
            if (m_isSubscribedToEntitySignals) return;

            m_entityManager.OnEntityGotComp.Add(_onComponentAdded);
            m_entityManager.OnEntityLoseComp.Add(_onComponentRemoved);
            m_isSubscribedToEntitySignals = true;
        }

        /// <summary>
        /// Releases entity change signal subscriptions once the last collector is gone.
        /// </summary>
        private void _releaseEntitySignalSubscriptionsIfUnused()
        {
            if (!m_isSubscribedToEntitySignals || m_collectors.Count > 0) return;

            m_entityManager.OnEntityGotComp.Remove(_onComponentAdded);
            m_entityManager.OnEntityLoseComp.Remove(_onComponentRemoved);
            m_isSubscribedToEntitySignals = false;
        }

        /// <summary>
        /// Handles component addition events.
        /// </summary>
        /// <param name="entityGraph">The entity graph that changed</param>
        private void _onComponentAdded(EntityGraph entityGraph, Type componentType)
        {
            _markStructuralPending(entityGraph, componentType, true);
        }

        /// <summary>
        /// Handles component removal events.
        /// </summary>
        /// <param name="entityGraph">The entity graph that changed</param>
        /// <param name="componentType">The type of the component that was removed</param>
        private void _onComponentRemoved(EntityGraph entityGraph, Type componentType)
        {
            if (componentType == null)
            {
                MarkEntityLifecycle(entityGraph.NativeEntityId);
                return;
            }

            _markStructuralPending(entityGraph, componentType, false);
        }

        /// <summary>
        /// Marks a structural component event for indexed subscribers without evaluating a matcher.
        /// </summary>
        /// <param name="entityGraph">The entity graph that changed</param>
        /// <param name="componentType">The type of the component that changed</param>
        private void _markStructuralPending(
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

        /// <summary>
        /// Updates a collector based on entity changes.
        /// </summary>
        /// <param name="collector">The collector to update</param>
        /// <param name="entityGraph">The entity graph that changed</param>
        /// <param name="isAdd">True if components were added, false if removed, null if only revision changed</param>
        /// <param name="init">True if this is during initialization</param>
        private void _changeCollector(Collector collector, EntityGraph entityGraph, bool? isAdd, bool init, Type componentType)
        {
            var matcher = collector.Matcher;
            // Quick-pass filter
            if ((matcher.EntityMask & entityGraph.Mask) == 0) return;
            
            var entityId = entityGraph.NativeEntityId;
            
            // Pending match/clash buffers can make an entity "already collected" before it
            // reaches Collected, or keep it in Collected after it is scheduled to leave.
            var alreadyCollected = !init &&
                (collector.ContainsInBuffer(COLLECTED_BUFFER_INDEX, entityId) ||
                 collector.ContainsInBuffer(CHANGE_MATCHING_BUFFER_INDEX, entityId)) &&
                !collector.ContainsInBuffer(CHANGE_CLASHING_BUFFER_INDEX, entityId);
            
            var isMatched = !entityGraph.WishDestroy && matcher.ComponentFilter(entityGraph.RwComponents);

            if (!isAdd.HasValue)
            {
                if (collector.TrackRevisionChanged && alreadyCollected && isMatched
                    && RelevanceGate(collector, matcher, componentType))
                    collector.MarkChanged(entityId);
                return;
            }

            // Membership unchanged, but match-relevant composition changed while still collected.
            if (!(isMatched ^ alreadyCollected))
            {
                if (alreadyCollected && isMatched
                    && RelevanceGate(collector, matcher, componentType))
                    collector.MarkChanged(entityId);
                return;
            }

            if (isMatched)
            {
                collector.RemoveFromBuffer(CHANGE_CLASHING_BUFFER_INDEX, entityId);
                collector.AddUniqueToBuffer(CHANGE_MATCHING_BUFFER_INDEX, entityId);

                if (collector.TrackMatchChanged)
                    collector.MarkChanged(entityId);
            }
            else
            {
                collector.RemoveFromBuffer(CHANGE_MATCHING_BUFFER_INDEX, entityId);
                collector.AddUniqueToBuffer(CHANGE_CLASHING_BUFFER_INDEX, entityId);

                if (collector.TrackClashChanged)
                    collector.MarkChanged(entityId);
            }
        }

        /// <summary>
        /// Determines whether a component event should be mirrored into <see cref="Collector.Changed"/>
        /// based on <see cref="EntityCollectorFlag.RelatedComponentOnly"/> and matcher relevance.
        /// When <paramref name="componentType"/> is null, or <see cref="Collector.HasChangeComponent"/>
        /// is false, always passes.
        /// </summary>
        private static bool RelevanceGate(Collector collector, EntityMatcher matcher, Type componentType)
        {
            if (componentType == null) return true;
            if (!collector.HasChangeComponent) return true;
            return matcher.IsRelevantComponent(componentType);
        }

        /// <summary>
        /// Removes a collector from the manager's list.
        /// </summary>
        /// <param name="collector">The collector to remove</param>
        private bool _onDisposeCollector(Collector collector)
        {
            m_subscriptions.Unregister(collector);
            collector.Pending.Clear();

            if (collector.TrackRevisionChanged)
            {
                m_revisionTrackingCollectorCount -= 1;
                m_entityManager.ReleaseRevisionInterest();
            }

            var removed = m_collectors.Remove(collector);
            if (removed)
                _releaseEntitySignalSubscriptionsIfUnused();

            return removed;
        }
        
        /// <summary>
        /// Creates a new entity collector with the specified matcher.
        /// </summary>
        /// <param name="matcher">The matcher to use for filtering entities</param>
        /// <returns>A new entity collector</returns>
        public IEntityCollector MakeCollector(EntityMatcher matcher)
        {
            return MakeCollector(EntityCollectorFlag.Default, matcher);
        }

        /// <summary>
        /// Creates a new entity collector with the specified matcher and flags.
        /// </summary>
        /// <param name="flag">Flags that control collector behavior</param>
        /// <param name="matcher">The matcher to use for filtering entities</param>
        /// <returns>A new entity collector</returns>
        public IEntityCollector MakeCollector(EntityCollectorFlag flag, EntityMatcher matcher)
        {
            Assertion.IsNotNull(matcher);
            EntityMatcherPlan plan = matcher.Bind();

            _ensureInfrastructure();
            m_changeJournal.AdvanceEpoch();
            ulong createdSequence = m_changeJournal.NextSequence;

            _ensureEntitySignalSubscriptions();

            var c = new Collector(matcher, flag, this, createdSequence);
            m_collectors.Add(c);
            m_subscriptions.Register(c, plan, flag);
            if (c.TrackRevisionChanged)
            {
                m_revisionTrackingCollectorCount += 1;
                m_entityManager.AcquireRevisionInterest();
            }

            var entityManager = World.GetManager<EntityManager>();
            foreach (var ec in entityManager.EntityCaches.Values)
            {
                _changeCollector(c, ec, false, true, null);
            }

            return c;
        }

        internal void PumpChanges()
        {
            _ensureInfrastructure();
            LastDeliveredCountForTesting = 0;
            LastUnrelatedCountForTesting = 0;
            m_changeJournal.Pump(entry =>
            {
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

        internal int LastDeliveredCountForTesting { get; private set; }

        internal int LastUnrelatedCountForTesting { get; private set; }

        internal int RegisteredCollectorCountForTesting =>
            m_subscriptions.RegisteredCountForTesting;

        internal int SubscriptionLinkCountForTesting =>
            m_subscriptions.SubscriptionLinkCountForTesting;

        internal ulong GetCreatedSequenceForTesting(IEntityCollector collector)
        {
            return ((Collector)collector).CreatedSequence;
        }

        internal int PendingCountForTesting(IEntityCollector collector)
        {
            return ((Collector)collector).Pending.Count;
        }

        internal bool HasPendingReasonForTesting(
            IEntityCollector collector,
            EntityId entityId,
            CollectorPendingReason reason)
        {
            return ((Collector)collector).Pending.TryGet(entityId, out CollectorPendingReason actual) &&
                   (actual & reason) == reason;
        }

        private void _prepareCollectorFlush(Collector collector)
        {
            PumpChanges();
            CollectorPendingSet pending = collector.Pending;
            for (int i = 0; i < pending.Count; i++)
            {
                EntityId entityId = pending.GetEntityIdAt(i);
                CollectorPendingReason reason = pending.GetReasonAt(i);
                EntityGraph graph = m_entityManager.Get(entityId);
                if (graph == null)
                {
                    _settleMissingEntity(collector, entityId);
                    continue;
                }

                if ((reason & (CollectorPendingReason.Structural |
                               CollectorPendingReason.Lifecycle)) != 0)
                {
                    bool wasCollected = collector.ContainsInBuffer(
                        COLLECTED_BUFFER_INDEX, entityId) ||
                        collector.ContainsInBuffer(CHANGE_MATCHING_BUFFER_INDEX, entityId);
                    _changeCollector(collector, graph, true, false, null);
                    bool structuralBounce =
                        (reason & CollectorPendingReason.StructuralClashed) != 0;
                    if (wasCollected && structuralBounce &&
                        !collector.ContainsInBuffer(CHANGE_CLASHING_BUFFER_INDEX, entityId))
                    {
                        collector.AddUniqueToBuffer(CHANGE_MATCHING_BUFFER_INDEX, entityId);
                    }
                }
                else if ((reason & CollectorPendingReason.Revision) != 0)
                {
                    _changeCollector(collector, graph, null, false, null);
                }
            }

            pending.Clear();
        }

        private static void _settleMissingEntity(Collector collector, EntityId entityId)
        {
            bool wasCollected = collector.ContainsInBuffer(COLLECTED_BUFFER_INDEX, entityId) ||
                                collector.ContainsInBuffer(CHANGE_MATCHING_BUFFER_INDEX, entityId);
            if (!wasCollected) return;

            collector.RemoveFromBuffer(CHANGE_MATCHING_BUFFER_INDEX, entityId);
            collector.AddUniqueToBuffer(CHANGE_CLASHING_BUFFER_INDEX, entityId);
            if (collector.TrackClashChanged)
            {
                collector.MarkChanged(entityId);
            }
        }

        private void _ensureInfrastructure()
        {
            if (m_componentManager == null)
            {
                m_componentManager = World.GetManager<ComponentManager>();
            }

            m_changeJournal = m_componentManager.ChangeJournal;
        }

        /// <summary>
        /// Called when the manager is created.
        /// </summary>
        public void OnManagerCreated()
        {
            ThrowIfStructuralMutationForbidden();
        }

        /// <summary>
        /// Called when the world starts.
        /// </summary>
        public void OnWorldStarted()
        {
            ThrowIfStructuralMutationForbidden();
        }

        /// <summary>
        /// Called when the world ends.
        /// </summary>
        public void OnWorldEnded()
        {
            ThrowIfStructuralMutationForbidden();
        }

        /// <summary>
        /// Called when the manager is destroyed.
        /// </summary>
        public void OnManagerDestroyed()
        {
            ThrowIfStructuralMutationForbidden();
            foreach (var collector in m_collectors)
            {
                m_subscriptions.Unregister(collector);
                collector.Pending.Clear();
                for (var i = 0; i < collector.Buffers.Length; i++)
                {
                    var buf = collector.Buffers[i];
                    collector.Buffers[i] = null;
                    buf.Clear();
                    var set = collector.BufferSets[i];
                    collector.BufferSets[i] = null;
                    set.Clear();
                }
            }
            
            for (int i = 0; i < m_revisionTrackingCollectorCount; i++)
            {
                m_entityManager.ReleaseRevisionInterest();
            }

            m_collectors.Clear();
            m_subscriptions.Clear();
            m_revisionTrackingCollectorCount = 0;
            _releaseEntitySignalSubscriptionsIfUnused();
            if (m_isSubscribedToEntitySignals)
            {
                m_entityManager.OnEntityGotComp.Remove(_onComponentAdded);
                m_entityManager.OnEntityLoseComp.Remove(_onComponentRemoved);
                m_isSubscribedToEntitySignals = false;
            }
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

        /// <summary>
        /// Initializes a new instance of the EntityMatchManager class.
        /// </summary>
        /// <param name="world">The world this manager belongs to</param>
        /// <param name="entityManager">The entity manager for tracking entity changes</param>
        public EntityMatchManager(IWorld world, EntityManager entityManager)
            : this(world, entityManager, null)
        {
        }

        /// <summary>
        /// Initializes a new instance with an explicitly supplied component manager.
        /// </summary>
        /// <param name="world">The world this manager belongs to</param>
        /// <param name="entityManager">The entity manager for tracking entity changes</param>
        /// <param name="componentManager">The component manager that owns the World mutation guard</param>
        public EntityMatchManager(IWorld world, EntityManager entityManager, ComponentManager componentManager)
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
