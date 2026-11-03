namespace CoreECS.Defines
{
    /// <summary>
    /// Controls where a group is inserted among the mixed children of its parent group.
    /// </summary>
    public enum GroupInsertMode
    {
        /// <summary>Insert the group before the parent's existing children.</summary>
        Early = 0,

        /// <summary>Insert the group after the parent's existing children.</summary>
        Later = 1,
    }
}
