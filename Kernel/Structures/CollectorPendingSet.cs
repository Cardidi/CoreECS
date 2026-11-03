using System;
using System.Collections.Generic;

namespace CoreECS.Structures
{
    [Flags]
    internal enum CollectorPendingReason : byte
    {
        None = 0,
        Structural = 1 << 0,
        Revision = 1 << 1,
        Lifecycle = 1 << 2,
    }

    internal sealed class CollectorPendingSet
    {
        private readonly Dictionary<EntityId, int> m_slots = new Dictionary<EntityId, int>();
        private readonly List<EntityId> m_entities = new List<EntityId>();
        private readonly List<CollectorPendingReason> m_reasons =
            new List<CollectorPendingReason>();

        internal int Count => m_entities.Count;

        internal void Mark(EntityId entityId, CollectorPendingReason reason)
        {
            if (!entityId.IsValid)
            {
                throw new ArgumentException("A pending reason requires a valid EntityId.",
                    nameof(entityId));
            }

            if (reason == CollectorPendingReason.None)
            {
                return;
            }

            if (m_slots.TryGetValue(entityId, out int slot))
            {
                m_reasons[slot] |= reason;
                return;
            }

            slot = m_entities.Count;
            m_slots.Add(entityId, slot);
            m_entities.Add(entityId);
            m_reasons.Add(reason);
        }

        internal bool TryGet(EntityId entityId, out CollectorPendingReason reason)
        {
            if (m_slots.TryGetValue(entityId, out int slot))
            {
                reason = m_reasons[slot];
                return true;
            }

            reason = CollectorPendingReason.None;
            return false;
        }

        internal EntityId GetEntityIdAt(int slot)
        {
            return m_entities[slot];
        }

        internal CollectorPendingReason GetReasonAt(int slot)
        {
            return m_reasons[slot];
        }

        internal void Clear()
        {
            m_slots.Clear();
            m_entities.Clear();
            m_reasons.Clear();
        }
    }
}
