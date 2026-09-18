using System;
using System.Collections.Generic;
using CoreECS.Defines;

namespace CoreECS.Structures
{
    /// <summary>
    /// Collection of sparse component stores attached to one structure.
    /// Stores are created lazily per sparse component type.
    /// </summary>
    internal sealed class SparseComponentContainer
    {
        private readonly Dictionary<uint, SparseStore> m_stores = new();
        private int m_count;

        /// <summary>Number of sparse component types present in this container.</summary>
        public int StoreCount => m_stores.Count;

        /// <summary>Number of rows tracked by this container (mirrors the owning structure).</summary>
        public int Count => m_count;

        /// <summary>Type ids of the sparse component stores present in this container.</summary>
        public IEnumerable<uint> TypeIds => m_stores.Keys;

        /// <summary>Gets the store for a type id, or null when absent.</summary>
        public SparseStore GetStore(uint typeId)
        {
            return m_stores.TryGetValue(typeId, out var store) ? store : null;
        }

        /// <summary>
        /// Gets or creates the store for a sparse component type.
        /// A newly created store is grown to the container row count so row writes are valid.
        /// </summary>
        public SparseStore<T> GetOrCreateStore<T>() where T : struct, IComponent<T>
        {
            var typeId = ComponentTypeRegistry.GetOrRegister<T>().TypeId;
            if (m_stores.TryGetValue(typeId, out var existing))
            {
                return (SparseStore<T>)existing;
            }

            var created = new SparseStore<T>();
            created.EnsureRows(m_count);
            m_stores.Add(typeId, created);
            return created;
        }

        /// <summary>Checks whether the row has the sparse component.</summary>
        public bool Has(uint typeId, int row)
        {
            var store = GetStore(typeId);
            return store != null && store.Has(row);
        }

        /// <summary>Appends an empty row to the container and every store.</summary>
        public void AddRow()
        {
            m_count += 1;
            foreach (var store in m_stores.Values)
            {
                store.AddRow();
            }
        }

        /// <summary>Grows the container to the given row count by appending empty rows.</summary>
        public void EnsureRows(int count)
        {
            if (count <= m_count) return;

            foreach (var store in m_stores.Values)
            {
                store.EnsureRows(count);
            }

            m_count = count;
        }

        /// <summary>Removes a row (swap-remove) from the container and every store.</summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the row is not live.</exception>
        public void RemoveRowSwap(int row)
        {
            if (row < 0 || row >= m_count)
            {
                throw new ArgumentOutOfRangeException(nameof(row));
            }

            foreach (var store in m_stores.Values)
            {
                store.RemoveRowSwap(row);
            }

            m_count -= 1;
        }

        /// <summary>Clears all sparse components at the row.</summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the row is not live.</exception>
        public void ClearRow(int row)
        {
            if (row < 0 || row >= m_count)
            {
                throw new ArgumentOutOfRangeException(nameof(row));
            }

            foreach (var store in m_stores.Values)
            {
                store.Remove(row);
            }
        }

        /// <summary>
        /// Releases every pooled ref core stored at the row and clears the slots.
        /// Used when an entity is destroyed; ownership ends here.
        /// </summary>
        public void ReleaseCoresAt(int row)
        {
            foreach (var store in m_stores.Values)
            {
                store.ReleaseCore(row);
            }
        }

        /// <summary>
        /// Copies one row into another container so the target row mirrors the source row:
        /// stores present in the source are copied (or cleared when absent at the source row),
        /// and target-only stores are cleared at the target row.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="target"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when either row is not live.</exception>
        public void CopyRowTo(int sourceRow, SparseComponentContainer target, int targetRow)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (sourceRow < 0 || sourceRow >= m_count)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceRow));
            }

            if (targetRow < 0 || targetRow >= target.m_count)
            {
                throw new ArgumentOutOfRangeException(nameof(targetRow));
            }

            foreach (var pair in m_stores)
            {
                if (!pair.Value.Has(sourceRow))
                {
                    target.GetStore(pair.Key)?.Remove(targetRow);
                    continue;
                }

                if (!target.m_stores.TryGetValue(pair.Key, out var targetStore))
                {
                    targetStore = pair.Value.CreateEmpty();
                    targetStore.EnsureRows(target.m_count);
                    target.m_stores.Add(pair.Key, targetStore);
                }

                pair.Value.CopyRowTo(sourceRow, targetStore, targetRow);
            }

            foreach (var pair in target.m_stores)
            {
                if (m_stores.ContainsKey(pair.Key)) continue;

                pair.Value.Remove(targetRow);
            }
        }
    }
}
