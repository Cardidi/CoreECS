using System.Collections.Generic;
using CoreECS.Structures;

namespace CoreECS.Utils
{
    /// <summary>
    /// Single-threaded stack pool for component ref cores. Released cores keep their
    /// <see cref="ComponentRefCore.BindGeneration"/> so stale handles can never alias
    /// a recycled core.
    /// </summary>
    internal static class ComponentRefCorePool
    {
        private static readonly Stack<ComponentRefCore> s_pool = new();

        public static ComponentRefCore Get()
        {
            return s_pool.Count > 0 ? s_pool.Pop() : new ComponentRefCore();
        }

        public static void Release(ComponentRefCore core)
        {
            if (core == null) return;
            core.Reset();
            s_pool.Push(core);
        }

        public static void Clear() => s_pool.Clear();
    }
}
