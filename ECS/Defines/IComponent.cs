namespace CoreECS.Defines
{
    /// <summary>
    /// The minimal define of a components on entity.
    /// </summary>
    public interface IComponent<TComponent> where TComponent : struct, IComponent<TComponent>
    {
        public void OnCreate(ulong entityId) {}

        public void OnDestroy(ulong entityId) {}
    }
}