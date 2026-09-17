using System;
using System.Collections.Generic;
using CoreECS.Defines;

namespace CoreECS.Managers
{
    /// <summary>
    /// Registration tree of groups and systems. The root node is the implicit default
    /// group; groups may nest and may be mixed with systems in registration order.
    /// This type only stores the registration structure and the declared Before / After
    /// anchors; flattening and execution order resolution are layered on top in later tasks.
    /// </summary>
    internal sealed class SystemSchedule
    {
        /// <summary>Gets the implicit root group that owns root-level registrations.</summary>
        public SystemGroupNode Root { get; } = new SystemGroupNode(null, null);

        /// <summary>Groups indexed by their unique name.</summary>
        private readonly Dictionary<string, SystemGroupNode> m_groups = new Dictionary<string, SystemGroupNode>();

        /// <summary>System entry nodes indexed by system type.</summary>
        private readonly Dictionary<Type, SystemEntryNode> m_systems = new Dictionary<Type, SystemEntryNode>();

        /// <summary>
        /// Finds a registered group by name.
        /// </summary>
        /// <param name="name">Group name; null returns null.</param>
        /// <returns>The group node, or null when no group with the name is registered.</returns>
        public SystemGroupNode FindGroup(string name)
        {
            if (name == null) return null;
            return m_groups.TryGetValue(name, out var group) ? group : null;
        }

        /// <summary>
        /// Finds the registration node of a registered system.
        /// </summary>
        /// <param name="systemType">System type to look up.</param>
        /// <returns>The system entry node, or null when the system is not registered.</returns>
        public SystemEntryNode FindSystem(Type systemType)
        {
            return m_systems.TryGetValue(systemType, out var system) ? system : null;
        }

        /// <summary>
        /// Adds a group under the specified parent. Early inserts at the front of the
        /// parent's children, Later appends; children mix groups and systems in
        /// registration order.
        /// </summary>
        /// <param name="name">Unique group name.</param>
        /// <param name="parent">Parent group node.</param>
        /// <param name="mode">Insertion position among the parent's children.</param>
        /// <returns>The created group node.</returns>
        public SystemGroupNode AddGroup(string name, SystemGroupNode parent, GroupInsertMode mode)
        {
            var node = new SystemGroupNode(name, parent);
            if (mode == GroupInsertMode.Early) parent.Children.Insert(0, node);
            else parent.Children.Add(node);

            m_groups.Add(name, node);
            return node;
        }

        /// <summary>
        /// Adds a system to a group. Systems always append at their level; precise
        /// placement is expressed with Before / After anchors.
        /// </summary>
        /// <param name="systemType">System type being registered.</param>
        /// <param name="group">Group that owns the system.</param>
        /// <returns>The created system entry node.</returns>
        public SystemEntryNode AddSystem(Type systemType, SystemGroupNode group)
        {
            var node = new SystemEntryNode(systemType, group);
            group.Children.Add(node);
            m_systems.Add(systemType, node);
            return node;
        }

        /// <summary>
        /// Removes a system from the tree. Does nothing when the system is not registered.
        /// </summary>
        /// <param name="systemType">System type to remove.</param>
        public void RemoveSystem(Type systemType)
        {
            if (!m_systems.TryGetValue(systemType, out var node)) return;

            m_systems.Remove(systemType);
            node.Parent.Children.Remove(node);
        }

        /// <summary>
        /// Removes every system entry from the tree. Used when the world ends; groups are
        /// kept because there is no unregister-group API.
        /// </summary>
        public void ClearSystems()
        {
            foreach (var pair in m_systems)
            {
                pair.Value.Parent.Children.Remove(pair.Value);
            }

            m_systems.Clear();
        }
    }

    /// <summary>
    /// Base node of the schedule tree: a group or a system entry. Nodes carry the
    /// Before / After anchors declared at registration time; anchors may reference
    /// targets that are not registered yet and are resolved when the order is rebuilt.
    /// </summary>
    internal abstract class SystemScheduleNode
    {
        /// <summary>Gets the group that owns this node.</summary>
        public SystemGroupNode Parent { get; }

        /// <summary>Gets the anchors declared for this node, in declaration order.</summary>
        public List<SystemAnchor> Anchors { get; } = new List<SystemAnchor>();

        /// <summary>Creates a node owned by the specified group.</summary>
        /// <param name="parent">Owning group.</param>
        protected SystemScheduleNode(SystemGroupNode parent)
        {
            Parent = parent;
        }
    }

    /// <summary>
    /// A group node: a pure sort bucket without masks. Children are groups and systems
    /// mixed in registration order (Early inserts at the front, Later appends).
    /// </summary>
    internal sealed class SystemGroupNode : SystemScheduleNode
    {
        /// <summary>Gets the group name; null for the implicit root group.</summary>
        public string Name { get; }

        /// <summary>Gets the child nodes in registration order.</summary>
        public List<SystemScheduleNode> Children { get; } = new List<SystemScheduleNode>();

        /// <summary>Creates a group node.</summary>
        /// <param name="name">Group name; null for the implicit root.</param>
        /// <param name="parent">Owning group; null for the implicit root.</param>
        public SystemGroupNode(string name, SystemGroupNode parent) : base(parent)
        {
            Name = name;
        }
    }

    /// <summary>A system entry node: a leaf that references the registered system type.</summary>
    internal sealed class SystemEntryNode : SystemScheduleNode
    {
        /// <summary>Gets the registered system type.</summary>
        public Type SystemType { get; }

        /// <summary>Creates a system entry node.</summary>
        /// <param name="systemType">Registered system type.</param>
        /// <param name="parent">Owning group.</param>
        public SystemEntryNode(Type systemType, SystemGroupNode parent) : base(parent)
        {
            SystemType = systemType;
        }
    }

    /// <summary>Anchor direction of a declared scheduling constraint.</summary>
    internal enum SystemAnchorKind
    {
        /// <summary>The node must run before the anchor target.</summary>
        Before = 0,

        /// <summary>The node must run after the anchor target.</summary>
        After = 1,
    }

    /// <summary>
    /// A declared Before / After anchor. Exactly one of <see cref="SystemType"/> and
    /// <see cref="GroupName"/> is set: a system-type anchor or a group-name anchor.
    /// Anchors are resolved when the execution order is rebuilt, so forward references
    /// are allowed.
    /// </summary>
    internal readonly struct SystemAnchor
    {
        /// <summary>Gets the anchor direction.</summary>
        public SystemAnchorKind Kind { get; }

        /// <summary>Gets the anchor system type; null for group-name anchors.</summary>
        public Type SystemType { get; }

        /// <summary>Gets the anchor group name; null for system-type anchors.</summary>
        public string GroupName { get; }

        /// <summary>Creates an anchor.</summary>
        /// <param name="kind">Anchor direction.</param>
        /// <param name="systemType">Anchor system type; null for group-name anchors.</param>
        /// <param name="groupName">Anchor group name; null for system-type anchors.</param>
        public SystemAnchor(SystemAnchorKind kind, Type systemType, string groupName)
        {
            Kind = kind;
            SystemType = systemType;
            GroupName = groupName;
        }
    }
}
