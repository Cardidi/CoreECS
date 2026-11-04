using System;
using System.Collections.Generic;
using CoreECS.Managers;
using CoreECS.Structures;

namespace CoreECS
{
    /// <summary>
    /// A long-lived phase observer that publishes net entity membership and eligible changes only
    /// when <see cref="Flush"/> is called.
    /// </summary>
    public sealed class EntityCollector : IDisposable, ICollectorSubscriptionTarget
    {
        private const int CollectedBuffer = 0;
        private const int MatchingBuffer = 1;
        private const int ClashingBuffer = 2;
        private const int ChangedBuffer = 3;
        private const int SpareMatchingBuffer = 4;
        private const int SpareClashingBuffer = 5;
        private const int SpareChangedBuffer = 6;

        private readonly EntityMatchManager m_manager;
        private readonly EntityMatcherPlan m_plan;
        private readonly EntityMatcher m_matcher;
        private readonly EntityCollectorFlag m_flag;
        private readonly List<EntityId>[] m_buffers;
        private readonly HashSet<EntityId>?[] m_phaseSets;
        private readonly Dictionary<EntityId, int> m_collectedSlots =
            new Dictionary<EntityId, int>();

        internal CollectorPendingSet Pending { get; } = new CollectorPendingSet();
        internal ulong CreatedSequence { get; }
        internal bool TrackRevisionChanged { get; }
        internal bool TrackMatchChanged { get; }
        internal bool TrackClashChanged { get; }
        internal bool RelatedComponentOnly { get; }
        internal bool Destroyed { get; private set; }
        internal int LastMatcherEvaluationCountForTesting { get; private set; }

        ulong ICollectorSubscriptionTarget.CreatedSequence => CreatedSequence;

        /// <summary>Gets the frozen matcher used by this collector.</summary>
        public EntityMatcher Matcher
        {
            get
            {
                ThrowIfDisposed();
                return m_matcher;
            }
        }

        /// <summary>Gets the flags controlling publication into <see cref="Changed"/>.</summary>
        public EntityCollectorFlag Flag
        {
            get
            {
                ThrowIfDisposed();
                return m_flag;
            }
        }

        /// <summary>Gets the stable membership after the last <see cref="Flush"/>.</summary>
        public IReadOnlyList<EntityId> Collected
        {
            get
            {
                ThrowIfDisposed();
                return m_buffers[CollectedBuffer];
            }
        }

        /// <summary>Gets net membership entries published by the last <see cref="Flush"/>.</summary>
        public IReadOnlyList<EntityId> Matching
        {
            get
            {
                ThrowIfDisposed();
                return m_buffers[MatchingBuffer];
            }
        }

        /// <summary>Gets net membership exits published by the last <see cref="Flush"/>.</summary>
        public IReadOnlyList<EntityId> Clashing
        {
            get
            {
                ThrowIfDisposed();
                return m_buffers[ClashingBuffer];
            }
        }

        /// <summary>Gets eligible changes published by the last <see cref="Flush"/>.</summary>
        public IReadOnlyList<EntityId> Changed
        {
            get
            {
                ThrowIfDisposed();
                return m_buffers[ChangedBuffer];
            }
        }

        internal EntityCollector(
            EntityMatcher matcher,
            EntityCollectorFlag flag,
            EntityMatchManager manager,
            ulong createdSequence)
        {
            m_matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
            m_flag = flag;
            m_manager = manager ?? throw new ArgumentNullException(nameof(manager));
            CreatedSequence = createdSequence;
            m_plan = matcher.Bind();
            TrackRevisionChanged = (flag & EntityCollectorFlag.RevisionAsChange) != 0;
            TrackMatchChanged = (flag & EntityCollectorFlag.MatchAsChange) != 0;
            TrackClashChanged = (flag & EntityCollectorFlag.ClashAsChange) != 0;
            RelatedComponentOnly = (flag & EntityCollectorFlag.RelatedComponentOnly) != 0;

            m_buffers = new[]
            {
                new List<EntityId>(),
                new List<EntityId>(),
                new List<EntityId>(),
                new List<EntityId>(),
                new List<EntityId>(),
                new List<EntityId>(),
                new List<EntityId>(),
            };
            m_phaseSets = new[]
            {
                null,
                new HashSet<EntityId>(),
                new HashSet<EntityId>(),
                new HashSet<EntityId>(),
                new HashSet<EntityId>(),
                new HashSet<EntityId>(),
                new HashSet<EntityId>(),
            };
        }

        /// <summary>
        /// Pumps the shared change journal and publishes this collector's pending phase. Pumping
        /// distributes reasons to every collector, but only this collector publishes.
        /// </summary>
        public void Flush()
        {
            ThrowIfDisposed();

            m_manager.PumpChanges();
            RotatePhaseBuffers();
            LastMatcherEvaluationCountForTesting = 0;

            for (int i = 0; i < Pending.Count; i++)
            {
                EntityId entityId = Pending.GetEntityIdAt(i);
                CollectorPendingReason reason = Pending.GetReasonAt(i);
                bool wasCollected = m_collectedSlots.ContainsKey(entityId);
                bool isMatched = MatchesFinalState(entityId);
                LastMatcherEvaluationCountForTesting++;

                if (!wasCollected && isMatched)
                {
                    AddCollected(entityId);
                    AddPhase(MatchingBuffer, entityId);
                    if (TrackMatchChanged ||
                        (TrackRevisionChanged &&
                         (reason & CollectorPendingReason.Revision) != 0))
                    {
                        AddPhase(ChangedBuffer, entityId);
                    }
                }
                else if (wasCollected && !isMatched)
                {
                    RemoveCollected(entityId);
                    AddPhase(ClashingBuffer, entityId);
                    if (TrackClashChanged)
                    {
                        AddPhase(ChangedBuffer, entityId);
                    }
                }
                else if (wasCollected)
                {
                    bool structuralChange =
                        (reason & CollectorPendingReason.Structural) != 0;
                    bool revisionChange = TrackRevisionChanged &&
                        (reason & CollectorPendingReason.Revision) != 0;
                    if (structuralChange || revisionChange)
                    {
                        AddPhase(ChangedBuffer, entityId);
                    }
                }
            }

            Pending.Clear();
        }

        /// <summary>Releases this collector and clears its reusable buffers.</summary>
        public void Dispose()
        {
            if (Destroyed) return;
            Destroyed = true;
            ClearState();
            m_manager.OnCollectorDisposed(this);
        }

        void ICollectorSubscriptionTarget.MarkPending(
            EntityId entityId,
            CollectorPendingReason reason)
        {
            if (!Destroyed)
            {
                Pending.Mark(entityId, reason);
            }
        }

        internal void MarkPending(EntityId entityId, CollectorPendingReason reason)
        {
            ((ICollectorSubscriptionTarget)this).MarkPending(entityId, reason);
        }

        internal void InvalidateFromManager()
        {
            if (Destroyed) return;
            Destroyed = true;
            ClearState();
        }

        private bool MatchesFinalState(EntityId entityId)
        {
            return m_manager.Matches(entityId, m_plan);
        }

        private void ThrowIfDisposed()
        {
            if (Destroyed)
            {
                throw new ObjectDisposedException(nameof(EntityCollector));
            }
        }

        private void RotatePhaseBuffers()
        {
            Swap(MatchingBuffer, SpareMatchingBuffer);
            Swap(ClashingBuffer, SpareClashingBuffer);
            Swap(ChangedBuffer, SpareChangedBuffer);

            ClearPhase(MatchingBuffer);
            ClearPhase(ClashingBuffer);
            ClearPhase(ChangedBuffer);
            ClearPhase(SpareMatchingBuffer);
            ClearPhase(SpareClashingBuffer);
            ClearPhase(SpareChangedBuffer);
        }

        private void Swap(int left, int right)
        {
            List<EntityId> list = m_buffers[left];
            m_buffers[left] = m_buffers[right];
            m_buffers[right] = list;

            HashSet<EntityId>? set = m_phaseSets[left];
            m_phaseSets[left] = m_phaseSets[right];
            m_phaseSets[right] = set;
        }

        private void AddPhase(int buffer, EntityId entityId)
        {
            if (m_phaseSets[buffer]!.Add(entityId))
            {
                m_buffers[buffer].Add(entityId);
            }
        }

        private void ClearPhase(int buffer)
        {
            m_buffers[buffer].Clear();
            m_phaseSets[buffer]!.Clear();
        }

        private void AddCollected(EntityId entityId)
        {
            if (m_collectedSlots.ContainsKey(entityId)) return;
            m_collectedSlots.Add(entityId, m_buffers[CollectedBuffer].Count);
            m_buffers[CollectedBuffer].Add(entityId);
        }

        private void RemoveCollected(EntityId entityId)
        {
            if (!m_collectedSlots.TryGetValue(entityId, out int slot)) return;

            List<EntityId> collected = m_buffers[CollectedBuffer];
            int lastSlot = collected.Count - 1;
            EntityId last = collected[lastSlot];
            collected[slot] = last;
            collected.RemoveAt(lastSlot);
            m_collectedSlots.Remove(entityId);
            if (slot != lastSlot)
            {
                m_collectedSlots[last] = slot;
            }
        }

        private void ClearState()
        {
            Pending.Clear();
            m_collectedSlots.Clear();
            for (int i = 0; i < m_buffers.Length; i++)
            {
                m_buffers[i].Clear();
                m_phaseSets[i]?.Clear();
            }
        }
    }
}
