using System;

namespace CoreECS.Structures
{
    internal sealed class StructuralMutationGuard
    {
        private int m_hookDepth;

        internal void ThrowIfMutationForbidden()
        {
            if (m_hookDepth != 0)
            {
                throw new InvalidOperationException(
                    "Entity and component structure cannot be changed from a component lifecycle Hook.");
            }
        }

        internal Scope EnterHook()
        {
            m_hookDepth++;
            return new Scope(this);
        }

        private void ExitHook()
        {
            m_hookDepth--;
        }

        internal readonly struct Scope : IDisposable
        {
            private readonly StructuralMutationGuard m_guard;

            internal Scope(StructuralMutationGuard guard)
            {
                m_guard = guard;
            }

            public void Dispose()
            {
                m_guard.ExitHook();
            }
        }
    }
}
