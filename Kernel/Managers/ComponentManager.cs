using System;
using System.Collections.Generic;
using CoreECS.Defines;
using CoreECS.Structures;

namespace CoreECS.Managers
{
    public sealed class ComponentManager : IWorldManager, IComponentChangeSink
    {
        private readonly StructuralMutationGuard m_structuralMutationGuard = new StructuralMutationGuard();
        private readonly ComponentChangeJournal m_changeJournal = new ComponentChangeJournal();
        private readonly EntityTable m_entityTable;
        private readonly ComponentStorageRegistry m_storages;
        private readonly HashSet<IComponentStorage> m_dirtyStorages = new HashSet<IComponentStorage>();
        private EntityMatchManager? m_entityMatchManager;
        private int m_revisionInterestCount;
        private bool m_active;

        internal StructuralMutationGuard StructuralMutationGuard => m_structuralMutationGuard;
        internal EntityTable EntityTable => m_entityTable;
        internal ComponentStorageRegistry StorageRegistry => m_storages;
        internal ComponentChangeJournal ChangeJournal => m_changeJournal;
        internal int DirtyStorageCountForTesting => m_dirtyStorages.Count;

        bool IComponentChangeSink.HasInterest => m_revisionInterestCount != 0;
        ulong IComponentChangeSink.Epoch => m_changeJournal.CurrentEpoch;

        public ComponentManager()
        {
            m_entityTable = new EntityTable();
            m_storages = new ComponentStorageRegistry(m_entityTable, this, OnHandlerAttachmentChanged);
        }

        internal ComponentRef<T> Create<T>(EntityId entityId)
            where T : struct, IComponent<T>
        {
            T initialValue = default(T);
            return Create(entityId, in initialValue);
        }

        internal ComponentRef<T> Create<T>(EntityId entityId, in T initialValue)
            where T : struct, IComponent<T>
        {
            m_structuralMutationGuard.ThrowIfMutationForbidden();
            ComponentTypeMetadata metadata = ComponentTypeRegistry.GetOrRegister<T>();
            ComponentHandler handler;
            if (metadata.Kind == ComponentKind.Tag)
            {
                var storage = (TagStorage<T>)m_storages.GetOrCreate<T>(ComponentKind.Tag);
                handler = storage.Create(entityId);
            }
            else
            {
                var storage = (ComponentStorage<T>)m_storages.GetOrCreate<T>(ComponentKind.Data);
                handler = storage.Create(entityId, in initialValue);
                ref T component = ref storage.GetPayloadAt(handler.PackedOffset);
                InvokeCreate(ref component, entityId);
            }

            return new ComponentRef<T>(handler);
        }

        internal ComponentRef<T> Get<T>(EntityId entityId)
            where T : struct, IComponent<T>
        {
            ComponentStorageBase? storage = GetStorageBase<T>(false);
            if (storage == null || !storage.Contains(entityId))
            {
                return default(ComponentRef<T>);
            }

            return new ComponentRef<T>(storage.GetHandler(entityId));
        }

        internal bool Has<T>(EntityId entityId)
            where T : struct, IComponent<T>
        {
            ComponentStorageBase? storage = GetStorageBase<T>(false);
            return storage != null && storage.Contains(entityId);
        }

        internal void Destroy<T>(EntityId entityId)
            where T : struct, IComponent<T>
        {
            ComponentRef<T> component = Get<T>(entityId);
            if (!component.NotNull)
            {
                throw new InvalidOperationException("The entity does not own this component type.");
            }

            Destroy(component);
        }

        internal void Destroy<T>(ComponentRef<T> component)
            where T : struct, IComponent<T>
        {
            Destroy((ComponentRef)component, component.EntityId);
        }

        internal void Destroy<T>(ComponentRef<T> component, EntityId expectedEntityId)
            where T : struct, IComponent<T>
        {
            Destroy((ComponentRef)component, expectedEntityId);
        }

        internal void Destroy(ComponentRef component)
        {
            Destroy(component, component.EntityId);
        }

        internal void Destroy(ComponentRef component, EntityId expectedEntityId)
        {
            m_structuralMutationGuard.ThrowIfMutationForbidden();
            if (!component.NotNull)
            {
                throw new InvalidOperationException("Component has already been destroyed!");
            }

            ComponentHandler handler = component.Handler;
            if (!m_storages.Owns(handler.Storage))
            {
                throw new InvalidOperationException(
                    "Component reference belongs to a different ComponentManager.");
            }

            if (handler.EntityId != expectedEntityId)
            {
                throw new InvalidOperationException(
                    "Component reference belongs to a different entity.");
            }

            EntityId entityId = handler.EntityId;
            IComponentStorage storage = handler.Storage;
            storage.InvokeDestroyHook(handler, entityId, m_structuralMutationGuard);
            if (!storage.MarkRemove(entityId))
            {
                throw new InvalidOperationException("Component has already been destroyed!");
            }

            m_dirtyStorages.Add(storage);
        }

        internal ComponentStorage<T>? GetStorage<T>(bool createIfNotExist = true)
            where T : struct, IComponent<T>
        {
            ComponentTypeMetadata metadata = ComponentTypeRegistry.GetOrRegister<T>();
            if (metadata.Kind != ComponentKind.Data)
            {
                return null;
            }

            if (m_storages.TryGet(metadata.TypeId, out IComponentStorage? storage))
            {
                return (ComponentStorage<T>)storage!;
            }

            return createIfNotExist
                ? (ComponentStorage<T>)m_storages.GetOrCreate<T>(ComponentKind.Data)
                : null;
        }

        internal void CompactDirtyStores()
        {
            m_structuralMutationGuard.ThrowIfMutationForbidden();
            foreach (IComponentStorage storage in m_dirtyStorages)
            {
                storage.Compact();
            }

            m_dirtyStorages.Clear();
        }

        internal void AcquireRevisionInterest()
        {
            m_revisionInterestCount++;
        }

        internal void ReleaseRevisionInterest()
        {
            if (m_revisionInterestCount > 0)
            {
                m_revisionInterestCount--;
            }
        }

        internal void AdvanceChangeEpoch()
        {
            m_changeJournal.AdvanceEpoch();
        }

        internal void AttachEntityMatchManager(EntityMatchManager entityMatchManager)
        {
            m_entityMatchManager = entityMatchManager ??
                throw new ArgumentNullException(nameof(entityMatchManager));
        }

        void IComponentChangeSink.OnChanged(ComponentHandler handler)
        {
            if (m_entityMatchManager != null)
            {
                m_entityMatchManager.MarkComponentRevision(handler.EntityId, handler.TypeId);
                return;
            }

            m_changeJournal.Append(handler.EntityId, handler.TypeId);
        }

        public void OnManagerCreated()
        {
            m_structuralMutationGuard.ThrowIfMutationForbidden();
            m_active = true;
        }

        public void OnWorldStarted()
        {
            m_structuralMutationGuard.ThrowIfMutationForbidden();
        }

        public void OnWorldEnded()
        {
            m_structuralMutationGuard.ThrowIfMutationForbidden();
        }

        public void OnManagerDestroyed()
        {
            m_structuralMutationGuard.ThrowIfMutationForbidden();
            m_active = false;
            m_storages.Clear();
            m_dirtyStorages.Clear();
            m_revisionInterestCount = 0;
            m_changeJournal.Reset();
            m_entityTable.ClearForRestart();
        }

        private ComponentStorageBase? GetStorageBase<T>(bool createIfNotExist)
            where T : struct, IComponent<T>
        {
            ComponentTypeMetadata metadata = ComponentTypeRegistry.GetOrRegister<T>();
            if (m_storages.TryGet(metadata.TypeId, out IComponentStorage? storage))
            {
                return (ComponentStorageBase)storage!;
            }

            return createIfNotExist
                ? (ComponentStorageBase)m_storages.GetOrCreate<T>(metadata.Kind)
                : null;
        }

        private void OnHandlerAttachmentChanged(ComponentHandler handler)
        {
            if (!m_active || m_entityMatchManager == null)
            {
                return;
            }

            m_entityMatchManager.MarkComponentStructural(handler.EntityId, handler.TypeId);
        }

        private void InvokeCreate<T>(ref T component, EntityId entityId)
            where T : struct, IComponent<T>
        {
            using (m_structuralMutationGuard.EnterHook())
            {
                ComponentHookDispatcher<T>.InvokeCreate(ref component, entityId);
            }
        }

    }
}
