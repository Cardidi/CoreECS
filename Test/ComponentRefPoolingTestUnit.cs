using CoreECS.Defines;
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class ComponentRefPoolingTestUnit
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
        private struct Velocity : IComponent<Velocity> { public int X; }
        private struct Mana : ISparseComponent<Mana> { public int Value; }

        [Test]
        public void GetComponent_ReturnsSameCoreAcrossCalls()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<Position>().RW.X = 1;

            var first = entity.GetComponent<Position>();
            var second = entity.GetComponent<Position>();

            Assert.AreSame(first.Core, second.Core);
            Assert.IsTrue(first.Equals(second));
        }

        [Test]
        public void CreateComponent_And_GetComponent_ShareCore()
        {
            var entity = _world.CreateEntity();
            var created = entity.CreateComponent<Position>();
            var fetched = entity.GetComponent<Position>();

            Assert.AreSame(created.Core, fetched.Core);
        }

        [Test]
        public void Migration_PreservesCoreInstanceAndValue()
        {
            var entity = _world.CreateEntity();
            var position = entity.CreateComponent<Position>();
            position.RW.X = 7;
            var core = position.Core;

            entity.CreateComponent<Velocity>();   // dense migration

            Assert.AreSame(core, position.Core);
            Assert.IsTrue(position.NotNull);
            Assert.AreEqual(7, entity.GetComponent<Position>().RW.X);
        }

        [Test]
        public void SwapRemove_PreservesOtherEntityCore()
        {
            var removed = _world.CreateEntity();
            var kept = _world.CreateEntity();
            removed.CreateComponent<Position>().RW.X = 1;
            var keptPosition = kept.CreateComponent<Position>();
            keptPosition.RW.X = 2;
            var keptCore = keptPosition.Core;

            _world.DestroyEntity(removed);

            Assert.AreSame(keptCore, keptPosition.Core);
            Assert.IsTrue(keptPosition.NotNull);
            Assert.AreEqual(2, keptPosition.RW.X);
        }

        [Test]
        public void DestroyComponent_StaleHandleIsCut()
        {
            var entity = _world.CreateEntity();
            var position = entity.CreateComponent<Position>();
            entity.DestroyComponent(position);

            Assert.IsFalse(position.NotNull);
            Assert.Throws<NullReferenceException>(() => { _ = position.RW.X; });
        }

        [Test]
        public void SparseComponent_CoreIsStoredAndReleased()
        {
            var entity = _world.CreateEntity();
            var mana = entity.CreateComponent<Mana>();
            var fetched = entity.GetComponent<Mana>();
            Assert.AreSame(mana.Core, fetched.Core);

            entity.DestroyComponent(mana);
            Assert.IsFalse(mana.NotNull);
        }

        [Test]
        public void GetComponents_ReusesStoredCores()
        {
            var entity = _world.CreateEntity();
            var position = entity.CreateComponent<Position>();
            var mana = entity.CreateComponent<Mana>();

            var all = entity.GetComponents();

            var positionEntry = all.First(r => r.Inspect<Position>());
            var manaEntry = all.First(r => r.Inspect<Mana>());
            Assert.AreSame(position.Core, positionEntry.Core);
            Assert.AreSame(mana.Core, manaEntry.Core);
        }
    }
}
