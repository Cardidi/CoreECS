using System.Collections.Generic;

namespace CoreECS.Structures
{
    internal sealed class PagedColumn<T>
    {
        private readonly List<T[]> m_pages = new List<T[]>();

        internal int CountPages => m_pages.Count;

        internal int Capacity => PackedPageLayout.CapacityBeforePage(m_pages.Count);

        internal T this[int slot]
        {
            get
            {
                PackedPageLayout.Locate(slot, out int page, out int offset);
                return m_pages[page][offset];
            }
            set
            {
                PackedPageLayout.Locate(slot, out int page, out int offset);
                m_pages[page][offset] = value;
            }
        }

        internal void EnsureSlot(int slot)
        {
            PackedPageLayout.Locate(slot, out int page, out _);
            while (m_pages.Count <= page)
            {
                m_pages.Add(new T[PackedPageLayout.PageSize(m_pages.Count)]);
            }
        }

        internal void ClearSlot(int slot)
        {
            PackedPageLayout.Locate(slot, out int page, out int offset);
            m_pages[page][offset] = default(T);
        }

        internal ref T GetRef(int slot)
        {
            PackedPageLayout.Locate(slot, out int page, out int offset);
            return ref m_pages[page][offset];
        }

        internal T[] GetPageForTesting(int page)
        {
            return m_pages[page];
        }
    }
}
