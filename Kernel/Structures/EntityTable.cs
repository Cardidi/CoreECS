using System;
using System.Collections.Generic;

#if COREECS_ULONG_ENTITY
using EntityValue = System.UInt64;
#else
using EntityValue = System.UInt32;
#endif

namespace CoreECS.Structures
{
    internal sealed class EntityTable
    {
        private const int PageShift = 10;
        private const int PageSize = 1 << PageShift;
        private const EntityValue PageMask = PageSize - 1;

        private readonly Dictionary<EntityValue, EntityRecord[]> m_pages =
            new Dictionary<EntityValue, EntityRecord[]>();
        private readonly Stack<EntityValue> m_freeIndexes = new Stack<EntityValue>();
        private readonly List<EntityValue> m_pendingReleases = new List<EntityValue>();
        private EntityValue m_nextIndex = 1;

        internal int AllocatedPageCount => m_pages.Count;

        internal int LiveCount { get; private set; }

        internal EntityId Create(ulong mask)
        {
            EntityValue index;
            if (m_freeIndexes.Count != 0)
            {
                index = m_freeIndexes.Pop();
            }
            else
            {
                if (m_nextIndex == 0)
                {
                    throw new InvalidOperationException("The EntityId index space is exhausted.");
                }

                index = m_nextIndex;
                m_nextIndex = index == EntityId.MaxIndex ? (EntityValue)0 : index + 1;
            }

            ref EntityRecord record = ref GetOrCreateRecord(index);
            record.Activate(mask);
            LiveCount++;
            return EntityId.Create(index, record.Generation);
        }

        internal bool IsAlive(EntityId entityId)
        {
            return TryGetRecord(entityId, out EntityRecord[]? page, out int offset) && page![offset].IsAlive;
        }

        internal bool TryGetCurrent(EntityValue index, out EntityId entityId)
        {
            if (index == 0 || index > EntityId.MaxIndex)
            {
                entityId = default(EntityId);
                return false;
            }

            Locate(index, out EntityValue pageIndex, out int offset);
            if (!m_pages.TryGetValue(pageIndex, out EntityRecord[]? page) || !page![offset].IsAlive)
            {
                entityId = default(EntityId);
                return false;
            }

            entityId = EntityId.Create(index, page![offset].Generation);
            return true;
        }

        internal ulong GetMask(EntityId entityId)
        {
            return GetCurrentRecord(entityId).Mask;
        }

        internal ComponentHandler? GetComponentHead(EntityId entityId)
        {
            return GetCurrentRecord(entityId).ComponentHead;
        }

        internal void SetComponentHead(EntityId entityId, ComponentHandler? componentHead)
        {
            GetCurrentRecord(entityId).SetComponentHead(componentHead);
        }

        internal bool MarkDestroyed(EntityId entityId)
        {
            if (!TryGetRecord(entityId, out EntityRecord[]? page, out int offset))
            {
                return false;
            }

            ref EntityRecord record = ref page![offset];
            if (!record.MarkDestroyed())
            {
                return false;
            }

            m_pendingReleases.Add(entityId.Index);
            LiveCount--;
            return true;
        }

        internal void AppendLiveEntityIds(List<EntityId> destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            foreach (KeyValuePair<EntityValue, EntityRecord[]> pair in m_pages)
            {
                EntityValue pageIndex = pair.Key;
                EntityRecord[] page = pair.Value;
                for (int offset = 0; offset < page.Length; offset++)
                {
                    EntityRecord record = page![offset];
                    if (!record.IsAlive)
                    {
                        continue;
                    }

                    EntityValue index = (pageIndex << PageShift) | (EntityValue)(uint)offset;
                    destination.Add(EntityId.Create(index, record.Generation));
                }
            }
        }

        internal void CommitReleases()
        {
            for (int i = 0; i < m_pendingReleases.Count; i++)
            {
                m_freeIndexes.Push(m_pendingReleases[i]);
            }

            m_pendingReleases.Clear();
        }

        internal void ClearForRestart()
        {
            m_pages.Clear();
            m_freeIndexes.Clear();
            m_pendingReleases.Clear();
            m_nextIndex = 1;
            LiveCount = 0;
        }

        private ref EntityRecord GetCurrentRecord(EntityId entityId)
        {
            if (!TryGetRecord(entityId, out EntityRecord[]? page, out int offset))
            {
                throw new ArgumentException("The EntityId does not identify a current entity record.", nameof(entityId));
            }

            return ref page![offset];
        }

        private bool TryGetRecord(EntityId entityId, out EntityRecord[]? page, out int offset)
        {
            if (!entityId.IsValid)
            {
                page = null;
                offset = 0;
                return false;
            }

            Locate(entityId.Index, out EntityValue pageIndex, out offset);
            return m_pages.TryGetValue(pageIndex, out page) &&
                   page![offset].Generation == entityId.Generation;
        }

        private ref EntityRecord GetOrCreateRecord(EntityValue index)
        {
            Locate(index, out EntityValue pageIndex, out int offset);
            if (!m_pages.TryGetValue(pageIndex, out EntityRecord[]? page))
            {
                page = new EntityRecord[PageSize];
                m_pages.Add(pageIndex, page);
            }

            return ref page![offset];
        }

        private static void Locate(EntityValue index, out EntityValue pageIndex, out int pageOffset)
        {
            pageIndex = index >> PageShift;
            pageOffset = (int)(index & PageMask);
        }
    }
}
