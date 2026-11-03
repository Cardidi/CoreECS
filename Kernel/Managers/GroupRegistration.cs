using System;
using CoreECS.Defines;
using CoreECS.Managers;

namespace CoreECS
{
    /// <summary>
    /// Fluent handle for declaring unresolved ordering anchors on a registered group.
    /// </summary>
    public sealed class GroupRegistration
    {
        private readonly SystemManager m_manager;
        private readonly SystemGroupNode m_node;
        private readonly int m_generation;

        internal GroupRegistration(SystemManager manager, SystemGroupNode node, int generation)
        {
            m_manager = manager;
            m_node = node;
            m_generation = generation;
        }

        public GroupRegistration Before(Type systemType)
        {
            m_manager.AddGroupAnchor(m_node,
                new SystemAnchor(SystemAnchorKind.Before, systemType, null), m_generation);
            return this;
        }

        public GroupRegistration Before<T>() where T : class, ISystem => Before(typeof(T));

        public GroupRegistration Before(string groupName)
        {
            m_manager.AddGroupAnchor(m_node,
                new SystemAnchor(SystemAnchorKind.Before, null, groupName), m_generation);
            return this;
        }

        public GroupRegistration After(Type systemType)
        {
            m_manager.AddGroupAnchor(m_node,
                new SystemAnchor(SystemAnchorKind.After, systemType, null), m_generation);
            return this;
        }

        public GroupRegistration After<T>() where T : class, ISystem => After(typeof(T));

        public GroupRegistration After(string groupName)
        {
            m_manager.AddGroupAnchor(m_node,
                new SystemAnchor(SystemAnchorKind.After, null, groupName), m_generation);
            return this;
        }
    }
}
