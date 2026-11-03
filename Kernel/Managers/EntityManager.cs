using System;
using CoreECS.Defines;
using CoreECS.Structures;
using CoreECS.Utils;

namespace CoreECS.Managers
{
    public sealed class EntityManager : IWorldManager
    {
        private readonly ComponentManager m_componentManager;
        private readonly EntityTable m_entityTable;
        private EntityMatchManager m_entityMatchManager;
        private bool m_init;
        private bool m_shutdown;

        public IWorld World { get; }

        public EntityManager(IWorld world, ComponentManager compManager)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            m_componentManager = compManager ?? throw new ArgumentNullException(nameof(compManager));
            m_entityTable = compManager.EntityTable;
        }

        internal EntityId Create(ulong mask)
        {
            m_componentManager.StructuralMutationGuard.ThrowIfMutationForbidden();
            EnsureAvailable();
            EntityId entityId = m_entityTable.Create(mask);
            m_entityMatchManager?.MarkEntityLifecycle(entityId);
            return entityId;
        }

        internal bool IsAlive(EntityId entityId)
        {
            return m_entityTable.IsAlive(entityId);
        }

        internal ulong GetMask(EntityId entityId)
        {
            return m_entityTable.GetMask(entityId);
        }

        internal bool Destroy(EntityId entityId)
        {
            m_componentManager.StructuralMutationGuard.ThrowIfMutationForbidden();
            EnsureAvailable();
            if (!m_entityTable.IsAlive(entityId))
            {
                return false;
            }

            ComponentHandler handler;
            while ((handler = m_entityTable.GetComponentHead(entityId)) != null)
            {
                m_componentManager.Destroy(new ComponentRef(handler), entityId);
            }

            if (!m_entityTable.MarkDestroyed(entityId))
            {
                return false;
            }

            m_entityMatchManager?.MarkEntityLifecycle(entityId);
            return true;
        }

        internal void AttachEntityMatchManager(EntityMatchManager entityMatchManager)
        {
            m_entityMatchManager = entityMatchManager ??
                throw new ArgumentNullException(nameof(entityMatchManager));
        }

        internal void CommitReleases()
        {
            m_componentManager.StructuralMutationGuard.ThrowIfMutationForbidden();
            m_entityTable.CommitReleases();
        }

        internal void AcquireRevisionInterest()
        {
            m_componentManager.AcquireRevisionInterest();
        }

        internal void ReleaseRevisionInterest()
        {
            m_componentManager.ReleaseRevisionInterest();
        }

        internal void AdvanceChangeEpoch()
        {
            m_componentManager.AdvanceChangeEpoch();
        }

        public void OnManagerCreated()
        {
            m_componentManager.StructuralMutationGuard.ThrowIfMutationForbidden();
            m_init = true;
            m_shutdown = false;
        }

        public void OnWorldStarted()
        {
            m_componentManager.StructuralMutationGuard.ThrowIfMutationForbidden();
        }

        public void OnWorldEnded()
        {
            m_componentManager.StructuralMutationGuard.ThrowIfMutationForbidden();
        }

        public void OnManagerDestroyed()
        {
            m_componentManager.StructuralMutationGuard.ThrowIfMutationForbidden();
            m_shutdown = true;
        }

        private void EnsureAvailable()
        {
            Assertion.IsTrue(m_init);
            Assertion.IsFalse(m_shutdown);
        }
    }
}
