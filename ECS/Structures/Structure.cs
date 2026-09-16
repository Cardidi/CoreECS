using System;
using System.Collections.Generic;
using CoreECS.Defines;

namespace CoreECS.Structures
{
    /// <summary>
    /// Observer notified by structures when component state changes.
    /// </summary>
    public interface IStructureObserver
    {
        /// <summary>A component (dense, discrete or tag) was added.</summary>
        void OnComponentAdded(Structure structure, int row, uint typeId);

        /// <summary>A component (dense, discrete or tag) was removed.</summary>
        void OnComponentRemoved(Structure structure, int row, uint typeId);

        /// <summary>A component revision changed.</summary>
        void OnComponentChanged(Structure structure, int row, uint typeId);
    }

    /// <summary>
    /// One archetype: every entity sharing the same dense composition and mask.
    /// Dense component data is stored in row-aligned SoA arrays; tags and discrete
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
        private readonly TagContainer m_tags = new();

        private ulong[] m_entityIds = new ulong[InitialCapacity];
        private EntityLocation[] m_locations = new EntityLocation[InitialCapacity];
        private SpareSetComponentContainer m_spareSet;
        private int m_capacity = InitialCapacity;
        private int m_count;

        /// <summary>Optional observer for component add/remove/change notifications.</summary>
        public IStructureObserver Observer { get; set; }

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

        internal SpareSetComponentContainer SpareSet => m_spareSet ??= CreateSpareSet();

        /// <summary>
        /// Creates a structure for the given key.
        /// </summary>
        public Structure(in StructureKey key)
        {
            m_key = key;
            m_denseTypeIds = key.ToArray();
            var denseCount = m_denseTypeIds.Length;
            m_denseTypes = new Type[denseCount];
            m_denseData = new Array[denseCount];
            m_denseVersions = new uint[denseCount][];
            m_denseRevisions = new uint[denseCount][];

            for (var i = 0; i < denseCount; i++)
            {
                var info = ComponentTypeRegistry.GetById(m_denseTypeIds[i]);
                m_denseTypes[i] = info.Type;
                m_denseData[i] = Array.CreateInstance(info.Type, InitialCapacity);
                m_denseVersions[i] = new uint[InitialCapacity];
                m_denseRevisions[i] = new uint[InitialCapacity];
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
        /// </summary>
        public int Append(ulong entityId, EntityLocation location)
        {
            if (m_count == m_capacity) Grow();

            var row = m_count;
            m_entityIds[row] = entityId;
            m_locations[row] = location;
            location.Structure = this;
            location.Row = row;
            m_tags.AddRow();
            m_spareSet?.AddRow();
            m_count += 1;
            return row;
        }

        /// <summary>
        /// Removes a row by moving the last row into its slot.
        /// The removed entity's location is left untouched for the caller to reassign or release.
        /// </summary>
        public void SwapRemove(int row)
        {
            var last = m_count - 1;
            if (row != last)
            {
                for (var i = 0; i < m_denseData.Length; i++)
                {
                    Array.Copy(m_denseData[i], last, m_denseData[i], row, 1);
                    m_denseVersions[i][row] = m_denseVersions[i][last];
                    m_denseRevisions[i][row] = m_denseRevisions[i][last];
                }

                m_entityIds[row] = m_entityIds[last];
                m_locations[row] = m_locations[last];
                m_locations[row].Row = row;
            }

            m_tags.RemoveRowSwap(row);
            m_spareSet?.RemoveRowSwap(row);
            m_entityIds[last] = 0;
            m_locations[last] = null;
            m_count -= 1;
        }

        /// <summary>Gets a read-only span over a dense component column.</summary>
        public ReadOnlySpan<T> RO<T>() where T : struct, IComponent<T>
        {
            return ((T[])m_denseData[SlotOf<T>()]).AsSpan(0, m_count);
        }

        /// <summary>
        /// Gets a writable span over a dense component column.
        /// Acquiring the span marks every row as changed (revision bump + observer notification).
        /// </summary>
        public Span<T> RW<T>() where T : struct, IComponent<T>
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

        /// <summary>Gets a writable reference to a dense component without marking it changed.</summary>
        public ref T GetDenseRef<T>(int row) where T : struct, IComponent<T>
        {
            return ref ((T[])m_denseData[SlotOf<T>()])[row];
        }

        /// <summary>Gets the dense component instance version at the row.</summary>
        public uint GetDenseVersion<T>(int row) where T : struct, IComponent<T>
        {
            return m_denseVersions[SlotOf<T>()][row];
        }

        /// <summary>Gets the dense component revision at the row.</summary>
        public uint GetDenseRevision<T>(int row) where T : struct, IComponent<T>
        {
            return m_denseRevisions[SlotOf<T>()][row];
        }

        /// <summary>Bumps the dense component revision and notifies the observer.</summary>
        public uint ChangeDenseRevision<T>(int row) where T : struct, IComponent<T>
        {
            var slot = SlotOf<T>();
            var revision = (m_denseRevisions[slot][row] % uint.MaxValue) + 1;
            m_denseRevisions[slot][row] = revision;
            Observer?.OnComponentChanged(this, row, m_denseTypeIds[slot]);
            return revision;
        }

        /// <summary>
        /// Writes a dense component value (used when a component is added or migrated in).
        /// </summary>
        public void SetDenseValue<T>(int row, in T value, uint version) where T : struct, IComponent<T>
        {
            var slot = SlotOf<T>();
            ((T[])m_denseData[slot])[row] = value;
            m_denseVersions[slot][row] = version;
            m_denseRevisions[slot][row] = 0;
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

        private SpareSetComponentContainer CreateSpareSet()
        {
            var container = new SpareSetComponentContainer();
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
            }

            m_capacity = newCapacity;
        }
    }
}
