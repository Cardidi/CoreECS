using System;
using System.Collections.Generic;
using CoreECS.Defines;
using CoreECS.Utils;

namespace CoreECS.Managers
{

    /// <summary>
    /// Manages entity collectors and matching logic.
    /// This class is responsible for creating and updating collectors based on entity changes.
    /// </summary>
    public sealed class EntityMatchManager : IWorldManager
    {
        
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
        private class Collector : IEntityCollector
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
            public readonly List<ulong>[] Buffers = new[]
            {
                new List<ulong>(),
                new List<ulong>(),
                new List<ulong>(),
                new List<ulong>(),
                new List<ulong>(),
                new List<ulong>(),
                new List<ulong>(),
            };

            /// <summary>
            /// Membership indexes kept in sync with <see cref="Buffers"/> so hot-path lookups
            /// can avoid repeated linear scans over the exposed lists.
            /// </summary>
            internal readonly HashSet<ulong>[] BufferSets = new[]
            {
                new HashSet<ulong>(),
                new HashSet<ulong>(),
                new HashSet<ulong>(),
                new HashSet<ulong>(),
                new HashSet<ulong>(),
                new HashSet<ulong>(),
                new HashSet<ulong>(),
            };
            
            /// <summary>
            /// Gets the flags for this collector.
            /// </summary>
            public EntityCollectorFlag Flag { get; }

            /// <summary>
            /// Gets the matcher for this collector.
            /// </summary>
            public IEntityMatcher Matcher { get; }

            /// <summary>
            /// Gets the collected entities buffer.
            /// </summary>
            public IReadOnlyList<ulong> Collected => Buffers[COLLECTED_BUFFER_INDEX];

            /// <summary>
            /// Gets the matching entities buffer.
            /// </summary>
            public IReadOnlyList<ulong> Matching => Buffers[MATCHING_BUFFER_INDEX];

            /// <summary>
            /// Gets the clashing entities buffer.
            /// </summary>
            public IReadOnlyList<ulong> Clashing => Buffers[CLASHING_BUFFER_INDEX];

            /// <summary>
            /// Gets the published changed-entity buffer for the current flush phase.
            /// </summary>
            public IReadOnlyList<ulong> Changed => Buffers[CHANGED_BUFFER_INDEX];

            /// <summary>
            /// Gets a value indicating whether this collector has been destroyed.
            /// </summary>
            public bool Destroyed { get; private set; } = false;

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

                    using (DictionaryPool<ulong, int>.Get(out var memo))
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
            public Collector(IEntityMatcher matcher, EntityCollectorFlag flag, EntityMatchManager manager)
            {
                Matcher = matcher;
                Flag = flag;
                TrackRevisionChanged = (flag & EntityCollectorFlag.RevisionAsChange) > 0;
                TrackMatchChanged = (flag & EntityCollectorFlag.MatchAsChange) > 0;
                TrackClashChanged = (flag & EntityCollectorFlag.ClashAsChange) > 0;
                HasChangeComponent = (flag & EntityCollectorFlag.RelatedComponentOnly) > 0;
                m_manager = manager;
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
            public bool ContainsInBuffer(int bufferIndex, ulong entityId)
            {
                return BufferSets[bufferIndex].Contains(entityId);
            }

            /// <summary>
            /// Adds the entity to the target buffer if it is not already tracked there.
            /// </summary>
            /// <param name="bufferIndex">Index of the buffer to update.</param>
            /// <param name="entityId">Entity identifier to add.</param>
            /// <returns>True if the entity was newly added; otherwise false.</returns>
            public bool AddUniqueToBuffer(int bufferIndex, ulong entityId)
            {
                if (!BufferSets[bufferIndex].Add(entityId)) return false;
                Buffers[bufferIndex].Add(entityId);
                return true;
            }

            /// <summary>
            /// Queues an entity for the next <see cref="Flush"/> <see cref="Changed"/> publish.
            /// </summary>
            /// <param name="entityId">Entity identifier to mark as changed.</param>
            public void MarkChanged(ulong entityId)
            {
                AddUniqueToBuffer(CHANGE_CHANGED_BUFFER_INDEX, entityId);
            }

            /// <summary>
            /// Removes the entity from the target buffer when it is currently tracked there.
            /// </summary>
            /// <param name="bufferIndex">Index of the buffer to update.</param>
            /// <param name="entityId">Entity identifier to remove.</param>
            /// <returns>True if the entity was removed; otherwise false.</returns>
            public bool RemoveFromBuffer(int bufferIndex, ulong entityId)
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
            m_entityManager.OnEntityChangeComp.Add(_onComponentChanged);
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
            m_entityManager.OnEntityChangeComp.Remove(_onComponentChanged);
            m_isSubscribedToEntitySignals = false;
        }

        /// <summary>
        /// Handles component addition events.
        /// </summary>
        /// <param name="entityGraph">The entity graph that changed</param>
        private void _onComponentAdded(EntityGraph entityGraph, Type componentType)
        {
            _onEntityChanged(entityGraph, componentType, true);
        }

        /// <summary>
        /// Handles component removal events.
        /// </summary>
        /// <param name="entityGraph">The entity graph that changed</param>
        /// <param name="componentType">The type of the component that was removed</param>
        private void _onComponentRemoved(EntityGraph entityGraph, Type componentType)
        {
            _onEntityChanged(entityGraph, componentType, false);
        }

        /// <summary>
        /// Handles component revision change events.
        /// </summary>
        /// <param name="entityGraph">The entity graph that changed</param>
        /// <param name="componentType">The type of the component that changed</param>
        private void _onComponentChanged(EntityGraph entityGraph, Type componentType)
        {
            if (m_revisionTrackingCollectorCount == 0) return;

            foreach (var collector in m_collectors)
            {
                _changeCollector(collector, entityGraph, null, false, componentType);
            }
        }

        /// <summary>
        /// Handles entity changes by updating all collectors.
        /// </summary>
        /// <param name="entityGraph">The entity graph that changed</param>
        /// <param name="componentType">The type of the component that changed</param>
        /// <param name="isAdd">True if components were added, false if removed</param>
        private void _onEntityChanged(EntityGraph entityGraph, Type componentType, bool isAdd)
        {
            foreach (var collector in m_collectors)
            {
                _changeCollector(collector, entityGraph, isAdd, false, componentType);
            }
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
            
            var entityId = entityGraph.EntityId;
            
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
        private static bool RelevanceGate(Collector collector, IEntityMatcher matcher, Type componentType)
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
            if (collector.TrackRevisionChanged)
                m_revisionTrackingCollectorCount -= 1;

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
        public IEntityCollector MakeCollector(IEntityMatcher matcher)
        {
            return MakeCollector(EntityCollectorFlag.Default, matcher);
        }

        /// <summary>
        /// Creates a new entity collector with the specified matcher and flags.
        /// </summary>
        /// <param name="flag">Flags that control collector behavior</param>
        /// <param name="matcher">The matcher to use for filtering entities</param>
        /// <returns>A new entity collector</returns>
        public IEntityCollector MakeCollector(EntityCollectorFlag flag, IEntityMatcher matcher)
        {
            Assertion.IsNotNull(matcher);

            _ensureEntitySignalSubscriptions();

            var c = new Collector(matcher, flag, this);
            m_collectors.Add(c);
            if (c.TrackRevisionChanged)
                m_revisionTrackingCollectorCount += 1;

            var entityManager = World.GetManager<EntityManager>();
            foreach (var ec in entityManager.EntityCaches.Values)
            {
                _changeCollector(c, ec, false, true, null);
            }

            return c;
        }

        /// <summary>
        /// Called when the manager is created.
        /// </summary>
        public void OnManagerCreated()
        {
        }

        /// <summary>
        /// Called when the world starts.
        /// </summary>
        public void OnWorldStarted()
        {
        }

        /// <summary>
        /// Called when the world ends.
        /// </summary>
        public void OnWorldEnded()
        {
        }

        /// <summary>
        /// Called when the manager is destroyed.
        /// </summary>
        public void OnManagerDestroyed()
        {
            foreach (var collector in m_collectors)
            {
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
            
            m_collectors.Clear();
            m_revisionTrackingCollectorCount = 0;
            _releaseEntitySignalSubscriptionsIfUnused();
            if (m_isSubscribedToEntitySignals)
            {
                m_entityManager.OnEntityGotComp.Remove(_onComponentAdded);
                m_entityManager.OnEntityLoseComp.Remove(_onComponentRemoved);
                m_entityManager.OnEntityChangeComp.Remove(_onComponentChanged);
                m_isSubscribedToEntitySignals = false;
            }
        }

        /// <summary>
        /// Initializes a new instance of the EntityMatchManager class.
        /// </summary>
        /// <param name="world">The world this manager belongs to</param>
        /// <param name="entityManager">The entity manager for tracking entity changes</param>
        public EntityMatchManager(IWorld world, EntityManager entityManager)
        {
            World = world;
            m_entityManager = entityManager;
        }
    }
}