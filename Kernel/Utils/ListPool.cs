using System.Collections.Generic;

namespace CoreECS.Utils
{
    public static class ListPool<T>
    {
        private static readonly Pool<List<T>> Pool = new(
            createFunc: () => new List<T>(),
            getAction: list => list.Clear(),
            returnAction: list => list.Clear());

        public static Pool<List<T>>.PooledItemDisposable Get(out List<T> list)
        {
            return Pool.Get(out list);
        }

        public static List<T> Get()
        {
            return Pool.Get();
        }

        public static void Return(List<T> list)
        {
            Pool.Release(list);
        }
    }
}