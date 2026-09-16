using System.Threading;

namespace CoreECS.Structures
{
    /// <summary>
    /// Supplies process-wide component instance versions.
    /// Re-adding a component gets a fresh version, so refs created before the removal
    /// can never match the new instance.
    /// </summary>
    internal static class ComponentVersion
    {
        private static int s_next = 0;

        /// <summary>
        /// Gets the next version value. Wraps after uint.MaxValue increments;
        /// version 0 is allowed (not used as a sentinel).
        /// </summary>
        public static uint Next()
        {
            return (uint)Interlocked.Increment(ref s_next);
        }
    }
}
