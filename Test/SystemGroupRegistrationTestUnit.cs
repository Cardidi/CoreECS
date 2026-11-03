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
        public void RegisterGroup_EarlyAndLater_ControlMixedChildrenInsideNestedParent()
        {
            m_world.RegisterGroup("Parent");
            m_world.RegisterSystem<InputSystem>("Parent");
            m_world.RegisterGroup("NestedLaterA", "Parent", GroupInsertMode.Later);
            m_world.RegisterSystem<MovementSystem>("Parent");
            m_world.RegisterGroup("NestedEarlyA", "Parent", GroupInsertMode.Early);
            m_world.RegisterGroup("NestedLaterB", "Parent");
            m_world.RegisterGroup("NestedEarlyB", "Parent", GroupInsertMode.Early);

            Assert.That(ChildNames(Schedule.FindGroup("Parent")!), Is.EqualTo(new[]
            {
                "NestedEarlyB", "NestedEarlyA", nameof(InputSystem), "NestedLaterA",
                nameof(MovementSystem), "NestedLaterB",
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
        public void RegistrationHandles_RejectPriorLifecycleGenerationWhenManagerIsNotShutdown()
        {
            SystemManager manager = m_world.GetManager<SystemManager>();
            GroupRegistration priorGeneration = m_world.RegisterGroup("PriorGeneration");

            manager.OnWorldEnded();
            manager.OnManagerCreated();
            GroupRegistration currentGeneration = manager.RegisterGroup("CurrentGeneration");

            Assert.DoesNotThrow(() => currentGeneration.Before("ForwardGroup"));
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => priorGeneration.After("ForwardGroup"))!;
            Assert.That(exception.Message, Is.EqualTo("Registration handle is no longer valid."));
        }

        [Test]
        public void SystemRegistration_RejectsOldNodeAfterUnregisterAndSameTypeReregister()
        {
            SystemRegistration oldRegistration = m_world.RegisterSystem<InputSystem>();
            m_world.UnregisterSystem<InputSystem>();
            SystemRegistration currentRegistration = m_world.RegisterSystem<InputSystem>();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => oldRegistration.Before<MovementSystem>())!;
            Assert.That(exception.Message, Is.EqualTo("Registration handle is no longer valid."));
            Assert.DoesNotThrow(() => currentRegistration.Before<MovementSystem>());
            Assert.That(Schedule.FindSystem(typeof(InputSystem))!.Anchors.Count, Is.EqualTo(1));
        }

        [Test]
        public void TickPhase_UnregisterRegisterUnregister_CancelsQueuedAdd()
        {
            m_world.RegisterSystem<InputSystem>();

            m_world.BeginTick();
            m_world.UnregisterSystem<InputSystem>();
            m_world.RegisterSystem<InputSystem>();
            m_world.UnregisterSystem<InputSystem>();
            m_world.Tick();
            m_world.EndTick();

            m_world.BeginTick();
            InputSystem? resurrected = m_world.FindSystem<InputSystem>();
            m_world.Tick();
            m_world.EndTick();

            Assert.That(resurrected, Is.Null);
            Assert.That(Schedule.FindSystem(typeof(InputSystem)), Is.Null);
        }

        [Test]
        public void RegisterSystem_PublicApiPreservesExactLegacyOverloads()
        {
            MethodInfo[] worldMethods = typeof(World).GetMethods(BindingFlags.Instance | BindingFlags.Public);
            MethodInfo[] managerMethods = typeof(SystemManager).GetMethods(BindingFlags.Instance | BindingFlags.Public);

            Assert.That(HasGenericRegisterSystem(worldMethods, Type.EmptyTypes), Is.True);
            Assert.That(HasGenericRegisterSystem(worldMethods, new[] { typeof(string) }), Is.True);
            Assert.That(HasRegisterSystem(worldMethods, new[] { typeof(Type) }), Is.True);
            Assert.That(HasRegisterSystem(worldMethods, new[] { typeof(Type), typeof(string) }), Is.True);
            Assert.That(HasRegisterSystem(managerMethods, new[] { typeof(Type) }), Is.True);
            Assert.That(HasRegisterSystem(managerMethods, new[] { typeof(Type), typeof(string) }), Is.True);

            MethodInfo groupedGeneric = worldMethods.Single(method => method.Name == "RegisterSystem" &&
                method.IsGenericMethodDefinition &&
                method.GetParameters().Select(parameter => parameter.ParameterType)
                    .SequenceEqual(new[] { typeof(string) }));
            Assert.That(groupedGeneric.GetParameters()[0].IsOptional, Is.False);
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

        private static bool HasRegisterSystem(IEnumerable<MethodInfo> methods, Type[] parameterTypes)
        {
            return methods.Any(method => method.Name == "RegisterSystem" &&
                !method.IsGenericMethodDefinition &&
                method.ReturnType == typeof(SystemRegistration) &&
                method.GetParameters().Select(parameter => parameter.ParameterType)
                    .SequenceEqual(parameterTypes));
        }

        private static bool HasGenericRegisterSystem(IEnumerable<MethodInfo> methods, Type[] parameterTypes)
        {
            return methods.Any(method => method.Name == "RegisterSystem" &&
                method.IsGenericMethodDefinition &&
                method.GetGenericArguments().Length == 1 &&
                method.ReturnType == typeof(SystemRegistration) &&
                method.GetParameters().Select(parameter => parameter.ParameterType)
                    .SequenceEqual(parameterTypes));
        }

        private sealed class InputSystem : ISystem
        {
            public void OnCreate() { }
            public void OnTick(ulong tickMask) { }
            public void OnDestroy() { }
        }

        private sealed class MovementSystem : ISystem
        {
            public void OnCreate() { }
            public void OnTick(ulong tickMask) { }
            public void OnDestroy() { }
        }

        private sealed class CollisionSystem : ISystem
        {
            public void OnCreate() { }
            public void OnTick(ulong tickMask) { }
            public void OnDestroy() { }
        }
    }
}
