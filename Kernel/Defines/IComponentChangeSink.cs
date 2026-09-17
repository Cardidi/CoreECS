using CoreECS.Structures;

namespace CoreECS.Defines
{
    /// <summary>
    /// Internal fast path for component revision changes. Implemented by the entity
    /// manager so the component manager can skip the public signal chain.
    /// </summary>
    internal interface IComponentChangeSink
    {
        /// <summary>
        /// A component revision changed. <paramref name="location"/> is the pooled
        /// location of the owning entity at the time of the write.
        /// </summary>
        void OnRevisionChanged(ulong entityId, uint typeId, EntityLocation location);
    }
}
