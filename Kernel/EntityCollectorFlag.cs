using System;

namespace CoreECS
{
    /// <summary>Controls which collector events are mirrored into Changed.</summary>
    [Flags]
    public enum EntityCollectorFlag
    {
        None = 0,
        RevisionAsChange = 1 << 0,
        MatchAsChange = 1 << 1,
        ClashAsChange = 1 << 2,
        RelatedComponentOnly = 1 << 3,
        Default = RevisionAsChange | MatchAsChange | RelatedComponentOnly,
    }
}
