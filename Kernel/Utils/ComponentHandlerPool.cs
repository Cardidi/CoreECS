using System;
using System.Collections.Generic;
using CoreECS.Structures;

namespace CoreECS.Utils
{
    internal sealed class ComponentHandlerPool
    {
        private readonly Stack<ComponentHandler> m_handlers = new Stack<ComponentHandler>();

        internal int Count => m_handlers.Count;

        internal ComponentHandler Get()
        {
            return m_handlers.Count == 0 ? new ComponentHandler() : m_handlers.Pop();
        }

        internal void Release(ComponentHandler handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            handler.Invalidate();
            m_handlers.Push(handler);
        }
    }
}
