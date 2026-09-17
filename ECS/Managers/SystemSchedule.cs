using System;
using System.Collections.Generic;
using CoreECS.Defines;
using CoreECS.Utils;

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

        /// <summary>
        /// Builds the execution order of the registered systems: the tree is flattened in
        /// registration order (depth-first; a group's contents land at the group's position),
        /// Before / After anchors are resolved into ordering edges, and a stable topological
        /// sort produces the sequence. Unconstrained systems keep their flatten order.
        /// </summary>
        /// <returns>
        /// System types in execution order. When the constraints contain a cycle, the error is
        /// logged and the flatten order is returned for the whole sequence.
        /// </returns>
        public List<Type> BuildExecutionOrder()
        {
            var flattened = new List<SystemEntryNode>();
            _flatten(Root, flattened);

            var indexes = new Dictionary<SystemEntryNode, int>();
            for (var i = 0; i < flattened.Count; i++)
                indexes.Add(flattened[i], i);

            var inDegree = new int[flattened.Count];
            var successors = new List<int>[flattened.Count];
            for (var i = 0; i < successors.Length; i++)
                successors[i] = new List<int>();

            _collectConstraints(Root, indexes, inDegree, successors);

            var order = new List<SystemEntryNode>(flattened.Count);
            var emitted = new bool[flattened.Count];

            for (var step = 0; step < flattened.Count; step++)
            {
                var pick = -1;
                for (var i = 0; i < flattened.Count; i++)
                {
                    if (!emitted[i] && inDegree[i] == 0)
                    {
                        pick = i;
                        break;
                    }
                }

                if (pick < 0)
                {
                    Log.Err("System execution order contains a cycle; falling back to registration order. Blocked systems: "
                        + _blockedSystemNames(flattened, emitted) + ".");
                    return _toTypes(flattened);
                }

                emitted[pick] = true;
                order.Add(flattened[pick]);
                foreach (var successor in successors[pick])
                    inDegree[successor]--;
            }

            return _toTypes(order);
        }

        /// <summary>
        /// Flattens a group subtree depth-first in child order; only system entries are emitted,
        /// so a group's contents land at the group's position among its siblings.
        /// </summary>
        /// <param name="group">Group whose subtree is flattened.</param>
        /// <param name="output">Receives the system entries in flatten order.</param>
        private static void _flatten(SystemGroupNode group, List<SystemEntryNode> output)
        {
            foreach (var child in group.Children)
            {
                if (child is SystemGroupNode childGroup) _flatten(childGroup, output);
                else output.Add((SystemEntryNode)child);
            }
        }

        /// <summary>
        /// Applies the anchors of every tree node to the edge arrays. A group's anchors apply
        /// to its entire subtree; a group-name anchor target expands to the target group's
        /// entire subtree. Unresolvable targets are logged and ignored.
        /// </summary>
        /// <param name="group">Group whose subtree is visited.</param>
        /// <param name="indexes">Flatten index of every system entry.</param>
        /// <param name="inDegree">In-degree accumulator per flatten index.</param>
        /// <param name="successors">Successor lists per flatten index.</param>
        private void _collectConstraints(SystemGroupNode group, Dictionary<SystemEntryNode, int> indexes, int[] inDegree, List<int>[] successors)
        {
            _applyAnchors(group, indexes, inDegree, successors);

            foreach (var child in group.Children)
            {
                if (child is SystemGroupNode childGroup) _collectConstraints(childGroup, indexes, inDegree, successors);
                else _applyAnchors(child, indexes, inDegree, successors);
            }
        }

        /// <summary>
        /// Resolves the anchors of a single node into edges. A system node constrains itself;
        /// a group node constrains every system in its subtree.
        /// </summary>
        /// <param name="node">Node whose anchors are applied.</param>
        /// <param name="indexes">Flatten index of every system entry.</param>
        /// <param name="inDegree">In-degree accumulator per flatten index.</param>
        /// <param name="successors">Successor lists per flatten index.</param>
        private void _applyAnchors(SystemScheduleNode node, Dictionary<SystemEntryNode, int> indexes, int[] inDegree, List<int>[] successors)
        {
            if (node.Anchors.Count == 0) return;

            var subjects = new List<SystemEntryNode>();
            if (node is SystemEntryNode entry) subjects.Add(entry);
            else _flatten((SystemGroupNode)node, subjects);

            for (var i = 0; i < node.Anchors.Count; i++)
            {
                var anchor = node.Anchors[i];

                if (anchor.SystemType != null)
                {
                    var target = FindSystem(anchor.SystemType);
                    if (target == null)
                    {
                        Log.Err($"System schedule anchor target '{anchor.SystemType.FullName}' is not registered; constraint ignored.");
                        continue;
                    }

                    _addEdges(subjects, new List<SystemEntryNode> { target }, anchor.Kind, indexes, inDegree, successors);
                }
                else
                {
                    var targetGroup = FindGroup(anchor.GroupName);
                    if (targetGroup == null)
                    {
                        Log.Err($"System schedule anchor target group '{anchor.GroupName}' is not registered; constraint ignored.");
                        continue;
                    }

                    var targets = new List<SystemEntryNode>();
                    _flatten(targetGroup, targets);

                    // A group anchor whose target subtree contains every subject is degenerate
                    // (self / ancestor constraint): skip it instead of adding mutual edges.
                    if (_isSubset(subjects, targets)) continue;

                    _addEdges(subjects, targets, anchor.Kind, indexes, inDegree, successors);
                }
            }
        }

        /// <summary>True when every entry in <paramref name="subset"/> is also in <paramref name="superset"/>.</summary>
        private static bool _isSubset(List<SystemEntryNode> subset, List<SystemEntryNode> superset)
        {
            for (var i = 0; i < subset.Count; i++)
            {
                if (!superset.Contains(subset[i])) return false;
            }

            return true;
        }

        /// <summary>
        /// Adds the edges for a subject / target cartesian product. Self edges are skipped:
        /// a system trivially precedes and follows itself, so a system anchored to a group
        /// that contains it is constrained against the other systems of that group only.
        /// </summary>
        /// <param name="subjects">Systems constrained by the anchor.</param>
        /// <param name="targets">Systems the anchor points at.</param>
        /// <param name="kind">Anchor direction.</param>
        /// <param name="indexes">Flatten index of every system entry.</param>
        /// <param name="inDegree">In-degree accumulator per flatten index.</param>
        /// <param name="successors">Successor lists per flatten index.</param>
        private static void _addEdges(List<SystemEntryNode> subjects, List<SystemEntryNode> targets, SystemAnchorKind kind,
            Dictionary<SystemEntryNode, int> indexes, int[] inDegree, List<int>[] successors)
        {
            for (var s = 0; s < subjects.Count; s++)
            {
                for (var t = 0; t < targets.Count; t++)
                {
                    var subject = subjects[s];
                    var target = targets[t];
                    if (ReferenceEquals(subject, target)) continue;

                    var subjectIndex = indexes[subject];
                    var targetIndex = indexes[target];

                    if (kind == SystemAnchorKind.Before)
                    {
                        successors[subjectIndex].Add(targetIndex);
                        inDegree[targetIndex]++;
                    }
                    else
                    {
                        successors[targetIndex].Add(subjectIndex);
                        inDegree[subjectIndex]++;
                    }
                }
            }
        }

        /// <summary>Collects the type names of the systems that could not be emitted, for cycle logging.</summary>
        /// <param name="flattened">System entries in flatten order.</param>
        /// <param name="emitted">Emission state per flatten index.</param>
        /// <returns>Comma separated system type names.</returns>
        private static string _blockedSystemNames(List<SystemEntryNode> flattened, bool[] emitted)
        {
            var names = new List<string>();
            for (var i = 0; i < flattened.Count; i++)
            {
                if (!emitted[i]) names.Add(flattened[i].SystemType.Name);
            }

            return string.Join(", ", names);
        }

        /// <summary>Projects flatten entries to their system types.</summary>
        /// <param name="entries">System entries in execution order.</param>
        /// <returns>System types in the same order.</returns>
        private static List<Type> _toTypes(List<SystemEntryNode> entries)
        {
            var result = new List<Type>(entries.Count);
            for (var i = 0; i < entries.Count; i++) result.Add(entries[i].SystemType);
            return result;
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
