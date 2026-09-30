namespace CoreECS.Defines
{
    /// <summary>
    /// Marks a component as a tag: stored as a per-entity bit, carries no data.
    /// Tag components do not participate in structure (archetype) membership.
    /// </summary>
    public interface ITagComponent<T> : IComponent<T>
        where T : struct, ITagComponent<T>
    {
    }
}
