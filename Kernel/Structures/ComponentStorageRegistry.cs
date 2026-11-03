using System;
using System.Collections.Generic;
using CoreECS.Utils;

namespace CoreECS.Structures
{
    internal sealed class ComponentStorageRegistry
    {
        private readonly Dictionary<uint, IComponentStorage> m_storages =
            new Dictionary<uint, IComponentStorage>();
        private readonly EntityTable m_entityTable;
        private readonly ComponentHandlerPool m_handlerPool = new ComponentHandlerPool();

        internal int Count => m_storages.Count;

        internal int PooledHandlerCountForTesting => m_handlerPool.Count;

        internal ComponentStorageRegistry(EntityTable entityTable)
        {
            m_entityTable = entityTable ?? throw new ArgumentNullException(nameof(entityTable));
        }

        internal IComponentStorage GetOrCreate<T>(ComponentKind kind)
            where T : struct
        {
            ComponentTypeMetadata metadata = ComponentTypeRegistry.GetOrRegister<T>();
            if (m_storages.TryGetValue(metadata.TypeId, out IComponentStorage existing))
            {
                if (existing.Kind != kind)
                {
                    throw new InvalidOperationException(
                        "A component TypeId cannot use multiple storage kinds in one World.");
                }

                return existing;
            }

            IComponentStorage storage;
            switch (kind)
            {
                case ComponentKind.Data:
                    storage = new ComponentStorage<T>(metadata.TypeId, m_entityTable, m_handlerPool);
                    break;
                case ComponentKind.Tag:
                    storage = new TagStorage<T>(metadata.TypeId, m_entityTable, m_handlerPool);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }

            m_storages.Add(metadata.TypeId, storage);
            return storage;
        }

        internal bool TryGet(uint typeId, out IComponentStorage storage)
        {
            return m_storages.TryGetValue(typeId, out storage);
        }

        internal void Compact()
        {
            foreach (IComponentStorage storage in m_storages.Values)
            {
                storage.Compact();
            }
        }

        internal void Clear()
        {
            foreach (IComponentStorage storage in m_storages.Values)
            {
                storage.Clear();
            }

            m_storages.Clear();
        }
    }
}
