using System.Collections.Generic;

namespace CoreECS.Structures
{
    /// <summary>
    /// Owns entity id allocation and maps live entity ids to pooled
    /// <see cref="EntityLocation"/> instances. One table per world.
    /// Ids increase monotonically and are never reused within a table.
    /// </summary>
    internal sealed class EntityTable
    {
        private readonly Dictionary<ulong, EntityLocation> m_locations = new();
        private ulong m_nextId;

        /// <summary>Number of live entities.</summary>
        public int Count => m_locations.Count;

        /// <summary>
        /// Allocates the next entity id, takes a location from
        /// <see cref="EntityLocation.Pool"/> and registers the pair.
        /// </summary>
        /// <returns>The new entity id and its location.</returns>
        public (ulong EntityId, EntityLocation Location) Create()
        {
            var entityId = NextId();
            var location = EntityLocation.Pool.Get();
            m_locations.Add(entityId, location);
            return (entityId, location);
        }

        /// <summary>
        /// Removes the entity entry and returns its location to the pool,
        /// which advances the location's generation so stale references can be detected.
        /// Unknown ids are ignored (no-op), making destroy idempotent for callers.
        /// </summary>
        public void Destroy(ulong entityId)
        {
            if (!m_locations.TryGetValue(entityId, out var location)) return;

            m_locations.Remove(entityId);
            EntityLocation.Pool.Release(location);
        }

        /// <summary>
        /// Releases every live entity location back to the pool without invoking component
        /// hooks or emitting signals. Used when the owning world shuts down; entity ids are
        /// not reused within this table.
        /// </summary>
        public void Clear()
        {
            foreach (var location in m_locations.Values)
            {
                EntityLocation.Pool.Release(location);
            }

            m_locations.Clear();
        }

        /// <summary>
        /// Tries to get the current location of a live entity id.
        /// </summary>
        /// <returns>True when the id is live; otherwise false with a null location.</returns>
        public bool TryGetLocation(ulong entityId, out EntityLocation location)
        {
            return m_locations.TryGetValue(entityId, out location);
        }

        /// <summary>
        /// Clears the pending revision marker of every live location. Called when the
        /// revision journal is reset or its coalescing floor rises, so a location can
        /// never claim a pending entry that is no longer coalescible.
        /// </summary>
        public void InvalidatePendingRevisions()
        {
            foreach (var location in m_locations.Values)
            {
                location.PendingRevisionIndex = -1;
                location.PendingRevisionTypeId = 0u;
            }
        }

        /// <summary>
        /// Live entity ids. Enumeration order is unspecified; the table must not be
        /// mutated while enumerating.
        /// </summary>
        public IEnumerable<ulong> EntityIds => m_locations.Keys;

        private ulong NextId() => ++m_nextId;
    }
}
