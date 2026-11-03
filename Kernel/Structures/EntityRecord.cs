#if COREECS_ULONG_ENTITY
using EntityValue = System.UInt64;
#else
using EntityValue = System.UInt32;
#endif

namespace CoreECS.Structures
{
    internal struct EntityRecord
    {
        internal EntityValue Generation { get; private set; }

        internal bool IsAlive { get; private set; }

        internal ulong Mask { get; private set; }

        internal ComponentHandler ComponentHead { get; private set; }

        internal EntityRecord(EntityValue generation)
        {
            Generation = generation;
            IsAlive = false;
            Mask = 0;
            ComponentHead = null;
        }

        internal void Activate(ulong mask)
        {
            Generation = EntityId.NextGeneration(Generation);
            IsAlive = true;
            Mask = mask;
            ComponentHead = null;
        }

        internal bool MarkDestroyed()
        {
            if (!IsAlive)
            {
                return false;
            }

            IsAlive = false;
            return true;
        }

        internal void SetComponentHead(ComponentHandler componentHead)
        {
            ComponentHead = componentHead;
        }
    }
}
