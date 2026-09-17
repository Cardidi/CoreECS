using CoreECS.Defines;
using CoreECS.Managers;

namespace CoreECS.Test
{
    /// <summary>
    /// Registration contract for the Phase 4 scheduling tree: groups are pure sort buckets
    /// (no masks) that may nest and mix with systems at the same level, the root level is an
    /// implicit default group, Early/Later control same-level insertion, and Before/After
    /// anchors are stored for later resolution (forward references allowed). Execution order
    /// resolution (flatten + topological sort) is covered by later tasks.
    /// </summary>
    [TestFixture]
    public class SystemGroupRegistrationTestUnit
    {
        private World _world = null!;

        [SetUp]
        public void Setup()
        {
            _world = new World();
            _world.Startup();
        }

        [TearDown]
        public void TearDown()
        {
            _world?.Shutdown();
        }

        private SystemSchedule Schedule => _world.GetManager<SystemManager>().Schedule;

        private static string[] ChildNames(SystemGroupNode group)
        {
            var names = new List<string>();
            foreach (var child in group.Children)
            {
                if (child is SystemGroupNode childGroup) names.Add(childGroup.Name);
                else names.Add(((SystemEntryNode)child).SystemType.Name);
            }

            return names.ToArray();
        }

        [Test]
        public void RegisterGroup_RootGroup_IsChildOfImplicitRoot()
        {
            _world.RegisterGroup("Physics");

            var root = Schedule.Root;
            var physics = Schedule.FindGroup("Physics");

            Assert.IsNull(root.Name);
            Assert.IsNotNull(physics);
            Assert.AreSame(root, physics.Parent);
            CollectionAssert.AreEqual(new[] { "Physics" }, ChildNames(root));
        }

        [Test]
        public void RegisterGroup_WithParent_NestsUnderParentGroup()
        {
            _world.RegisterGroup("Physics");
            _world.RegisterGroup("Collision", "Physics");

            var physics = Schedule.FindGroup("Physics");
            var collision = Schedule.FindGroup("Collision");

            Assert.IsNotNull(physics);
            Assert.IsNotNull(collision);
            Assert.AreSame(physics, collision.Parent);
            CollectionAssert.AreEqual(new[] { "Collision" }, ChildNames(physics));
            CollectionAssert.AreEqual(new[] { "Physics" }, ChildNames(Schedule.Root));
        }

        [Test]
        public void RegisterGroup_NestedThreeLevels_PreservesHierarchy()
        {
            _world.RegisterGroup("A");
            _world.RegisterGroup("B", "A");
            _world.RegisterGroup("C", "B");

            var a = Schedule.FindGroup("A");
            var b = Schedule.FindGroup("B");
            var c = Schedule.FindGroup("C");

            Assert.AreSame(Schedule.Root, a.Parent);
            Assert.AreSame(a, b.Parent);
            Assert.AreSame(b, c.Parent);
            CollectionAssert.AreEqual(new[] { "B" }, ChildNames(a));
            CollectionAssert.AreEqual(new[] { "C" }, ChildNames(b));
        }

        [Test]
        public void RegisterGroup_EarlyAndLater_ControlPositionAmongSameLevelSiblings()
        {
            _world.RegisterSystem<InputSystem>();
            _world.RegisterSystem<MovementSystem>();
            _world.RegisterGroup("LaterGroup");
            _world.RegisterGroup("EarlyGroup", GroupInsertMode.Early);

            CollectionAssert.AreEqual(
                new[] { "EarlyGroup", "InputSystem", "MovementSystem", "LaterGroup" },
                ChildNames(Schedule.Root));
        }

        [Test]
        public void RegisterGroup_InvalidNameOrDuplicate_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _world.RegisterGroup(null));

            var emptyEx = Assert.Throws<InvalidOperationException>(() => _world.RegisterGroup(string.Empty));
            Assert.AreEqual("Group name must not be empty.", emptyEx!.Message);

            _world.RegisterGroup("Physics");
            var duplicateEx = Assert.Throws<InvalidOperationException>(() => _world.RegisterGroup("Physics"));
            Assert.AreEqual("Group 'Physics' is already registered.", duplicateEx!.Message);
        }

        [Test]
        public void RegisterGroup_WithUnknownParent_Throws()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => _world.RegisterGroup("Collision", "Physics"));

            Assert.AreEqual("Group 'Physics' is not registered.", ex!.Message);
            Assert.IsNull(Schedule.FindGroup("Collision"));
        }

        [Test]
        public void RegisterSystem_Placement_UsesGroupOrRoot()
        {
            _world.RegisterGroup("Gameplay");
            _world.RegisterSystem<InputSystem>();
            _world.RegisterSystem<MovementSystem>("Gameplay");

            var rootNode = Schedule.FindSystem(typeof(InputSystem));
            var groupNode = Schedule.FindSystem(typeof(MovementSystem));

            Assert.AreSame(Schedule.Root, rootNode.Parent);
            Assert.AreSame(Schedule.FindGroup("Gameplay"), groupNode.Parent);
            CollectionAssert.AreEqual(new[] { "Gameplay", "InputSystem" }, ChildNames(Schedule.Root));
            CollectionAssert.AreEqual(new[] { "MovementSystem" }, ChildNames(Schedule.FindGroup("Gameplay")));
        }

        [Test]
        public void RegisterSystem_WithUnknownGroup_Throws()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => _world.RegisterSystem<InputSystem>("Gameplay"));

            Assert.AreEqual("Group 'Gameplay' is not registered.", ex!.Message);
            Assert.IsNull(Schedule.FindSystem(typeof(InputSystem)));
            Assert.IsNull(_world.FindSystem<InputSystem>());
        }

        [Test]
        public void RegistrationAnchors_AllowForwardReferencesAndCrossLevelTargets()
        {
            _world.RegisterGroup("Gameplay");
            _world.RegisterGroup("Physics");

            _world.RegisterSystem<InputSystem>("Gameplay")
                .Before<MovementSystem>()
                .After("Physics");

            var inputNode = Schedule.FindSystem(typeof(InputSystem));
            Assert.AreEqual(2, inputNode.Anchors.Count);
            Assert.AreEqual(SystemAnchorKind.Before, inputNode.Anchors[0].Kind);
            Assert.AreEqual(typeof(MovementSystem), inputNode.Anchors[0].SystemType);
            Assert.IsNull(inputNode.Anchors[0].GroupName);
            Assert.AreEqual(SystemAnchorKind.After, inputNode.Anchors[1].Kind);
            Assert.IsNull(inputNode.Anchors[1].SystemType);
            Assert.AreEqual("Physics", inputNode.Anchors[1].GroupName);

            // Forward reference: MovementSystem registers later without disturbing the stored anchor
            _world.RegisterSystem<MovementSystem>("Gameplay");

            Assert.AreSame(inputNode, Schedule.FindSystem(typeof(InputSystem)));
            Assert.AreEqual(typeof(MovementSystem), inputNode.Anchors[0].SystemType);
        }

        [Test]
        public void RegisterGroup_AnchorsAreStoredForSystemTypesAndGroupNames()
        {
            _world.RegisterGroup("Gameplay");
            _world.RegisterSystem<CleanupSystem>();

            _world.RegisterGroup("Render").After("Gameplay").Before<CleanupSystem>();

            var renderNode = Schedule.FindGroup("Render");
            Assert.AreEqual(2, renderNode.Anchors.Count);
            Assert.AreEqual(SystemAnchorKind.After, renderNode.Anchors[0].Kind);
            Assert.AreEqual("Gameplay", renderNode.Anchors[0].GroupName);
            Assert.AreEqual(SystemAnchorKind.Before, renderNode.Anchors[1].Kind);
            Assert.AreEqual(typeof(CleanupSystem), renderNode.Anchors[1].SystemType);
        }

        [Test]
        public void RegistrationOrder_IsPreservedAsTreeChildOrder()
        {
            _world.RegisterGroup("First");
            _world.RegisterSystem<InputSystem>();
            _world.RegisterGroup("Second", "First");
            _world.RegisterSystem<MovementSystem>("First");
            _world.RegisterSystem<CleanupSystem>();

            CollectionAssert.AreEqual(new[] { "First", "InputSystem", "CleanupSystem" }, ChildNames(Schedule.Root));
            CollectionAssert.AreEqual(new[] { "Second", "MovementSystem" }, ChildNames(Schedule.FindGroup("First")));
        }

        [Test]
        public void RegisterSystem_LegacyCallShapes_KeepV1Behavior()
        {
            _world.RegisterSystem<LegacySystem>();
            _world.GetManager<SystemManager>().RegisterSystem(typeof(OtherLegacySystem));

            var legacy = _world.FindSystem<LegacySystem>();
            Assert.IsNotNull(legacy);
            Assert.IsTrue(legacy.OnCreateCalled);
            Assert.AreSame(Schedule.Root, Schedule.FindSystem(typeof(LegacySystem)).Parent);

            _world.BeginTick();
            _world.Tick();
            _world.EndTick();

            Assert.IsTrue(legacy.OnTickCalled);
            Assert.IsTrue(_world.FindSystem<OtherLegacySystem>().OnTickCalled);
        }

        [Test]
        public void UnregisterSystem_RemovesNodeFromTree()
        {
            _world.RegisterSystem<InputSystem>();
            Assert.IsNotNull(Schedule.FindSystem(typeof(InputSystem)));

            _world.UnregisterSystem<InputSystem>();

            Assert.IsNull(Schedule.FindSystem(typeof(InputSystem)));
            CollectionAssert.DoesNotContain(ChildNames(Schedule.Root), "InputSystem");
        }

        [Test]
        public void RegisterSystem_DuringTick_AddsNodeAndInstantiatesAtNextBeginTick()
        {
            _world.BeginTick();
            _world.RegisterSystem<InputSystem>();

            Assert.IsNotNull(Schedule.FindSystem(typeof(InputSystem)));
            Assert.IsNull(_world.FindSystem<InputSystem>());

            _world.Tick();
            _world.EndTick();

            Assert.IsNull(_world.FindSystem<InputSystem>());

            _world.BeginTick();
            Assert.IsNotNull(_world.FindSystem<InputSystem>());
            Assert.IsNotNull(Schedule.FindSystem(typeof(InputSystem)));

            _world.Tick();
            _world.EndTick();
        }

        [Test]
        public void UnregisterSystem_DuringTick_RemovesNodeAtCleanup()
        {
            _world.RegisterSystem<InputSystem>();
            _world.BeginTick();
            _world.UnregisterSystem<InputSystem>();

            Assert.IsNotNull(Schedule.FindSystem(typeof(InputSystem)));

            _world.Tick();
            _world.EndTick();

            Assert.IsNull(Schedule.FindSystem(typeof(InputSystem)));
            CollectionAssert.DoesNotContain(ChildNames(Schedule.Root), "InputSystem");
        }

        [Test]
        public void Shutdown_ClearsSystemNodesAndKeepsGroups()
        {
            _world.RegisterGroup("Physics");
            _world.RegisterSystem<InputSystem>("Physics");

            var schedule = Schedule;
            _world.Shutdown();

            Assert.IsNull(schedule.FindSystem(typeof(InputSystem)));
            Assert.IsNotNull(schedule.FindGroup("Physics"));

            _world = null!;
        }

        private class InputSystem : ISystem
        {
            public void OnTick(ulong tickMask)
            {
            }
        }

        private class MovementSystem : ISystem
        {
            public void OnTick(ulong tickMask)
            {
            }
        }

        private class CleanupSystem : ISystem
        {
            public void OnTick(ulong tickMask)
            {
            }
        }

        private class LegacySystem : ISystem
        {
            public bool OnCreateCalled { get; private set; }

            public bool OnTickCalled { get; private set; }

            public void OnCreate()
            {
                OnCreateCalled = true;
            }

            public void OnTick(ulong tickMask)
            {
                OnTickCalled = true;
            }
        }

        private class OtherLegacySystem : ISystem
        {
            public bool OnTickCalled { get; private set; }

            public void OnTick(ulong tickMask)
            {
                OnTickCalled = true;
            }
        }
    }
}
