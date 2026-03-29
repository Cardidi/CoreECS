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
            /// Gets the changed entities buffer.
            /// </summary>
            public IReadOnlyList<ulong> Changed => Buffers[CHANGED_BUFFER_INDEX];

            /// <summary>
            /// Gets a value indicating whether this collector has been destroyed.
            /// </summary>
            public bool Destroyed { get; private set; } = false;

            /// <summary>
            /// Gets a value indicating whether matching entities should be deferred until <see cref="Flush"/>.
            /// </summary>
            public readonly bool HasLazyAdd;

            /// <summary>
            /// Gets a value indicating whether removals should be deferred until <see cref="Flush"/>.
            /// </summary>
            public readonly bool HasLazyRemove;

            /// <summary>
            /// Gets a value indicating whether changed entities should be deferred until <see cref="Flush"/>.
            /// </summary>
            public readonly bool HasLazyChange;

            /// <summary>
            /// Gets a value indicating whether revision-only updates should appear in the changed buffer.
            /// </summary>
            public readonly bool TrackRevisionChanged;

            /// <summary>
            /// Gets a value indicating whether entities entering the collector should appear in the changed buffer.
            /// </summary>
            public readonly bool TrackMatchChanged;

            /// <summary>
            /// Gets a value indicating whether entities leaving the collector should appear in the changed buffer.
            /// </summary>
            public readonly bool TrackClashChanged;

            /// <summary>
            /// Summarizes previous changes and starts a new collecting phase.
            /// </summary>
            public void Flush()
            {
                if (HasLazyChange)
                {
                    // Swap both the ordered buffers and their membership indexes together,
                    // otherwise the hash sets would describe the wrong side of the double buffer.
                    (Buffers[1], Buffers[2], Buffers[3], Buffers[4], Buffers[5], Buffers[6]) =
                        (Buffers[4], Buffers[5], Buffers[6], Buffers[1], Buffers[2], Buffers[3]);
                    (BufferSets[1], BufferSets[2], BufferSets[3], BufferSets[4], BufferSets[5], BufferSets[6]) =
                        (BufferSets[4], BufferSets[5], BufferSets[6], BufferSets[1], BufferSets[2], BufferSets[3]);
                }
                else
                {
                    // Matching and clashing are always flush-based summaries,
                    // but Changed is realtime when LazyChange is disabled.
                    (Buffers[1], Buffers[2], Buffers[4], Buffers[5]) =
                        (Buffers[4], Buffers[5], Buffers[1], Buffers[2]);
                    (BufferSets[1], BufferSets[2], BufferSets[4], BufferSets[5]) =
                        (BufferSets[4], BufferSets[5], BufferSets[1], BufferSets[2]);
                }
                
                // Clear previous change buffers
                ClearBuffer(CHANGE_MATCHING_BUFFER_INDEX);
                ClearBuffer(CHANGE_CLASHING_BUFFER_INDEX);
                ClearBuffer(CHANGE_CHANGED_BUFFER_INDEX);
                if (!HasLazyChange)
                {
                    // Start a fresh phase for realtime changed tracking.
                    ClearBuffer(CHANGED_BUFFER_INDEX);
                }
                
                // Copy data from back to front
                var processRemove = HasLazyRemove;
                var processAdd = HasLazyAdd;
                var collected = Buffers[COLLECTED_BUFFER_INDEX];
                var collectedSet = BufferSets[COLLECTED_BUFFER_INDEX];
                var changedMatch = Buffers[MATCHING_BUFFER_INDEX];
                var changedClash = Buffers[CLASHING_BUFFER_INDEX];
                var changedClashSet = BufferSets[CLASHING_BUFFER_INDEX];

                // Must do a removal at the end of match and start of change
                var newLength = collected.Count;
                
                if (processRemove && changedClash.Count > 0)
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
                if (processAdd && changedMatch.Count > 0)
                {
                    var startAt = newLength;
                    newLength += changedMatch.Count;

#if NET6_0_OR_GREATER
                    // Ensure collection capacity to reduce reallocation
                    collected.EnsureCapacity(Math.Max(newLength, collected.Count));
#endif
                    
                    for (var i = 0; i < changedMatch.Count; i++)
                    {
                        collectedSet.Add(changedMatch[i]);
                        var finPos = startAt + i;
                        if (finPos < collected.Count) collected[finPos] = changedMatch[i];
                        else collected.Add(changedMatch[i]);
                    }
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
                HasLazyAdd = (flag & EntityCollectorFlag.LazyAdd) > 0;
                HasLazyRemove = (flag & EntityCollectorFlag.LazyRemove) > 0;
                HasLazyChange = (flag & EntityCollectorFlag.LazyChange) > 0;
                TrackRevisionChanged = (flag & EntityCollectorFlag.ChangedOnRevision) > 0;
                TrackMatchChanged = (flag & EntityCollectorFlag.ChangedOnMatching) > 0;
                TrackClashChanged = (flag & EntityCollectorFlag.ChangedOnClashing) > 0;
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
            /// Marks an entity as changed in either realtime or deferred mode.
            /// </summary>
            /// <param name="entityId">Entity identifier to mark as changed.</param>
            public void MarkChanged(ulong entityId)
            {
                AddUniqueToBuffer(HasLazyChange ? CHANGE_CHANGED_BUFFER_INDEX : CHANGED_BUFFER_INDEX, entityId);
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
        /// Handles component addition events.
        /// </summary>
        /// <param name="entityGraph">The entity graph that changed</param>
        private void _onComponentAdded(EntityGraph entityGraph)
        {
            _onEntityChanged(entityGraph, true);
        }

        /// <summary>
        /// Handles component removal events.
        /// </summary>
        /// <param name="entityGraph">The entity graph that changed</param>
        private void _onComponentRemoved(EntityGraph entityGraph)
        {
            _onEntityChanged(entityGraph, false);
        }

        /// <summary>
        /// Handles component revision change events.
        /// </summary>
        /// <param name="entityGraph">The entity graph that changed</param>
        private void _onComponentChanged(EntityGraph entityGraph)
        {
            foreach (var collector in m_collectors)
            {
                _changeCollector(collector, entityGraph, null, false);
            }
        }

        /// <summary>
        /// Handles entity changes by updating all collectors.
        /// </summary>
        /// <param name="entityGraph">The entity graph that changed</param>
        /// <param name="isAdd">True if components were added, false if removed</param>
        private void _onEntityChanged(EntityGraph entityGraph, bool isAdd)
        {
            foreach (var collector in m_collectors)
            {
                _changeCollector(collector, entityGraph, isAdd, false);
            }
        }

        /// <summary>
        /// Updates a collector based on entity changes.
        /// </summary>
        /// <param name="collector">The collector to update</param>
        /// <param name="entityGraph">The entity graph that changed</param>
        /// <param name="isAdd">True if components were added, false if removed, null if only revision changed</param>
        /// <param name="init">True if this is during initialization</param>
        private void _changeCollector(Collector collector, EntityGraph entityGraph, bool? isAdd, bool init)
        {
            var matcher = collector.Matcher;
            // Quick-pass filter
            if ((matcher.EntityMask & entityGraph.Mask) == 0) return;
            
            // Config
            var dontAdd = collector.HasLazyAdd;
            var dontRemove = collector.HasLazyRemove;
            var entityId = entityGraph.EntityId;
            
            // LazyAdd can make an entity "already collected" before it reaches Collected,
            // while LazyRemove can keep it in Collected even after it is scheduled to leave.
            var alreadyCollected = !init &&
                (collector.ContainsInBuffer(COLLECTED_BUFFER_INDEX, entityId) ||
                 (dontAdd && collector.ContainsInBuffer(CHANGE_MATCHING_BUFFER_INDEX, entityId))) &&
                !(dontRemove && collector.ContainsInBuffer(CHANGE_CLASHING_BUFFER_INDEX, entityId));
            
            var isMatched = !entityGraph.WishDestroy && matcher.ComponentFilter(entityGraph.RwComponents);

            if (!isAdd.HasValue)
            {
                if (collector.TrackRevisionChanged && alreadyCollected && isMatched)
                    collector.MarkChanged(entityId);
                return;
            }

            // Membership unchanged, but structure still changed while the entity stayed in the collector.
            if (!(isMatched ^ alreadyCollected))
            {
                if (alreadyCollected && isMatched) collector.MarkChanged(entityId);
                return;
            }

            if (isMatched)
            {
                if (!dontAdd) collector.AddUniqueToBuffer(COLLECTED_BUFFER_INDEX, entityId);
                collector.RemoveFromBuffer(CHANGE_CLASHING_BUFFER_INDEX, entityId);
                collector.AddUniqueToBuffer(CHANGE_MATCHING_BUFFER_INDEX, entityId);

                if (collector.TrackMatchChanged)
                    collector.MarkChanged(entityId);
            }
            else
            {
                if (!dontRemove) collector.RemoveFromBuffer(COLLECTED_BUFFER_INDEX, entityId);
                collector.RemoveFromBuffer(CHANGE_MATCHING_BUFFER_INDEX, entityId);
                collector.AddUniqueToBuffer(CHANGE_CLASHING_BUFFER_INDEX, entityId);

                if (collector.TrackClashChanged)
                    collector.MarkChanged(entityId);
            }
        }

        /// <summary>
        /// Removes a collector from the manager's list.
        /// </summary>
        /// <param name="collector">The collector to remove</param>
        private bool _onDisposeCollector(Collector collector)
        {
            return m_collectors.Remove(collector);
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
            
            var c = new Collector(matcher, flag, this);
            m_collectors.Add(c);

            var entityManager = World.GetManager<EntityManager>();
            foreach (var ec in entityManager.EntityCaches.Values)
            {
                _changeCollector(c, ec, false, true);
            }

            return c;
        }

        /// <summary>
        /// Called when the manager is created.
        /// </summary>
        public void OnManagerCreated()
        {
            m_entityManager.OnEntityGotComp.Add(_onComponentAdded);
            m_entityManager.OnEntityLoseComp.Add(_onComponentRemoved);
            m_entityManager.OnEntityChangeComp.Add(_onComponentChanged);
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

            m_entityManager.OnEntityGotComp.Remove(_onComponentAdded);
            m_entityManager.OnEntityLoseComp.Remove(_onComponentRemoved);
            m_entityManager.OnEntityChangeComp.Remove(_onComponentChanged);
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