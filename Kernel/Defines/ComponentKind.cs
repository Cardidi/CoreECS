namespace CoreECS.Defines
{
    /// <summary>
    /// Storage category of a component type.
    /// </summary>
    internal enum ComponentKind : byte
    {
        /// <summary>Stored in structure SoA arrays; participates in structure membership.</summary>
        Dense = 0,

        /// <summary>Stored in a per-structure spare set; does not affect structure membership.</summary>
        Discrete = 1,

        /// <summary>Stored as a per-entity bit; carries no data; does not affect structure membership.</summary>
        Tag = 2,
    }
}
