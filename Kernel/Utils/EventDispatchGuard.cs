using System;

namespace CoreECS.Utils
{
    /// <summary>
    /// Per-event dispatch flag. Re-entering the same event from one of its own handlers
    /// is rejected instead of recursing.
    /// </summary>
    public sealed class EventDispatchState
    {
        private bool m_dispatching;

        public void Enter()
        {
            if (m_dispatching)
                throw new InvalidOperationException("Re-entrant event dispatch detected.");
            m_dispatching = true;
        }

        public void Exit() => m_dispatching = false;

        public EventDispatchGuard Using => new(this);
    }

    /// <summary>
    /// Scope guard used around event dispatch: sets the state on construction and clears
    /// it on dispose.
    /// </summary>
    public readonly struct EventDispatchGuard : IDisposable
    {
        private readonly EventDispatchState m_state;

        public EventDispatchGuard(EventDispatchState state)
        {
            m_state = state;
            state.Enter();
        }

        public void Dispose() => m_state.Exit();
    }
}
