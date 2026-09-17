namespace CoreECS.Defines
{
    /// <summary>
    /// Internal fast path for component revision changes. Implemented by the entity
    /// manager so the component manager can skip the public signal chain.
    /// </summary>
    internal interface IComponentChangeSink
    {
        void OnRevisionChanged(ulong entityId, uint typeId);
    }
}
