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

        private static World StartWorld()
        {
            var world = new World();
            world.Startup();
            return world;
        }
    }
}
