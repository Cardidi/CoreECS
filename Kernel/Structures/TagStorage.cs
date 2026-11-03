using CoreECS.Utils;

namespace CoreECS.Structures
{
    internal sealed class TagStorage<T> : ComponentStorageBase
        where T : struct
    {
        private T m_sharedDefault;

        public override ComponentKind Kind => ComponentKind.Tag;

        internal int PayloadPageCount => 0;

        internal TagStorage(
            uint typeId,
            EntityTable entityTable,
            ComponentHandlerPool handlerPool)
            : base(typeId, entityTable, handlerPool)
        {
        }

        internal ComponentHandler Create(EntityId entityId)
        {
            int packedSlot = CreateSlot(entityId);
            return GetHandlerAt(packedSlot);
        }

        internal ref T GetSharedDefault()
        {
            return ref m_sharedDefault;
        }

        public override void Clear()
        {
            base.Clear();
            m_sharedDefault = default(T);
        }

        protected override void MovePayload(int sourceSlot, int destinationSlot)
        {
        }

        protected override void ClearPayloadSlot(int packedSlot)
        {
        }
    }
}
