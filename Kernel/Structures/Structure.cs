using System;
using System.Collections.Generic;
using System.Diagnostics;
using CoreECS.Defines;
using CoreECS.Utils;

namespace CoreECS.Structures
{
    /// <summary>
    /// Observer notified by structures when component state changes.
    /// Structures raise sparse/tag add and remove events and revision-change events;
    /// dense component add/remove events are raised by migration orchestration.
    /// </summary>
    internal interface IStructureObserver
    {
        /// <summary>
        /// A component was added. Raised by structures for sparse and tag components;
        /// dense component additions are reported by migration orchestration.
        /// </summary>
        void OnComponentAdded(Structure structure, int row, uint typeId);

        /// <summary>
        /// A component was removed. Raised by structures for sparse and tag components;
        /// dense component removals are reported by migration orchestration.
        /// </summary>
        void OnComponentRemoved(Structure structure, int row, uint typeId);

        /// <summary>A component revision changed.</summary>
        void OnComponentChanged(Structure structure, int row, uint typeId);
    }

    /// <summary>
    /// One archetype: every entity sharing the same dense composition and mask.
    /// Dense component data is stored in row-aligned SoA arrays; tags and sparse
    /// components live in auxiliary containers attached to the structure.
    /// </summary>
    public sealed class Structure
    {
        private const int InitialCapacity = 8;

        private readonly StructureKey m_key;
        private readonly uint[] m_denseTypeIds;
        private readonly Type[] m_denseTypes;
        private readonly Array[] m_denseData;
        private readonly uint[][] m_denseVersions;
        private readonly uint[][] m_denseRevisions;
        private readonly ComponentRefCore[][] m_denseCores;
        private readonly TagContainer m_tags = new(); //forai: this should be set as optional

        private ulong[] m_entityIds = new ulong[InitialCapacity];
        private EntityLocation[] m_locations = new EntityLocation[InitialCapacity];
        private SparseComponentContainer m_sparse;
        private int m_capacity = InitialCapacity;
        private int m_count;

        /// <summary>Optional observer for component add/remove/change notifications.</summary>
        internal IStructureObserver Observer { get; set; }

        /// <summary>The archetype key of this structure.</summary>
        public StructureKey Key => m_key;

        /// <summary>The entity mask shared by all rows.</summary>
        public ulong Mask => m_key.Mask;

        /// <summary>Number of live rows (entities).</summary>
        public int Count => m_count;

        /// <summary>Sorted dense component type ids.</summary>
        public IReadOnlyList<uint> DenseTypeIds => m_denseTypeIds;

        /// <summary>Entity ids aligned with row indexes.</summary>
        public ReadOnlySpan<ulong> Entities => m_entityIds.AsSpan(0, m_count);

        internal SparseComponentContainer Sparse => m_sparse ??= CreateSparse();

        /// <summary>
        /// Sparse component store container, or null when no store was ever created.
        /// Unlike <see cref="Sparse"/> this getter never allocates.
        /// </summary>
        internal SparseComponentContainer SparseOrNull => m_sparse;

        /// <summary>
        /// Creates a structure for the given key.
        /// The structure owns its own copy of the key's dense type id array.
        /// </summary>
        internal Structure(in StructureKey key)
        {
            m_denseTypeIds = key.ToArray();
            m_key = new StructureKey(m_denseTypeIds, key.Mask);
            var denseCount = m_denseTypeIds.Length;
            for (var i = 1; i < denseCount; i++)
            {
                Debug.Assert(m_denseTypeIds[i - 1] < m_denseTypeIds[i],
                    "Dense type ids must be sorted and unique.");
            }

            m_denseTypes = new Type[denseCount];
            m_denseData = new Array[denseCount];
            m_denseVersions = new uint[denseCount][];
            m_denseRevisions = new uint[denseCount][];
            m_denseCores = new ComponentRefCore[denseCount][];

            for (var i = 0; i < denseCount; i++)
            {
                var info = ComponentTypeRegistry.GetById(m_denseTypeIds[i]);
                m_denseTypes[i] = info.Type;
                m_denseData[i] = Array.CreateInstance(info.Type, InitialCapacity);
                m_denseVersions[i] = new uint[InitialCapacity];
                m_denseRevisions[i] = new uint[InitialCapacity];
                m_denseCores[i] = new ComponentRefCore[InitialCapacity];
            }
        }

        /// <summary>Checks whether the structure carries the dense component type.</summary>
        public bool HasDense(uint typeId) => IndexOfDense(typeId) >= 0;

        /// <summary>Returns the dense slot for a type id, or -1 when absent.</summary>
        public int IndexOfDense(uint typeId)
        {
            var low = 0;
            var high = m_denseTypeIds.Length - 1;
            while (low <= high)
            {
                var mid = (low + high) >> 1;
                var value = m_denseTypeIds[mid];
                if (value == typeId) return mid;
                if (value < typeId) low = mid + 1;
                else high = mid - 1;
            }

            return -1;
        }

        /// <summary>
        /// Appends a row for the entity and binds its location to this structure.
        /// Recycled dense slots are cleared (default value, version 0, revision 0);
        /// a non-null recycled core is released defensively.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="location"/> is null.</exception>
        internal int Append(ulong entityId, EntityLocation location)
        {
            if (location == null) throw new ArgumentNullException(nameof(location));
            if (m_count == m_capacity) Grow();

            var row = m_count;
            for (var i = 0; i < m_denseData.Length; i++)
            {
                Array.Clear(m_denseData[i], row, 1);
                m_denseVersions[i][row] = 0;
                m_denseRevisions[i][row] = 0;

                var recycled = m_denseCores[i][row];
                if (recycled != null)
                {
                    ComponentRefCorePool.Release(recycled);
                    m_denseCores[i][row] = null;
                }
            }

            m_entityIds[row] = entityId;
            m_locations[row] = location;
            location.Structure = this;
            location.Row = row;
            m_tags.AddRow();
            m_sparse?.AddRow();
            m_count += 1;
            return row;
        }

        /// <summary>
        /// Removes a row by moving the last row into its slot.
        /// The removed entity's location is left untouched for the caller to reassign or release.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the row is not live.</exception>
        internal void SwapRemove(int row)
        {
            if (row < 0 || row >= m_count)
            {
                throw new ArgumentOutOfRangeException(nameof(row));
            }

            var last = m_count - 1;
            if (row != last)
            {
                for (var i = 0; i < m_denseData.Length; i++)
                {
                    Array.Copy(m_denseData[i], last, m_denseData[i], row, 1);
                    m_denseVersions[i][row] = m_denseVersions[i][last];
                    m_denseRevisions[i][row] = m_denseRevisions[i][last];
                    m_denseCores[i][row] = m_denseCores[i][last];
                }

                m_entityIds[row] = m_entityIds[last];
                m_locations[row] = m_locations[last];
                m_locations[row].Row = row;
            }

            // Ownership of the last row's cores has moved (to the target structure for a
            // migrating entity, or to the removed row for the swap): drop without release.
            for (var i = 0; i < m_denseCores.Length; i++) m_denseCores[i][last] = null;

            m_tags.RemoveRowSwap(row);
            m_sparse?.RemoveRowSwap(row);
            m_entityIds[last] = 0;
            m_locations[last] = null;
            m_count -= 1;
        }

        /// <summary>
        /// Gets a read-only span over a dense component column.
        /// The span is invalidated by structural changes (Append/SwapRemove/Grow).
        /// </summary>
        public ReadOnlySpan<T> GetReadOnlyDenseColumn<T>() where T : struct, IComponent<T>
        {
            return ((T[])m_denseData[SlotOf<T>()]).AsSpan(0, m_count);
        }

        /// <summary>
        /// Gets a writable span over a dense component column.
        /// Acquiring the span marks every row as changed (revision bump + observer notification),
        /// so per-row acquisition is O(n²); acquire once per structure.
        /// The span is invalidated by structural changes (Append/SwapRemove/Grow).
        /// </summary>
        public Span<T> GetReadWriteDenseColumn<T>() where T : struct, IComponent<T>
        {
            var slot = SlotOf<T>();
            var revisions = m_denseRevisions[slot];
            var typeId = m_denseTypeIds[slot];
            for (var row = 0; row < m_count; row++)
            {
                revisions[row] = (revisions[row] % uint.MaxValue) + 1;
                Observer?.OnComponentChanged(this, row, typeId);
            }

            return ((T[])m_denseData[slot]).AsSpan(0, m_count);
        }

        /// <summary>
        /// Gets a writable reference to a dense component without marking it changed.
        /// The reference is invalidated by structural changes; the row must be live.
        /// </summary>
        public ref T GetDenseRef<T>(int row) where T : struct, IComponent<T>
        {
            Debug.Assert(row >= 0 && row < m_count, "Row must be live.");
            return ref ((T[])m_denseData[SlotOf<T>()])[row];
        }

        /// <summary>Gets the dense component instance version at the row; the row must be live.</summary>
        public uint GetDenseVersion<T>(int row) where T : struct, IComponent<T>
        {
            Debug.Assert(row >= 0 && row < m_count, "Row must be live.");
            return m_denseVersions[SlotOf<T>()][row];
        }

        /// <summary>Gets the dense component revision at the row; the row must be live.</summary>
        public uint GetDenseRevision<T>(int row) where T : struct, IComponent<T>
        {
            Debug.Assert(row >= 0 && row < m_count, "Row must be live.");
            return m_denseRevisions[SlotOf<T>()][row];
        }

        /// <summary>
        /// Bumps the dense component revision and notifies the observer.
        /// The row must be live.
        /// </summary>
        public uint ChangeDenseRevision<T>(int row) where T : struct, IComponent<T>
        {
            Debug.Assert(row >= 0 && row < m_count, "Row must be live.");
            var slot = SlotOf<T>();
            var revision = (m_denseRevisions[slot][row] % uint.MaxValue) + 1;
            m_denseRevisions[slot][row] = revision;
            Observer?.OnComponentChanged(this, row, m_denseTypeIds[slot]);
            return revision;
        }

        /// <summary>
        /// Writes a dense component value (used when a component is added or migrated in).
        /// The row must be live.
        /// </summary>
        public void SetDenseValue<T>(int row, in T value, uint version) where T : struct, IComponent<T>
        {
            Debug.Assert(row >= 0 && row < m_count, "Row must be live.");
            var slot = SlotOf<T>();
            ((T[])m_denseData[slot])[row] = value;
            m_denseVersions[slot][row] = version;
            m_denseRevisions[slot][row] = 0;
        }

        /// <summary>Checks whether the row carries the tag.</summary>
        public bool HasTag(uint tagId, int row) => m_tags.Has(row, tagId);

        /// <summary>Adds the tag to the row; notifies the observer when newly added.</summary>
        public bool AddTag(uint tagId, int row)
        {
            if (!m_tags.Add(row, tagId)) return false;

            Observer?.OnComponentAdded(this, row, tagId);
            return true;
        }

        /// <summary>Removes the tag from the row; notifies the observer when present.</summary>
        public bool RemoveTag(uint tagId, int row)
        {
            if (!m_tags.Remove(row, tagId)) return false;

            Observer?.OnComponentRemoved(this, row, tagId);
            return true;
        }

        /// <summary>Checks whether the row has the sparse component.</summary>
        public bool HasSparse(uint typeId, int row) => m_sparse != null && m_sparse.Has(typeId, row);

        /// <summary>
        /// Writes a sparse component at the row. Adding a new instance notifies
        /// <see cref="IStructureObserver.OnComponentAdded"/>; overwriting an existing one
        /// notifies <see cref="IStructureObserver.OnComponentChanged"/>. Either way the
        /// instance is stamped with the given version and its revision resets to 0.
        /// </summary>
        public void SetSparse<T>(int row, in T value, uint version)
            where T : struct, IComponent<T>
        {
            if (row < 0 || row >= m_count)
            {
                throw new ArgumentOutOfRangeException(nameof(row));
            }

            var store = Sparse.GetOrCreateStore<T>();
            var existed = store.Has(row);
            store.Set(row, value, version);

            if (existed) Observer?.OnComponentChanged(this, row, store.TypeId);
            else Observer?.OnComponentAdded(this, row, store.TypeId);
        }

        /// <summary>Removes the sparse component from the row when present.</summary>
        public void RemoveSparse(uint typeId, int row)
        {
            var store = m_sparse?.GetStore(typeId);
            if (store == null || !store.Has(row)) return;

            store.Remove(row);
            Observer?.OnComponentRemoved(this, row, typeId);
        }

        /// <summary>Gets a writable reference to a sparse component; throws when absent.</summary>
        public ref T GetSparseRef<T>(int row) where T : struct, IComponent<T>
        {
            var typeId = ComponentTypeRegistry.GetOrRegister<T>().TypeId;
            var store = m_sparse?.GetStore(typeId);
            if (store == null || !store.Has(row))
            {
                throw new InvalidOperationException(
                    $"Sparse component {typeof(T).Name} is not present at row {row}.");
            }

            return ref ((SparseStore<T>)store).Get(row);
        }

        /// <summary>
        /// Gets the sparse component instance version at the row.
        /// Returns 0 when the component is absent; the row must be live.
        /// </summary>
        public uint GetSparseVersion<T>(int row) where T : struct, ISparseComponent<T>
        {
            var store = m_sparse?.GetStore(ComponentTypeRegistry.GetOrRegister<T>().TypeId);
            return store == null ? 0u : store.GetVersion(row);
        }

        /// <summary>
        /// Gets the sparse component revision at the row.
        /// Returns 0 when the component is absent; the row must be live.
        /// </summary>
        public uint GetSparseRevision<T>(int row) where T : struct, ISparseComponent<T>
        {
            var store = m_sparse?.GetStore(ComponentTypeRegistry.GetOrRegister<T>().TypeId);
            return store == null ? 0u : store.GetRevision(row);
        }

        /// <summary>Bumps the sparse component revision and notifies the observer.</summary>
        public uint ChangeSparseRevision<T>(int row) where T : struct, ISparseComponent<T>
        {
            var typeId = ComponentTypeRegistry.GetOrRegister<T>().TypeId;
            var store = m_sparse?.GetStore(typeId);
            if (store == null || !store.Has(row)) return 0u;

            var revision = store.ChangeRevision(row);
            Observer?.OnComponentChanged(this, row, typeId);
            return revision;
        }

        /// <summary>
        /// Gets the dense component instance version at the row by type id.
        /// Returns 0 when the type is absent; the row must be live.
        /// </summary>
        internal uint GetDenseVersion(uint typeId, int row)
        {
            Debug.Assert(row >= 0 && row < m_count, "Row must be live.");
            var slot = IndexOfDense(typeId);
            return slot < 0 ? 0u : m_denseVersions[slot][row];
        }

        /// <summary>
        /// Gets the dense component revision at the row by type id.
        /// Returns 0 when the type is absent; the row must be live.
        /// </summary>
        internal uint GetDenseRevision(uint typeId, int row)
        {
            Debug.Assert(row >= 0 && row < m_count, "Row must be live.");
            var slot = IndexOfDense(typeId);
            return slot < 0 ? 0u : m_denseRevisions[slot][row];
        }

        /// <summary>
        /// Bumps the dense component revision by type id and notifies the observer.
        /// Returns 0 when the type is absent; the row must be live.
        /// </summary>
        internal uint ChangeDenseRevision(uint typeId, int row)
        {
            Debug.Assert(row >= 0 && row < m_count, "Row must be live.");
            var slot = IndexOfDense(typeId);
            if (slot < 0) return 0u;

            var revision = (m_denseRevisions[slot][row] % uint.MaxValue) + 1;
            m_denseRevisions[slot][row] = revision;
            Observer?.OnComponentChanged(this, row, typeId);
            return revision;
        }

        /// <summary>
        /// Gets the sparse component instance version at the row by type id.
        /// Returns 0 when the component is absent; the row must be live.
        /// </summary>
        internal uint GetSparseVersion(uint typeId, int row)
        {
            Debug.Assert(row >= 0 && row < m_count, "Row must be live.");
            var store = m_sparse?.GetStore(typeId);
            return store == null ? 0u : store.GetVersion(row);
        }

        /// <summary>
        /// Gets the sparse component revision at the row by type id.
        /// Returns 0 when the component is absent; the row must be live.
        /// </summary>
        internal uint GetSparseRevision(uint typeId, int row)
        {
            Debug.Assert(row >= 0 && row < m_count, "Row must be live.");
            var store = m_sparse?.GetStore(typeId);
            return store == null ? 0u : store.GetRevision(row);
        }

        /// <summary>
        /// Bumps the sparse component revision by type id and notifies the observer.
        /// Returns 0 when the component is absent; the row must be live.
        /// </summary>
        internal uint ChangeSparseRevision(uint typeId, int row)
        {
            Debug.Assert(row >= 0 && row < m_count, "Row must be live.");
            var store = m_sparse?.GetStore(typeId);
            if (store == null || !store.Has(row)) return 0u;

            var revision = store.ChangeRevision(row);
            Observer?.OnComponentChanged(this, row, typeId);
            return revision;
        }

        /// <summary>
        /// Copies dense component data shared with the target structure for one row,
        /// preserving versions and revisions. Types absent from the target are skipped.
        /// Target-only dense types are not cleared; callers must copy into a freshly
        /// appended (cleared) target row.
        /// </summary>
        internal void CopyDenseTo(Structure target, int sourceRow, int targetRow)
        {
            Debug.Assert(sourceRow >= 0 && sourceRow < m_count, "Row must be live.");
            for (var i = 0; i < m_denseTypeIds.Length; i++)
            {
                var targetSlot = target.IndexOfDense(m_denseTypeIds[i]);
                if (targetSlot < 0) continue;

                Array.Copy(m_denseData[i], sourceRow, target.m_denseData[targetSlot], targetRow, 1);
                target.m_denseVersions[targetSlot][targetRow] = m_denseVersions[i][sourceRow];
                target.m_denseRevisions[targetSlot][targetRow] = m_denseRevisions[i][sourceRow];
                target.m_denseCores[targetSlot][targetRow] = m_denseCores[i][sourceRow];
            }
        }

        /// <summary>Gets the pooled ref core stored at a dense slot/row, or null when unbound.</summary>
        internal ComponentRefCore GetDenseCore(int slot, int row) => m_denseCores[slot][row];

        /// <summary>Stores (or clears, with null) the pooled ref core at a dense slot/row.</summary>
        internal void SetDenseCore(int slot, int row, ComponentRefCore core) => m_denseCores[slot][row] = core;

        /// <summary>
        /// Releases every dense core stored at the row and clears the slots.
        /// Used when an entity is destroyed; ownership ends here.
        /// </summary>
        internal void ReleaseDenseCoresAt(int row)
        {
            for (var i = 0; i < m_denseCores.Length; i++)
            {
                var core = m_denseCores[i][row];
                if (core == null) continue;
                ComponentRefCorePool.Release(core);
                m_denseCores[i][row] = null;
            }
        }

        /// <summary>Copies one row of tag bits into the target structure.</summary>
        internal void CopyTagsTo(Structure target, int sourceRow, int targetRow)
        {
            Debug.Assert(sourceRow >= 0 && sourceRow < m_count, "Row must be live.");
            m_tags.CopyRowTo(sourceRow, target.m_tags, targetRow);
        }

        /// <summary>
        /// Moves one row of sparse components into the target structure,
        /// mirroring the source row: target-only components at the row are cleared.
        /// </summary>
        internal void MoveSparseTo(Structure target, int sourceRow, int targetRow)
        {
            Debug.Assert(sourceRow >= 0 && sourceRow < m_count, "Row must be live.");
            var targetSparse = target.Sparse;
            if (m_sparse == null)
            {
                targetSparse.ClearRow(targetRow);
                return;
            }

            m_sparse.CopyRowTo(sourceRow, targetSparse, targetRow);
        }

        private int SlotOf<T>() where T : struct, IComponent<T>
        {
            var slot = IndexOfDense(ComponentTypeRegistry.GetOrRegister<T>().TypeId);
            if (slot < 0)
            {
                throw new InvalidOperationException(
                    $"Component {typeof(T).Name} is not part of structure with mask {Mask}.");
            }

            return slot;
        }

        private SparseComponentContainer CreateSparse()
        {
            var container = new SparseComponentContainer();
            container.EnsureRows(m_count);
            return container;
        }

        private void Grow()
        {
            var newCapacity = Math.Max(InitialCapacity, m_capacity * 2);
            Array.Resize(ref m_entityIds, newCapacity);
            Array.Resize(ref m_locations, newCapacity);

            for (var i = 0; i < m_denseData.Length; i++)
            {
                var grown = Array.CreateInstance(m_denseTypes[i], newCapacity);
                Array.Copy(m_denseData[i], grown, m_denseData[i].Length);
                m_denseData[i] = grown;
                Array.Resize(ref m_denseVersions[i], newCapacity);
                Array.Resize(ref m_denseRevisions[i], newCapacity);
                Array.Resize(ref m_denseCores[i], newCapacity);
            }

            m_capacity = newCapacity;
        }
    }
}
