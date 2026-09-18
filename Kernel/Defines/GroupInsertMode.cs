namespace CoreECS.Defines
{
    /// <summary>
    /// Controls where a newly registered group is inserted among its same-level siblings.
    /// </summary>
    public enum GroupInsertMode
    {
        /// <summary>Insert at the front of the current level.</summary>
        Early = 0,

        /// <summary>Append to the current level (default).</summary>
        Later = 1,
    }
}
