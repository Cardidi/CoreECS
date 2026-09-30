using System;
using System.Collections.Generic;

namespace CoreECS.Structures
{
    /// <summary>
    /// Compact identity of an archetype: the entity mask plus a hash of its sorted dense
    /// component type ids. The key does not retain the input array.
    /// </summary>
    public readonly struct StructureKey : IEquatable<StructureKey>
    {
        private readonly ulong m_denseTypeIdsHash;

        /// <summary>Entity mask; part of archetype identity.</summary>
        public readonly ulong Mask;

        /// <summary>
        /// Creates a key from sorted dense type ids and a mask without retaining the array.
        /// </summary>
        public StructureKey(uint[] sortedDenseTypeIds, ulong mask)
        {
            m_denseTypeIdsHash = HashDenseTypeIds(sortedDenseTypeIds);
            Mask = mask;
        }

        internal static ulong HashDenseTypeIds(IReadOnlyList<uint> sortedDenseTypeIds)
        {
            unchecked
            {
                // FNV-1a over both the count and values keeps the empty composition distinct
                // from compositions whose values happen to hash from the same initial state.
                var hash = 14695981039346656037UL;
                var length = sortedDenseTypeIds?.Count ?? 0;
                hash = (hash ^ (uint)length) * 1099511628211UL;
                for (var i = 0; i < length; i++)
                {
                    hash = (hash ^ sortedDenseTypeIds[i]) * 1099511628211UL;
                }

                return hash;
            }
        }

        /// <inheritdoc />
        public bool Equals(StructureKey other)
        {
            return Mask == other.Mask && m_denseTypeIdsHash == other.m_denseTypeIdsHash;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is StructureKey other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)(Mask ^ (Mask >> 32));
                return hash * 397 + (int)(m_denseTypeIdsHash ^ (m_denseTypeIdsHash >> 32));
            }
        }
    }
}
