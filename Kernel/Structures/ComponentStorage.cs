using CoreECS.Utils;

namespace CoreECS.Structures
{
    internal sealed class ComponentStorage<T> : ComponentStorageBase
        where T : struct
    {
        private readonly PagedColumn<T> m_payloads = new PagedColumn<T>();

        public override ComponentKind Kind => ComponentKind.Data;

        internal int PayloadPageCount => m_payloads.CountPages;

        internal ComponentStorage(
            uint typeId,
            EntityTable entityTable,
            ComponentHandlerPool handlerPool)
            : base(typeId, entityTable, handlerPool)
        {
        }

        internal ComponentHandler Create(EntityId entityId)
        {
            T initialValue = default(T);
            return Create(entityId, in initialValue);
        }

        internal ComponentHandler Create(EntityId entityId, in T initialValue)
        {
            int packedSlot = CreateSlot(entityId);
            m_payloads.EnsureSlot(packedSlot);
            m_payloads[packedSlot] = initialValue;
            return GetHandlerAt(packedSlot);
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
