using CoreECS.Utils;
using System;

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
            ComponentHandlerPool handlerPool,
            IComponentChangeSink? changeSink = null,
            Action<ComponentHandler>? attachmentChanged = null)
            : base(typeId, typeof(T), entityTable, handlerPool, changeSink, attachmentChanged)
        {
        }

        internal ComponentHandler Create(EntityId entityId)
        {
            ComponentHandler handler = ReserveSlot(entityId, out int packedSlot);
            return PublishSlot(entityId, packedSlot, handler);
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
