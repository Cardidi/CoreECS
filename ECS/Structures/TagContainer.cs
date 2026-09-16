using System;

namespace CoreECS.Structures
{
    /// <summary>
    /// Per-row tag bitmaps owned by a Structure.
    /// Rows are addressed by structure row index; width grows as tag types register.
    /// Words are stored row-major: row * WordCount + word.
    /// </summary>
    public sealed class TagContainer
    {
        private const int InitialRowCapacity = 8;

        private ulong[] m_words = Array.Empty<ulong>();
        private int m_wordCount;
        private int m_rowCapacity;
        private int m_count;

        /// <summary>Number of live rows.</summary>
        public int Count => m_count;

        /// <summary>Appends an empty row.</summary>
        public void AddRow()
        {
            EnsureRowCapacity(m_count + 1);
            ClearRow(m_count);
            m_count += 1;
        }

        /// <summary>
        /// Removes a row by moving the last row into its slot.
        /// </summary>
        public void RemoveRowSwap(int row)
        {
            var last = m_count - 1;
            if (row != last) CopyRow(last, row);
            ClearRow(last);
            m_count -= 1;
        }

        /// <summary>Checks whether the row carries the tag.</summary>
        public bool Has(int row, uint tagId)
        {
            var word = (int)(tagId >> 6);
            if (word >= m_wordCount) return false;

            return (m_words[row * m_wordCount + word] & (1UL << (int)(tagId & 63))) != 0;
        }

        /// <summary>Adds the tag to the row; returns false when already present.</summary>
        public bool Add(int row, uint tagId)
        {
            EnsureWidth(tagId);

            var index = row * m_wordCount + (int)(tagId >> 6);
            var bit = 1UL << (int)(tagId & 63);
            if ((m_words[index] & bit) != 0) return false;

            m_words[index] |= bit;
            return true;
        }

        /// <summary>Removes the tag from the row; returns false when absent.</summary>
        public bool Remove(int row, uint tagId)
        {
            var word = (int)(tagId >> 6);
            if (word >= m_wordCount) return false;

            var index = row * m_wordCount + word;
            var bit = 1UL << (int)(tagId & 63);
            if ((m_words[index] & bit) == 0) return false;

            m_words[index] &= ~bit;
            return true;
        }

        /// <summary>
        /// Copies one row into another container, widening the target when needed.
        /// </summary>
        public void CopyRowTo(int sourceRow, TagContainer target, int targetRow)
        {
            target.EnsureWordCount(m_wordCount);
            target.EnsureRowCapacity(targetRow + 1);

            for (var word = 0; word < m_wordCount; word++)
            {
                target.m_words[targetRow * target.m_wordCount + word] =
                    m_words[sourceRow * m_wordCount + word];
            }
        }

        private void EnsureRowCapacity(int rows)
        {
            if (rows <= m_rowCapacity) return;

            var newCapacity = Math.Max(rows, Math.Max(InitialRowCapacity, m_rowCapacity * 2));
            var newWords = new ulong[newCapacity * m_wordCount];
            if (m_words.Length > 0) Array.Copy(m_words, newWords, m_words.Length);

            m_words = newWords;
            m_rowCapacity = newCapacity;
        }

        private void EnsureWordCount(int words)
        {
            if (words <= m_wordCount) return;

            var newWords = new ulong[m_rowCapacity * words];
            for (var row = 0; row < m_count; row++)
            {
                for (var word = 0; word < m_wordCount; word++)
                {
                    newWords[row * words + word] = m_words[row * m_wordCount + word];
                }
            }

            m_words = newWords;
            m_wordCount = words;
        }

        private void EnsureWidth(uint tagId)
        {
            EnsureWordCount((int)(tagId >> 6) + 1);
        }

        private void CopyRow(int fromRow, int toRow)
        {
            for (var word = 0; word < m_wordCount; word++)
            {
                m_words[toRow * m_wordCount + word] = m_words[fromRow * m_wordCount + word];
            }
        }

        private void ClearRow(int row)
        {
            for (var word = 0; word < m_wordCount; word++)
            {
                m_words[row * m_wordCount + word] = 0;
            }
        }
    }
}
