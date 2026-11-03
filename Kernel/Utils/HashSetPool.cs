using System.Collections.Generic;

namespace CoreECS.Utils
{
    public static class HashSetPool<T>
    {
        private static readonly Pool<HashSet<T>> Pool = new(
            createFunc: () => new HashSet<T>(),
            getAction: set => set.Clear(),
            returnAction: set => set.Clear());

        public static Pool<HashSet<T>>.PooledItemDisposable Get(out HashSet<T> set)
        {
            return Pool.Get(out set);
        }

        public static HashSet<T> Get()
        {
            return Pool.Get();
        }

        public static void Return(HashSet<T> list)
        {
            Pool.Release(list);
        }
    }
}