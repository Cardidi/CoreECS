// ReSharper disable ForCanBeConvertedToForeach

using System;
using System.Collections.Generic;

namespace CoreECS.Utils
{
    /// <summary>
    /// Delegate for emitting signals without arguments.
    /// </summary>
    /// <typeparam name="THandler">The handler delegate type</typeparam>
    public delegate void Emitter<in THandler>(THandler h) where THandler : Delegate;
    
    /// <summary>
    /// Delegate for emitting signals with one argument.
    /// </summary>
    /// <typeparam name="THandler">The handler delegate type</typeparam>
    /// <typeparam name="TArg1">The type of the first argument</typeparam>
    public delegate void Emitter<in THandler, in TArg1>(THandler element, TArg1 arg1) where THandler : Delegate;
    
    /// <summary>
    /// Delegate for emitting signals with two arguments.
    /// </summary>
    /// <typeparam name="THandler">The handler delegate type</typeparam>
    /// <typeparam name="TArg1">The type of the first argument</typeparam>
    /// <typeparam name="TArg2">The type of the second argument</typeparam>
    public delegate void Emitter<in THandler, in TArg1, in TArg2>(THandler element, TArg1 arg1, TArg2 arg2) 
        where THandler : Delegate;
    
    /// <summary>
    /// Delegate for emitting signals with three arguments.
    /// </summary>
    /// <typeparam name="THandler">The handler delegate type</typeparam>
    /// <typeparam name="TArg1">The type of the first argument</typeparam>
    /// <typeparam name="TArg2">The type of the second argument</typeparam>
    /// <typeparam name="TArg3">The type of the third argument</typeparam>
    public delegate void Emitter<in THandler, in TArg1, in TArg2, in TArg3>(
        THandler element, TArg1 arg1, TArg2 arg2, TArg3 arg3) where THandler : Delegate;
    
    /// <summary>
    /// Delegate for emitting signals with four arguments.
    /// </summary>
    /// <typeparam name="THandler">The handler delegate type</typeparam>
    /// <typeparam name="TArg1">The type of the first argument</typeparam>
    /// <typeparam name="TArg2">The type of the second argument</typeparam>
    /// <typeparam name="TArg3">The type of the third argument</typeparam>
    /// <typeparam name="TArg4">The type of the fourth argument</typeparam>
    public delegate void Emitter<in THandler, in TArg1, in TArg2, in TArg3, in TArg4>(
        THandler element, TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4) where THandler : Delegate;
    
    /// <summary>
    /// Delegate for emitting signals with five arguments.
    /// </summary>
    /// <typeparam name="THandler">The handler delegate type</typeparam>
    /// <typeparam name="TArg1">The type of the first argument</typeparam>
    /// <typeparam name="TArg2">The type of the second argument</typeparam>
    /// <typeparam name="TArg3">The type of the third argument</typeparam>
    /// <typeparam name="TArg4">The type of the fourth argument</typeparam>
    /// <typeparam name="TArg5">The type of the fifth argument</typeparam>
    public delegate void Emitter<in THandler, in TArg1, in TArg2, in TArg3, in TArg4, in TArg5>(
        THandler element, TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5) where THandler : Delegate;
    
    /// <summary>
    /// Delegate for emitting signals with six arguments.
    /// </summary>
    /// <typeparam name="THandler">The handler delegate type</typeparam>
    /// <typeparam name="TArg1">The type of the first argument</typeparam>
    /// <typeparam name="TArg2">The type of the second argument</typeparam>
    /// <typeparam name="TArg3">The type of the third argument</typeparam>
    /// <typeparam name="TArg4">The type of the fourth argument</typeparam>
    /// <typeparam name="TArg5">The type of the fifth argument</typeparam>
    /// <typeparam name="TArg6">The type of the sixth argument</typeparam>
    public delegate void Emitter<in THandler, in TArg1, in TArg2, in TArg3, in TArg4, in TArg5, in TArg6>(
        THandler element, TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5, TArg6 arg6)
        where THandler : Delegate;

    
    /// <summary>
    /// An ECS infrastructure for broadcasting messages to each module.
    /// This class provides a signal system that allows components and systems to communicate
    /// without direct references to each other.
    /// </summary>
    /// <typeparam name="T">The delegate type for the signal handlers</typeparam>
    public class Signal<T> where T : Delegate
    {
        /// <summary>
        /// A disposable wrapper for signal handlers that automatically removes them from the signal when disposed.
        /// </summary>
        public struct SignalDisposal : IDisposable
        {
            /// <summary>
            /// The delegate handler.
            /// </summary>
            private readonly T m_delegate;

            /// <summary>
            /// The signal this handler is registered with.
            /// </summary>
            private readonly Signal<T> m_signal;
            
            /// <summary>
            /// Removes the handler from the signal.
            /// </summary>
            public void Dispose()
            {
                if (m_delegate == null || m_signal == null) return;

                var idx = SortedObject<T>.IndexOfElement(m_signal.m_expose, in m_delegate);
                if (idx >= 0)
                {
                    m_signal.m_expose.RemoveAt(idx);
                    m_signal.m_dirty = DirtyType.Dirty;
                }

                this = default;
            }

            /// <summary>
            /// Initializes a new instance of the SignalDisposal struct.
            /// </summary>
            /// <param name="delegate">The delegate handler</param>
            /// <param name="signal">The signal to register with</param>
            internal SignalDisposal(T @delegate, Signal<T> signal)
            {
                m_delegate = @delegate;
                m_signal = signal;
            }
        }
        
        /// <summary>
        /// Represents the dirty state of the signal.
        /// </summary>
        private enum DirtyType
        {
            /// <summary>
            /// The signal is clean and up-to-date.
            /// </summary>
            Clean = 0,
            
            /// <summary>
            /// The signal has changes that need to be applied.
            /// </summary>
            Dirty = 1,
            
            /// <summary>
            /// The signal has changes that need to be applied and needs reordering.
            /// </summary>
            DirtyAndReorder = 2
        }

        /// <summary>
        /// List of exposed handlers that can be modified.
        /// </summary>
        private List<SortedObject<T>> m_expose = new();
        
        /// <summary>
        /// List of handlers currently being executed.
        /// </summary>
        private List<SortedObject<T>> m_executed = new();

        /// <summary>
        /// The current dirty state of the signal.
        /// </summary>
        private DirtyType m_dirty = DirtyType.Clean;
        
        /// <summary>
        /// Indicates whether the signal is currently executing.
        /// </summary>
        private bool m_executing = false;

        /// <summary>
        /// Swaps the expose and executed lists and clears the dirty flag.
        /// </summary>
        private void SwapAndUndirty()
        {
            if (m_dirty > DirtyType.Clean)
            {
                if (m_dirty == DirtyType.DirtyAndReorder) 
                    m_expose.Sort();
                
                m_executed.Clear();
                m_executed.AddRange(m_expose);

                m_dirty = DirtyType.Clean;
            }
            
            (m_expose, m_executed) = (m_executed, m_expose);
        }

        /// <summary>
        /// Adds a receiver to the signal.
        /// </summary>
        /// <param name="dg">The receiver function to add</param>
        /// <param name="order">The emission order in the signal (lower values execute first)</param>
        /// <param name="allowDuplication">Whether to allow adding the same receiver multiple times</param>
        /// <returns>A disposable that will remove the receiver when disposed</returns>
        /// <exception cref="InvalidOperationException">Thrown when a duplicate receiver is added and allowDuplication is false</exception>
        public SignalDisposal Add(T dg, int order = 0, bool allowDuplication = true)
        {
            var save = new SortedObject<T>(order, dg);
            if (!allowDuplication && SortedObject<T>.IndexOfElement(m_expose, in dg) >= 0)
                throw new InvalidOperationException("Could not add a same delegate for twice!");

            m_dirty = DirtyType.DirtyAndReorder;
            m_expose.Add(save);
            return new SignalDisposal(dg, this);
        }

        /// <summary>
        /// Removes a receiver from the signal.
        /// </summary>
        /// <param name="dg">The receiver function to remove</param>
        /// <returns>True if the receiver was removed, false if it wasn't found</returns>
        public bool Remove(T dg)
        {
            var idx = SortedObject<T>.IndexOfElement(m_expose, in dg);
            if (idx < 0) return false;

            m_dirty = DirtyType.Dirty;
            m_expose.RemoveAt(idx);
            return true;
        }

        /// <summary>
        /// Clears all registered signal receivers.
        /// </summary>
        public void Clear()
        {
            m_expose.Clear();
            m_executed.Clear();
            m_dirty = DirtyType.Clean;
        }
        
        /// <summary>
        /// Executes all signal receivers without arguments.
        /// </summary>
        /// <param name="emitter">The emitting function</param>
        public void Emit(Emitter<T> emitter)
        {
            Assertion.IsFalse(m_executing);
            m_executing = true;
            
            SwapAndUndirty();
            for (var i = 0; i < m_executed.Count; i++)
            {
                var exec = m_executed[i];
                try { emitter(exec.Element); }
                catch (Exception e) { Log.Exp(e); }
            }
            
            m_executing = false;
        }

        /// <summary>
        /// Executes all signal receivers with one argument.
        /// </summary>
        /// <typeparam name="TArg1">The type of the first argument</typeparam>
        /// <param name="arg1">The first argument</param>
        /// <param name="emitter">The emitting function</param>
        public void Emit<TArg1>(in TArg1 arg1, Emitter<T, TArg1> emitter)
        {
            Assertion.IsFalse(m_executing);
            m_executing = true;
            
            SwapAndUndirty();
            for (var i = 0; i < m_executed.Count; i++)
            {
                var exec = m_executed[i];
                try { emitter(exec.Element, arg1); }
                catch (Exception e) { Log.Exp(e); }
            }
            
            m_executing = false;
        }
        
        /// <summary>
        /// Executes all signal receivers with two arguments.
        /// </summary>
        /// <typeparam name="TArg1">The type of the first argument</typeparam>
        /// <typeparam name="TArg2">The type of the second argument</typeparam>
        /// <param name="arg1">The first argument</param>
        /// <param name="arg2">The second argument</param>
        /// <param name="emitter">The emitting function</param>
        public void Emit<TArg1, TArg2>(in TArg1 arg1, in TArg2 arg2, Emitter<T, TArg1, TArg2> emitter)
        {
            Assertion.IsFalse(m_executing);
            m_executing = true;
            
            SwapAndUndirty();
            for (var i = 0; i < m_executed.Count; i++)
            {
                var exec = m_executed[i];
                try { emitter(exec.Element, arg1, arg2); }
                catch (Exception e) { Log.Exp(e); }
            }
            
            m_executing = false;
        }

        /// <summary>
        /// Executes all signal receivers with three arguments.
        /// </summary>
        /// <typeparam name="TArg1">The type of the first argument</typeparam>
        /// <typeparam name="TArg2">The type of the second argument</typeparam>
        /// <typeparam name="TArg3">The type of the third argument</typeparam>
        /// <param name="arg1">The first argument</param>
        /// <param name="arg2">The second argument</param>
        /// <param name="arg3">The third argument</param>
        /// <param name="emitter">The emitting function</param>
        public void Emit<TArg1, TArg2, TArg3>(in TArg1 arg1, in TArg2 arg2, in TArg3 arg3, Emitter<T, TArg1, TArg2, TArg3> emitter)
        {
            Assertion.IsFalse(m_executing);
            m_executing = true;
            
            SwapAndUndirty();
            for (var i = 0; i < m_executed.Count; i++)
            {
                var exec = m_executed[i];
                try { emitter(exec.Element, arg1, arg2, arg3); }
                catch (Exception e) { Log.Exp(e); }
            }
            
            m_executing = false;
        }

        /// <summary>
        /// Executes all signal receivers with four arguments.
        /// </summary>
        /// <typeparam name="TArg1">The type of the first argument</typeparam>
        /// <typeparam name="TArg2">The type of the second argument</typeparam>
        /// <typeparam name="TArg3">The type of the third argument</typeparam>
        /// <typeparam name="TArg4">The type of the fourth argument</typeparam>
        /// <param name="arg1">The first argument</param>
        /// <param name="arg2">The second argument</param>
        /// <param name="arg3">The third argument</param>
        /// <param name="arg4">The fourth argument</param>
        /// <param name="emitter">The emitting function</param>
        public void Emit<TArg1, TArg2, TArg3, TArg4>(
            in TArg1 arg1, in TArg2 arg2, in TArg3 arg3, in TArg4 arg4,
            Emitter<T, TArg1, TArg2, TArg3, TArg4> emitter)
        {
            Assertion.IsFalse(m_executing);
            m_executing = true;
            
            SwapAndUndirty();
            for (var i = 0; i < m_executed.Count; i++)
            {
                var exec = m_executed[i];
                try { emitter(exec.Element, arg1, arg2, arg3, arg4); }
                catch (Exception e) { Log.Exp(e); }
            }
            
            m_executing = false;
        }
        
        /// <summary>
        /// Executes all signal receivers with five arguments.
        /// </summary>
        /// <typeparam name="TArg1">The type of the first argument</typeparam>
        /// <typeparam name="TArg2">The type of the second argument</typeparam>
        /// <typeparam name="TArg3">The type of the third argument</typeparam>
        /// <typeparam name="TArg4">The type of the fourth argument</typeparam>
        /// <typeparam name="TArg5">The type of the fifth argument</typeparam>
        /// <param name="arg1">The first argument</param>
        /// <param name="arg2">The second argument</param>
        /// <param name="arg3">The third argument</param>
        /// <param name="arg4">The fourth argument</param>
        /// <param name="arg5">The fifth argument</param>
        /// <param name="emitter">The emitting function</param>
        public void Emit<TArg1, TArg2, TArg3, TArg4, TArg5>(
            in TArg1 arg1, in TArg2 arg2, in TArg3 arg3, in TArg4 arg4, in TArg5 arg5,
            Emitter<T, TArg1, TArg2, TArg3, TArg4, TArg5> emitter)
        {
            Assertion.IsFalse(m_executing);
            m_executing = true;
            
            SwapAndUndirty();
            for (var i = 0; i < m_executed.Count; i++)
            {
                var exec = m_executed[i];
                try { emitter(exec.Element, arg1, arg2, arg3, arg4, arg5); }
                catch (Exception e) { Log.Exp(e); }
            }
            
            m_executing = false;
        }

        /// <summary>
        /// Executes all signal receivers with six arguments.
        /// </summary>
        /// <typeparam name="TArg1">The type of the first argument</typeparam>
        /// <typeparam name="TArg2">The type of the second argument</typeparam>
        /// <typeparam name="TArg3">The type of the third argument</typeparam>
        /// <typeparam name="TArg4">The type of the fourth argument</typeparam>
        /// <typeparam name="TArg5">The type of the fifth argument</typeparam>
        /// <typeparam name="TArg6">The type of the sixth argument</typeparam>
        /// <param name="arg1">The first argument</param>
        /// <param name="arg2">The second argument</param>
        /// <param name="arg3">The third argument</param>
        /// <param name="arg4">The fourth argument</param>
        /// <param name="arg5">The fifth argument</param>
        /// <param name="arg6">The sixth argument</param>
        /// <param name="emitter">The emitting function</param>
        public void Emit<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6>(
            in TArg1 arg1, in TArg2 arg2, in TArg3 arg3, in TArg4 arg4, in TArg5 arg5, in TArg6 arg6,
            Emitter<T, TArg1, TArg2, TArg3, TArg4, TArg5, TArg6> emitter)
        {
            Assertion.IsFalse(m_executing);
            m_executing = true;
            
            SwapAndUndirty();
            for (var i = 0; i < m_executed.Count; i++)
            {
                var exec = m_executed[i];
                try { emitter(exec.Element, arg1, arg2, arg3, arg4, arg5, arg6); }
                catch (Exception e) { Log.Exp(e); }
            }
            
            m_executing = false;
        }
    }
}