using System.Collections.Generic;

namespace CoreECS.Structures
{
    /// <summary>
    /// Deduplicates structures by their (dense composition, mask) key.
    /// </summary>
    public sealed class StructureRegistry
    {
        private readonly Dictionary<StructureKey, Structure> m_structures = new();

        /// <summary>Number of registered structures.</summary>
        public int Count => m_structures.Count;

        /// <summary>All registered structures.</summary>
        public IEnumerable<Structure> Structures => m_structures.Values;

        /// <summary>Gets or creates the structure for a composition and mask.</summary>
        public Structure GetOrCreate(uint[] sortedDenseTypeIds, ulong mask)
        {
            return GetOrCreate(new StructureKey(sortedDenseTypeIds, mask));
        }

        /// <summary>
        /// Gets or creates the structure for a key.
        /// The registry stores its own copy of the key array so later caller-side
        /// mutations of the input array cannot corrupt the dictionary.
        /// </summary>
        public Structure GetOrCreate(in StructureKey key)
        {
            if (m_structures.TryGetValue(key, out var existing)) return existing;

            var created = new Structure(key);
            m_structures.Add(new StructureKey(key.ToArray(), key.Mask), created);
            return created;
        }
    }
}
