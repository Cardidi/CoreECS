using CoreECS.Defines;
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class SpareSetComponentContainerTestUnit
    {
        private struct ManaComponent : IDiscreteComponent<ManaComponent>
        {
            public int Value;
        }

        private struct RageComponent : IDiscreteComponent<RageComponent>
        {
            public int Value;
        }

        [Test]
        public void Store_Set_Get_Has_Remove()
        {
            var store = new DiscreteStore<ManaComponent>();
            store.AddRow();
            Assert.IsFalse(store.Has(0));

            store.Set(0, new ManaComponent { Value = 5 }, 11);

            Assert.IsTrue(store.Has(0));
            Assert.AreEqual(5, store.Get(0).Value);
            Assert.AreEqual(11u, store.GetVersion(0));

            store.Remove(0);

            Assert.IsFalse(store.Has(0));
            Assert.AreEqual(0u, store.GetVersion(0));
        }

        [Test]
        public void ChangeRevision_Increments()
        {
            var store = new DiscreteStore<ManaComponent>();
            store.AddRow();
            store.Set(0, default, 1);

            Assert.AreEqual(1u, store.ChangeRevision(0));
            Assert.AreEqual(2u, store.ChangeRevision(0));
        }

        [Test]
        public void RemoveRowSwap_MovesPresenceAndData()
        {
            var store = new DiscreteStore<ManaComponent>();
            store.AddRow();
            store.AddRow();
            store.Set(1, new ManaComponent { Value = 9 }, 3);

            store.RemoveRowSwap(0);

            Assert.AreEqual(1, store.Count);
            Assert.IsTrue(store.Has(0));
            Assert.AreEqual(9, store.Get(0).Value);
            Assert.AreEqual(3u, store.GetVersion(0));
        }

        [Test]
        public void CopyRowTo_TransfersDataVersionAndRevision()
        {
            var source = new DiscreteStore<ManaComponent>();
            source.AddRow();
            source.Set(0, new ManaComponent { Value = 7 }, 42);
            source.ChangeRevision(0);

            var target = new DiscreteStore<ManaComponent>();
            target.AddRow();

            source.CopyRowTo(0, target, 0);

            Assert.IsTrue(target.Has(0));
            Assert.AreEqual(7, target.Get(0).Value);
            Assert.AreEqual(42u, target.GetVersion(0));
            Assert.AreEqual(1u, target.GetRevision(0));
        }

        [Test]
        public void Container_ManagesIndependentStores()
        {
            var container = new SpareSetComponentContainer();
            container.AddRow();

            var mana = container.GetOrCreateStore<ManaComponent>();
            var rage = container.GetOrCreateStore<RageComponent>();
            mana.Set(0, new ManaComponent { Value = 1 }, 1);
            rage.Set(0, new RageComponent { Value = 2 }, 1);

            Assert.IsTrue(container.Has(mana.TypeId, 0));
            Assert.IsTrue(container.Has(rage.TypeId, 0));
            Assert.AreSame(mana, container.GetOrCreateStore<ManaComponent>());
        }

        [Test]
        public void Container_CopyRowTo_CreatesTargetStores()
        {
            var source = new SpareSetComponentContainer();
            source.AddRow();
            var mana = source.GetOrCreateStore<ManaComponent>();
            mana.Set(0, new ManaComponent { Value = 3 }, 8);

            var target = new SpareSetComponentContainer();
            target.AddRow();

            source.CopyRowTo(0, target, 0);

            var copied = target.GetOrCreateStore<ManaComponent>();
            Assert.IsTrue(copied.Has(0));
            Assert.AreEqual(3, copied.Get(0).Value);
            Assert.AreEqual(8u, copied.GetVersion(0));
        }

        [Test]
        public void Set_ThrowsForDeadRow()
        {
            var store = new DiscreteStore<ManaComponent>();
            store.AddRow();

            Assert.Throws<ArgumentOutOfRangeException>(() => store.Set(1, default, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => store.Get(1));
        }

        [Test]
        public void RemoveRowSwap_ThrowsForInvalidRow()
        {
            var store = new DiscreteStore<ManaComponent>();

            Assert.Throws<ArgumentOutOfRangeException>(() => store.RemoveRowSwap(0));
        }

        [Test]
        public void CopyRowTo_ThrowsForDeadTargetRow()
        {
            var source = new DiscreteStore<ManaComponent>();
            source.AddRow();
            source.Set(0, new ManaComponent { Value = 1 }, 1);

            var target = new DiscreteStore<ManaComponent>();
            target.AddRow();

            Assert.Throws<ArgumentOutOfRangeException>(() => source.CopyRowTo(0, target, 1));
        }

        [Test]
        public void Container_LazyStoreCreation_MatchesContainerRowCount()
        {
            var container = new SpareSetComponentContainer();
            container.AddRow();
            container.AddRow();

            var store = container.GetOrCreateStore<ManaComponent>();

            Assert.AreEqual(2, container.Count);
            Assert.AreEqual(2, store.Count);

            store.Set(1, new ManaComponent { Value = 6 }, 2);
            Assert.IsTrue(store.Has(1));
        }

        [Test]
        public void Container_RemoveRowSwap_KeepsStoreCountAligned()
        {
            var container = new SpareSetComponentContainer();
            container.AddRow();
            container.AddRow();
            var store = container.GetOrCreateStore<ManaComponent>();
            store.Set(1, new ManaComponent { Value = 6 }, 2);

            container.RemoveRowSwap(0);

            Assert.AreEqual(1, container.Count);
            Assert.AreEqual(1, store.Count);
            Assert.IsTrue(store.Has(0));
            Assert.AreEqual(6, store.Get(0).Value);
        }

        [Test]
        public void CopyRowTo_AbsentSource_ClearsTargetPresence()
        {
            var source = new DiscreteStore<ManaComponent>();
            source.AddRow();

            var target = new DiscreteStore<ManaComponent>();
            target.AddRow();
            target.Set(0, new ManaComponent { Value = 9 }, 4);

            source.CopyRowTo(0, target, 0);

            Assert.IsFalse(target.Has(0));
        }

        [Test]
        public void Container_CopyRowTo_ClearsTargetComponentsMissingInSource()
        {
            var source = new SpareSetComponentContainer();
            source.AddRow();

            var target = new SpareSetComponentContainer();
            target.AddRow();
            var rage = target.GetOrCreateStore<RageComponent>();
            rage.Set(0, new RageComponent { Value = 2 }, 1);

            source.CopyRowTo(0, target, 0);

            Assert.IsFalse(target.Has(rage.TypeId, 0));
        }

        [Test]
        public void EnsureRows_GrowsStoreAndClearsNewSlots()
        {
            var store = new DiscreteStore<ManaComponent>();
            store.EnsureRows(3);

            Assert.AreEqual(3, store.Count);
            Assert.IsFalse(store.Has(2));

            store.Set(2, new ManaComponent { Value = 1 }, 1);
            Assert.IsTrue(store.Has(2));
        }

        [Test]
        public void EnsureRows_SupportsRowsBeyondBitmapWord()
        {
            var store = new DiscreteStore<ManaComponent>();
            store.EnsureRows(70);
            store.Set(65, new ManaComponent { Value = 7 }, 1);

            Assert.IsTrue(store.Has(65));
            Assert.IsFalse(store.Has(64));
        }

        [Test]
        public void Container_AddRow_GrowsExistingStores()
        {
            var container = new SpareSetComponentContainer();
            container.AddRow();
            var store = container.GetOrCreateStore<ManaComponent>();

            container.AddRow();

            Assert.AreEqual(2, container.Count);
            Assert.AreEqual(2, store.Count);
        }

        [Test]
        public void RemoveRowSwap_OnLastRow_ClearsSlot()
        {
            var store = new DiscreteStore<ManaComponent>();
            store.AddRow();
            store.Set(0, new ManaComponent { Value = 5 }, 1);

            store.RemoveRowSwap(0);

            Assert.AreEqual(0, store.Count);

            store.AddRow();

            Assert.IsFalse(store.Has(0));
        }
    }
}
