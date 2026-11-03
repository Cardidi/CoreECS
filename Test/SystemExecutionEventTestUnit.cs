using CoreECS.Defines;
using CoreECS.Managers;
using CoreECS.Utils;

namespace CoreECS.Test
{
    [TestFixture]
    public class SystemExecutionEventTestUnit
    {
        private World m_world = null!;
        private SystemManager m_manager = null!;
        private RecordingLogger m_logger = null!;

        [SetUp]
        public void SetUp()
        {
            ExecutionOrderSystem.Reset();
            NoAllocationSystem.TickCount = 0;
            TickMutationSystem.Reset();
            AddedByTickSystem.TickCount = 0;
            LifecycleBypassFirstSystem.TickCount = 0;
            LifecycleBypassSecondSystem.TickCount = 0;

            m_logger = new RecordingLogger();
            Log.Logger = m_logger;
            m_world = new World();
            m_world.Startup();
            m_manager = m_world.GetManager<SystemManager>();
        }

        [TearDown]
        public void TearDown()
        {
            if (m_world.Ready) m_world.Shutdown();
            Log.Logger = null;
        }

        [Test]
        public void ExecuteSystems_EmitsBeginBeforeTickAndEndAfterTick()
        {
            m_world.RegisterSystem<ExecutionOrderSystem>();
            m_manager.OnSystemBeginExecute.Add((world, system) =>
            {
                Assert.That(world, Is.SameAs(m_world));
                Assert.That(system, Is.SameAs(m_world.FindSystem<ExecutionOrderSystem>()));
                ExecutionOrderSystem.Calls.Add("begin");
            });
            m_manager.OnSystemEndExecute.Add((world, system) =>
            {
                Assert.That(world, Is.SameAs(m_world));
                Assert.That(system, Is.SameAs(m_world.FindSystem<ExecutionOrderSystem>()));
                ExecutionOrderSystem.Calls.Add("end");
            });

            RunTick();

            Assert.That(ExecutionOrderSystem.Calls,
                Is.EqualTo(new[] { "begin", "tick", "end" }));
        }

        [Test]
        public void ExecuteSystems_WithoutObservers_DoesNotAllocate()
        {
            m_world.RegisterSystem<NoAllocationSystem>();
            m_world.BeginTick();

            m_manager.ExecuteSystems(ulong.MaxValue);
            long before = GC.GetAllocatedBytesForCurrentThread();

            for (int i = 0; i < 1024; i++)
                m_manager.ExecuteSystems(ulong.MaxValue);

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            m_world.EndTick();

            Assert.That(allocated, Is.Zero);
            Assert.That(NoAllocationSystem.TickCount, Is.EqualTo(1025));
        }

        [Test]
        public void ObserverExceptions_AreLoggedAndDoNotSkipSystemExecution()
        {
            var beginException = new InvalidOperationException("begin failed");
            var endException = new InvalidOperationException("end failed");
            m_world.RegisterSystem<NoAllocationSystem>();
            m_manager.OnSystemBeginExecute.Add((_, _) => throw beginException);
            m_manager.OnSystemEndExecute.Add((_, _) => throw endException);

            Assert.DoesNotThrow(RunTick);

            Assert.That(NoAllocationSystem.TickCount, Is.EqualTo(1));
            Assert.That(m_logger.Exceptions,
                Is.EqualTo(new Exception[] { beginException, endException }));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ObserverCallbacks_RejectEveryDagMutationWithoutChangingGraph(bool begin)
        {
            GroupRegistration group = m_world.RegisterGroup("Existing");
            SystemRegistration observed = m_world.RegisterSystem<ObservedSystem>("Existing");
            m_world.RegisterSystem<MutationTargetSystem>();

            AddObserver(begin, () => m_world.RegisterSystem<AddedFromObserverSystem>());
            AddObserver(begin, () => m_world.UnregisterSystem<MutationTargetSystem>());
            AddObserver(begin, () => m_world.RegisterGroup("AddedFromObserver"));
            AddObserver(begin, () => observed.After<MutationTargetSystem>());
            AddObserver(begin, () => group.Before<MutationTargetSystem>());

            RunTick();

            Assert.That(m_logger.Exceptions, Has.Count.EqualTo(5));
            Assert.That(m_logger.Exceptions,
                Has.All.TypeOf<InvalidOperationException>());
            Assert.That(m_manager.Schedule.FindSystem(typeof(AddedFromObserverSystem)), Is.Null);
            Assert.That(m_manager.Schedule.FindSystem(typeof(MutationTargetSystem)), Is.Not.Null);
            Assert.That(m_manager.Schedule.FindGroup("AddedFromObserver"), Is.Null);
            Assert.That(m_manager.Schedule.FindSystem(typeof(ObservedSystem))!.Anchors, Is.Empty);
            Assert.That(m_manager.Schedule.FindGroup("Existing")!.Anchors, Is.Empty);
        }

        [Test]
        public void OnTick_CanMutatePendingGraphForNextTick()
        {
            m_world.RegisterSystem<TickMutationSystem>();

            RunTick();

            Assert.That(m_manager.Schedule.FindSystem(typeof(AddedByTickSystem)), Is.Not.Null);
            Assert.That(m_world.FindSystem<AddedByTickSystem>(), Is.Null);
            Assert.That(m_logger.Exceptions, Is.Empty);

            RunTick();

            Assert.That(m_world.FindSystem<AddedByTickSystem>(), Is.Not.Null);
            Assert.That(AddedByTickSystem.TickCount, Is.EqualTo(1));
            Assert.That(m_logger.Exceptions, Is.Empty);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ObserverCallback_CannotInvokeManagerLifecycleToClearCurrentCache(bool begin)
        {
            m_world.RegisterSystem<LifecycleBypassFirstSystem>();
            m_world.RegisterSystem<LifecycleBypassSecondSystem>();
            bool attempted = false;

            AddObserver(begin, () =>
            {
                if (attempted) return;
                attempted = true;
                m_manager.OnWorldEnded();
            });

            RunTick();

            Assert.That(LifecycleBypassFirstSystem.TickCount, Is.EqualTo(1));
            Assert.That(LifecycleBypassSecondSystem.TickCount, Is.EqualTo(1));
            Assert.That(m_manager.Schedule.FindSystem(
                typeof(LifecycleBypassFirstSystem)), Is.Not.Null);
            Assert.That(m_manager.Schedule.FindSystem(
                typeof(LifecycleBypassSecondSystem)), Is.Not.Null);
            Assert.That(m_logger.Exceptions, Has.Count.EqualTo(1));
            Assert.That(m_logger.Exceptions[0], Is.TypeOf<InvalidOperationException>());

            RunTick();

            Assert.That(LifecycleBypassFirstSystem.TickCount, Is.EqualTo(2));
            Assert.That(LifecycleBypassSecondSystem.TickCount, Is.EqualTo(2));
        }

        [Test]
        public void SystemManager_DoesNotExposeTeardownOrCleanupObservationSignals()
        {
            Assert.That(typeof(SystemManager).GetProperty("OnSystemTeardown"), Is.Null);
            Assert.That(typeof(SystemManager).GetProperty("OnSystemCleanup"), Is.Null);
            Assert.That(typeof(SystemManager).Assembly.GetType("CoreECS.Managers.SystemTeardown"),
                Is.Null);
            Assert.That(typeof(SystemManager).Assembly.GetType("CoreECS.Managers.SystemCleanup"),
                Is.Null);
        }

        private void AddObserver(bool begin, Action mutation)
        {
            if (begin)
                m_manager.OnSystemBeginExecute.Add((_, _) => mutation());
            else
                m_manager.OnSystemEndExecute.Add((_, _) => mutation());
        }

        private void RunTick()
        {
            m_world.BeginTick();
            m_world.Tick();
            m_world.EndTick();
        }

        private sealed class ExecutionOrderSystem : ISystem
        {
            internal static List<string> Calls { get; } = new List<string>();

            internal static void Reset() => Calls.Clear();
            public void OnCreate() { }
            public void OnTick(ulong tickMask) => Calls.Add("tick");
            public void OnDestroy() { }
        }

        private sealed class NoAllocationSystem : ISystem
        {
            internal static int TickCount;

            public void OnCreate() { }
            public void OnTick(ulong tickMask) => TickCount++;
            public void OnDestroy() { }
        }

        private sealed class ObservedSystem : ISystem
        {
            public void OnCreate() { }
            public void OnTick(ulong tickMask) { }
            public void OnDestroy() { }
        }

        private sealed class MutationTargetSystem : ISystem
        {
            public ulong TickGroup => 0;
            public void OnCreate() { }
            public void OnTick(ulong tickMask) { }
            public void OnDestroy() { }
        }

        private sealed class AddedFromObserverSystem : ISystem
        {
            public void OnCreate() { }
            public void OnTick(ulong tickMask) { }
            public void OnDestroy() { }
        }

        private sealed class TickMutationSystem : ISystem
        {
            private readonly World m_world;
            private static bool s_registered;

            public TickMutationSystem(World world)
            {
                m_world = world;
            }

            internal static void Reset() => s_registered = false;
            public void OnCreate() { }

            public void OnTick(ulong tickMask)
            {
                if (s_registered) return;
                s_registered = true;
                m_world.RegisterSystem<AddedByTickSystem>();
            }

            public void OnDestroy() { }
        }

        private sealed class AddedByTickSystem : ISystem
        {
            internal static int TickCount;

            public void OnCreate() { }
            public void OnTick(ulong tickMask) => TickCount++;
            public void OnDestroy() { }
        }

        private sealed class LifecycleBypassFirstSystem : ISystem
        {
            internal static int TickCount;
            public void OnCreate() { }
            public void OnTick(ulong tickMask) => TickCount++;
            public void OnDestroy() { }
        }

        private sealed class LifecycleBypassSecondSystem : ISystem
        {
            internal static int TickCount;
            public void OnCreate() { }
            public void OnTick(ulong tickMask) => TickCount++;
            public void OnDestroy() { }
        }

        private sealed class RecordingLogger : ILogger
        {
            internal List<Exception> Exceptions { get; } = new List<Exception>();

            public void Debug(string msg, object? context = null) { }
            public void Info(string msg, object? context = null) { }
            public void Warn(string msg, object? context = null) { }
            public void Err(string msg, object? context = null) { }
            public void Exp(Exception err, object? context = null) => Exceptions.Add(err);
        }
    }
}
