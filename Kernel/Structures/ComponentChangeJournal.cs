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

        internal ulong CurrentEpoch { get; private set; } = 1;

        internal ulong NextSequence { get; private set; } = 1;

        internal void Append(EntityId entityId, uint typeId)
        {
            m_entries.Add(new Entry(NextSequence, entityId, typeId));
            NextSequence++;
        }

        internal void AdvanceEpoch()
        {
            CurrentEpoch = CurrentEpoch == ulong.MaxValue ? 1 : CurrentEpoch + 1;
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
    }
}
