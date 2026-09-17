using CoreECS.Defines;
using CoreECS.Managers;

namespace CoreECS.Test
{
    /// <summary>
    /// Tick-time convergence contract for the Phase 4 scheduling tree: changes made to the
    /// registration graph while a tick is running are applied at the next BeginTick, paired
    /// changes collapse to the final graph state (unregister + re-register keeps the instance,
    /// register + unregister cancels the pending add), anchors declared during a tick take
    /// effect at the next BeginTick, and the sequence scheduled for the current tick is never
    /// rebuilt or extended while it is executing.
    /// </summary>
    [TestFixture]
    public class SystemConvergenceTestUnit
    {
        private static readonly List<string> ExecutionLog = new List<string>();
        private static World CurrentWorld = null!;

        private World _world = null!;

        [SetUp]
        public void Setup()
        {
            ExecutionLog.Clear();
            _world = new World();
            _world.Startup();
            CurrentWorld = _world;
        }

        [TearDown]
        public void TearDown()
        {
            CurrentWorld = null!;
            _world?.Shutdown();
        }

        private SystemSchedule Schedule => _world.GetManager<SystemManager>().Schedule;

        private static string[] RunningOrder(World world)
        {
            var names = new List<string>();
            foreach (var system in world.GetManager<SystemManager>().Systems) names.Add(system.GetType().Name);
            return names.ToArray();
        }

        [Test]
        public void UnregisterThenRegister_DuringTick_CancelsRemovalAndKeepsInstance()
        {
            _world.RegisterSystem<SystemA>();
            var first = _world.FindSystem<SystemA>();
            Assert.IsNotNull(first);

            _world.BeginTick();
            _world.UnregisterSystem<SystemA>();
            _world.RegisterSystem<SystemA>();
            _world.Tick();
            _world.EndTick();

            var second = _world.FindSystem<SystemA>();
            Assert.AreSame(first, second);
            Assert.AreEqual(1, second.CreateCount);
            Assert.AreEqual(0, second.DestroyCount);
            Assert.AreEqual(1, second.TickCount);
            Assert.IsNotNull(Schedule.FindSystem(typeof(SystemA)));

            _world.BeginTick();
            _world.Tick();
            _world.EndTick();

            Assert.AreSame(first, _world.FindSystem<SystemA>());
            Assert.AreEqual(1, second.CreateCount);
        }

        [Test]
        public void UnregisterThenRegister_DuringTick_AppliesRequestedGroupAtNextBeginTick()
        {
            _world.RegisterGroup("First");
            _world.RegisterGroup("Second");
            _world.RegisterSystem<SystemB>("Second");
            _world.RegisterSystem<SystemA>("First");
            var first = _world.FindSystem<SystemA>();

            _world.BeginTick();

            // Duplicate registration of a live system stays a no-op: the node does not move.
            _world.RegisterSystem<SystemA>("Second");
            Assert.AreSame(Schedule.FindGroup("First"), Schedule.FindSystem(typeof(SystemA)).Parent);

            // Unregister + re-register collapses into "registered in Second": the removal is
            // cancelled and the node is repositioned without touching the current tick.
            _world.UnregisterSystem<SystemA>();
            _world.RegisterSystem<SystemA>("Second");
            CollectionAssert.AreEqual(new[] { "SystemA", "SystemB" }, RunningOrder(_world));

            _world.Tick();
            _world.EndTick();

            Assert.AreSame(first, _world.FindSystem<SystemA>());
            Assert.AreSame(Schedule.FindGroup("Second"), Schedule.FindSystem(typeof(SystemA)).Parent);

            _world.BeginTick();
            CollectionAssert.AreEqual(new[] { "SystemB", "SystemA" }, RunningOrder(_world));
            _world.Tick();
            _world.EndTick();
        }

        [Test]
        public void UnregisterThenRegister_DuringTick_IntoDifferentGroup_KeepsAnchors()
        {
            _world.RegisterGroup("Late");
            _world.RegisterGroup("Early", GroupInsertMode.Early);
            _world.RegisterSystem<SystemA>("Late").After<SystemB>();
            _world.RegisterSystem<SystemB>();

            _world.BeginTick();
            _world.UnregisterSystem<SystemA>();
            _world.RegisterSystem<SystemA>("Early");
            _world.Tick();
            _world.EndTick();
            _world.BeginTick();

            // The B -> A anchor survives the group move: B still runs before A.
            CollectionAssert.AreEqual(new[] { "SystemB", "SystemA" }, RunningOrder(_world));
            _world.Tick();
            _world.EndTick();
        }

        [Test]
        public void RegisterAfterTick_QueuedThenChangable_InstantiatesWithoutDuplicateNode()
        {
            _world.BeginTick();
            _world.RegisterSystem<SystemA>();
            _world.Tick();
            _world.EndTick();

            // The queued add survived CleanupSystems; a second register converges on the
            // pending registration instead of adding a duplicate schedule node.
            Assert.DoesNotThrow(() => _world.RegisterSystem<SystemA>());
            Assert.IsNotNull(_world.FindSystem<SystemA>());
            CollectionAssert.AreEqual(new[] { "SystemA" }, RunningOrder(_world));

            _world.BeginTick();
            CollectionAssert.AreEqual(new[] { "SystemA" }, RunningOrder(_world));
            _world.Tick();
            _world.EndTick();
        }

        [Test]
        public void RegisterThenUnregisterThenRegisterAfterTick_RestoresScheduleNode()
        {
            _world.BeginTick();
            _world.RegisterSystem<SystemA>();
            _world.UnregisterSystem<SystemA>();
            _world.Tick();
            _world.EndTick();

            // The cancelled add left no schedule node; the changable re-register must
            // restore both the instance and the tree entry.
            Assert.DoesNotThrow(() => _world.RegisterSystem<SystemA>());
            Assert.IsNotNull(_world.FindSystem<SystemA>());
            Assert.IsNotNull(Schedule.FindSystem(typeof(SystemA)));
            CollectionAssert.AreEqual(new[] { "SystemA" }, RunningOrder(_world));

            _world.BeginTick();
            CollectionAssert.AreEqual(new[] { "SystemA" }, RunningOrder(_world));
            _world.Tick();
            _world.EndTick();
        }

        [Test]
        public void RegisterThenUnregister_DuringTick_CancelsPendingAdd()
        {
            _world.BeginTick();
            _world.RegisterSystem<SystemA>();
            Assert.IsNotNull(Schedule.FindSystem(typeof(SystemA)));
            Assert.IsNull(_world.FindSystem<SystemA>());

            _world.UnregisterSystem<SystemA>();
            Assert.IsNull(Schedule.FindSystem(typeof(SystemA)));

            var ex = Assert.Throws<InvalidOperationException>(() => _world.UnregisterSystem<SystemA>());
            StringAssert.Contains("is not registered", ex!.Message);

            _world.Tick();
            _world.EndTick();

            _world.BeginTick();
            Assert.IsNull(_world.FindSystem<SystemA>());
            Assert.IsNull(Schedule.FindSystem(typeof(SystemA)));
            _world.Tick();
            _world.EndTick();
        }

        [Test]
        public void RegisterThenUnregisterThenRegister_DuringTick_InstantiatesOnceAtNextBeginTick()
        {
            _world.BeginTick();
            _world.RegisterSystem<SystemA>();
            _world.UnregisterSystem<SystemA>();
            _world.RegisterSystem<SystemA>();

            Assert.IsNull(_world.FindSystem<SystemA>());

            _world.Tick();
            _world.EndTick();
            Assert.IsNull(_world.FindSystem<SystemA>());

            _world.BeginTick();
            var system = _world.FindSystem<SystemA>();
            Assert.IsNotNull(system);
            Assert.AreEqual(1, system.CreateCount);
            Assert.IsNotNull(Schedule.FindSystem(typeof(SystemA)));

            _world.Tick();
            _world.EndTick();
            Assert.AreEqual(1, system.TickCount);
        }

        [Test]
        public void UnregisterThenRegisterThenUnregister_DuringTick_DestroysAtCleanup()
        {
            _world.RegisterSystem<SystemA>();
            var system = _world.FindSystem<SystemA>();

            _world.BeginTick();
            _world.UnregisterSystem<SystemA>();
            _world.RegisterSystem<SystemA>();
            _world.UnregisterSystem<SystemA>();
            _world.Tick();
            _world.EndTick();

            Assert.IsNull(_world.FindSystem<SystemA>());
            Assert.IsNull(Schedule.FindSystem(typeof(SystemA)));
            Assert.AreEqual(1, system.DestroyCount);
            Assert.AreEqual(1, system.TickCount);
        }

        [Test]
        public void AnchorsDeclaredDuringTick_ApplyAtNextBeginTick()
        {
            var handle = _world.RegisterSystem<SystemA>();
            _world.RegisterSystem<SystemB>();

            _world.BeginTick();
            handle.After<SystemB>();
            CollectionAssert.AreEqual(new[] { "SystemA", "SystemB" }, RunningOrder(_world));

            _world.Tick();
            _world.EndTick();

            _world.BeginTick();
            CollectionAssert.AreEqual(new[] { "SystemB", "SystemA" }, RunningOrder(_world));
            _world.Tick();
            _world.EndTick();
        }

        [Test]
        public void AnchorDeclaredOnNewSystemDuringTick_AppliesWithItsFirstTick()
        {
            _world.RegisterSystem<SystemB>();

            _world.BeginTick();
            _world.RegisterSystem<SystemA>().Before<SystemB>();
            Assert.IsNull(_world.FindSystem<SystemA>());

            _world.Tick();
            _world.EndTick();

            _world.BeginTick();
            Assert.IsNotNull(_world.FindSystem<SystemA>());
            CollectionAssert.AreEqual(new[] { "SystemA", "SystemB" }, RunningOrder(_world));
            _world.Tick();
            _world.EndTick();
        }

        [Test]
        public void TreeChangesDuringTick_DoNotAffectRemainingExecution()
        {
            _world.RegisterSystem<MutatorSystem>();
            _world.RegisterSystem<VictimSystem>();
            _world.RegisterSystem<TailSystem>();

            _world.BeginTick();
            _world.Tick();

            CollectionAssert.AreEqual(new[] { "MutatorSystem", "VictimSystem", "TailSystem" }, ExecutionLog);
            Assert.AreEqual(1, _world.FindSystem<VictimSystem>().TickCount);
            Assert.IsNull(_world.FindSystem<NewSystem>());

            _world.EndTick();

            Assert.IsNull(_world.FindSystem<VictimSystem>());
            Assert.IsNull(Schedule.FindSystem(typeof(VictimSystem)));

            _world.BeginTick();
            Assert.IsNotNull(_world.FindSystem<NewSystem>());
            CollectionAssert.AreEqual(new[] { "MutatorSystem", "TailSystem", "NewSystem" }, RunningOrder(_world));
            _world.Tick();
            _world.EndTick();
        }

        [Test]
        public void ExecuteSystems_ScheduledSequenceIsSnapshot_MidTickRebuildDoesNotExtendIt()
        {
            _world.RegisterSystem<QueueAddSystem>();
            _world.RegisterSystem<RebuildSystem>();
            _world.RegisterSystem<TailSystem>();

            _world.BeginTick();
            _world.Tick();

            // The direct TeardownSystems call from OnTick instantiated LateSystem and rebuilt
            // m_systems, but the sequence scheduled for this tick was already snapshotted.
            CollectionAssert.AreEqual(new[] { "QueueAddSystem", "RebuildSystem", "TailSystem" }, ExecutionLog);
            var late = _world.FindSystem<LateSystem>();
            Assert.IsNotNull(late);
            Assert.AreEqual(0, late.TickCount);
            _world.EndTick();

            ExecutionLog.Clear();
            _world.BeginTick();
            _world.Tick();
            CollectionAssert.AreEqual(new[] { "QueueAddSystem", "RebuildSystem", "TailSystem", "LateSystem" }, ExecutionLog);
            Assert.AreEqual(1, late.TickCount);
            _world.EndTick();
        }

        [Test]
        public void GroupRegisteredDuringTick_IsUsableForSameTickRegistration()
        {
            _world.RegisterSystem<SystemB>();

            _world.BeginTick();
            _world.RegisterGroup("Late");
            _world.RegisterSystem<SystemA>("Late");

            Assert.IsNotNull(Schedule.FindGroup("Late"));
            Assert.IsNull(_world.FindSystem<SystemA>());
            CollectionAssert.AreEqual(new[] { "SystemB" }, RunningOrder(_world));

            _world.Tick();
            _world.EndTick();

            _world.BeginTick();
            Assert.IsNotNull(_world.FindSystem<SystemA>());
            CollectionAssert.AreEqual(new[] { "SystemB", "SystemA" }, RunningOrder(_world));
            _world.Tick();
            _world.EndTick();
        }

        [Test]
        public void OnSystemTeardown_SeesConvergedStateAndQueuesHandlerRegistrations()
        {
            var manager = _world.GetManager<SystemManager>();
            var observed = new List<string>();
            var registeredFromHandler = false;

            manager.OnSystemTeardown.Add(world =>
            {
                var systemManager = world.GetManager<SystemManager>();
                observed.Add(systemManager.SystemTransformer.ContainsKey(typeof(SystemA)) ? "A" : "-");

                if (registeredFromHandler) return;
                registeredFromHandler = true;
                _world.RegisterSystem<SystemB>();
            });

            _world.RegisterSystem<SystemA>();

            _world.BeginTick();
            Assert.IsNull(_world.FindSystem<SystemB>());
            _world.Tick();
            _world.EndTick();

            _world.BeginTick();
            Assert.IsNotNull(_world.FindSystem<SystemB>());
            CollectionAssert.AreEqual(new[] { "A", "A" }, observed);
            _world.Tick();
            _world.EndTick();
        }

        [Test]
        public void OnSystemCleanup_RunsAfterRemovalAndAllowsImmediateRegistration()
        {
            var manager = _world.GetManager<SystemManager>();
            var removalObserved = false;
            var immediateRegistrationObserved = false;

            _world.RegisterSystem<SystemA>();
            var system = _world.FindSystem<SystemA>();

            manager.OnSystemCleanup.Add(world =>
            {
                var systemManager = world.GetManager<SystemManager>();
                removalObserved = !systemManager.SystemTransformer.ContainsKey(typeof(SystemA))
                    && system.DestroyCount == 1;

                _world.RegisterSystem<SystemB>();
                immediateRegistrationObserved = _world.FindSystem<SystemB>() != null;
            });

            _world.BeginTick();
            _world.UnregisterSystem<SystemA>();
            _world.Tick();
            _world.EndTick();

            Assert.IsTrue(removalObserved);
            Assert.IsTrue(immediateRegistrationObserved);
            Assert.IsNull(_world.FindSystem<SystemA>());
            Assert.IsNotNull(_world.FindSystem<SystemB>());
            Assert.AreEqual(1, system.DestroyCount);
        }

        [Test]
        public void RegistrationHandles_AfterShutdown_Throw()
        {
            _world.RegisterGroup("Physics");
            var systemHandle = _world.RegisterSystem<SystemA>("Physics");
            var groupHandle = _world.RegisterGroup("Render");

            _world.Shutdown();
            _world = null!;

            var systemEx = Assert.Throws<InvalidOperationException>(() => systemHandle.After<SystemB>());
            Assert.AreEqual("SystemManager has already shutdown.", systemEx!.Message);

            var groupTypeEx = Assert.Throws<InvalidOperationException>(() => groupHandle.After<SystemB>());
            Assert.AreEqual("SystemManager has already shutdown.", groupTypeEx!.Message);

            var groupNameEx = Assert.Throws<InvalidOperationException>(() => groupHandle.After("Physics"));
            Assert.AreEqual("SystemManager has already shutdown.", groupNameEx!.Message);
        }

        private class RecordingSystem : ISystem
        {
            public int CreateCount { get; private set; }

            public int DestroyCount { get; private set; }

            public int TickCount { get; private set; }

            public void OnCreate() => CreateCount += 1;

            public virtual void OnTick(ulong tickMask)
            {
                TickCount += 1;
                ExecutionLog.Add(GetType().Name);
            }

            public void OnDestroy() => DestroyCount += 1;
        }

        private class SystemA : RecordingSystem
        {
        }

        private class SystemB : RecordingSystem
        {
        }

        private class NewSystem : RecordingSystem
        {
        }

        private class TailSystem : RecordingSystem
        {
        }

        private class LateSystem : RecordingSystem
        {
        }

        private class VictimSystem : RecordingSystem
        {
        }

        private class MutatorSystem : RecordingSystem
        {
            public override void OnTick(ulong tickMask)
            {
                base.OnTick(tickMask);
                CurrentWorld.UnregisterSystem<VictimSystem>();
                CurrentWorld.RegisterSystem<NewSystem>();
            }
        }

        private class QueueAddSystem : RecordingSystem
        {
            public override void OnTick(ulong tickMask)
            {
                base.OnTick(tickMask);
                CurrentWorld.RegisterSystem<LateSystem>();
            }
        }

        private class RebuildSystem : RecordingSystem
        {
            public override void OnTick(ulong tickMask)
            {
                base.OnTick(tickMask);
                CurrentWorld.GetManager<SystemManager>().TeardownSystems();
            }
        }
    }
}
