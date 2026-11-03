using CoreECS.Defines;
using CoreECS.Managers;

namespace CoreECS.Test
{
    [TestFixture]
    public sealed class SystemConvergenceTestUnit
    {
        private World m_world = null!;

        [SetUp]
        public void SetUp()
        {
            NeverActivatedSystem.Reset();
            AddedSystem.Reset();
            ExecutionLog.Clear();
            m_world = new World();
            m_world.Startup();
        }

        [TearDown]
        public void TearDown()
        {
            if (m_world.Ready) m_world.Shutdown();
        }

        [Test]
        public void RegisterDuringTick_ActivatesAtNextBeginTick()
        {
            m_world.BeginTick();
            m_world.RegisterSystem<AddedSystem>();

            Assert.That(AddedSystem.ConstructorCount, Is.Zero);
            Assert.That(AddedSystem.CreateCount, Is.Zero);
            Assert.That(m_world.FindSystem<AddedSystem>(), Is.Null);

            m_world.Tick();
            m_world.EndTick();

            Assert.That(AddedSystem.TickCount, Is.Zero);
            Assert.That(AddedSystem.CreateCount, Is.Zero);

            m_world.BeginTick();
            Assert.That(AddedSystem.ConstructorCount, Is.EqualTo(1));
            Assert.That(AddedSystem.CreateCount, Is.EqualTo(1));
            m_world.Tick();
            m_world.EndTick();

            Assert.That(AddedSystem.TickCount, Is.EqualTo(1));
        }

        [Test]
        public void UnregisterDuringTick_KeepsCurrentSnapshotAndDestroysAtEndTick()
        {
            m_world.RegisterSystem<LifecycleSystem>();
            LifecycleSystem instance = m_world.FindSystem<LifecycleSystem>()!;

            m_world.BeginTick();
            m_world.UnregisterSystem<LifecycleSystem>();

            Assert.That(instance.DestroyCount, Is.Zero);
            Assert.That(m_world.FindSystem<LifecycleSystem>(), Is.SameAs(instance));

            m_world.Tick();
            Assert.That(instance.TickCount, Is.EqualTo(1));
            Assert.That(instance.DestroyCount, Is.Zero);

            m_world.EndTick();
            Assert.That(instance.DestroyCount, Is.EqualTo(1));
            Assert.That(m_world.FindSystem<LifecycleSystem>(), Is.Null);

            m_world.BeginTick();
            m_world.Tick();
            m_world.EndTick();
            Assert.That(instance.TickCount, Is.EqualTo(1));
        }

        [Test]
        public void RegisterThenUnregisterDuringTick_CancelsWithoutLifecycleHooks()
        {
            m_world.BeginTick();
            m_world.RegisterSystem<NeverActivatedSystem>();
            m_world.UnregisterSystem<NeverActivatedSystem>();
            m_world.Tick();
            m_world.EndTick();

            m_world.BeginTick();
            m_world.Tick();
            m_world.EndTick();

            Assert.That(NeverActivatedSystem.ConstructorCount, Is.Zero);
            Assert.That(NeverActivatedSystem.CreateCount, Is.Zero);
            Assert.That(NeverActivatedSystem.TickCount, Is.Zero);
            Assert.That(NeverActivatedSystem.DestroyCount, Is.Zero);
        }

        [Test]
        public void RemoveReaddAndMoveDuringTick_ReusesInstanceAndAppliesOnlyFinalPlacement()
        {
            m_world.RegisterGroup("Early");
            m_world.RegisterGroup("Late");
            m_world.RegisterSystem<LifecycleSystem>("Early");
            m_world.RegisterSystem<PeerSystem>("Late");
            LifecycleSystem original = m_world.FindSystem<LifecycleSystem>()!;

            m_world.BeginTick();
            m_world.UnregisterSystem<LifecycleSystem>();
            m_world.RegisterSystem<LifecycleSystem>("Late").After<PeerSystem>();

            m_world.Tick();
            Assert.That(ExecutionLog.Items, Is.EqualTo(new[] { "lifecycle", "peer" }));
            m_world.EndTick();

            Assert.That(original.DestroyCount, Is.Zero);
            Assert.That(m_world.FindSystem<LifecycleSystem>(), Is.SameAs(original));

            ExecutionLog.Clear();
            m_world.BeginTick();
            m_world.Tick();
            m_world.EndTick();

            Assert.That(ExecutionLog.Items, Is.EqualTo(new[] { "peer", "lifecycle" }));
            Assert.That(m_world.FindSystem<LifecycleSystem>(), Is.SameAs(original));
            Assert.That(original.CreateCount, Is.EqualTo(1));
        }

        [Test]
        public void AnchorAddedDuringTick_LeavesCurrentOrderAndChangesNextSnapshot()
        {
            SystemRegistration first = m_world.RegisterSystem<LifecycleSystem>();
            m_world.RegisterSystem<PeerSystem>();

            m_world.BeginTick();
            first.After<PeerSystem>();
            m_world.Tick();
            Assert.That(ExecutionLog.Items, Is.EqualTo(new[] { "lifecycle", "peer" }));
            m_world.EndTick();

            ExecutionLog.Clear();
            m_world.BeginTick();
            m_world.Tick();
            m_world.EndTick();

            Assert.That(ExecutionLog.Items, Is.EqualTo(new[] { "peer", "lifecycle" }));
        }

        [Test]
        public void MultipleSameTypeOperationsDuringTick_ConvergeToFinalDesiredRegistration()
        {
            m_world.RegisterGroup("One");
            m_world.RegisterGroup("Two");

            m_world.BeginTick();
            m_world.RegisterSystem<AddedSystem>("One");
            m_world.UnregisterSystem<AddedSystem>();
            m_world.RegisterSystem<AddedSystem>("Two");
            m_world.UnregisterSystem<AddedSystem>();
            m_world.RegisterSystem<AddedSystem>("One");
            m_world.EndTick();

            Assert.That(AddedSystem.ConstructorCount, Is.Zero);
            Assert.That(AddedSystem.CreateCount, Is.Zero);

            m_world.BeginTick();
            m_world.Tick();
            m_world.EndTick();

            Assert.That(AddedSystem.ConstructorCount, Is.EqualTo(1));
            Assert.That(AddedSystem.CreateCount, Is.EqualTo(1));
            Assert.That(AddedSystem.TickCount, Is.EqualTo(1));
            SystemSchedule schedule = m_world.GetManager<SystemManager>().Schedule;
            Assert.That(schedule.FindSystem(typeof(AddedSystem))!.Parent,
                Is.SameAs(schedule.FindGroup("One")));
        }

        private static class ExecutionLog
        {
            internal static List<string> Items { get; } = new List<string>();
            internal static void Clear() => Items.Clear();
        }

        private sealed class LifecycleSystem : ISystem
        {
            internal int CreateCount { get; private set; }
            internal int TickCount { get; private set; }
            internal int DestroyCount { get; private set; }

            public void OnCreate() => CreateCount++;
            public void OnTick(ulong tickMask)
            {
                TickCount++;
                ExecutionLog.Items.Add("lifecycle");
            }
            public void OnDestroy() => DestroyCount++;
        }

        private sealed class PeerSystem : ISystem
        {
            public void OnCreate() { }
            public void OnTick(ulong tickMask) => ExecutionLog.Items.Add("peer");
            public void OnDestroy() { }
        }

        private sealed class NeverActivatedSystem : ISystem
        {
            internal static int ConstructorCount { get; private set; }
            internal static int CreateCount { get; private set; }
            internal static int TickCount { get; private set; }
            internal static int DestroyCount { get; private set; }

            public NeverActivatedSystem() => ConstructorCount++;
            internal static void Reset()
            {
                ConstructorCount = 0;
                CreateCount = 0;
                TickCount = 0;
                DestroyCount = 0;
            }
            public void OnCreate() => CreateCount++;
            public void OnTick(ulong tickMask) => TickCount++;
            public void OnDestroy() => DestroyCount++;
        }

        private sealed class AddedSystem : ISystem
        {
            internal static int ConstructorCount { get; private set; }
            internal static int CreateCount { get; private set; }
            internal static int TickCount { get; private set; }

            public AddedSystem() => ConstructorCount++;
            internal static void Reset()
            {
                ConstructorCount = 0;
                CreateCount = 0;
                TickCount = 0;
            }
            public void OnCreate() => CreateCount++;
            public void OnTick(ulong tickMask) => TickCount++;
            public void OnDestroy() { }
        }
    }
}
