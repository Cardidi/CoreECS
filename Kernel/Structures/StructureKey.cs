using System;
using System.Collections.Generic;

namespace CoreECS.Structures
{
    /// <summary>
    /// Identity of an archetype: the sorted dense component type ids plus the entity mask.
    /// Callers must pass an already sorted, never-mutated id array.
    /// </summary>
    public readonly struct StructureKey : IEquatable<StructureKey>
    {
        private readonly uint[] m_denseTypeIds;

        /// <summary>Entity mask; part of archetype identity.</summary>
        public readonly ulong Mask;

        /// <summary>Sorted dense component type ids.</summary>
        public IReadOnlyList<uint> DenseTypeIds => m_denseTypeIds ?? Array.Empty<uint>();

        /// <summary>Number of dense component types.</summary>
        public int DenseCount => m_denseTypeIds?.Length ?? 0;

        /// <summary>
        /// Creates a key from sorted dense type ids and a mask.
        /// </summary>
        public StructureKey(uint[] sortedDenseTypeIds, ulong mask)
        {
            m_denseTypeIds = sortedDenseTypeIds ?? Array.Empty<uint>();
            Mask = mask;
        }

        /// <summary>Returns a defensive copy of the dense type ids.</summary>
        public uint[] ToArray()
        {
            var length = m_denseTypeIds?.Length ?? 0;
            var copy = new uint[length];
            if (length > 0) Array.Copy(m_denseTypeIds, copy, length);
            return copy;
        }

        /// <summary>
        /// Returns a new sorted id array with the type added.
        /// Returns the same array instance when the type is already present.
        /// </summary>
        public static uint[] AddType(uint[] sortedIds, uint typeId)
        {
            var source = sortedIds ?? Array.Empty<uint>();
            var index = Array.BinarySearch(source, typeId);
            if (index >= 0) return source;

            index = ~index;
            var result = new uint[source.Length + 1];
            Array.Copy(source, 0, result, 0, index);
            result[index] = typeId;
            Array.Copy(source, index, result, index + 1, source.Length - index);
            return result;
        }

        /// <summary>
        /// Returns a new sorted id array with the type removed.
        /// Returns the same array instance when the type is absent.
        /// </summary>
        public static uint[] RemoveType(uint[] sortedIds, uint typeId)
        {
            var source = sortedIds ?? Array.Empty<uint>();
            var index = Array.BinarySearch(source, typeId);
            if (index < 0) return source;

            var result = new uint[source.Length - 1];
            Array.Copy(source, 0, result, 0, index);
            Array.Copy(source, index + 1, result, index, source.Length - index - 1);
            return result;
        }

        /// <inheritdoc />
        public bool Equals(StructureKey other)
        {
            if (Mask != other.Mask) return false;

            var mine = m_denseTypeIds;
            var theirs = other.m_denseTypeIds;
            var myLength = mine?.Length ?? 0;
            var theirLength = theirs?.Length ?? 0;
            if (myLength != theirLength) return false;

            for (var i = 0; i < myLength; i++)
            {
                if (mine[i] != theirs[i]) return false;
            }

            return true;
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
                var hash = (int)(Mask ^ (Mask >> 32)) * 397;
                if (m_denseTypeIds != null)
                {
                    for (var i = 0; i < m_denseTypeIds.Length; i++)
                    {
                        hash = hash * 31 + (int)m_denseTypeIds[i];
                    }
                }

                return hash;
            }
        }
    }
}
