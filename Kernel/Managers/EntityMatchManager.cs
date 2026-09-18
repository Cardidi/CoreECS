using System;
using System.Collections.Generic;
using CoreECS.Defines;
using CoreECS.Structures;
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
        /// One deferred component revision write. Settlement is deferred until a collector's
        /// <see cref="Collector.Flush"/> so that the entity's live structure can be evaluated
        /// at publish time.
        /// </summary>
        private readonly struct RevisionEntry
        {
            public readonly ulong EntityId;
            public readonly uint TypeId;

            public RevisionEntry(ulong entityId, uint typeId)
            {
                EntityId = entityId;
                TypeId = typeId;
            }
        }
        
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
            /// can avoid repeated linear scans over the exposed lists. Index 6 (change-changed) is
            /// intentionally unused: dedup for that buffer is handled by the stamp array, and
            /// <see cref="ClearBuffer"/> on index 6 remains harmless.
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
            /// Upper bound (exclusive) for the stamp fast path, i.e. 1,048,576 entries and 8 MB
            /// worst case per collector. Entity ids at or above the cap use the hash-set fallback
            /// instead of growing the stamp array further.
            /// </summary>
            private const int MaxStampIndex = 1 << 20;

            /// <summary>
            /// Per-entity stamp recording the flush phase in which the entity was last appended to
            /// the change-changed buffer. A stamp equal to <see cref="m_changedEpoch"/> means the
            /// entity is already queued for the current phase. Memory use is
            /// 8 bytes × min(maxMarkedId, <see cref="MaxStampIndex"/>) per collector.
            /// </summary>
            private ulong[] m_changedStamp = new ulong[16];

            /// <summary>
            /// Current change-changed phase, incremented after each <see cref="Flush"/> so stale
            /// stamps are implicitly invalidated.
            /// </summary>
            private ulong m_changedEpoch = 1;
            
            /// <summary>
            /// Gets the flags for this collector.
            /// </summary>
            public EntityCollectorFlag Flag { get; }

            /// <summary>
            /// Gets the matcher for this collector.
            /// </summary>
            public IEntityMatcher Matcher { get; }

            /// <summary>
            /// Per-collector cache of the matcher's structure-level result, keyed by structure.
            /// Structures are immutable in composition (they are only created; rows change),
            /// so entries never need invalidation. Only used when <see cref="Matcher"/> is the
            /// built-in <see cref="EntityMatcher"/>.
            /// The matcher must be fully configured before the collector is created:
            /// reconfiguring a matcher afterwards (its condition sets stay mutable) can make
            /// cached structure-level results disagree with the row-level conditions.
            /// </summary>
            public readonly Dictionary<Structure, EntityMatcher.StructureMatch> StructureMatches = new();

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
            /// Next logical journal index this collector must settle. Entries with lower logical
            /// indices were written before the collector was created (or already settled) and are
            /// intentionally invisible to it.
            /// </summary>
            public int JournalCursor;

            /// <summary>
            /// Summarizes previous changes and starts a new collecting phase.
            /// </summary>
            public void Flush()
            {
                // Settle pending revision writes before the swap so they are published by this flush.
                m_manager.SettleRevisions(this);

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

                m_changedEpoch += 1;
                
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

                Array.Clear(m_changedStamp, 0, m_changedStamp.Length);

                // Drop the cached structure-level results
                StructureMatches.Clear();

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
            /// Evaluates the matcher for a live structure row. When the matcher is the built-in
            /// <see cref="EntityMatcher"/>, the structure-level result is cached per structure
            /// and only tag/sparse conditions are evaluated per row. Other
            /// <see cref="IEntityMatcher"/> implementations fall back to
            /// <see cref="IEntityMatcher.ComponentFilter"/> without caching.
            /// </summary>
            /// <param name="structure">Structure owning the row.</param>
            /// <param name="row">Live row inside the structure.</param>
            /// <returns>True when the row matches the collector's matcher.</returns>
            public bool Matches(Structure structure, int row)
            {
                if (Matcher is EntityMatcher fastMatcher)
                {
                    if (!StructureMatches.TryGetValue(structure, out var match))
                    {
                        match = fastMatcher.EvaluateStructure(structure);
                        StructureMatches.Add(structure, match);
                    }

                    return match.Passes && fastMatcher.RowFilter(structure, row, match.AnySatisfied);
                }

                return Matcher.ComponentFilter(structure, row);
            }

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
                if (entityId < MaxStampIndex)
                {
                    var index = (int)entityId;
                    if (index >= m_changedStamp.Length)
                    {
                        var grown = Math.Max(index + 1, m_changedStamp.Length * 2);
                        Array.Resize(ref m_changedStamp, grown);
                    }

                    if (m_changedStamp[index] == m_changedEpoch) return;
                    m_changedStamp[index] = m_changedEpoch;
                    Buffers[CHANGE_CHANGED_BUFFER_INDEX].Add(entityId);
                    return;
                }

                AddUniqueToBuffer(CHANGE_CHANGED_BUFFER_INDEX, entityId);
            }

            /// <summary>
            /// Settles one deferred revision write against the entity's live structure.
            /// Writes that no longer resolve, no longer match, or are irrelevant to the matcher
            /// are dropped; writes that predate this collector are filtered by its journal cursor
            /// before this method is called.
            /// </summary>
            /// <param name="entityId">Entity that owns the changed component.</param>
            /// <param name="typeId">Raw component type id that changed.</param>
            /// <param name="componentType">Resolved component type that changed.</param>
            public void SettleRevision(ulong entityId, uint typeId, Type componentType)
            {
                if (!TrackRevisionChanged) return;
                if (!m_manager.m_entityManager.Table.TryGetLocation(entityId, out var location) || location.Structure == null) return;
                if ((Matcher.EntityMask & location.Structure.Mask) == 0) return;
                if (HasChangeComponent && !Matcher.IsRelevantComponent(componentType)) return;

                var alreadyCollected =
                    (ContainsInBuffer(COLLECTED_BUFFER_INDEX, entityId) ||
                     ContainsInBuffer(CHANGE_MATCHING_BUFFER_INDEX, entityId)) &&
                    !ContainsInBuffer(CHANGE_CLASHING_BUFFER_INDEX, entityId);
                if (!alreadyCollected) return;
                if (!Matches(location.Structure, location.Row)) return;

                MarkChanged(entityId);
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
        /// Number of revision-tracking collectors that gate changes on component relevance
        /// (<see cref="EntityCollectorFlag.RelatedComponentOnly"/>). While zero, settlement
        /// can skip resolving journal entry type ids to <see cref="Type"/> instances.
        /// </summary>
        private int m_relevanceGatedRevisionCollectors;

        /// <summary>
        /// Deferred component revision writes, settled into collectors at flush time.
        /// Physical index 0 corresponds to logical index <see cref="m_journalBase"/>.
        /// </summary>
        private readonly List<RevisionEntry> m_journal = new();

        /// <summary>
        /// Number of journal entries that were compacted away; logical indices are never reused.
        /// </summary>
        private int m_journalBase;

        /// <summary>
        /// Oldest logical index a new write may coalesce into. Writes must not merge into an
        /// entry that some live collector's cursor already skips, so this is raised to the
        /// journal end whenever a collector is created or settles, and reset when the journal is
        /// cleared.
        /// </summary>
        private int m_coalesceFloor;

        /// <summary>
        /// Collectors that consume the revision journal, in creation order.
        /// </summary>
        private readonly List<Collector> m_revisionCollectors = new();

        /// <summary>
        /// Raised when <see cref="HasRevisionInterest"/> transitions. Used by the entity
        /// manager to keep the component manager's cached interest flags current.
        /// </summary>
        internal Action RevisionInterestChanged;

        /// <summary>True when at least one collector consumes revision changes.</summary>
        internal bool HasRevisionInterest => m_revisionTrackingCollectorCount > 0;

        /// <summary>
        /// Next logical journal index to be written.
        /// </summary>
        private int JournalLogicalEnd => m_journalBase + m_journal.Count;

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

            m_entityManager.OnEntityGotComp += _onComponentAdded;
            m_entityManager.OnEntityLoseComp += _onComponentRemoved;
            m_isSubscribedToEntitySignals = true;
        }

        /// <summary>
        /// Releases entity change signal subscriptions once the last collector is gone.
        /// </summary>
        private void _releaseEntitySignalSubscriptionsIfUnused()
        {
            if (!m_isSubscribedToEntitySignals || m_collectors.Count > 0) return;

            m_entityManager.OnEntityGotComp -= _onComponentAdded;
            m_entityManager.OnEntityLoseComp -= _onComponentRemoved;
            m_isSubscribedToEntitySignals = false;
        }

        /// <summary>
        /// Handles component addition events.
        /// </summary>
        /// <param name="entityId">The entity that gained the component</param>
        /// <param name="componentType">The type of the component that was added</param>
        private void _onComponentAdded(ulong entityId, Type componentType)
        {
            _onEntityChanged(entityId, componentType, true);
        }

        /// <summary>
        /// Handles component removal events. A null component type signals entity destruction:
        /// the entity is no longer in the table, so it is evaluated as unmatched and leaves
        /// the collected buffer on the next flush.
        /// </summary>
        /// <param name="entityId">The entity that lost the component</param>
        /// <param name="componentType">The type of the component that was removed</param>
        private void _onComponentRemoved(ulong entityId, Type componentType)
        {
            if (componentType == null)
            {
                foreach (var collector in m_collectors)
                {
                    _changeCollector(collector, entityId, false, false, null, true);
                }

                return;
            }

            _onEntityChanged(entityId, componentType, false);
        }

        /// <summary>
        /// Handles component revision changes. Called directly by the entity manager
        /// (bypassing the public entity signal) with the raw component type id and the
        /// owning entity's location.
        /// </summary>
        /// <param name="entityId">The entity that owns the component</param>
        /// <param name="typeId">The id of the component type that changed</param>
        /// <param name="location">The owning entity's pooled location</param>
        internal void OnRevisionChanged(ulong entityId, uint typeId, EntityLocation location)
        {
            if (m_revisionTrackingCollectorCount == 0) return;
            if (location == null || location.Structure == null) return;

            // A handler may have destroyed the entity and a new entity may already have
            // recycled this pooled location; never let its marker absorb that write.
            if (location.Structure.Entities[location.Row] != entityId) return;

            var pending = location.PendingRevisionIndex;
            if (pending >= m_coalesceFloor && pending < JournalLogicalEnd && location.PendingRevisionTypeId == typeId)
                return;

            m_journal.Add(new RevisionEntry(entityId, typeId));
            location.PendingRevisionIndex = JournalLogicalEnd - 1;
            location.PendingRevisionTypeId = typeId;
        }

        /// <summary>
        /// Handles entity changes by updating all collectors.
        /// </summary>
        /// <param name="entityId">The entity that changed</param>
        /// <param name="componentType">The type of the component that changed</param>
        /// <param name="isAdd">True if components were added, false if removed</param>
        private void _onEntityChanged(ulong entityId, Type componentType, bool isAdd)
        {
            foreach (var collector in m_collectors)
            {
                _changeCollector(collector, entityId, isAdd, false, componentType);
            }
        }

        /// <summary>
        /// Updates a collector based on entity changes. The entity's structure and row are
        /// resolved from the entity table and evaluated with the v2 structure matcher;
        /// destroyed entities are evaluated as unmatched without a table lookup.
        /// </summary>
        /// <param name="collector">The collector to update</param>
        /// <param name="entityId">The entity that changed</param>
        /// <param name="isAdd">True if components were added, false if removed</param>
        /// <param name="init">True if this is during initialization</param>
        /// <param name="componentType">The type of the component that changed</param>
        /// <param name="destroyed">True when the entity was destroyed (no structure lookup)</param>
        private void _changeCollector(Collector collector, ulong entityId, bool isAdd, bool init, Type componentType, bool destroyed = false)
        {
            var matcher = collector.Matcher;

            Structure structure = null;
            var row = 0;
            if (!destroyed)
            {
                if (!m_entityManager.Table.TryGetLocation(entityId, out var location) || location.Structure == null) return;

                structure = location.Structure;
                row = location.Row;
                // Quick-pass filter
                if ((matcher.EntityMask & structure.Mask) == 0) return;
            }

            // Pending match/clash buffers can make an entity "already collected" before it
            // reaches Collected, or keep it in Collected after it is scheduled to leave.
            var alreadyCollected = !init &&
                (collector.ContainsInBuffer(COLLECTED_BUFFER_INDEX, entityId) ||
                 collector.ContainsInBuffer(CHANGE_MATCHING_BUFFER_INDEX, entityId)) &&
                !collector.ContainsInBuffer(CHANGE_CLASHING_BUFFER_INDEX, entityId);

            var isMatched = !destroyed && collector.Matches(structure, row);

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
            {
                m_revisionTrackingCollectorCount -= 1;
                if (collector.HasChangeComponent) m_relevanceGatedRevisionCollectors -= 1;
                m_revisionCollectors.Remove(collector);
                if (m_revisionTrackingCollectorCount == 0) RevisionInterestChanged?.Invoke();
                if (m_revisionCollectors.Count == 0)
                {
                    m_journal.Clear();
                    m_journalBase = 0;
                    m_coalesceFloor = 0;
                    m_entityManager.Table.InvalidatePendingRevisions();
                }
            }

            var removed = m_collectors.Remove(collector);
            if (removed)
                _releaseEntitySignalSubscriptionsIfUnused();

            return removed;
        }

        /// <summary>
        /// Settles journal entries this collector has not consumed yet, then advances its cursor.
        /// </summary>
        /// <param name="collector">Collector being flushed.</param>
        private void SettleRevisions(Collector collector)
        {
            if (!collector.TrackRevisionChanged) return;

            for (var i = collector.JournalCursor; i < JournalLogicalEnd; i++)
            {
                var entry = m_journal[i - m_journalBase];
                var type = m_relevanceGatedRevisionCollectors > 0
                    ? ComponentTypeRegistry.GetById(entry.TypeId).Type
                    : null;
                collector.SettleRevision(entry.EntityId, entry.TypeId, type);

                // Once an entry has been evaluated by a collector it must no longer absorb new
                // writes: coalescing a later write into a consumed entry would drop it for every
                // collector flushing after that write.
                if (m_entityManager.Table.TryGetLocation(entry.EntityId, out var location) &&
                    location.PendingRevisionIndex == i)
                {
                    location.PendingRevisionIndex = -1;
                    location.PendingRevisionTypeId = 0u;
                }
            }

            collector.JournalCursor = JournalLogicalEnd;
            m_coalesceFloor = JournalLogicalEnd;
            CompactJournalIfNeeded();
        }

        /// <summary>
        /// Drops journal entries every revision collector has already consumed. The journal is
        /// cleared outright once no revision collector remains.
        /// </summary>
        private void CompactJournalIfNeeded()
        {
            if (m_journal.Count == 0) return;

            if (m_revisionCollectors.Count == 0)
            {
                m_journal.Clear();
                m_journalBase = 0;
                m_coalesceFloor = 0;
                m_entityManager.Table.InvalidatePendingRevisions();
                return;
            }

            var min = int.MaxValue;
            for (var i = 0; i < m_revisionCollectors.Count; i++)
            {
                var cursor = m_revisionCollectors[i].JournalCursor;
                if (cursor < min) min = cursor;
            }

            var removable = min - m_journalBase;
            if (removable < 1024) return;

            m_journal.RemoveRange(0, removable);
            m_journalBase += removable;
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
            {
                m_revisionTrackingCollectorCount += 1;
                if (c.HasChangeComponent) m_relevanceGatedRevisionCollectors += 1;
                c.JournalCursor = JournalLogicalEnd;
                m_coalesceFloor = JournalLogicalEnd;
                m_revisionCollectors.Add(c);
                if (m_journal.Count > 0) m_entityManager.Table.InvalidatePendingRevisions();
                if (m_revisionTrackingCollectorCount == 1) RevisionInterestChanged?.Invoke();
            }

            foreach (var entityId in m_entityManager.Table.EntityIds)
            {
                _changeCollector(c, entityId, false, true, null);
            }

            return c;
        }

        /// <summary>
        /// Called when the manager is created.
        /// </summary>
        public void OnManagerCreated()
        {
            m_entityManager.ConnectMatchManager(this);
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
            m_entityManager.DisconnectMatchManager();
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

                collector.StructureMatches.Clear();
            }
            
            m_collectors.Clear();
            m_revisionTrackingCollectorCount = 0;
            m_relevanceGatedRevisionCollectors = 0;
            RevisionInterestChanged?.Invoke();
            m_journal.Clear();
            m_journalBase = 0;
            m_coalesceFloor = 0;
            m_entityManager.Table.InvalidatePendingRevisions();
            m_revisionCollectors.Clear();
            _releaseEntitySignalSubscriptionsIfUnused();
            if (m_isSubscribedToEntitySignals)
            {
                m_entityManager.OnEntityGotComp -= _onComponentAdded;
                m_entityManager.OnEntityLoseComp -= _onComponentRemoved;
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