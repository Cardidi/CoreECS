namespace CoreECS.Defines
{
    /// <summary>
    /// Marks a component as discrete: stored in a per-structure spare set.
    /// Discrete components do not participate in structure (archetype) membership.
    /// </summary>
    public interface IDiscreteComponent<T> : IComponent<T>
        where T : struct, IDiscreteComponent<T>
    {
    }
}
