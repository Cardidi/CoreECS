using CoreECS.Defines;
using CoreECS.Managers;

namespace CoreECS.Test
{
    [TestFixture]
    public class ManagerLifecycleWiringTestUnit
    {
        private struct Position : IComponent<Position> { public int X; }

        [Test]
        public void Startup_WiresMatcherSink_CollectorsSeeStructuralEvents()
        {
            var world = new World();
            world.Startup();

            var entity = world.CreateEntity();
            var collector = world.CreateCollector(EntityMatcher.With.OfAll<Position>());
            collector.Flush();

            entity.CreateComponent<Position>();
            collector.Flush();

            Assert.AreEqual(1, collector.Matching.Count);
            world.Shutdown();
        }

        [Test]
        public void Shutdown_WithLiveCollector_DoesNotThrow()
        {
            var world = new World();
            world.Startup();
            var collector = world.CreateCollector(EntityMatcher.With.OfAll<Position>());
            var entity = world.CreateEntity();
            entity.CreateComponent<Position>();
            collector.Flush();

            Assert.DoesNotThrow(() => world.Shutdown());
        }
    }
}
