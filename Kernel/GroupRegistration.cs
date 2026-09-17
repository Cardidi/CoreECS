using System;
using CoreECS.Defines;
using CoreECS.Managers;
using CoreECS.Utils;

namespace CoreECS
{
    /// <summary>
    /// Fluent registration handle for a group. Declares Before / After anchors relative
    /// to systems or other groups; anchors are resolved when the execution order is
    /// rebuilt, so forward references (targets registered later) are allowed.
    /// </summary>
    public sealed class GroupRegistration
    {
        /// <summary>Manager that owns the registered group.</summary>
        private readonly SystemManager m_manager;

        /// <summary>Registered group name.</summary>
        private readonly string m_groupName;

        /// <summary>Creates a handle for a registered group.</summary>
        /// <param name="manager">Manager owning the group.</param>
        /// <param name="groupName">Registered group name.</param>
        internal GroupRegistration(SystemManager manager, string groupName)
        {
            m_manager = manager;
            m_groupName = groupName;
        }

        /// <summary>Declares that this group runs before the specified system type.</summary>
        /// <typeparam name="T">Anchor system type; may be registered later.</typeparam>
        /// <returns>This handle for chaining.</returns>
        public GroupRegistration Before<T>() where T : class, ISystem
        {
            m_manager.AddGroupAnchor(m_groupName, new SystemAnchor(SystemAnchorKind.Before, typeof(T), null));
            return this;
        }

        /// <summary>Declares that this group runs after the specified system type.</summary>
        /// <typeparam name="T">Anchor system type; may be registered later.</typeparam>
        /// <returns>This handle for chaining.</returns>
        public GroupRegistration After<T>() where T : class, ISystem
        {
            m_manager.AddGroupAnchor(m_groupName, new SystemAnchor(SystemAnchorKind.After, typeof(T), null));
            return this;
        }

        /// <summary>Declares that this group runs before the specified group.</summary>
        /// <param name="groupName">Anchor group name; may be registered later.</param>
        /// <returns>This handle for chaining.</returns>
        public GroupRegistration Before(string groupName)
        {
            Assertion.ArgumentNotNull(groupName, nameof(groupName));
            m_manager.AddGroupAnchor(m_groupName, new SystemAnchor(SystemAnchorKind.Before, null, groupName));
            return this;
        }

        /// <summary>Declares that this group runs after the specified group.</summary>
        /// <param name="groupName">Anchor group name; may be registered later.</param>
        /// <returns>This handle for chaining.</returns>
        public GroupRegistration After(string groupName)
        {
            Assertion.ArgumentNotNull(groupName, nameof(groupName));
            m_manager.AddGroupAnchor(m_groupName, new SystemAnchor(SystemAnchorKind.After, null, groupName));
            return this;
        }
    }
}
