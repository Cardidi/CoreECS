namespace CoreECS.Defines
{
    /// <summary>
    /// The minimal define of a system to process entities. It can only driven by world.
    /// </summary>
    public interface ISystem
    {
        public ulong TickGroup { get => ulong.MaxValue; }
        
        public void OnCreate() {}

        public void OnTick(ulong tickMask);

        public void OnDestroy() {}
    }
}