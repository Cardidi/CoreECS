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
            Assert.AreSame(core, entity.GetComponent<Position>().Core);
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
            Assert.AreSame(keptCore, kept.GetComponent<Position>().Core);
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
        public void SparseAdd_DestroyInSignalHandler_DoesNotCrashAndLeavesDeadHandle()
        {
            var entity = _world.CreateEntity();
            _world.GetManager<CoreECS.Managers.EntityManager>().OnEntityGotComp.Add((id, type) =>
            {
                if (type == typeof(Mana)) _world.DestroyEntity(id);
            });

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
            Assert.AreSame(position.Core, positionEntry.Core);
            Assert.AreSame(mana.Core, manaEntry.Core);
        }

        [Test]
        public void SparseOverwrite_RebindsStoredCoreAndKeepsSingleOwner()
        {
            var entity = _world.CreateEntity();
            var mana = entity.CreateComponent<Mana>();
            mana.RW.Value = 1;
            var core = mana.Core;

            var overwritten = entity.CreateComponent(new Mana { Value = 2 });

            Assert.AreSame(core, entity.GetComponent<Mana>().Core);
            Assert.AreSame(core, overwritten.Core);
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
            var positionCore = position.Core;
            var manaCore = mana.Core;

            entity.SetMask(0b1010UL);

            Assert.AreSame(positionCore, entity.GetComponent<Position>().Core);
            Assert.AreSame(manaCore, entity.GetComponent<Mana>().Core);
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
            var core = position.Core;

            // InitialCapacity is 8: 16 more rows force two capacity grows.
            for (var i = 0; i < 16; i++)
            {
                _world.CreateEntity().CreateComponent<Position>();
            }

            Assert.AreSame(core, first.GetComponent<Position>().Core);
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
            var positionCore = position.Core;
            var manaCore = mana.Core;

            entity.DestroyComponent(velocity);

            Assert.IsFalse(velocity.NotNull);
            Assert.AreSame(positionCore, entity.GetComponent<Position>().Core);
            Assert.AreSame(manaCore, entity.GetComponent<Mana>().Core);
            Assert.IsTrue(position.NotNull);
            Assert.IsTrue(mana.NotNull);
        }
    }
}
