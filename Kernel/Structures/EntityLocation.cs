using CoreECS.Utils;

namespace CoreECS.Structures
{
    /// <summary>
    /// Pooled anchor shared by Entity handles and ComponentRefs.
    /// Moving an entity only mutates this object, so existing references follow automatically.
    /// Note: do not cache instances in production; they are pooled and reused.
    /// </summary>
    internal sealed class EntityLocation
    {
        /// <summary>
        /// Object pool for EntityLocation instances.
        /// </summary>
        public static readonly Pool<EntityLocation> Pool = new(
            createFunc: () => new EntityLocation(),
            returnAction: x => x.Reset());

        /// <summary>The structure currently owning the entity.</summary>
        public Structure Structure;

        /// <summary>The entity row inside <see cref="Structure"/>.</summary>
        public int Row;

        /// <summary>Generation used to detect stale handles after the instance is recycled.</summary>
        public uint Generation;

        /// <summary>Logical journal index of the pending revision entry for this location; -1 when none.</summary>
        public int PendingRevisionIndex = -1;

        /// <summary>Component type id of the pending revision entry; only meaningful when <see cref="PendingRevisionIndex"/> is non-negative.</summary>
        public uint PendingRevisionTypeId;

        private EntityLocation()
        {
            Row = -1;
        }

        private void Reset()
        {
            Structure = null;
            Row = -1;
            PendingRevisionIndex = -1;
            PendingRevisionTypeId = 0u;
            Generation = (Generation % uint.MaxValue) + 1;
        }
    }
}
