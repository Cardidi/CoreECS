using System;
using System.Collections.Generic;

namespace CoreECS.Defines
{
    /// <summary>
    /// Flags that control which entity events are mirrored into <see cref="IEntityCollector.Changed"/>.
    /// Collectors always defer <see cref="IEntityCollector.Collected"/>,
    /// <see cref="IEntityCollector.Matching"/>, <see cref="IEntityCollector.Clashing"/>, and
    /// <see cref="IEntityCollector.Changed"/> until <see cref="IEntityCollector.Flush"/> is called.
    /// </summary>
    [Flags]
    public enum EntityCollectorFlag
    {
        /// <summary>
        /// No events are mirrored into <see cref="IEntityCollector.Changed"/>.
        /// Membership enter/leave is still tracked in <see cref="IEntityCollector.Matching"/> and
        /// <see cref="IEntityCollector.Clashing"/> after <see cref="IEntityCollector.Flush"/>.
        /// </summary>
        None = 0,

        /// <summary>
        /// Mirror entities whose tracked component <em>data</em> changed (revision) into
        /// <see cref="IEntityCollector.Changed"/>.
        /// </summary>
        RevisionAsChange = 1 << 0,

        /// <summary>
        /// Mirror entities <em>entering</em> the collector (structural match) into
        /// <see cref="IEntityCollector.Changed"/>.
        /// </summary>
        MatchAsChange = 1 << 1,

        /// <summary>
        /// Mirror entities <em>leaving</em> the collector (structural clash) into
        /// <see cref="IEntityCollector.Changed"/>.
        /// </summary>
        ClashAsChange = 1 << 2,

        /// <summary>
        /// Limit component-driven <see cref="IEntityCollector.Changed"/> entries to component types
        /// relevant to the collector matcher. When unset, all component add/remove/revision events
        /// that reach the collector are eligible for <see cref="IEntityCollector.Changed"/>.
        /// </summary>
        RelatedComponentOnly = 1 << 3,

        /// <summary>
        /// Default structural-change collector: newly matching entities and match-relevant
        /// composition/revision updates while collected are mirrored into
        /// <see cref="IEntityCollector.Changed"/>; departures stay in
        /// <see cref="IEntityCollector.Clashing"/> unless <see cref="ClashAsChange"/> is added.
        /// </summary>
        Default = RevisionAsChange | MatchAsChange | RelatedComponentOnly,

    }
    
    /// <summary>
    /// Collects entities that satisfy a matcher and summarizes structural membership changes
    /// per <see cref="Flush"/> phase. <see cref="Changed"/> reports which collected entities
    /// should be reprocessed, according to <see cref="EntityCollectorFlag"/>.
    /// </summary>
    public interface IEntityCollector : IDisposable
    {
        /// <summary>
        /// Gets the matcher of this collector.
        /// </summary>
        public IEntityMatcher Matcher { get; }

        /// <summary>
        /// Gets entities currently in the collector after the last <see cref="Flush"/>.
        /// </summary>
        public IReadOnlyList<ulong> Collected { get; }

        /// <summary>
        /// Gets entities that entered the collector during the last flush phase (structural match).
        /// </summary>
        public IReadOnlyList<ulong> Matching { get; }

        /// <summary>
        /// Gets entities that left the collector during the last flush phase (structural clash).
        /// </summary>
        public IReadOnlyList<ulong> Clashing { get; }

        /// <summary>
        /// Gets entities to reprocess from the last flush phase. With
        /// <see cref="EntityCollectorFlag.Default"/>, this is the structural-change view of the
        /// collector: newly matching entities plus match-relevant composition/revision updates
        /// while still collected. Departures are listed in <see cref="Clashing"/> unless
        /// <see cref="EntityCollectorFlag.ClashAsChange"/> is enabled. Use flags to include or
        /// exclude additional categories.
        /// </summary>
        public IReadOnlyList<ulong> Changed { get; }

        /// <summary>
        /// Publishes pending membership and change buffers, then starts a new collecting phase.
        /// </summary>
        public void Flush();

        /// <summary>
        /// Summarizes previous changes and starts a new collecting phase.
        /// </summary>
        [Obsolete("Use Flush() instead.")]
        public void Change();
    }

}