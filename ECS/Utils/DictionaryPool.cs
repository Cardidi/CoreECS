using System.Collections.Generic;

namespace TinyECS.Utils
{
    public static class DictionaryPool<TKey, TValue>
    {
        private static readonly Pool<Dictionary<TKey, TValue>> _pool = new(
            createFunc: () => new Dictionary<TKey, TValue>(),
            returnAction: d => d.Clear());
        
        public static Dictionary<TKey, TValue> Get() => _pool.Get();
        
        public static Pool<Dictionary<TKey, TValue>>.PooledItemDisposable Get(out Dictionary<TKey, TValue> dict)
            => _pool.Get(out dict);
        
        public static void Return(Dictionary<TKey, TValue> dict) => _pool.Release(dict);
    }
}