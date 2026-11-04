using CoreECS.Defines;
using Microsoft.Extensions.DependencyInjection;

namespace CoreECS.Test
{
    [TestFixture]
    public class WorldStateMachineTestUnit
    {
        [Test]
        public void DirectWorld_NormalCycleTracksStateAndTickCount()
        {
            var world = new World();

            Assert.Multiple(() =>
            {
                Assert.That(world.Ready, Is.False);
                Assert.That(world.Ticking, Is.False);
                Assert.That(world.TickCount, Is.Zero);
            });

            world.Startup();
            Assert.Multiple(() =>
            {
                Assert.That(world.Ready, Is.True);
                Assert.That(world.Ticking, Is.False);
                Assert.That(world.TickCount, Is.Zero);
            });

            world.BeginTick();
            Assert.Multiple(() =>
            {
                Assert.That(world.Ready, Is.True);
                Assert.That(world.Ticking, Is.True);
                Assert.That(world.TickCount, Is.EqualTo(1));
            });

            world.Tick();
            world.EndTick();
            Assert.Multiple(() =>
            {
                Assert.That(world.Ready, Is.True);
                Assert.That(world.Ticking, Is.False);
                Assert.That(world.TickCount, Is.EqualTo(1));
            });

            world.Shutdown();
            Assert.Multiple(() =>
            {
                Assert.That(world.Ready, Is.False);
                Assert.That(world.Ticking, Is.False);
                Assert.That(world.TickCount, Is.EqualTo(1));
            });
        }

        [Test]
        public void Startup_WhenAlreadyStartedThrowsWithoutChangingState()
        {
            var world = StartWorld();
            try
            {
                Assert.Throws<InvalidOperationException>(world.Startup);
                Assert.Multiple(() =>
                {
                    Assert.That(world.Ready, Is.True);
                    Assert.That(world.Ticking, Is.False);
                    Assert.That(world.TickCount, Is.Zero);
                });
            }
            finally
            {
                world.Shutdown();
            }
        }

        [Test]
        public void Shutdown_BeforeStartupThrowsWithoutChangingState()
        {
            var world = new World();

            Assert.Throws<InvalidOperationException>(world.Shutdown);
            Assert.Multiple(() =>
            {
                Assert.That(world.Ready, Is.False);
                Assert.That(world.Ticking, Is.False);
                Assert.That(world.TickCount, Is.Zero);
            });
        }

        [Test]
        public void BeginTick_WhenAlreadyTickingThrowsWithoutChangingState()
        {
            var world = StartWorld();
            try
            {
                world.BeginTick();

                Assert.Throws<InvalidOperationException>(world.BeginTick);
                Assert.Multiple(() =>
                {
                    Assert.That(world.Ready, Is.True);
                    Assert.That(world.Ticking, Is.True);
                    Assert.That(world.TickCount, Is.EqualTo(1));
                });

                world.EndTick();
            }
            finally
            {
                world.Shutdown();
            }
        }

        [Test]
        public void Tick_WithoutBeginTickThrowsWithoutChangingState()
        {
            var world = StartWorld();
            try
            {
                Assert.Throws<InvalidOperationException>(() => world.Tick());
                Assert.Multiple(() =>
                {
                    Assert.That(world.Ready, Is.True);
                    Assert.That(world.Ticking, Is.False);
                    Assert.That(world.TickCount, Is.Zero);
                });
            }
            finally
            {
                world.Shutdown();
            }
        }

        [Test]
        public void EndTick_WithoutBeginTickThrowsWithoutChangingState()
        {
            var world = StartWorld();
            try
            {
                Assert.Throws<InvalidOperationException>(world.EndTick);
                Assert.Multiple(() =>
                {
                    Assert.That(world.Ready, Is.True);
                    Assert.That(world.Ticking, Is.False);
                    Assert.That(world.TickCount, Is.Zero);
                });
            }
            finally
            {
                world.Shutdown();
            }
        }

        [Test]
        public void Shutdown_WhileTickingThrowsWithoutChangingState()
        {
            var world = StartWorld();
            world.BeginTick();

            Assert.Throws<InvalidOperationException>(world.Shutdown);
            Assert.Multiple(() =>
            {
                Assert.That(world.Ready, Is.True);
                Assert.That(world.Ticking, Is.True);
                Assert.That(world.TickCount, Is.EqualTo(1));
            });

            world.EndTick();
            world.Shutdown();
        }

        [Test]
        public void EndTick_CalledFromSystemOnTickIsRejectedWithoutEndingTheTick()
        {
            var world = new ReentrantEndTickWorld();
            world.Startup();
            world.BeginTick();

            Assert.DoesNotThrow(() => world.Tick());
            Assert.Multiple(() =>
            {
                Assert.That(world.Ticking, Is.True);
                Assert.That(world.Ready, Is.True);
            });
            Assert.DoesNotThrow(world.EndTick);

            world.Shutdown();
        }

        [Test]
        public void Startup_CalledFromOnRegisterIsRejectedAndCanBeRetried()
        {
            var world = new ReentrantStartupWorld();
            Assert.Throws<InvalidOperationException>(() => world.Startup());
            Assert.That(world.Ready, Is.False);

            world.Startup();
            Assert.That(world.Ready, Is.True);
            world.Shutdown();
        }

        [Test]
        public void SetupAndCleanup_CannotReenterLifecycle()
        {
            var world = new ReentrantSetupCleanupWorld();
            world.Startup();
            Assert.That(world.SetupBlocked, Is.True);

            world.Shutdown();
            Assert.That(world.CleanupBlocked, Is.True);
        }

        [Test]
        public void Shutdown_WhenAlreadyShutdownThrowsWithoutChangingState()
        {
            var world = StartWorld();
            world.Shutdown();

            Assert.Throws<InvalidOperationException>(world.Shutdown);
            Assert.Multiple(() =>
            {
                Assert.That(world.Ready, Is.False);
                Assert.That(world.Ticking, Is.False);
                Assert.That(world.TickCount, Is.Zero);
            });
        }

        private sealed class ReentrantEndTickWorld : World
        {
            protected override void OnSetup()
            {
                RegisterSystem<EndTickCallerSystem>();
            }
        }

        private sealed class EndTickCallerSystem : CoreECS.Defines.ISystem
        {
            private readonly IWorld m_world;

            public EndTickCallerSystem(IWorld world)
            {
                m_world = world;
            }

            public void OnCreate() { }
            public void OnTick(ulong tickMask) => m_world.EndTick();
            public void OnDestroy() { }
        }

        private sealed class ReentrantStartupWorld : World
        {
            private bool m_tried;

            protected override void OnRegister(
                IManagerRegister managers,
                IServiceCollection services)
            {
                if (m_tried) return;
                m_tried = true;
                Startup();
            }
        }

        private sealed class ReentrantSetupCleanupWorld : World
        {
            internal bool SetupBlocked { get; private set; }
            internal bool CleanupBlocked { get; private set; }

            protected override void OnSetup()
            {
                try
                {
                    BeginTick();
                }
                catch (InvalidOperationException)
                {
                    SetupBlocked = true;
                }
            }

            protected override void OnCleanup()
            {
                try
                {
                    Shutdown();
                }
                catch (InvalidOperationException)
                {
                    CleanupBlocked = true;
                }
            }
        }

        private static World StartWorld()
        {
            var world = new World();
            world.Startup();
            return world;
        }
    }
}
