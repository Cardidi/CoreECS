using System;
using System.Collections.Generic;

#if COREECS_ULONG_ENTITY
using EntityValue = System.UInt64;
#else
using EntityValue = System.UInt32;
#endif

namespace CoreECS.Structures
{
    internal sealed class SparseIndex
    {
        private const int PageShift = 10;
        private const int PageSize = 1 << PageShift;
        private const EntityValue PageMask = PageSize - 1;

        private readonly Dictionary<EntityValue, Entry[]> m_pages = new Dictionary<EntityValue, Entry[]>();

        internal int AllocatedPageCount => m_pages.Count;

        internal bool TryGet(EntityId entityId, out int packedSlot)
        {
            Locate(entityId.Index, out EntityValue pageIndex, out int pageOffset);
            if (m_pages.TryGetValue(pageIndex, out Entry[]? page))
            {
                Entry entry = page![pageOffset];
                if (entry.PackedSlotPlusOne != 0 && entry.Generation == entityId.Generation)
                {
                    packedSlot = entry.PackedSlotPlusOne - 1;
                    return true;
                }
            }

            packedSlot = -1;
            return false;
        }

        internal void Set(EntityId entityId, int packedSlot)
        {
            if (!entityId.IsValid)
            {
                throw new ArgumentException("A sparse entry requires a valid EntityId.", nameof(entityId));
            }

            if (packedSlot < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(packedSlot));
            }

            Locate(entityId.Index, out EntityValue pageIndex, out int pageOffset);
            if (!m_pages.TryGetValue(pageIndex, out Entry[]? page))
            {
                page = new Entry[PageSize];
                m_pages.Add(pageIndex, page);
            }

            Entry existing = page![pageOffset];
            if (existing.PackedSlotPlusOne != 0 && existing.Generation == entityId.Generation)
            {
                throw new InvalidOperationException(
                    $"Entity index {entityId.Index} generation {entityId.Generation} already has a sparse entry.");
            }

            page[pageOffset] = new Entry(entityId.Generation, packedSlot + 1);
        }

        internal void Remove(EntityId entityId)
        {
            Locate(entityId.Index, out EntityValue pageIndex, out int pageOffset);
            if (!m_pages.TryGetValue(pageIndex, out Entry[]? page))
            {
                return;
            }

            Entry entry = page![pageOffset];
            if (entry.PackedSlotPlusOne != 0 && entry.Generation == entityId.Generation)
            {
                page![pageOffset] = default(Entry);
            }
        }

        internal void Clear()
        {
            m_pages.Clear();
        }

        private static void Locate(EntityValue index, out EntityValue pageIndex, out int pageOffset)
        {
            pageIndex = index >> PageShift;
            pageOffset = (int)(index & PageMask);
        }

        private readonly struct Entry
        {
            internal readonly EntityValue Generation;
            internal readonly int PackedSlotPlusOne;

            internal Entry(EntityValue generation, int packedSlotPlusOne)
            {
                Generation = generation;
                PackedSlotPlusOne = packedSlotPlusOne;
            }
        }
    }
}
