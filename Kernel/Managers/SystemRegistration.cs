using System;
using CoreECS.Defines;
using CoreECS.Managers;

namespace CoreECS
{
    /// <summary>
    /// Fluent handle for declaring unresolved ordering anchors on a registered system.
    /// </summary>
    public sealed class SystemRegistration
    {
        private readonly SystemManager m_manager;
        private readonly SystemEntryNode m_node;
        private readonly int m_generation;

        internal SystemRegistration(SystemManager manager, SystemEntryNode node, int generation)
        {
            m_manager = manager;
            m_node = node;
            m_generation = generation;
        }

        public SystemRegistration Before(Type systemType)
        {
            m_manager.AddSystemAnchor(m_node,
                new SystemAnchor(SystemAnchorKind.Before, systemType, null), m_generation);
            return this;
        }

        public SystemRegistration Before<T>() where T : class, ISystem => Before(typeof(T));

        public SystemRegistration Before(string groupName)
        {
            m_manager.AddSystemAnchor(m_node,
                new SystemAnchor(SystemAnchorKind.Before, null, groupName), m_generation);
            return this;
        }

        public SystemRegistration After(Type systemType)
        {
            m_manager.AddSystemAnchor(m_node,
                new SystemAnchor(SystemAnchorKind.After, systemType, null), m_generation);
            return this;
        }

        public SystemRegistration After<T>() where T : class, ISystem => After(typeof(T));

        public SystemRegistration After(string groupName)
        {
            m_manager.AddSystemAnchor(m_node,
                new SystemAnchor(SystemAnchorKind.After, null, groupName), m_generation);
            return this;
        }
    }
}
