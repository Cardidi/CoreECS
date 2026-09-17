using System;
using CoreECS.Defines;

namespace CoreECS.Structures
{
    /// <summary>
    /// Non-generic base for a per-structure store of one sparse component type.
    /// </summary>
    internal abstract class SparseStore
    {
        /// <summary>Registered type id of the stored component.</summary>
        public abstract uint TypeId { get; }

        /// <summary>Number of rows (mirrors the owning structure row count).</summary>
        public abstract int Count { get; }

        /// <summary>Checks whether the row has the component.</summary>
        public abstract bool Has(int row);

        /// <summary>Removes the component from the row.</summary>
        public abstract void Remove(int row);

        /// <summary>Appends an empty row.</summary>
        public abstract void AddRow();

        /// <summary>Removes a row by moving the last row into its slot.</summary>
        public abstract void RemoveRowSwap(int row);

        /// <summary>Grows the store to the given row count by appending empty rows.</summary>
        public abstract void EnsureRows(int count);

        /// <summary>Creates an empty store of the same concrete type.</summary>
        public abstract SparseStore CreateEmpty();

        /// <summary>Copies one row into another store, including version and revision.</summary>
        public abstract void CopyRowTo(int sourceRow, SparseStore target, int targetRow);

        /// <summary>Gets the component instance version at the row.</summary>
        public abstract uint GetVersion(int row);

        /// <summary>Gets the modification revision at the row.</summary>
        public abstract uint GetRevision(int row);

        /// <summary>Bumps and returns the modification revision at the row.</summary>
        public abstract uint ChangeRevision(int row);
    }

    /// <summary>
    /// Spare-set storage for a single sparse component type inside one structure.
    /// Data arrays are row-aligned; presence is tracked with a bitmap.
    /// </summary>
    internal sealed class SparseStore<T> : SparseStore
        where T : struct, IComponent<T>
    {
        private static readonly uint s_typeId = ComponentTypeRegistry.GetOrRegister<T>().TypeId;

        private const int InitialCapacity = 8;

        private T[] m_data = new T[InitialCapacity];
        private ulong[] m_present = new ulong[1];
        private uint[] m_versions = new uint[InitialCapacity];
        private uint[] m_revisions = new uint[InitialCapacity];
        private int m_capacity = InitialCapacity;
        private int m_count;

        /// <inheritdoc />
        public override uint TypeId => s_typeId;

        /// <inheritdoc />
        public override int Count => m_count;

        /// <inheritdoc />
        public override bool Has(int row)
        {
            if (row < 0 || row >= m_count) return false;

            return (m_present[row >> 6] & (1UL << (row & 63))) != 0;
        }

        /// <summary>
        /// Writes the component at the row, stamping it with the given version
        /// and resetting its revision.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the row is not live.</exception>
        public void Set(int row, in T value, uint version)
        {
            if (row < 0 || row >= m_count)
            {
                throw new ArgumentOutOfRangeException(nameof(row));
            }

            m_data[row] = value;
            m_versions[row] = version;
            m_revisions[row] = 0;
            SetPresence(row, true);
        }

        /// <summary>Gets a writable reference to the component data at the row.</summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the row is not live.</exception>
        public ref T Get(int row)
        {
            if (row < 0 || row >= m_count)
            {
                throw new ArgumentOutOfRangeException(nameof(row));
            }

            return ref m_data[row];
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the row is not live.</exception>
        public override uint GetVersion(int row)
        {
            if (row < 0 || row >= m_count)
            {
                throw new ArgumentOutOfRangeException(nameof(row));
            }

            return m_versions[row];
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the row is not live.</exception>
        public override uint GetRevision(int row)
        {
            if (row < 0 || row >= m_count)
            {
                throw new ArgumentOutOfRangeException(nameof(row));
            }

            return m_revisions[row];
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the row is not live.</exception>
        public override uint ChangeRevision(int row)
        {
            if (row < 0 || row >= m_count)
            {
                throw new ArgumentOutOfRangeException(nameof(row));
            }

            var revision = (m_revisions[row] % uint.MaxValue) + 1;
            m_revisions[row] = revision;
            return revision;
        }

        /// <inheritdoc />
        public override void Remove(int row)
        {
            if (!Has(row)) return;

            SetPresence(row, false);
            m_data[row] = default;
            m_versions[row] = 0;
            m_revisions[row] = 0;
        }

        /// <inheritdoc />
        public override void AddRow()
        {
            EnsureCapacity(m_count + 1);
            m_count += 1;
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the row is not live.</exception>
        public override void RemoveRowSwap(int row)
        {
            if (row < 0 || row >= m_count)
            {
                throw new ArgumentOutOfRangeException(nameof(row));
            }

            var last = m_count - 1;
            if (row != last)
            {
                m_data[row] = m_data[last];
                m_versions[row] = m_versions[last];
                m_revisions[row] = m_revisions[last];
                SetPresence(row, Has(last));
            }

            ClearSlot(last);
            m_count -= 1;
        }

        /// <inheritdoc />
        public override void EnsureRows(int count)
        {
            if (count <= m_count) return;

            EnsureCapacity(count);
            for (var row = m_count; row < count; row++)
            {
                ClearSlot(row);
            }

            m_count = count;
        }

        /// <inheritdoc />
        public override SparseStore CreateEmpty() => new SparseStore<T>();

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="target"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when either row is not live.</exception>
        /// <exception cref="ArgumentException">Thrown when the target store type does not match.</exception>
        public override void CopyRowTo(int sourceRow, SparseStore target, int targetRow)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (sourceRow < 0 || sourceRow >= m_count)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceRow));
            }

            if (target is not SparseStore<T> typed)
            {
                throw new ArgumentException(
                    $"Target store type {target.GetType().Name} does not match {typeof(SparseStore<T>).Name}.",
                    nameof(target));
            }

            if (targetRow < 0 || targetRow >= typed.m_count)
            {
                throw new ArgumentOutOfRangeException(nameof(targetRow));
            }

            if (!Has(sourceRow))
            {
                typed.Remove(targetRow);
                return;
            }

            typed.m_data[targetRow] = m_data[sourceRow];
            typed.m_versions[targetRow] = m_versions[sourceRow];
            typed.m_revisions[targetRow] = m_revisions[sourceRow];
            typed.SetPresence(targetRow, true);
        }

        private void EnsureCapacity(int rows)
        {
            if (rows <= m_capacity) return;

            var newCapacity = Math.Max(rows, Math.Max(InitialCapacity, m_capacity * 2));
            Array.Resize(ref m_data, newCapacity);
            Array.Resize(ref m_versions, newCapacity);
            Array.Resize(ref m_revisions, newCapacity);
            Array.Resize(ref m_present, (newCapacity + 63) >> 6);
            m_capacity = newCapacity;
        }

        private void SetPresence(int row, bool present)
        {
            var word = row >> 6;
            var bit = 1UL << (row & 63);
            if (present) m_present[word] |= bit;
            else m_present[word] &= ~bit;
        }

        private void ClearSlot(int row)
        {
            m_data[row] = default;
            m_versions[row] = 0;
            m_revisions[row] = 0;
            SetPresence(row, false);
        }
    }
}
