using CoreECS.Defines;

namespace CoreECS.Structures
{
    /// <summary>
    /// Typeless, mutable and pooled core shared by component reference handles. Storage
    /// slots own one core per component instance: dense columns and sparse stores hold
    /// them, release them on removal and recycle them through the pool. Holds the entity
    /// location, the location generation captured at bind, the component type id/kind and
    /// the component instance version. Every accessor resolves through the live location,
    /// so references stay valid across migrations and in-structure swap-removes.
    /// </summary>
    internal sealed class ComponentRefCore
    {
        /// <summary>Location shared with the owning entity; may be recycled after destroy.</summary>
        public EntityLocation Location { get; private set; }

        /// <summary>Generation captured at bind; detects recycled locations. Zeroed by Reset when the core returns to the pool.</summary>
        public uint Generation { get; private set; }

        /// <summary>Registered component type id.</summary>
        public uint TypeId { get; private set; }

        /// <summary>Storage kind of the referenced component.</summary>
        public ComponentKind Kind { get; private set; }

        /// <summary>Component instance version captured at creation.</summary>
        public uint Version { get; private set; }

        /// <summary>Bumped on every bind; stale handles capture the old value.</summary>
        public uint BindGeneration { get; private set; }

        internal Structure CachedStructure { get; private set; }
        internal int CachedSlot { get; private set; }
        internal SparseStore CachedSparseStore { get; private set; }

        /// <summary>
        /// Creates an unbound component reference core for pooling; bind before use.
        /// </summary>
        public ComponentRefCore()
        {
            CachedSlot = -1;
        }

        /// <summary>
        /// Creates a bound component reference core.
        /// </summary>
        public ComponentRefCore(EntityLocation location, uint generation, uint typeId, ComponentKind kind, uint version)
        {
            CachedSlot = -1;
            Bind(location, generation, typeId, kind, version);
        }

        internal void Bind(EntityLocation location, uint generation, uint typeId, ComponentKind kind, uint version)
        {
            Location = location;
            Generation = generation;
            TypeId = typeId;
            Kind = kind;
            Version = version;
            BindGeneration = unchecked(BindGeneration + 1);
            CachedStructure = null;
            CachedSlot = -1;
            CachedSparseStore = null;
        }

        internal void Reset()
        {
            Location = null;
            Generation = 0u;
            TypeId = 0u;
            Kind = default;
            Version = 0u;
            CachedStructure = null;
            CachedSlot = -1;
            CachedSparseStore = null;
        }

        /// <summary>
        /// Resolves and caches the dense slot of this core's type inside the structure.
        /// A cached slot is reused while the structure instance is unchanged (dense
        /// composition is fixed per structure); migration falls back to a binary search.
        /// </summary>
        internal bool TryGetDenseSlot(Structure structure, out int slot)
        {
            if (ReferenceEquals(CachedStructure, structure) && CachedSlot >= 0)
            {
                slot = CachedSlot;
                return true;
            }

            slot = structure.IndexOfDense(TypeId);
            if (slot >= 0)
            {
                CachedStructure = structure;
                CachedSlot = slot;
                CachedSparseStore = null;
            }

            return slot >= 0;
        }

        /// <summary>
        /// Resolves and caches the sparse store of this core's type inside the structure,
        /// or null when no store exists.
        /// </summary>
        internal SparseStore GetSparseStore(Structure structure)
        {
            if (ReferenceEquals(CachedStructure, structure) && CachedSparseStore != null) return CachedSparseStore;

            var store = structure.SparseOrNull?.GetStore(TypeId);
            if (store != null)
            {
                CachedStructure = structure;
                CachedSlot = -1;
                CachedSparseStore = store;
            }

            return store;
        }

        /// <summary>
        /// True when the location is alive (structure bound and generation matches), the
        /// row is within the structure, the component is present at the location row and,
        /// for dense/sparse components, the stored instance version matches. Tags are
        /// presence-only. A stale row left behind by an in-structure swap-remove (before
        /// the location is released) reports false instead of reading out of range.
        /// </summary>
        public bool NotNull
        {
            get
            {
                var structure = Location?.Structure;
                if (structure == null || Location.Generation != Generation) return false;

                var row = Location.Row;
                switch (Kind)
                {
                    case ComponentKind.Dense:
                        return row >= 0 && row < structure.Count &&
                               TryGetDenseSlot(structure, out var slot) &&
                               structure.GetDenseVersionAt(slot, row) == Version;
                    case ComponentKind.Sparse:
                    {
                        var store = GetSparseStore(structure);
                        return store != null && store.Has(row) && store.GetVersion(row) == Version;
                    }
                    case ComponentKind.Tag:
                        return structure.HasTag(TypeId, row);
                    default:
                        return false;
                }
            }
        }

        /// <summary>Entity id owning the referenced component, or 0 when this ref is not valid.</summary>
        public ulong EntityId
        {
            get
            {
                if (!NotNull) return 0UL;
                return Location.Structure.Entities[Location.Row];
            }
        }

        /// <summary>Current component revision; 0 for tags and invalid refs.</summary>
        public uint Revision
        {
            get
            {
                if (!NotNull) return 0u;
                switch (Kind)
                {
                    case ComponentKind.Dense:
                        return TryGetDenseSlot(Location.Structure, out var slot)
                            ? Location.Structure.GetDenseRevisionAt(slot, Location.Row)
                            : 0u;
                    case ComponentKind.Sparse:
                    {
                        var store = GetSparseStore(Location.Structure);
                        return store != null && store.Has(Location.Row)
                            ? store.GetRevision(Location.Row)
                            : 0u;
                    }
                    default:
                        return 0u;
                }
            }
        }

        /// <summary>
        /// Validates the dense component at the core's live location and bumps its
        /// revision in one pass. Returns false (with <paramref name="slot"/> = -1) when
        /// the row is out of range, the type is absent from the structure or the stored
        /// instance version differs. Does not notify; callers notify separately.
        /// </summary>
        internal bool TryBumpDenseRevision(Structure structure, int row, out int slot)
        {
            slot = -1;
            if (row < 0 || row >= structure.Count) return false;
            if (!TryGetDenseSlot(structure, out slot)) return false;
            if (structure.GetDenseVersionAt(slot, row) != Version) return false;

            structure.BumpDenseRevisionAt(slot, row);
            return true;
        }

        /// <summary>
        /// Validates the sparse component at the core's live location and bumps its
        /// revision. Returns false when the store is absent, the row is not tracked or
        /// the stored instance version differs. Does not notify; callers notify separately.
        /// </summary>
        internal bool TryBumpSparseRevision(Structure structure, int row)
        {
            var store = GetSparseStore(structure);
            if (store == null || !store.Has(row) || store.GetVersion(row) != Version) return false;

            store.ChangeRevision(row);
            return true;
        }

        /// <summary>
        /// Bumps and returns the component revision (notifying the structure observer).
        /// Returns 0 for tags and invalid refs.
        /// </summary>
        public uint ChangeRevision()
        {
            if (!NotNull) return 0u;
            switch (Kind)
            {
                case ComponentKind.Dense:
                {
                    var structure = Location.Structure;
                    var row = Location.Row;
                    TryGetDenseSlot(structure, out var slot);
                    var revision = structure.BumpDenseRevisionAt(slot, row);
                    structure.NotifyChanged(row, TypeId);
                    return revision;
                }
                case ComponentKind.Sparse:
                {
                    var structure = Location.Structure;
                    var row = Location.Row;
                    var store = GetSparseStore(structure);
                    if (store == null || !store.Has(row)) return 0u;

                    var revision = store.ChangeRevision(row);
                    structure.NotifyChanged(row, TypeId);
                    return revision;
                }
                default:
                    return 0u;
            }
        }
    }
}
