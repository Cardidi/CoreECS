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

            Assert.AreSame(first.Handler, second.Handler);
            Assert.IsTrue(first.Equals(second));
        }

        [Test]
        public void CreateComponent_And_GetComponent_ShareCore()
        {
            var entity = _world.CreateEntity();
            var created = entity.CreateComponent<Position>();
            var fetched = entity.GetComponent<Position>();

            Assert.AreSame(created.Handler, fetched.Handler);
        }

        [Test]
        public void Migration_PreservesCoreInstanceAndValue()
        {
            var entity = _world.CreateEntity();
            var position = entity.CreateComponent<Position>();
            position.RW.X = 7;
            var handler = position.Handler;

            entity.CreateComponent<Velocity>();   // dense migration

            Assert.AreSame(handler, position.Handler);
            Assert.AreSame(handler, entity.GetComponent<Position>().Handler);
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
            var keptCore = keptPosition.Handler;

            _world.DestroyEntity(removed);

            Assert.AreSame(keptCore, keptPosition.Handler);
            Assert.AreSame(keptCore, kept.GetComponent<Position>().Handler);
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
            Assert.AreSame(mana.Handler, fetched.Handler);

            entity.DestroyComponent(mana);
            Assert.IsFalse(mana.NotNull);
        }

        [Test]
        public void SparseAdd_DestroyInSignalHandler_DoesNotCrashAndLeavesDeadHandle()
        {
            var entity = _world.CreateEntity();
            _world.GetManager<CoreECS.Managers.EntityManager>().OnEntityGotComp += (id, type) =>
            {
                if (type == typeof(Mana)) _world.DestroyEntity(id);
            };

            var mana = entity.CreateComponent<Mana>();

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
            Assert.AreSame(position.Handler, positionEntry.Handler);
            Assert.AreSame(mana.Handler, manaEntry.Handler);
        }

        [Test]
        public void SparseOverwrite_RebindsStoredCoreAndKeepsSingleOwner()
        {
            var entity = _world.CreateEntity();
            var mana = entity.CreateComponent<Mana>();
            mana.RW.Value = 1;
            var handler = mana.Handler;

            var overwritten = entity.CreateComponent(new Mana { Value = 2 });

            Assert.AreSame(handler, entity.GetComponent<Mana>().Handler);
            Assert.AreSame(handler, overwritten.Handler);
            Assert.IsFalse(mana.NotNull);
            Assert.IsTrue(overwritten.NotNull);
            Assert.AreEqual(2, overwritten.RO.Value);
        }

        [Test]
        public void SetMask_PreservesDenseAndSparseCores()
        {
            var entity = _world.CreateEntity();
            var position = entity.CreateComponent<Position>();
            position.RW.X = 3;
            var mana = entity.CreateComponent<Mana>();
            mana.RW.Value = 4;
            var positionCore = position.Handler;
            var manaCore = mana.Handler;

            entity.SetMask(0b1010UL);

            Assert.AreSame(positionCore, entity.GetComponent<Position>().Handler);
            Assert.AreSame(manaCore, entity.GetComponent<Mana>().Handler);
            Assert.IsTrue(position.NotNull);
            Assert.IsTrue(mana.NotNull);
            Assert.AreEqual(3, position.RO.X);
            Assert.AreEqual(4, mana.RO.Value);
        }

        [Test]
        public void Grow_PreservesCores()
        {
            var first = _world.CreateEntity();
            var position = first.CreateComponent<Position>();
            position.RW.X = 5;
            var handler = position.Handler;

            // InitialCapacity is 8: 16 more rows force two capacity grows.
            for (var i = 0; i < 16; i++)
            {
                _world.CreateEntity().CreateComponent<Position>();
            }

            Assert.AreSame(handler, first.GetComponent<Position>().Handler);
            Assert.IsTrue(position.NotNull);
            Assert.AreEqual(5, position.RO.X);
        }

        [Test]
        public void RemoveOneComponent_KeepsOtherCores()
        {
            var entity = _world.CreateEntity();
            var position = entity.CreateComponent<Position>();
            var velocity = entity.CreateComponent<Velocity>();
            var mana = entity.CreateComponent<Mana>();
            var positionCore = position.Handler;
            var manaCore = mana.Handler;

            entity.DestroyComponent(velocity);

            Assert.IsFalse(velocity.NotNull);
            Assert.AreSame(positionCore, entity.GetComponent<Position>().Handler);
            Assert.AreSame(manaCore, entity.GetComponent<Mana>().Handler);
            Assert.IsTrue(position.NotNull);
            Assert.IsTrue(mana.NotNull);
        }
    }
}
