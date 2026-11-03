using CoreECS.Utils;
using System;

namespace CoreECS.Structures
{
    internal sealed class ComponentStorage<T> : ComponentStorageBase
        where T : struct
    {
        private readonly PagedColumn<T> m_payloads = new PagedColumn<T>();

        public override ComponentKind Kind => ComponentKind.Data;

        internal int PayloadPageCount => m_payloads.CountPages;

        internal System.Action BeforePayloadPageAllocationForTesting
        {
            get => m_payloads.BeforePageAllocationForTesting;
            set => m_payloads.BeforePageAllocationForTesting = value;
        }

        internal ComponentStorage(
            uint typeId,
            EntityTable entityTable,
            ComponentHandlerPool handlerPool,
            IComponentChangeSink changeSink = null,
            Action<ComponentHandler> attachmentChanged = null)
            : base(typeId, typeof(T), entityTable, handlerPool, changeSink, attachmentChanged)
        {
        }

        internal ComponentHandler Create(EntityId entityId)
        {
            T initialValue = default(T);
            return Create(entityId, in initialValue);
        }

        internal ComponentHandler Create(EntityId entityId, in T initialValue)
        {
            ComponentHandler handler = ReserveSlot(entityId, out int packedSlot);
            bool payloadReserved = false;
            bool publicationAttempted = false;
            try
            {
                m_payloads.EnsureSlot(packedSlot);
                payloadReserved = true;
                m_payloads[packedSlot] = initialValue;

                publicationAttempted = true;
                return PublishSlot(entityId, packedSlot, handler);
            }
            catch
            {
                if (payloadReserved)
                {
                    m_payloads.ClearSlot(packedSlot);
                }

                if (!publicationAttempted)
                {
                    ReleaseReservedHandler(handler);
                }

                throw;
            }
        }

        internal ref T Get(EntityId entityId)
        {
            return ref m_payloads.GetRef(GetPackedSlot(entityId));
        }

        internal ref T GetPayloadAt(int packedSlot)
        {
            ValidateAllocatedSlot(packedSlot);
            return ref m_payloads.GetRef(packedSlot);
        }

        protected override void MovePayload(int sourceSlot, int destinationSlot)
        {
            m_payloads[destinationSlot] = m_payloads[sourceSlot];
        }

        protected override void ClearPayloadSlot(int packedSlot)
        {
            m_payloads.ClearSlot(packedSlot);
        }
    }
}
