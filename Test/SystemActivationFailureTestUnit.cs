using CoreECS.Defines;
using CoreECS.Managers;
using CoreECS.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace CoreECS.Test
{
    [TestFixture]
    public sealed class SystemActivationFailureTestUnit
    {
        private World m_world = null!;
        private RecordingLogger m_logger = null!;
        private FaultInjectingProxy m_proxy = null!;
        private SystemManager m_manager = null!;

        [SetUp]
        public void SetUp()
        {
            ExecutionLog.Clear();
            PendingBeforeFailureSystem.Reset();
            PendingAfterFailureSystem.Reset();
            RemovedBeforeFailureSystem.Reset();
            m_logger = new RecordingLogger();
            Log.Logger = m_logger;
            m_world = new World();
            m_world.Startup();
            m_proxy = new FaultInjectingProxy(typeof(FailingActivationSystem));
            m_manager = new SystemManager(m_world, m_proxy);
            m_manager.OnManagerCreated();
            m_manager.OnWorldStarted();
        }

        [TearDown]
        public void TearDown()
        {
            m_manager.OnWorldEnded();
            m_manager.OnManagerDestroyed();
            if (m_world.Ready) m_world.Shutdown();
            Log.Logger = null;
        }

        [Test]
        public void TeardownSystems_WhenMiddleActivationFails_PreservesCacheAndRetriesAtomically()
        {
            m_manager.RegisterSystem(typeof(CachedFirstSystem));
            m_manager.RegisterSystem(typeof(CachedLastSystem));
            m_manager.TeardownSystems();
            m_manager.ExecuteSystems(ulong.MaxValue);
            m_manager.CleanupSystems();

            Assert.That(ExecutionLog.Items, Is.EqualTo(new[] { "cached-first", "cached-last" }));
            Assert.That(m_manager.ExecutionCacheCopyCount, Is.EqualTo(1));
            ExecutionLog.Clear();

            m_manager.TeardownSystems();
            m_manager.RegisterSystem(typeof(PendingBeforeFailureSystem))
                .Before<CachedLastSystem>();
            m_manager.RegisterSystem(typeof(FailingActivationSystem))
                .After<PendingBeforeFailureSystem>()
                .Before<CachedLastSystem>();
            m_manager.RegisterSystem(typeof(PendingAfterFailureSystem))
                .After<FailingActivationSystem>()
                .Before<CachedLastSystem>();
            m_manager.CleanupSystems();

            Assert.DoesNotThrow(m_manager.TeardownSystems);

            Assert.That(m_logger.Exceptions, Has.Count.EqualTo(1));
            Assert.That(m_logger.Exceptions[0], Is.TypeOf<ActivationFailureException>());
            Assert.That(m_manager.Schedule.IsDirty, Is.True);
            Assert.That(m_manager.ExecutionCacheCopyCount, Is.EqualTo(1));
            Assert.That(PendingBeforeFailureSystem.CreateCount, Is.EqualTo(1));
            Assert.That(PendingBeforeFailureSystem.DestroyCount, Is.EqualTo(1));
            Assert.That(PendingAfterFailureSystem.CreateCount, Is.Zero);
            Assert.That(m_manager.SystemTransformer.ContainsKey(
                typeof(PendingBeforeFailureSystem)), Is.False);

            m_manager.ExecuteSystems(ulong.MaxValue);
            m_manager.CleanupSystems();

            Assert.That(ExecutionLog.Items, Is.EqualTo(new[] { "cached-first", "cached-last" }));
            ExecutionLog.Clear();

            m_manager.TeardownSystems();
            m_manager.ExecuteSystems(ulong.MaxValue);
            m_manager.CleanupSystems();

            Assert.That(m_manager.Schedule.IsDirty, Is.False);
            Assert.That(m_manager.ExecutionCacheCopyCount, Is.EqualTo(2));
            Assert.That(PendingBeforeFailureSystem.CreateCount, Is.EqualTo(2));
            Assert.That(PendingBeforeFailureSystem.DestroyCount, Is.EqualTo(1));
            Assert.That(PendingAfterFailureSystem.CreateCount, Is.EqualTo(1));
            Assert.That(ExecutionLog.Items, Is.EqualTo(new[]
            {
                "cached-first", "pending-before", "recovered", "pending-after", "cached-last",
            }));
        }

        [Test]
        public void TeardownSystems_WhenActivationFailsAfterRemoval_FallbackContainsOnlyLiveSystems()
        {
            m_manager.RegisterSystem(typeof(RemovedBeforeFailureSystem));
            m_manager.RegisterSystem(typeof(CachedLastSystem));
            m_manager.TeardownSystems();
            m_manager.CleanupSystems();
            Assert.That(m_manager.ExecutionCacheCopyCount, Is.EqualTo(1));

            m_manager.TeardownSystems();
            m_manager.UnregisterSystem(typeof(RemovedBeforeFailureSystem));
            m_manager.RegisterSystem(typeof(FailingActivationSystem))
                .After<CachedLastSystem>();
            m_manager.ExecuteSystems(ulong.MaxValue);
            m_manager.CleanupSystems();

            Assert.That(RemovedBeforeFailureSystem.DestroyCount, Is.EqualTo(1));
            ExecutionLog.Clear();

            Assert.DoesNotThrow(m_manager.TeardownSystems);
            Assert.That(m_manager.Schedule.IsDirty, Is.True);
            Assert.That(m_manager.ExecutionCacheCopyCount, Is.EqualTo(1));

            m_manager.ExecuteSystems(ulong.MaxValue);
            m_manager.CleanupSystems();

            Assert.That(ExecutionLog.Items, Is.EqualTo(new[] { "cached-last" }));
            Assert.That(RemovedBeforeFailureSystem.TickAfterDestroyCount, Is.Zero);
            ExecutionLog.Clear();

            m_manager.TeardownSystems();
            m_manager.ExecuteSystems(ulong.MaxValue);
            m_manager.CleanupSystems();

            Assert.That(m_manager.Schedule.IsDirty, Is.False);
            Assert.That(m_manager.ExecutionCacheCopyCount, Is.EqualTo(2));
            Assert.That(ExecutionLog.Items,
                Is.EqualTo(new[] { "cached-last", "recovered" }));
        }

        private static class ExecutionLog
        {
            internal static List<string> Items { get; } = new List<string>();
            internal static void Clear() => Items.Clear();
        }

        private abstract class RecordingSystem : ISystem
        {
            private readonly string m_name;

            protected RecordingSystem(string name)
            {
                m_name = name;
            }

            public virtual void OnCreate() { }
            public void OnTick(ulong tickMask) => ExecutionLog.Items.Add(m_name);
            public virtual void OnDestroy() { }
        }

        private sealed class CachedFirstSystem : RecordingSystem
        {
            public CachedFirstSystem() : base("cached-first") { }
        }

        private sealed class CachedLastSystem : RecordingSystem
        {
            public CachedLastSystem() : base("cached-last") { }
        }

        private sealed class RemovedBeforeFailureSystem : ISystem
        {
            private bool m_destroyed;
            internal static int DestroyCount { get; private set; }
            internal static int TickAfterDestroyCount { get; private set; }

            internal static void Reset()
            {
                DestroyCount = 0;
                TickAfterDestroyCount = 0;
            }

            public void OnCreate() { }
            public void OnTick(ulong tickMask)
            {
                if (m_destroyed) TickAfterDestroyCount++;
                ExecutionLog.Items.Add("removed");
            }
            public void OnDestroy()
            {
                m_destroyed = true;
                DestroyCount++;
            }
        }

        private sealed class PendingBeforeFailureSystem : RecordingSystem
        {
            internal static int CreateCount { get; private set; }
            internal static int DestroyCount { get; private set; }

            public PendingBeforeFailureSystem() : base("pending-before") { }
            internal static void Reset()
            {
                CreateCount = 0;
                DestroyCount = 0;
            }
            public override void OnCreate() => CreateCount++;
            public override void OnDestroy() => DestroyCount++;
        }

        private sealed class FailingActivationSystem : RecordingSystem
        {
            public FailingActivationSystem() : base("recovered") { }
        }

        private sealed class PendingAfterFailureSystem : RecordingSystem
        {
            internal static int CreateCount { get; private set; }

            public PendingAfterFailureSystem() : base("pending-after") { }
            internal static void Reset() => CreateCount = 0;
            public override void OnCreate() => CreateCount++;
        }

        private sealed class FaultInjectingProxy : IInjectionProxy
        {
            private readonly Type m_failureType;
            private int m_failuresRemaining = 1;

            public IServiceProvider ServiceProvider { get; } =
                new ServiceCollection().BuildServiceProvider();

            internal FaultInjectingProxy(Type failureType)
            {
                m_failureType = failureType;
            }

            public object CreateObject(Type objectType)
            {
                if (objectType == m_failureType && m_failuresRemaining-- > 0)
                    throw new ActivationFailureException();

                return Activator.CreateInstance(objectType, true)!;
            }

            public T CreateObject<T>() => (T)CreateObject(typeof(T));
        }

        private sealed class ActivationFailureException : Exception { }

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
