using System.Collections.Generic;
using CoreECS.Structures;

namespace CoreECS.Utils
{
    /// <summary>
    /// Single-threaded stack pool for component handlers. Storage slots own their handler
    /// and must release it at most once, on component removal or entity destroy. Released
    /// handlers keep their <see cref="ComponentHandler.BindGeneration"/> so stale handles can
    /// never alias a recycled handler.
    /// </summary>
    internal static class ComponentHandlerPool
    {
        private static readonly Stack<ComponentHandler> s_pool = new();

        public static ComponentHandler Get()
        {
            return s_pool.Count > 0 ? s_pool.Pop() : new ComponentHandler();
        }

        public static void Release(ComponentHandler handler)
        {
            if (handler == null) return;
            handler.Reset();
            s_pool.Push(handler);
        }

        public static void Clear() => s_pool.Clear();
    }
}
