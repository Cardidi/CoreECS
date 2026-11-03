namespace CoreECS.Defines
{
    /// <summary>
    /// Interface for world managers that handle specific aspects of the ECS world.
    /// World managers are responsible for managing entities, components, systems, and other
    /// world-related functionality. They follow a specific lifecycle managed by the world.
    /// </summary>
    public interface IWorldManager
    {
        /// <summary>
        /// Called when the manager is created and registered with the world.
        /// This is the first lifecycle event for a manager, allowing it to perform
        /// initialization tasks before the world starts.
        /// </summary>
        public void OnManagerCreated();
        
        /// <summary>
        /// Called when the world starts up after all managers have been created.
        /// This event signals that the world is ready for operation and managers
        /// can perform any startup logic required for normal operation.
        /// </summary>
        public void OnWorldStarted();

        /// <summary>
        /// Called when the world is shutting down.
        /// This event allows managers to perform cleanup operations before the world
        /// completely shuts down. Managers should still be functional during this phase.
        /// </summary>
        public void OnWorldEnded();

        /// <summary>
        /// Called when the manager is being destroyed.
        /// This is the final lifecycle event for a manager, allowing it to release
        /// all resources and perform final cleanup before being removed from memory.
        /// </summary>
        public void OnManagerDestroyed();
    }
}