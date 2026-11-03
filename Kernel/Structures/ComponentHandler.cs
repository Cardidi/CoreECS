#if COREECS_ULONG_ENTITY
using RevisionValue = System.UInt64;
#else
using RevisionValue = System.UInt32;
#endif

namespace CoreECS.Structures
{
    internal interface IComponentChangeSink
    {
        bool HasInterest { get; }

        ulong Epoch { get; }

        void OnChanged(ComponentHandler handler);
    }

    internal sealed class ComponentHandler
    {
        internal EntityId EntityId { get; private set; }

        internal uint TypeId { get; private set; }

        internal IComponentStorage Storage { get; private set; }

        internal int PackedOffset { get; set; }

        internal RevisionValue Revision { get; set; }

        internal ulong LastJournalEpoch { get; set; }

        internal ulong BindGeneration { get; private set; }

        internal IComponentChangeSink ChangeSink { get; set; }

        internal ComponentHandler Previous { get; set; }

        internal ComponentHandler Next { get; set; }

        internal ComponentHandler()
        {
            PackedOffset = -1;
        }

        internal void Bind(EntityId entityId, uint typeId, IComponentStorage storage, int packedOffset)
        {
            EntityId = entityId;
            TypeId = typeId;
            Storage = storage;
            PackedOffset = packedOffset;
            Revision = 0;
            LastJournalEpoch = 0;
            Previous = null;
            Next = null;
            ChangeSink = storage.ChangeSink;
            BindGeneration = BindGeneration == ulong.MaxValue ? 1 : BindGeneration + 1;
        }

        internal void IncrementRevision()
        {
            Revision = Revision == RevisionValue.MaxValue ? (RevisionValue)1 : Revision + 1;
        }

        internal void RecordWrite()
        {
            IncrementRevision();
            IComponentChangeSink sink = ChangeSink;
            if (sink == null || !sink.HasInterest || LastJournalEpoch == sink.Epoch)
            {
                return;
            }

            LastJournalEpoch = sink.Epoch;
            sink.OnChanged(this);
        }

        internal void Invalidate()
        {
            EntityId = default(EntityId);
            TypeId = 0;
            Storage = null;
            PackedOffset = -1;
            Revision = 0;
            LastJournalEpoch = 0;
            ChangeSink = null;
            Previous = null;
            Next = null;
        }
    }
}
