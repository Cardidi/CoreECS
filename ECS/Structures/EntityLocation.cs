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

        private EntityLocation()
        {
            Row = -1;
        }

        private void Reset()
        {
            Structure = null;
            Row = -1;
            Generation = (Generation % uint.MaxValue) + 1;
        }
    }
}
