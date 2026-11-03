using System.Reflection;
using CoreECS.Defines;
using CoreECS.Managers;

namespace CoreECS.Test
{
    [TestFixture]
    public class SystemGroupRegistrationTestUnit
    {
        private World m_world = null!;

        private SystemSchedule Schedule => m_world.GetManager<SystemManager>().Schedule;

        [SetUp]
        public void SetUp()
        {
            m_world = new World();
            m_world.Startup();
        }

        [TearDown]
        public void TearDown()
        {
            if (m_world.Ready) m_world.Shutdown();
        }

        [Test]
        public void RegisterAtRoot_MixesSystemsAndGroupsInDeclarationOrder()
        {
            SystemRegistration system = m_world.RegisterSystem<InputSystem>();
            GroupRegistration group = m_world.RegisterGroup("Gameplay");
            m_world.RegisterSystem(typeof(MovementSystem));

            Assert.That(system, Is.Not.Null);
            Assert.That(group, Is.Not.Null);
            Assert.That(Schedule.Root.Name, Is.Null);
            Assert.That(Schedule.FindSystem(typeof(InputSystem))!.Parent, Is.SameAs(Schedule.Root));
            Assert.That(Schedule.FindGroup("Gameplay")!.Parent, Is.SameAs(Schedule.Root));
            Assert.That(ChildNames(Schedule.Root), Is.EqualTo(new[]
            {
                nameof(InputSystem), "Gameplay", nameof(MovementSystem),
            }));
        }

        [Test]
        public void RegisterGroup_WithParent_BuildsNestedTree()
        {
            m_world.RegisterGroup("Gameplay");
            m_world.RegisterGroup("Physics", "Gameplay");
            m_world.RegisterGroup("Collision", "Physics");
            m_world.RegisterSystem<MovementSystem>("Gameplay");
            m_world.RegisterSystem<CollisionSystem>("Collision");

            SystemGroupNode gameplay = Schedule.FindGroup("Gameplay")!;
            SystemGroupNode physics = Schedule.FindGroup("Physics")!;
            SystemGroupNode collision = Schedule.FindGroup("Collision")!;

            Assert.That(physics.Parent, Is.SameAs(gameplay));
            Assert.That(collision.Parent, Is.SameAs(physics));
            Assert.That(ChildNames(gameplay), Is.EqualTo(new[] { "Physics", nameof(MovementSystem) }));
            Assert.That(ChildNames(physics), Is.EqualTo(new[] { "Collision" }));
            Assert.That(ChildNames(collision), Is.EqualTo(new[] { nameof(CollisionSystem) }));
        }

        [Test]
        public void RegisterGroup_EarlyAndLater_ControlMixedChildOrder()
        {
            m_world.RegisterSystem<InputSystem>();
            m_world.RegisterGroup("LaterA", GroupInsertMode.Later);
            m_world.RegisterSystem<MovementSystem>();
            m_world.RegisterGroup("EarlyA", GroupInsertMode.Early);
            m_world.RegisterGroup("LaterB");
            m_world.RegisterGroup("EarlyB", GroupInsertMode.Early);

            Assert.That(ChildNames(Schedule.Root), Is.EqualTo(new[]
            {
                "EarlyB", "EarlyA", nameof(InputSystem), "LaterA", nameof(MovementSystem), "LaterB",
            }));
        }

        [Test]
        public void RegisterGroup_RequiresGloballyUniqueName()
        {
            m_world.RegisterGroup("ParentA");
            m_world.RegisterGroup("ParentB");
            m_world.RegisterGroup("Shared", "ParentA");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => m_world.RegisterGroup("Shared", "ParentB"))!;

            Assert.That(exception.Message, Is.EqualTo("Group 'Shared' is already registered."));
            Assert.That(Schedule.FindGroup("Shared")!.Parent, Is.SameAs(Schedule.FindGroup("ParentA")));
        }

        [Test]
        public void RegisterSystem_RequiresGloballyUniqueConcreteType()
        {
            m_world.RegisterGroup("Gameplay");
            m_world.RegisterGroup("Physics");
            m_world.RegisterSystem<MovementSystem>("Gameplay");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => m_world.RegisterSystem(typeof(MovementSystem), "Physics"))!;

            Assert.That(exception.Message,
                Is.EqualTo($"System {typeof(MovementSystem).FullName} is already registered."));
            Assert.That(Schedule.FindSystem(typeof(MovementSystem))!.Parent,
                Is.SameAs(Schedule.FindGroup("Gameplay")));
        }

        [Test]
        public void RegisterUnderUnknownParent_ThrowsWithoutChangingTree()
        {
            InvalidOperationException groupException = Assert.Throws<InvalidOperationException>(
                () => m_world.RegisterGroup("Collision", "Missing"))!;
            InvalidOperationException systemException = Assert.Throws<InvalidOperationException>(
                () => m_world.RegisterSystem<InputSystem>("Missing"))!;

            Assert.That(groupException.Message, Is.EqualTo("Group 'Missing' is not registered."));
            Assert.That(systemException.Message, Is.EqualTo("Group 'Missing' is not registered."));
            Assert.That(Schedule.FindGroup("Collision"), Is.Null);
            Assert.That(Schedule.FindSystem(typeof(InputSystem)), Is.Null);
        }

        [Test]
        public void SystemRegistration_StoresTypeGenericAndGroupAnchorsAsForwardDeclarations()
        {
            SystemRegistration registration = m_world.RegisterSystem<InputSystem>();

            registration
                .Before(typeof(MovementSystem))
                .After<CollisionSystem>()
                .Before("FutureGroup");

            SystemEntryNode node = Schedule.FindSystem(typeof(InputSystem))!;
            Assert.That(node.Anchors.Count, Is.EqualTo(3));
            AssertAnchor(node.Anchors[0], SystemAnchorKind.Before, typeof(MovementSystem), null);
            AssertAnchor(node.Anchors[1], SystemAnchorKind.After, typeof(CollisionSystem), null);
            AssertAnchor(node.Anchors[2], SystemAnchorKind.Before, null, "FutureGroup");
            Assert.That(Schedule.FindSystem(typeof(MovementSystem)), Is.Null);
            Assert.That(Schedule.FindGroup("FutureGroup"), Is.Null);

            m_world.RegisterGroup("FutureGroup");
            m_world.RegisterSystem<MovementSystem>();
            m_world.RegisterSystem<CollisionSystem>();

            Assert.That(node.Anchors.Count, Is.EqualTo(3));
        }

        [Test]
        public void GroupRegistration_StoresTypeGenericAndGroupAnchorsAsForwardDeclarations()
        {
            GroupRegistration registration = m_world.RegisterGroup("Render");

            registration
                .After(typeof(InputSystem))
                .Before<MovementSystem>()
                .After("FutureGroup");

            SystemGroupNode node = Schedule.FindGroup("Render")!;
            Assert.That(node.Anchors.Count, Is.EqualTo(3));
            AssertAnchor(node.Anchors[0], SystemAnchorKind.After, typeof(InputSystem), null);
            AssertAnchor(node.Anchors[1], SystemAnchorKind.Before, typeof(MovementSystem), null);
            AssertAnchor(node.Anchors[2], SystemAnchorKind.After, null, "FutureGroup");
            Assert.That(Schedule.FindSystem(typeof(InputSystem)), Is.Null);
            Assert.That(Schedule.FindGroup("FutureGroup"), Is.Null);
        }

        [Test]
        public void RegistrationHandles_AreInvalidAfterWorldShutdown()
        {
            SystemRegistration system = m_world.RegisterSystem<InputSystem>();
            GroupRegistration group = m_world.RegisterGroup("Gameplay");

            m_world.Shutdown();

            InvalidOperationException systemException = Assert.Throws<InvalidOperationException>(
                () => system.Before<MovementSystem>())!;
            InvalidOperationException groupException = Assert.Throws<InvalidOperationException>(
                () => group.After("Other"))!;
            Assert.That(systemException.Message, Is.EqualTo("Registration handle is no longer valid."));
            Assert.That(groupException.Message, Is.EqualTo("Registration handle is no longer valid."));
        }

        [Test]
        public void PublicApi_DoesNotExposeUnregisterGroup()
        {
            static bool HasUnregisterGroup(Type type) => type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Any(method => method.Name == "UnregisterGroup");

            Assert.That(HasUnregisterGroup(typeof(World)), Is.False);
            Assert.That(HasUnregisterGroup(typeof(SystemManager)), Is.False);
            Assert.That(HasUnregisterGroup(typeof(GroupRegistration)), Is.False);
        }

        private static string[] ChildNames(SystemGroupNode group)
        {
            return group.Children.Select(child => child is SystemGroupNode childGroup
                ? childGroup.Name!
                : ((SystemEntryNode)child).SystemType.Name).ToArray();
        }

        private static void AssertAnchor(SystemAnchor anchor, SystemAnchorKind kind,
            Type? systemType, string? groupName)
        {
            Assert.That(anchor.Kind, Is.EqualTo(kind));
            Assert.That(anchor.SystemType, Is.EqualTo(systemType));
            Assert.That(anchor.GroupName, Is.EqualTo(groupName));
        }

        private sealed class InputSystem : ISystem
        {
            public void OnTick(ulong tickMask) { }
        }

        private sealed class MovementSystem : ISystem
        {
            public void OnTick(ulong tickMask) { }
        }

        private sealed class CollisionSystem : ISystem
        {
            public void OnTick(ulong tickMask) { }
        }
    }
}
