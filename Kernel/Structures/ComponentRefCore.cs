using CoreECS.Defines;

namespace CoreECS.Structures
{
    /// <summary>
    /// Typeless core shared by component reference handles. Holds the entity location,
    /// the location generation captured at creation, the component type id/kind and the
    /// component instance version. Every accessor resolves through the live location,
    /// so references stay valid across migrations and in-structure swap-removes.
    /// </summary>
    internal sealed class ComponentRefCore
    {
        /// <summary>Location shared with the owning entity; may be recycled after destroy.</summary>
        public EntityLocation Location { get; }

        /// <summary>Generation captured at creation; detects recycled locations.</summary>
        public uint Generation { get; }

        /// <summary>Registered component type id.</summary>
        public uint TypeId { get; }

        /// <summary>Storage kind of the referenced component.</summary>
        public ComponentKind Kind { get; }

        /// <summary>Component instance version captured at creation.</summary>
        public uint Version { get; }

        /// <summary>
        /// Creates a component reference core.
        /// </summary>
        public ComponentRefCore(EntityLocation location, uint generation, uint typeId, ComponentKind kind, uint version)
        {
            Location = location;
            Generation = generation;
            TypeId = typeId;
            Kind = kind;
            Version = version;
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
                               structure.HasDense(TypeId) &&
                               structure.GetDenseVersion(TypeId, row) == Version;
                    case ComponentKind.Sparse:
                        return structure.HasSparse(TypeId, row) &&
                               structure.GetSparseVersion(TypeId, row) == Version;
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
                        return Location.Structure.GetDenseRevision(TypeId, Location.Row);
                    case ComponentKind.Sparse:
                        return Location.Structure.GetSparseRevision(TypeId, Location.Row);
                    default:
                        return 0u;
                }
            }
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
                    return Location.Structure.ChangeDenseRevision(TypeId, Location.Row);
                case ComponentKind.Sparse:
                    return Location.Structure.ChangeSparseRevision(TypeId, Location.Row);
                default:
                    return 0u;
            }
        }
    }
}
