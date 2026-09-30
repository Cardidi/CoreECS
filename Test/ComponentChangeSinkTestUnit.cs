using CoreECS.Defines;
using CoreECS.Managers;

namespace CoreECS.Test
{
    [TestFixture]
    public class ComponentChangeSinkTestUnit
    {
        private World _world;

        [SetUp]
        public void Setup()
        {
            _world = new World();
            _world.Startup();
        }

        [TearDown]
        public void TearDown() => _world?.Shutdown();

        private struct Position : IComponent<Position> { public int X; }

        [Test]
        public void ComponentManager_NoRevisionListeners_MeansNoChangeInterest()
        {
            var component = _world.GetManager<ComponentManager>();
            Assert.IsFalse(component.HasChangeInterest, "no revision listeners means no change interest");

            ComponentChanged handler = (_, _) => { };
            component.OnComponentChanged += handler;
            Assert.IsTrue(component.HasChangeInterest);

            component.OnComponentChanged -= handler;
            Assert.IsFalse(component.HasChangeInterest);
        }

        [Test]
        public void RevisionChange_UserSubscribed_StillReceivesEvent()
        {
            var component = _world.GetManager<ComponentManager>();
            var received = 0;
            component.OnComponentChanged += (_, _) => received += 1;

            var entity = _world.CreateEntity();
            var position = entity.CreateComponent<Position>();
            position.RW.X = 1;

            Assert.AreEqual(1, received);
        }

        [Test]
        public void RevisionChange_CollectorStillTracksChanged()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<Position>();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<Position>(),
                EntityCollectorFlag.RevisionAsChange);
            collector.Flush();
            collector.Flush();

            entity.GetComponent<Position>().RW.X = 5;
            collector.Flush();

            Assert.AreEqual(1, collector.Changed.Count);
            Assert.AreEqual(entity.EntityId, collector.Changed[0]);
        }
    }
}
