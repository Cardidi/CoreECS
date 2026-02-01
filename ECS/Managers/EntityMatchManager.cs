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
        /// <summary>
        /// Internal implementation of IEntityCollector that manages multiple buffers for efficient entity tracking.
        /// </summary>
        private class Collector : IEntityCollector
        {
            // Buffers:
            // [0] = collected
            // [1] = matching
            // [2] = clashing
            // [3] = change matching
            // [4] = change clashing
            public readonly List<ulong>[] Buffers = new[]
            {
                new List<ulong>(),
                new List<ulong>(),
                new List<ulong>(),
                new List<ulong>(),
                new List<ulong>(),
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
            public IReadOnlyList<ulong> Collected => Buffers[0];

            /// <summary>
            /// Gets the matching entities buffer.
            /// </summary>
            public IReadOnlyList<ulong> Matching => Buffers[1];

            /// <summary>
            /// Gets the clashing entities buffer.
            /// </summary>
            public IReadOnlyList<ulong> Clashing => Buffers[2];

            /// <summary>
            /// Gets a value indicating whether this collector has been destroyed.
            /// </summary>
            public bool Destroyed { get; private set; } = false;

            /// <summary>
            /// Summarizes previous changes and starts a new collecting phase.
            /// </summary>
            public void Change()
            {
                (Buffers[1], Buffers[2], Buffers[3], Buffers[4]) = (Buffers[3], Buffers[4], Buffers[1], Buffers[2]);
                
                // Clear previous matching and clashing buffers
                Buffers[3].Clear();
                Buffers[4].Clear();
                
                // Copy data from back to front
                var processRemove = (Flag & EntityCollectorFlag.LazyRemove) > 0;
                var processAdd = (Flag & EntityCollectorFlag.LazyAdd) > 0;
                var changedMatch = Buffers[1];
                var changedClash = Buffers[2];

                // Must do a removal at the end of match and start of change
                var collected = Buffers[0];
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
                                memo[collected[^changed]] = removalIdx;
                                memo.Remove(entityId);
                                (collected[removalIdx], collected[^changed]) = (collected[^changed], collected[removalIdx]);
                            }
                            else
                            {
                                phantom += 1;
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
            /// Releases all resources used by the collector.
            /// Clears all buffers and removes the collector from the EntityMatchManager.
            /// </summary>
            public void Dispose()
            {
                Destroyed = true;
                
                // Clear all buffers
                for (var i = 0; i < Buffers.Length; i++)
                {
                    Buffers[i].Clear();
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
                m_manager = manager;
            }

            /// <summary>
            /// Reference to the manager that created this collector.
            /// </summary>
            private readonly EntityMatchManager m_manager;
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
        /// <param name="isAdd">True if components were added, false if removed</param>
        /// <param name="init">True if this is during initialization</param>
        private void _changeCollector(Collector collector, EntityGraph entityGraph, bool isAdd, bool init)
        {
            var matcher = collector.Matcher;
            // Quick-pass filter
            if ((matcher.EntityMask & entityGraph.Mask) == 0) return;
            
            // Buffers
            var collectBuffer = collector.Buffers[0]; // Now directly use collected buffer
            var matchBuffer = collector.Buffers[3]; // Previously [4], now [3]
            var clashBuffer = collector.Buffers[4]; // Previously [5], now [4]

            // Config
            var dontAdd = (collector.Flag & EntityCollectorFlag.LazyAdd) > 0;
            var dontRemove = (collector.Flag & EntityCollectorFlag.LazyRemove) > 0;
            var entityId = entityGraph.EntityId;
            
            // If lazy removal, it will not represent in collectBuffer.
            var alreadyCollected = !init &&
                (collectBuffer.Contains(entityId) || (dontAdd && matchBuffer.Contains(entityId))) &&
                !(dontRemove && clashBuffer.Contains(entityId));
            
            var isMatched = !entityGraph.WishDestroy && matcher.ComponentFilter(entityGraph.RwComponents);
            
            // Unchanged then do nothing
            if (!(isMatched ^ alreadyCollected)) return;
                
            if (isMatched)
            {
                if (!dontAdd) collectBuffer.Add(entityId);
                clashBuffer.Remove(entityId);
                matchBuffer.Add(entityId);
            }
            else
            {
                if (!dontRemove) collectBuffer.Remove(entityId);
                matchBuffer.Remove(entityId);
                clashBuffer.Add(entityId);
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
            return MakeCollector(EntityCollectorFlag.Lazy, matcher);
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
                }
            }
            
            m_collectors.Clear();

            m_entityManager.OnEntityGotComp.Remove(_onComponentAdded);
            m_entityManager.OnEntityLoseComp.Remove(_onComponentRemoved);
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