using System;
using System.Collections.Generic;
using CoreECS.Structures;

namespace CoreECS.Defines
{
    /// <summary>
    /// A non-pooled query over the entities matching an <see cref="IEntityMatcher"/>.
    /// The exposed snapshot is rebuilt by <see cref="Refresh"/> and stays stable while
    /// enumerated; call <see cref="Refresh"/> again to recompute it. Dispose when done.
    /// </summary>
    /// <remarks>
    /// <see cref="IDisposable.Dispose"/> is currently a no-op: the snapshot stays readable
    /// after disposal, but callers should not rely on that once query pooling lands.
    /// </remarks>
    public interface IEntityQuery : IDisposable
    {
        /// <summary>
        /// Gets the matcher this query was created with.
        /// </summary>
        public IEntityMatcher Matcher { get; }

        /// <summary>
        /// Gets the distinct structures containing at least one matching entity in the last
        /// snapshot. Empty until <see cref="Refresh"/> is called. Order is unspecified.
        /// </summary>
        public IReadOnlyList<Structure> Structures { get; }

        /// <summary>
        /// Gets the matching entity ids of the last snapshot. Empty until <see cref="Refresh"/>
        /// is called. The enumeration is stable until the next <see cref="Refresh"/>.
        /// </summary>
        public IEnumerable<ulong> Entities { get; }

        /// <summary>
        /// Recomputes the snapshot from the live entity table.
        /// </summary>
        public void Refresh();
    }
}
