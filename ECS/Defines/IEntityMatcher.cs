using System.Collections.Generic;

namespace CoreECS.Defines
{
    /// <summary>
    /// Defines a matcher to filter entities based on their components.
    /// </summary>
    public interface IEntityMatcher
    {
        /// <summary>
        /// Determines if an entity satisfies all requirements of the matcher.
        /// </summary>
        /// <param name="components">All components of this entity</param>
        /// <returns>True if the entity matches the criteria, false otherwise</returns>
        public bool ComponentFilter(IReadOnlyCollection<IComponentRefCore> components);

        /// <summary>
        /// Gets the allowed entities mask for this matcher.
        /// </summary>
        public ulong EntityMask { get; }
    }
}