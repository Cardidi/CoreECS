using System.Collections.Generic;

namespace CoreECS.Structures
{
    /// <summary>
    /// Deduplicates structures by their (dense composition, mask) key.
    /// </summary>
    internal sealed class StructureRegistry
    {
        private readonly Dictionary<StructureKey, Structure> m_structures = new();

        /// <summary>Number of registered structures.</summary>
        public int Count => m_structures.Count;

        /// <summary>All registered structures.</summary>
        public IEnumerable<Structure> Structures => m_structures.Values;

        /// <summary>Gets or creates the structure for a composition and mask.</summary>
        public Structure GetOrCreate(uint[] sortedDenseTypeIds, ulong mask)
        {
            var key = new StructureKey(sortedDenseTypeIds, mask);
            if (m_structures.TryGetValue(key, out var existing)) return existing;

            var created = new Structure(sortedDenseTypeIds, key);
            m_structures.Add(key, created);
            return created;
        }

        /// <summary>Gets or creates the same dense composition under a different mask.</summary>
        public Structure GetOrCreateWithMask(Structure source, ulong mask)
        {
            var key = new StructureKey(source.DenseTypeIdsArray, mask);
            if (m_structures.TryGetValue(key, out var existing)) return existing;
            return CreateFrom(source.DenseTypeIdsArray, key);
        }

        /// <summary>Gets or creates the composition formed by adding one dense type.</summary>
        public Structure GetOrCreateWithAddedType(Structure source, uint typeId)
        {
            var ids = source.DenseTypeIdsArray;
            var index = source.InsertionIndexOfDense(typeId);
            var targetIds = new uint[ids.Length + 1];
            System.Array.Copy(ids, 0, targetIds, 0, index);
            targetIds[index] = typeId;
            System.Array.Copy(ids, index, targetIds, index + 1, ids.Length - index);
            return GetOrCreate(targetIds, source.Mask);
        }

        /// <summary>Gets or creates the composition formed by removing one dense type.</summary>
        public Structure GetOrCreateWithRemovedType(Structure source, uint typeId)
        {
            var ids = source.DenseTypeIdsArray;
            var index = source.IndexOfDense(typeId);
            var targetIds = new uint[ids.Length - 1];
            System.Array.Copy(ids, 0, targetIds, 0, index);
            System.Array.Copy(ids, index + 1, targetIds, index, ids.Length - index - 1);
            return GetOrCreate(targetIds, source.Mask);
        }

        private Structure CreateFrom(uint[] sortedDenseTypeIds, in StructureKey key)
        {
            var created = new Structure(sortedDenseTypeIds, key);
            m_structures.Add(key, created);
            return created;
        }
    }
}
