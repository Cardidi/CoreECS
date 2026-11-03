using System;
using System.Collections.Generic;

namespace CoreECS.Structures
{
    internal sealed class ComponentChangeJournal
    {
        internal readonly struct Entry
        {
            internal ulong Sequence { get; }

            internal EntityId EntityId { get; }

            internal uint TypeId { get; }

            internal Entry(ulong sequence, EntityId entityId, uint typeId)
            {
                Sequence = sequence;
                EntityId = entityId;
                TypeId = typeId;
            }
        }

        private readonly List<Entry> m_entries = new List<Entry>();
        private readonly HashSet<ChangeKey> m_epochReasons = new HashSet<ChangeKey>();

        internal ulong CurrentEpoch { get; private set; } = 1;

        internal ulong NextSequence { get; private set; } = 1;

        internal int ReasonCountForTesting => m_entries.Count;

        internal void Append(EntityId entityId, uint typeId)
        {
            var key = new ChangeKey(entityId, typeId);
            if (m_epochReasons.Contains(key))
            {
                return;
            }

            if (NextSequence == ulong.MaxValue)
            {
                throw new InvalidOperationException("The component change journal sequence space is exhausted.");
            }

            m_epochReasons.Add(key);
            m_entries.Add(new Entry(NextSequence, entityId, typeId));
            NextSequence++;
        }

        internal void AdvanceEpoch()
        {
            m_epochReasons.Clear();
            CurrentEpoch = CurrentEpoch == ulong.MaxValue ? 1 : CurrentEpoch + 1;
        }

        internal void Reset()
        {
            m_entries.Clear();
            m_epochReasons.Clear();
            CurrentEpoch = 1;
            NextSequence = 1;
        }

        internal void Pump(Action<Entry> visitor)
        {
            if (visitor == null)
            {
                throw new ArgumentNullException(nameof(visitor));
            }

            int tail = m_entries.Count;
            for (int i = 0; i < tail; i++)
            {
                visitor(m_entries[i]);
            }

            if (tail != 0)
            {
                m_entries.RemoveRange(0, tail);
            }

            AdvanceEpoch();
        }

        private readonly struct ChangeKey : IEquatable<ChangeKey>
        {
            private readonly EntityId m_entityId;
            private readonly uint m_typeId;

            internal ChangeKey(EntityId entityId, uint typeId)
            {
                m_entityId = entityId;
                m_typeId = typeId;
            }

            public bool Equals(ChangeKey other)
            {
                return m_entityId == other.m_entityId && m_typeId == other.m_typeId;
            }

            public override bool Equals(object obj)
            {
                return obj is ChangeKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (m_entityId.GetHashCode() * 397) ^ (int)m_typeId;
                }
            }
        }
    }
}
