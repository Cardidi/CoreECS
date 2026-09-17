using System;
using CoreECS.Structures;

namespace CoreECS.Defines
{
    /// <summary>
    /// Defines a matcher to filter entities based on their components.
    /// </summary>
    public interface IEntityMatcher
    {
        /// <summary>
        /// Determines if a structure row satisfies all requirements of the matcher.
        /// Dense conditions are structure-level; tag and sparse conditions are row-level.
        /// </summary>
        /// <param name="structure">Structure owning the row</param>
        /// <param name="row">Live row inside the structure</param>
        /// <returns>True if the row matches the criteria, false otherwise</returns>
        public bool ComponentFilter(Structure structure, int row);

        /// <summary>
        /// Gets the allowed entities mask for this matcher.
        /// </summary>
        public ulong EntityMask { get; }

        /// <summary>
        /// Determines whether the specified component type is relevant
        /// to this matcher's criteria (all, any, or none sets).
        /// </summary>
        /// <param name="componentType">The component type to check</param>
        /// <returns>True if the component appears in any matcher set</returns>
        public bool IsRelevantComponent(Type componentType);
    }
}
