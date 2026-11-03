using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CoreECS.Structures;

namespace CoreECS
{
    /// <summary>
    /// A reusable materialized snapshot of the entities matching an <see cref="EntityMatcher"/>.
    /// The snapshot changes only when <see cref="Refresh"/> is called. The query is invalid after
    /// its owning World shuts down and must then be discarded.
    /// </summary>
    public sealed class EntityQuery
    {
        private readonly World m_world;
        private readonly EntityMatcherPlan m_plan;
        private readonly EntityTable m_entityTable;
        private readonly ComponentStorageRegistry m_storages;
        private readonly List<EntityId> m_entities = new List<EntityId>();
        private readonly ReadOnlyCollection<EntityId> m_readOnlyEntities;
        private readonly List<EntityId> m_candidates = new List<EntityId>();
        private readonly HashSet<EntityId> m_seenCandidates = new HashSet<EntityId>();

        /// <summary>
        /// Gets the frozen matcher used by this query.
        /// </summary>
        public EntityMatcher Matcher { get; }

        /// <summary>
        /// Gets the current materialized EntityId snapshot.
        /// </summary>
        public IReadOnlyList<EntityId> Entities => m_readOnlyEntities;

        /// <summary>
        /// Gets the number of EntityIds in the current snapshot.
        /// </summary>
        public int Count => m_entities.Count;

        /// <summary>
        /// Gets an EntityId from the current snapshot. Result order is unspecified.
        /// </summary>
        public EntityId this[int index] => m_entities[index];

        internal EntityMatcherCandidateStrategy CandidateStrategyForTesting =>
            m_plan.CandidateStrategy;

        internal int CandidateCountForTesting { get; private set; }

        internal int ComponentMatchCountForTesting { get; private set; }

        internal int BufferCapacityForTesting => m_entities.Capacity;

        internal EntityQuery(
            World world,
            EntityMatcher matcher,
            EntityTable entityTable,
            ComponentStorageRegistry storages,
            bool refreshImmediately)
        {
            m_world = world ?? throw new ArgumentNullException(nameof(world));
            Matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
            m_entityTable = entityTable ?? throw new ArgumentNullException(nameof(entityTable));
            m_storages = storages ?? throw new ArgumentNullException(nameof(storages));
            m_plan = matcher.Bind();
            m_readOnlyEntities = m_entities.AsReadOnly();

            if (refreshImmediately)
            {
                Refresh();
            }
        }

        /// <summary>
        /// Replaces the materialized snapshot with the entities matching the current World state.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown after the owning World shuts down.</exception>
        public void Refresh()
        {
            if (!m_world.Ready)
            {
                throw new InvalidOperationException("EntityQuery cannot be refreshed because its World is not ready.");
            }

            m_entities.Clear();
            m_candidates.Clear();
            m_seenCandidates.Clear();
            CandidateCountForTesting = 0;
            ComponentMatchCountForTesting = 0;

            GatherCandidates();
            CandidateCountForTesting = m_candidates.Count;

            for (int i = 0; i < m_candidates.Count; i++)
            {
                EntityId entityId = m_candidates[i];
                if (!m_entityTable.IsAlive(entityId))
                {
                    continue;
                }

                if ((m_plan.EntityMask & m_entityTable.GetMask(entityId)) == 0)
                {
                    continue;
                }

                ComponentMatchCountForTesting++;
                if (MatchesComponents(entityId))
                {
                    m_entities.Add(entityId);
                }
            }
        }

        private void GatherCandidates()
        {
            switch (m_plan.CandidateStrategy)
            {
                case EntityMatcherCandidateStrategy.LiveEntityTable:
                    m_entityTable.AppendLiveEntityIds(m_candidates);
                    return;
                case EntityMatcherCandidateStrategy.SmallestAllStorage:
                    GatherSmallestAllStorage();
                    return;
                case EntityMatcherCandidateStrategy.AnyStorageUnion:
                    GatherAnyStorageUnion();
                    return;
                default:
                    throw new InvalidOperationException("Unknown EntityQuery candidate strategy.");
            }
        }

        private void GatherSmallestAllStorage()
        {
            IComponentStorage smallest = null;
            ReadOnlySpan<uint> allTypeIds = m_plan.AllTypeIds;
            for (int i = 0; i < allTypeIds.Length; i++)
            {
                if (!m_storages.TryGet(allTypeIds[i], out IComponentStorage storage))
                {
                    return;
                }

                if (smallest == null || storage.LiveCount < smallest.LiveCount)
                {
                    smallest = storage;
                }
            }

            AppendStorageEntities(smallest, false);
        }

        private void GatherAnyStorageUnion()
        {
            ReadOnlySpan<uint> anyTypeIds = m_plan.AnyTypeIds;
            for (int i = 0; i < anyTypeIds.Length; i++)
            {
                if (m_storages.TryGet(anyTypeIds[i], out IComponentStorage storage))
                {
                    AppendStorageEntities(storage, true);
                }
            }
        }

        private void AppendStorageEntities(IComponentStorage storage, bool deduplicate)
        {
            if (storage == null)
            {
                return;
            }

            for (int packedSlot = 0; packedSlot < storage.AllocatedCount; packedSlot++)
            {
                EntityId entityId = storage.GetEntityIdAt(packedSlot);
                if (!entityId.IsValid)
                {
                    continue;
                }

                if (!deduplicate || m_seenCandidates.Add(entityId))
                {
                    m_candidates.Add(entityId);
                }
            }
        }

        private bool MatchesComponents(EntityId entityId)
        {
            ReadOnlySpan<uint> noneTypeIds = m_plan.NoneTypeIds;
            for (int i = 0; i < noneTypeIds.Length; i++)
            {
                if (m_storages.TryGet(noneTypeIds[i], out IComponentStorage storage) &&
                    storage.Contains(entityId))
                {
                    return false;
                }
            }

            ReadOnlySpan<uint> allTypeIds = m_plan.AllTypeIds;
            for (int i = 0; i < allTypeIds.Length; i++)
            {
                if (!m_storages.TryGet(allTypeIds[i], out IComponentStorage storage) ||
                    !storage.Contains(entityId))
                {
                    return false;
                }
            }

            ReadOnlySpan<uint> anyTypeIds = m_plan.AnyTypeIds;
            if (anyTypeIds.Length == 0)
            {
                return true;
            }

            for (int i = 0; i < anyTypeIds.Length; i++)
            {
                if (m_storages.TryGet(anyTypeIds[i], out IComponentStorage storage) &&
                    storage.Contains(entityId))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
