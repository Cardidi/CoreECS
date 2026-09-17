using System.Collections.Generic;
using CoreECS.Defines;
using CoreECS.Managers;
using CoreECS.Structures;

namespace CoreECS
{
    /// <summary>
    /// Non-pooled <see cref="IEntityQuery"/> over the entity table. Every <see cref="Refresh"/>
    /// rebuilds the snapshot by iterating live entity ids and evaluating the matcher per row.
    /// </summary>
    internal sealed class EntityQuery : IEntityQuery
    {
        private readonly IEntityMatcher m_matcher;
        private readonly EntityManager m_entityManager;
        private readonly List<ulong> m_entities = new();
        private readonly List<Structure> m_structures = new();
        private readonly HashSet<Structure> m_structureSet = new();

        /// <inheritdoc />
        public IEntityMatcher Matcher => m_matcher;

        /// <inheritdoc />
        public IReadOnlyList<Structure> Structures => m_structures;

        /// <inheritdoc />
        public IEnumerable<ulong> Entities => m_entities;

        /// <summary>
        /// Creates a query bound to the entity manager. The snapshot starts empty; call
        /// <see cref="Refresh"/> before reading <see cref="Entities"/> or <see cref="Structures"/>.
        /// </summary>
        /// <param name="matcher">Matcher that defines the query conditions.</param>
        /// <param name="entityManager">Entity manager owning the queried entity table.</param>
        public EntityQuery(IEntityMatcher matcher, EntityManager entityManager)
        {
            m_matcher = matcher;
            m_entityManager = entityManager;
        }

        /// <inheritdoc />
        public void Refresh()
        {
            m_entities.Clear();
            m_structures.Clear();
            m_structureSet.Clear();

            foreach (var entityId in m_entityManager.Table.EntityIds)
            {
                if (!m_entityManager.Table.TryGetLocation(entityId, out var location) || location.Structure == null) continue;
                if (!m_matcher.ComponentFilter(location.Structure, location.Row)) continue;

                m_entities.Add(entityId);
                if (m_structureSet.Add(location.Structure))
                    m_structures.Add(location.Structure);
            }
        }

        /// <summary>
        /// No-op today: the query owns no pooled resources. Kept so pooling can be introduced
        /// later without an interface change.
        /// </summary>
        public void Dispose()
        {
        }
    }
}
