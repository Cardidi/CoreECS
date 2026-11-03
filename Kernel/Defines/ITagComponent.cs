namespace CoreECS.Defines
{
    /// <summary>
    /// Marks an empty component whose presence is its complete state.
    /// </summary>
    public interface ITagComponent<TComponent> : IComponent<TComponent>
        where TComponent : struct, ITagComponent<TComponent>
    {
        void IComponent<TComponent>.OnCreate(EntityId entityId)
        {
        }

        void IComponent<TComponent>.OnDestroy(EntityId entityId)
        {
        }
    }
}
