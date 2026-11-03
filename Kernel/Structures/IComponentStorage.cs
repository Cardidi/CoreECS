using System;
using System.Collections.Generic;
using CoreECS.Utils;

namespace CoreECS.Structures
{
    internal interface IComponentStorage
    {
        uint TypeId { get; }

        Type ComponentType { get; }

        ComponentKind Kind { get; }

        IComponentChangeSink ChangeSink { get; }

        int LiveCount { get; }

        int AllocatedCount { get; }

        int Capacity { get; }

        bool Contains(EntityId entityId);

        ComponentHandler GetHandler(EntityId entityId);

        EntityId GetEntityIdAt(int packedSlot);

        bool MarkRemove(EntityId entityId);

        void Compact();

        void Clear();
    }

    internal abstract class ComponentStorageBase : IComponentStorage
    {
        private readonly SparseIndex m_sparse = new SparseIndex();
        private readonly PagedColumn<EntityId> m_entities = new PagedColumn<EntityId>();
        private readonly PagedColumn<ComponentHandler> m_handlers = new PagedColumn<ComponentHandler>();
        private readonly List<int> m_holes = new List<int>();
        private readonly EntityTable m_entityTable;
        private readonly ComponentHandlerPool m_handlerPool;
        private readonly Action<ComponentHandler> m_attachmentChanged;

        public uint TypeId { get; }

        public Type ComponentType { get; }

        public abstract ComponentKind Kind { get; }

        public IComponentChangeSink ChangeSink { get; }

        public int LiveCount { get; private set; }

        public int AllocatedCount { get; private set; }

        public int Capacity => m_entities.Capacity;

        internal Action BeforeEntityPageAllocationForTesting
        {
            get => m_entities.BeforePageAllocationForTesting;
            set => m_entities.BeforePageAllocationForTesting = value;
        }

        internal Action BeforeHandlerPageAllocationForTesting
        {
            get => m_handlers.BeforePageAllocationForTesting;
            set => m_handlers.BeforePageAllocationForTesting = value;
        }

        internal Action<ComponentHandler> AfterHandlerReservedForTesting { get; set; }

        internal Action BeforeAttachForTesting { get; set; }

        internal int MarkRemoveCallCountForTesting { get; private set; }

        protected ComponentStorageBase(
            uint typeId,
            Type componentType,
            EntityTable entityTable,
            ComponentHandlerPool handlerPool,
            IComponentChangeSink changeSink = null,
            Action<ComponentHandler> attachmentChanged = null)
        {
            if (typeId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(typeId));
            }

            TypeId = typeId;
            ComponentType = componentType ?? throw new ArgumentNullException(nameof(componentType));
            m_entityTable = entityTable ?? throw new ArgumentNullException(nameof(entityTable));
            m_handlerPool = handlerPool ?? throw new ArgumentNullException(nameof(handlerPool));
            ChangeSink = changeSink;
            m_attachmentChanged = attachmentChanged;
        }

        public bool Contains(EntityId entityId)
        {
            return m_sparse.TryGet(entityId, out _);
        }

        public ComponentHandler GetHandler(EntityId entityId)
        {
            int packedSlot = GetPackedSlot(entityId);
            return m_handlers[packedSlot];
        }

        public EntityId GetEntityIdAt(int packedSlot)
        {
            ValidateAllocatedSlot(packedSlot);
            return m_entities[packedSlot];
        }

        public bool MarkRemove(EntityId entityId)
        {
            MarkRemoveCallCountForTesting++;
            if (!m_sparse.TryGet(entityId, out int packedSlot))
            {
                return false;
            }

            m_sparse.Remove(entityId);
            m_entities[packedSlot] = default(EntityId);

            ComponentHandler handler = m_handlers[packedSlot];
            Detach(handler);
            m_handlerPool.Release(handler);
            m_handlers.ClearSlot(packedSlot);
            m_holes.Add(packedSlot);
            LiveCount--;
            return true;
        }

        public void Compact()
        {
            if (m_holes.Count == 0)
            {
                return;
            }

            m_holes.Sort();
            int tail = AllocatedCount - 1;
            for (int i = 0; i < m_holes.Count; i++)
            {
                int hole = m_holes[i];
                if (hole >= LiveCount)
                {
                    break;
                }

                while (tail > hole && !m_entities[tail].IsValid)
                {
                    tail--;
                }

                EntityId movedEntity = m_entities[tail];
                ComponentHandler movedHandler = m_handlers[tail];
                MovePayload(tail, hole);
                m_entities[hole] = movedEntity;
                m_handlers[hole] = movedHandler;

                m_sparse.Remove(movedEntity);
                m_sparse.Set(movedEntity, hole);
                movedHandler.PackedOffset = hole;
                tail--;
            }

            for (int packedSlot = LiveCount; packedSlot < AllocatedCount; packedSlot++)
            {
                m_entities.ClearSlot(packedSlot);
                m_handlers.ClearSlot(packedSlot);
                ClearPayloadSlot(packedSlot);
            }

            AllocatedCount = LiveCount;
            m_holes.Clear();
        }

        public virtual void Clear()
        {
            for (int packedSlot = 0; packedSlot < AllocatedCount; packedSlot++)
            {
                EntityId entityId = m_entities[packedSlot];
                if (entityId.IsValid)
                {
                    ComponentHandler handler = m_handlers[packedSlot];
                    Detach(handler);
                    m_handlerPool.Release(handler);
                }

                m_entities.ClearSlot(packedSlot);
                m_handlers.ClearSlot(packedSlot);
                ClearPayloadSlot(packedSlot);
            }

            m_sparse.Clear();
            m_holes.Clear();
            LiveCount = 0;
            AllocatedCount = 0;
        }

        protected ComponentHandler ReserveSlot(EntityId entityId, out int packedSlot)
        {
            if (!m_entityTable.IsAlive(entityId))
            {
                throw new ArgumentException("Components require a live EntityId.", nameof(entityId));
            }

            if (m_sparse.TryGet(entityId, out _))
            {
                throw new InvalidOperationException("The entity already owns this component type.");
            }

            packedSlot = AllocatedCount;
            m_entities.EnsureSlot(packedSlot);
            m_handlers.EnsureSlot(packedSlot);

            ComponentHandler handler = m_handlerPool.Get();
            try
            {
                AfterHandlerReservedForTesting?.Invoke(handler);
                return handler;
            }
            catch
            {
                m_handlerPool.Release(handler);
                throw;
            }
        }

        protected ComponentHandler PublishSlot(
            EntityId entityId,
            int packedSlot,
            ComponentHandler handler)
        {
            bool sparsePublished = false;
            bool packedPublished = false;
            bool attached = false;
            try
            {
                handler.Bind(entityId, TypeId, this, packedSlot);
                m_sparse.Set(entityId, packedSlot);
                sparsePublished = true;

                m_entities[packedSlot] = entityId;
                m_handlers[packedSlot] = handler;
                packedPublished = true;

                BeforeAttachForTesting?.Invoke();
                Attach(handler);
                attached = true;

                AllocatedCount++;
                LiveCount++;
                return handler;
            }
            catch
            {
                if (attached)
                {
                    Detach(handler);
                }

                if (sparsePublished)
                {
                    m_sparse.Remove(entityId);
                }

                if (packedPublished)
                {
                    m_entities.ClearSlot(packedSlot);
                    m_handlers.ClearSlot(packedSlot);
                }

                m_handlerPool.Release(handler);
                throw;
            }
        }

        protected void ReleaseReservedHandler(ComponentHandler handler)
        {
            m_handlerPool.Release(handler);
        }

        protected int GetPackedSlot(EntityId entityId)
        {
            if (!m_sparse.TryGet(entityId, out int packedSlot))
            {
                throw new InvalidOperationException("The entity does not own this component type.");
            }

            return packedSlot;
        }

        protected void ValidateAllocatedSlot(int packedSlot)
        {
            if ((uint)packedSlot >= (uint)AllocatedCount)
            {
                throw new ArgumentOutOfRangeException(nameof(packedSlot));
            }
        }

        protected abstract void MovePayload(int sourceSlot, int destinationSlot);

        protected abstract void ClearPayloadSlot(int packedSlot);

        private void Attach(ComponentHandler handler)
        {
            ComponentHandler head = m_entityTable.GetComponentHead(handler.EntityId);
            m_entityTable.SetComponentHead(handler.EntityId, handler);
            handler.Next = head;
            if (head != null)
            {
                head.Previous = handler;
            }

            m_attachmentChanged?.Invoke(handler);
        }

        private void Detach(ComponentHandler handler)
        {
            ComponentHandler previous = handler.Previous;
            ComponentHandler next = handler.Next;
            if (previous == null)
            {
                m_entityTable.SetComponentHead(handler.EntityId, next);
            }
            else
            {
                previous.Next = next;
            }

            if (next != null)
            {
                next.Previous = previous;
            }

            m_attachmentChanged?.Invoke(handler);
        }
    }
}
