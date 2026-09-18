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

        private struct ManaComponent : ISparseComponent<ManaComponent>
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
        public void SparseRef_NotNull_TracksPresenceAndVersion()
        {
            var structure = MakeStructure();
            var location = EntityLocation.Pool.Get();
            var row = structure.Append(3, location);
            structure.SetSparse(row, new ManaComponent { Value = 4 }, 9);

            var matching = new ComponentRefCore(location, location.Generation, IdOf<ManaComponent>(), ComponentKind.Sparse, 9);
            var staleVersion = new ComponentRefCore(location, location.Generation, IdOf<ManaComponent>(), ComponentKind.Sparse, 10);

            Assert.IsTrue(matching.NotNull);
            Assert.IsFalse(staleVersion.NotNull);

            structure.RemoveSparse(IdOf<ManaComponent>(), row);

            Assert.IsFalse(matching.NotNull);
        }

        [Test]
        public void SparseRef_Revision_And_ChangeRevision()
        {
            var structure = MakeStructure();
            var location = EntityLocation.Pool.Get();
            var row = structure.Append(3, location);
            structure.SetSparse(row, new ManaComponent { Value = 4 }, 9);

            var core = new ComponentRefCore(location, location.Generation, IdOf<ManaComponent>(), ComponentKind.Sparse, 9);

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

        [Test]
        public void RecycledLocation_ReboundToNewStructureWithNewerGeneration_InvalidatesRef()
        {
            // Drain the pool so the released location is the only reuse candidate.
            EntityLocation.Pool.Clear();

            var structure = MakeStructure();
            var location = EntityLocation.Pool.Get();
            var row = structure.Append(8, location);
            structure.SetDenseValue(row, new Position { X = 2 }, 1);

            var core = new ComponentRefCore(location, location.Generation, IdOf<Position>(), ComponentKind.Dense, 1);
            Assert.IsTrue(core.NotNull);

            structure.SwapRemove(row);
            EntityLocation.Pool.Release(location);

            // The pool hands the same instance back, rebound to a fresh structure whose
            // dense column carries the same type and version; only the generation differs.
            var recycled = EntityLocation.Pool.Get();
            Assert.AreSame(location, recycled);
            var reborn = MakeStructure();
            var rebornRow = reborn.Append(99, recycled);
            reborn.SetDenseValue(rebornRow, new Position { X = 2 }, 1);

            Assert.AreNotEqual(core.Generation, recycled.Generation);
            Assert.IsFalse(core.NotNull);
            Assert.AreEqual(0UL, core.EntityId);
        }

        [Test]
        public void StaleRow_AfterSwapRemoveWithoutRelease_ReportsInvalid()
        {
            var structure = MakeStructure();
            var location = EntityLocation.Pool.Get();
            var row = structure.Append(8, location);
            structure.SetDenseValue(row, new Position { X = 2 }, 1);

            var core = new ComponentRefCore(location, location.Generation, IdOf<Position>(), ComponentKind.Dense, 1);
            Assert.IsTrue(core.NotNull);

            // SwapRemove leaves the removed location untouched for the caller to release;
            // the ref must not read the vacated row in the window before that release.
            structure.SwapRemove(row);

            Assert.IsFalse(core.NotNull);
            Assert.AreEqual(0UL, core.EntityId);
        }
    }
}
