using System;
using System.Collections.Generic;

namespace CoreECS.Defines
{
    /// <summary>
    /// Flags that control the behavior of entity collectors.
    /// </summary>
    [Flags]
    public enum EntityCollectorFlag
    {
        /// <summary>
        /// No special behavior.
        /// </summary>
        None = 0,
        
        /// <summary>
        /// Don't remove elements from Collected before Change() is called.
        /// </summary>
        LazyRemove = 1 << 0,
        
        /// <summary>
        /// Don't add elements from Collected before Change() is called.
        /// </summary>
        LazyAdd = 1 << 1,

        /// <summary>
        /// Don't change elements in Collected before Change() is called.
        /// </summary>
        Lazy = LazyRemove | LazyAdd,

        /// <summary>
        /// Include entities with component revision changes in Changed.
        /// </summary>
        ChangedOnRevision = 1 << 2,

        /// <summary>
        /// Include entities entering collector in Changed.
        /// </summary>
        ChangedOnMatching = 1 << 3,

        /// <summary>
        /// Include entities leaving collector in Changed.
        /// </summary>
        ChangedOnClashing = 1 << 4,

        /// <summary>
        /// Default collector behavior.
        /// </summary>
        Default = Lazy | ChangedOnRevision | ChangedOnMatching,

    }
    
    /// <summary>
    /// Collects entities that satisfy a matcher's criteria.
    /// </summary>
    public interface IEntityCollector : IDisposable
    {
        /// <summary>
        /// Gets the matcher of this collector.
        /// </summary>
        public IEntityMatcher Matcher { get; }

        /// <summary>
        /// Gets all collected entities.
        /// </summary>
        public IReadOnlyList<ulong> Collected { get; }

        /// <summary>
        /// Gets entities that were previously excluded from collector and are now being collected.
        /// </summary>
        public IReadOnlyList<ulong> Matching { get; }

        /// <summary>
        /// Gets entities that were previously included in collector and are now being excluded.
        /// </summary>
        public IReadOnlyList<ulong> Clashing { get; }

        /// <summary>
        /// Gets entities that changed during the previous collecting phase.
        /// </summary>
        public IReadOnlyList<ulong> Changed { get; }

        /// <summary>
        /// Summarizes previous changes and starts a new collecting phase.
        /// </summary>
        public void Flush();

        /// <summary>
        /// Summarizes previous changes and starts a new collecting phase.
        /// </summary>
        [Obsolete("Use Flush() instead.")]
        public void Change();
    }

}