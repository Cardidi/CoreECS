namespace CoreECS.Defines
{
    /// <summary>
    /// The minimal define of a components on entity.
    /// </summary>
    public interface IComponent<TComponent> where TComponent : struct, IComponent<TComponent>
    {
        void OnCreate(EntityId entityId);

        void OnDestroy(EntityId entityId);
    }
}
