using System;

#if COREECS_ULONG_ENTITY
using EntityValue = System.UInt64;
#else
using EntityValue = System.UInt32;
#endif

namespace CoreECS
{
    public readonly struct EntityId : IEquatable<EntityId>
    {
#if COREECS_ULONG_ENTITY
        private const int IndexBits = 32;
        internal const EntityValue MaxIndex = uint.MaxValue;
        internal const EntityValue MaxGeneration = uint.MaxValue;
#else
        private const int IndexBits = 20;
        internal const EntityValue MaxIndex = (1U << IndexBits) - 1;
        internal const EntityValue MaxGeneration = (1U << 12) - 1;
#endif

        internal EntityValue Raw { get; }

        public EntityValue Index => Raw & MaxIndex;

        public EntityValue Generation => (Raw >> IndexBits) & MaxGeneration;

        public bool IsValid => Raw != 0;

        private EntityId(EntityValue raw)
        {
            Raw = raw;
        }

        internal static EntityId Create(EntityValue index, EntityValue generation)
        {
            if (index == 0 || index > MaxIndex)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            if (generation == 0 || generation > MaxGeneration)
            {
                throw new ArgumentOutOfRangeException(nameof(generation));
            }

            return new EntityId(index | (generation << IndexBits));
        }

        internal static EntityValue NextGeneration(EntityValue current)
        {
            return current == 0 || current == MaxGeneration ? 1 : current + 1;
        }

        public bool Equals(EntityId other)
        {
            return Raw == other.Raw;
        }

        public override bool Equals(object obj)
        {
            return obj is EntityId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Raw.GetHashCode();
        }

        public override string ToString()
        {
            return $"EntityId(Index={Index}, Generation={Generation})";
        }

        public static bool operator ==(EntityId left, EntityId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(EntityId left, EntityId right)
        {
            return !left.Equals(right);
        }
    }
}
