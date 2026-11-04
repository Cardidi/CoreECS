using CoreECS.Defines;
using Microsoft.Extensions.DependencyInjection;

namespace CoreECS.Test
{
    [TestFixture]
    public class WorldRestartTestUnit
    {
        [Test]
        public void Restart_ReusesManagersAndProxyAndRunsEachCycleHooks()
        {
            var counters = new RestartCounters();
            var world = new RestartWorld(counters);

            world.Startup();
            IInjectionProxy firstProxy = world.InjectionProxy!;
            var firstManager = world.GetManager<RestartManager>();

            Assert.Multiple(() =>
            {
                Assert.That(counters.RegisterCount, Is.EqualTo(1));
                Assert.That(counters.SetupCount, Is.EqualTo(1));
                Assert.That(firstManager.CreatedCount, Is.EqualTo(1));
                Assert.That(firstManager.StartedCount, Is.EqualTo(1));
                Assert.That(counters.SystemCreatedCount, Is.EqualTo(1));
            });

            world.BeginTick();
            world.Tick();
            world.EndTick();
            Assert.That(counters.SystemTickCount, Is.EqualTo(1));

            world.Shutdown();

            Assert.Multiple(() =>
            {
                Assert.That(counters.CleanupCount, Is.EqualTo(1));
                Assert.That(firstManager.EndedCount, Is.EqualTo(1));
                Assert.That(firstManager.DestroyedCount, Is.EqualTo(1));
                Assert.That(counters.SystemDestroyedCount, Is.EqualTo(1));
            });

            world.Startup();

            Assert.Multiple(() =>
            {
                Assert.That(counters.RegisterCount, Is.EqualTo(1));
                Assert.That(counters.SetupCount, Is.EqualTo(2));
                Assert.That(world.GetManager<RestartManager>(), Is.SameAs(firstManager));
                Assert.That(world.InjectionProxy, Is.SameAs(firstProxy));
                Assert.That(firstManager.CreatedCount, Is.EqualTo(2));
                Assert.That(firstManager.StartedCount, Is.EqualTo(2));
                Assert.That(firstManager.EndedCount, Is.EqualTo(1));
                Assert.That(firstManager.DestroyedCount, Is.EqualTo(1));
                Assert.That(counters.SystemCreatedCount, Is.EqualTo(2));
                Assert.That(counters.SystemDestroyedCount, Is.EqualTo(1));
            });

            world.BeginTick();
            world.Tick();
            world.EndTick();
            Assert.That(counters.SystemTickCount, Is.EqualTo(2));

            world.Shutdown();
            Assert.Multiple(() =>
            {
                Assert.That(counters.CleanupCount, Is.EqualTo(2));
                Assert.That(firstManager.EndedCount, Is.EqualTo(2));
                Assert.That(firstManager.DestroyedCount, Is.EqualTo(2));
                Assert.That(counters.SystemDestroyedCount, Is.EqualTo(2));
            });
        }

        [Test]
        public void Restart_ClearsGroupsAnchorsAndInvalidatesRegistrationHandles()
        {
            var counters = new RestartCounters();
            var world = new RestartWorld(counters);

            world.Startup();
            SystemRegistration? firstHandle = counters.LastSystemRegistration;
            Assert.That(firstHandle, Is.Not.Null);

            world.Shutdown();
            world.Startup();

            Assert.Multiple(() =>
            {
                Assert.That(counters.GroupRegisteredCount, Is.EqualTo(2));
                Assert.That(counters.LastSystemRegistration, Is.Not.SameAs(firstHandle));
            });

            Assert.Throws<InvalidOperationException>(() => firstHandle!.Before<RestartSystem>());

            world.Shutdown();
        }

        [Test]
        public void Restart_ClearsEntitiesStoragesCollectorsAndPendingChanges()
        {
            var counters = new RestartCounters();
            var world = new RestartWorld(counters);
            world.Startup();

            Entity entity = world.CreateEntity();
            entity.CreateComponent(new RestartPosition { X = 7 });
            EntityCollector collector = world.CreateCollector(
                EntityMatcher.With.OfAll<RestartPosition>());
            collector.Flush();
            entity.GetComponent<RestartPosition>().RW.X = 9;
            world.DestroyEntity(entity);

            world.Shutdown();
            Assert.Throws<ObjectDisposedException>(() => collector.Flush());
            Assert.DoesNotThrow(collector.Dispose);
            Assert.DoesNotThrow(collector.Dispose);

            world.Startup();

            Entity restarted = world.CreateEntity();
            restarted.CreateComponent(new RestartPosition { X = 3 });

            Assert.Multiple(() =>
            {
                Assert.That(restarted.IsValid, Is.True);
                Assert.That(restarted.HasComponent<RestartPosition>(), Is.True);
                Assert.That(restarted.GetComponent<RestartPosition>().RO.X, Is.EqualTo(3));
            });

            world.Shutdown();
        }

        [Test]
        public void Restart_RollsBackLaterSetupFailureAndAllowsRetry()
        {
            var counters = new RestartCounters();
            var world = new RestartWorld(counters);
            world.Startup();
            IInjectionProxy proxy = world.InjectionProxy!;
            var manager = world.GetManager<RestartManager>();
            world.Shutdown();

            counters.FailNextSetup = true;
            Assert.Throws<InvalidOperationException>(() => world.Startup());

            Assert.Multiple(() =>
            {
                Assert.That(world.Ready, Is.False);
                Assert.That(counters.SetupCount, Is.EqualTo(2));
                Assert.That(counters.CleanupCount, Is.EqualTo(1));
                Assert.That(manager.EndedCount, Is.EqualTo(2));
                Assert.That(manager.DestroyedCount, Is.EqualTo(2));
                Assert.That(counters.SystemCreatedCount, Is.EqualTo(2));
                Assert.That(counters.SystemDestroyedCount, Is.EqualTo(2));
            });

            counters.FailNextSetup = false;
            world.Startup();

            Assert.Multiple(() =>
            {
                Assert.That(world.Ready, Is.True);
                Assert.That(world.InjectionProxy, Is.SameAs(proxy));
                Assert.That(world.GetManager<RestartManager>(), Is.SameAs(manager));
                Assert.That(counters.RegisterCount, Is.EqualTo(1));
                Assert.That(counters.SetupCount, Is.EqualTo(3));
                Assert.That(manager.CreatedCount, Is.EqualTo(3));
                Assert.That(manager.StartedCount, Is.EqualTo(3));
                Assert.That(counters.SystemCreatedCount, Is.EqualTo(3));
                Assert.That(counters.SystemDestroyedCount, Is.EqualTo(2));
            });

            world.Shutdown();
        }

        [Test]
        public void Restart_InvalidatesComponentRefsFromPreviousCycle()
        {
            var counters = new RestartCounters();
            var world = new RestartWorld(counters);
            world.Startup();

            Entity entity = world.CreateEntity();
            ComponentRef<RestartPosition> stale = entity.CreateComponent(
                new RestartPosition { X = 5 });

            world.Shutdown();
            world.Startup();

            Entity replacement = world.CreateEntity();
            replacement.CreateComponent(new RestartPosition { X = 9 });

            Assert.Multiple(() =>
            {
                Assert.That(stale.NotNull, Is.False);
                Assert.Throws<InvalidOperationException>(() => _ = stale.RO.X);
            });

            world.Shutdown();
        }

        [Test]
        public void Restart_FirstSuccessfulCycleRepeatsWithoutLeakingEntityIds()
        {
            var counters = new RestartCounters();
            var world = new RestartWorld(counters);

            for (int cycle = 0; cycle < 3; cycle++)
            {
                world.Startup();
                Entity entity = world.CreateEntity();
                entity.CreateComponent(new RestartPosition { X = cycle });
                Assert.That(entity.IsValid, Is.True);
                world.BeginTick();
                world.Tick();
                world.EndTick();
                world.Shutdown();
            }

            Assert.Multiple(() =>
            {
                Assert.That(counters.RegisterCount, Is.EqualTo(1));
                Assert.That(counters.SetupCount, Is.EqualTo(3));
                Assert.That(counters.CleanupCount, Is.EqualTo(3));
                Assert.That(counters.SystemCreatedCount, Is.EqualTo(3));
                Assert.That(counters.SystemDestroyedCount, Is.EqualTo(3));
            });
        }

        private sealed class RestartCounters
        {
            internal int RegisterCount;
            internal int SetupCount;
            internal int CleanupCount;
            internal int GroupRegisteredCount;
            internal int SystemCreatedCount;
            internal int SystemDestroyedCount;
            internal int SystemTickCount;
            internal bool FailNextSetup;
            internal SystemRegistration? LastSystemRegistration;
        }

        private sealed class RestartWorld : World
        {
            private readonly RestartCounters m_counters;

            internal RestartWorld(RestartCounters counters)
            {
                m_counters = counters;
            }

            protected override void OnRegister(IManagerRegister managers,
                IServiceCollection services)
            {
                m_counters.RegisterCount++;
                services.AddSingleton(m_counters);
                managers.RegisterManager<RestartManager>();
            }

            protected override void OnSetup()
            {
                m_counters.SetupCount++;
                RegisterGroup("Gameplay");
                m_counters.GroupRegisteredCount++;

                m_counters.LastSystemRegistration =
                    RegisterSystem<RestartSystem>("Gameplay");

                if (m_counters.FailNextSetup)
                {
                    m_counters.FailNextSetup = false;
                    throw new InvalidOperationException("Setup failed by request.");
                }
            }

            protected override void OnCleanup()
            {
                m_counters.CleanupCount++;
            }
        }

        private sealed class RestartManager : IWorldManager
        {
            internal int CreatedCount;
            internal int StartedCount;
            internal int EndedCount;
            internal int DestroyedCount;

            public void OnManagerCreated() => CreatedCount++;

            public void OnWorldStarted() => StartedCount++;

            public void OnWorldEnded() => EndedCount++;

            public void OnManagerDestroyed() => DestroyedCount++;
        }

        private struct RestartPosition : IComponent<RestartPosition>
        {
            public int X;

            public void OnCreate(EntityId entityId) { }

            public void OnDestroy(EntityId entityId) { }
        }

        private sealed class RestartSystem : ISystem
        {
            private readonly RestartCounters m_counters;

            public RestartSystem(RestartCounters counters)
            {
                m_counters = counters;
            }

            public void OnCreate() => m_counters.SystemCreatedCount++;

            public void OnTick(ulong tickMask) => m_counters.SystemTickCount++;

            public void OnDestroy() => m_counters.SystemDestroyedCount++;
        }
    }
}
