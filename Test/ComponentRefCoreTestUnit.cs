using CoreECS.Defines;
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class ComponentRefCoreTestUnit
    {
        private struct Position : IComponent<Position>
        {
            public int X;
        }

        private struct ManaComponent : IDiscreteComponent<ManaComponent>
        {
            public int Value;
        }

        private struct PlayerTag : ITagComponent<PlayerTag>
        {
        }

        private static uint IdOf<T>() where T : struct, IComponent<T>
            => ComponentTypeRegistry.GetOrRegister<T>().TypeId;

        private static Structure MakeStructure()
        {
            return new Structure(new StructureKey(new[] { IdOf<Position>() }, 0));
        }

        [Test]
        public void DenseRef_NotNull_TracksPresenceAndVersion()
        {
            var structure = MakeStructure();
            var location = EntityLocation.Pool.Get();
            var row = structure.Append(7, location);
            structure.SetDenseValue(row, new Position { X = 1 }, 5);

            var matching = new ComponentRefCore(location, location.Generation, IdOf<Position>(), ComponentKind.Dense, 5);
            var staleVersion = new ComponentRefCore(location, location.Generation, IdOf<Position>(), ComponentKind.Dense, 6);

            Assert.IsTrue(matching.NotNull);
            Assert.IsFalse(staleVersion.NotNull);
        }

        [Test]
        public void DenseRef_EntityId_Revision_And_ChangeRevision()
        {
            var structure = MakeStructure();
            var location = EntityLocation.Pool.Get();
            var row = structure.Append(11, location);
            structure.SetDenseValue(row, new Position { X = 1 }, 3);

            var core = new ComponentRefCore(location, location.Generation, IdOf<Position>(), ComponentKind.Dense, 3);

            Assert.AreEqual(11UL, core.EntityId);
            Assert.AreEqual(0u, core.Revision);
            Assert.AreEqual(1u, core.ChangeRevision());
            Assert.AreEqual(1u, core.Revision);
        }

        [Test]
        public void DiscreteRef_NotNull_TracksPresenceAndVersion()
        {
            var structure = MakeStructure();
            var location = EntityLocation.Pool.Get();
            var row = structure.Append(3, location);
            structure.SetDiscrete(row, new ManaComponent { Value = 4 }, 9);

            var matching = new ComponentRefCore(location, location.Generation, IdOf<ManaComponent>(), ComponentKind.Discrete, 9);
            var staleVersion = new ComponentRefCore(location, location.Generation, IdOf<ManaComponent>(), ComponentKind.Discrete, 10);

            Assert.IsTrue(matching.NotNull);
            Assert.IsFalse(staleVersion.NotNull);

            structure.RemoveDiscrete(IdOf<ManaComponent>(), row);

            Assert.IsFalse(matching.NotNull);
        }

        [Test]
        public void DiscreteRef_Revision_And_ChangeRevision()
        {
            var structure = MakeStructure();
            var location = EntityLocation.Pool.Get();
            var row = structure.Append(3, location);
            structure.SetDiscrete(row, new ManaComponent { Value = 4 }, 9);

            var core = new ComponentRefCore(location, location.Generation, IdOf<ManaComponent>(), ComponentKind.Discrete, 9);

            Assert.AreEqual(0u, core.Revision);
            Assert.AreEqual(1u, core.ChangeRevision());
            Assert.AreEqual(1u, core.Revision);
        }

        [Test]
        public void TagRef_NotNull_TracksTagBitOnly()
        {
            var structure = MakeStructure();
            var location = EntityLocation.Pool.Get();
            var row = structure.Append(5, location);

            var core = new ComponentRefCore(location, location.Generation, IdOf<PlayerTag>(), ComponentKind.Tag, 0);

            Assert.IsFalse(core.NotNull);

            structure.AddTag(IdOf<PlayerTag>(), row);

            Assert.IsTrue(core.NotNull);
            Assert.AreEqual(0u, core.Revision);
            Assert.AreEqual(0u, core.ChangeRevision());

            structure.RemoveTag(IdOf<PlayerTag>(), row);

            Assert.IsFalse(core.NotNull);
        }

        [Test]
        public void StaleLocation_GenerationMismatch_InvalidatesRef()
        {
            var structure = MakeStructure();
            var location = EntityLocation.Pool.Get();
            var row = structure.Append(8, location);
            structure.SetDenseValue(row, new Position { X = 2 }, 1);

            var core = new ComponentRefCore(location, location.Generation, IdOf<Position>(), ComponentKind.Dense, 1);
            Assert.IsTrue(core.NotNull);

            EntityLocation.Pool.Release(location);

            Assert.IsFalse(core.NotNull);
            Assert.AreEqual(0UL, core.EntityId);
        }
    }
}
