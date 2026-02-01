using System;
using System.Collections.Concurrent;

namespace CoreECS.Utils
{
    /// <summary>
    /// Generic object pool for reusing instances to reduce memory allocations.
    /// </summary>
    /// <typeparam name="T">The type of objects to pool</typeparam>
    public class Pool<T>
    {
        /// <summary>
        /// Queue of available objects in the pool.
        /// </summary>
        private readonly ConcurrentQueue<T> m_objects;
        
        /// <summary>
        /// Function to create new objects when the pool is empty.
        /// </summary>
        private readonly Func<T> m_createFunc;
        
        /// <summary>
        /// Action to perform when an object is retrieved from the pool.
        /// </summary>
        private readonly Action<T> m_getAction;
        
        /// <summary>
        /// Action to perform when an object is returned to the pool.
        /// </summary>
        private readonly Action<T> m_returnAction;
        
        /// <summary>
        /// Initializes a new instance of the Pool class.
        /// </summary>
        /// <param name="createFunc">Function to create new objects when the pool is empty</param>
        /// <param name="getAction">Optional action to perform when an object is retrieved from the pool</param>
        /// <param name="returnAction">Optional action to perform when an object is returned to the pool</param>
        /// <param name="initialCapacity">Initial number of objects to pre-create in the pool</param>
        public Pool(Func<T> createFunc, Action<T> getAction = null, Action<T> returnAction = null, int initialCapacity = 0)
        {
            m_objects = new ConcurrentQueue<T>();
            m_createFunc = createFunc ?? throw new ArgumentNullException(nameof(createFunc));
            m_getAction = getAction;
            m_returnAction = returnAction;
            
            // Pre-create the specified number of objects
            if (initialCapacity > 0)
            {
                for (int i = 0; i < initialCapacity; i++)
                {
                    T item = m_createFunc();
                    m_returnAction?.Invoke(item);
                    m_objects.Enqueue(item);
                }
            }
        }
        
        /// <summary>
        /// Gets an object from the pool or creates a new one if the pool is empty.
        /// </summary>
        /// <returns>An object from the pool or a new object</returns>
        public T Get()
        {
            if (m_objects.TryDequeue(out T item))
            {
                m_getAction?.Invoke(item);
                return item;
            }
            
            item = m_createFunc();
            m_getAction?.Invoke(item);
            return item;
        }
        
        /// <summary>
        /// Returns an object to the pool.
        /// </summary>
        /// <param name="item">The object to return to the pool</param>
        /// <exception cref="ArgumentNullException">Thrown when item is null</exception>
        public void Release(T item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }
            
            m_returnAction?.Invoke(item);
            m_objects.Enqueue(item);
        }
        
        /// <summary>
        /// Gets an object from the pool and returns a disposable that will return it to the pool when disposed.
        /// </summary>
        /// <param name="item">The object retrieved from the pool</param>
        /// <returns>A disposable that will return the object to the pool when disposed</returns>
        public PooledItemDisposable Get(out T item)
        {
            item = Get();
            return new PooledItemDisposable(this, item);
        }
        
        /// <summary>
        /// Gets the number of objects currently in the pool.
        /// </summary>
        public int Count => m_objects.Count;
        
        /// <summary>
        /// Clears all objects from the pool.
        /// </summary>
        public void Clear()
        {
            m_objects.Clear();
        }
        
        /// <summary>
        /// A disposable wrapper for pooled objects that automatically returns them to the pool when disposed.
        /// </summary>
        public readonly struct PooledItemDisposable : IDisposable
        {
            /// <summary>
            /// The pool that owns this item.
            /// </summary>
            private readonly Pool<T> m_pool;
            
            /// <summary>
            /// The pooled item.
            /// </summary>
            private readonly T m_item;
            
            /// <summary>
            /// Initializes a new instance of the PooledItemDisposable struct.
            /// </summary>
            /// <param name="pool">The pool that owns this item</param>
            /// <param name="item">The pooled item</param>
            /// <exception cref="ArgumentNullException">Thrown when pool is null</exception>
            public PooledItemDisposable(Pool<T> pool, T item)
            {
                m_pool = pool ?? throw new ArgumentNullException(nameof(pool));
                m_item = item;
            }
            
            /// <summary>
            /// Returns the item to the pool.
            /// </summary>
            public void Dispose()
            {
                m_pool.Release(m_item);
            }
        }
    }
}