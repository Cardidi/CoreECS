namespace CoreECS.Defines
{
    /// <summary>
    /// The minimal define of a world to hold all entities, systems and components.
    /// </summary>
    public interface IWorld
    {
        /// <summary>
        /// How many ticks have passed since the world started.
        /// </summary>
        public uint TickCount { get; }
        
        /// <summary>
        /// Query for all managers with type constraint.
        /// </summary>
        public TMgr GetManager<TMgr>() where TMgr : IWorldManager;

        /// <summary>
        /// Post to all managers to set up a new tick.
        /// </summary>
        public void BeginTick();

        /// <summary>
        /// Post to all managers to update for the current tick and can use tick mask to different systems.
        /// </summary>
        public void Tick(ulong tickMask = ulong.MaxValue);

        /// <summary>
        /// Post to all managers to end current tick.
        /// </summary>
        public void EndTick();
    }
}