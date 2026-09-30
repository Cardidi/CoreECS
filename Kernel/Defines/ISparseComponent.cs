namespace CoreECS.Defines
{
    /// <summary>
    /// Marks a component as sparse: stored in a per-structure spare set.
    /// Sparse components do not participate in structure (archetype) membership.
    /// </summary>
    public interface ISparseComponent<T> : IComponent<T>
        where T : struct, ISparseComponent<T>
    {
    }
}
