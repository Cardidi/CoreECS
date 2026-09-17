using CoreECS.Defines;
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class StructureMigrationTestUnit
    {
        private struct Position : IComponent<Position>
        {
            public int X;
        }

        private struct Velocity : IComponent<Velocity>
        {
            public int Y;
        }

        private struct Mana : ISparseComponent<Mana>
        {
            public int Value;
        }

        private struct Player : ITagComponent<Player>
        {
        }

        private sealed class RecordingObserver : IStructureObserver
        {
            public readonly List<(uint TypeId, int Row)> Added = new();
            public readonly List<(uint TypeId, int Row)> Removed = new();
            public readonly List<(uint TypeId, int Row)> Changed = new();

            public void OnComponentAdded(Structure structure, int row, uint typeId) => Added.Add((typeId, row));
            public void OnComponentRemoved(Structure structure, int row, uint typeId) => Removed.Add((typeId, row));
            public void OnComponentChanged(Structure structure, int row, uint typeId) => Changed.Add((typeId, row));
        }

        private static uint IdOf<T>() where T : struct, IComponent<T>
            => ComponentTypeRegistry.GetOrRegister<T>().TypeId;

        private static Structure MakeStructure(params uint[] denseTypeIds)
        {
            var sorted = (uint[])denseTypeIds.Clone();
            Array.Sort(sorted);
            return new Structure(new StructureKey(sorted, 0));
        }

        [Test]
        public void Tag_AddAndRemove_NotifyObserver()
        {
            var structure = MakeStructure(IdOf<Position>());
            var observer = new RecordingObserver();
            structure.Observer = observer;
            var row = structure.Append(1, EntityLocation.Pool.Get());

            Assert.IsTrue(structure.AddTag(IdOf<Player>(), row));
            Assert.IsTrue(structure.HasTag(IdOf<Player>(), row));
            Assert.IsFalse(structure.AddTag(IdOf<Player>(), row));
            Assert.IsTrue(structure.RemoveTag(IdOf<Player>(), row));
            Assert.IsFalse(structure.HasTag(IdOf<Player>(), row));

            Assert.AreEqual(1, observer.Added.Count);
            Assert.AreEqual(1, observer.Removed.Count);
        }

        [Test]
        public void Sparse_SetOverwriteRemove_NotifyObserver()
        {
            var structure = MakeStructure(IdOf<Position>());
            var observer = new RecordingObserver();
            structure.Observer = observer;
            var row = structure.Append(1, EntityLocation.Pool.Get());

            structure.SetSparse(row, new Mana { Value = 1 }, 5);
            structure.SetSparse(row, new Mana { Value = 2 }, 5);
            structure.RemoveSparse(IdOf<Mana>(), row);

            Assert.IsFalse(structure.HasSparse(IdOf<Mana>(), row));
            Assert.AreEqual(1, observer.Added.Count);
            Assert.AreEqual(1, observer.Changed.Count);
            Assert.AreEqual(1, observer.Removed.Count);
        }

        [Test]
        public void Sparse_ChangeRevision_NotifiesObserver()
        {
            var structure = MakeStructure(IdOf<Position>());
            var observer = new RecordingObserver();
            structure.Observer = observer;
            var row = structure.Append(1, EntityLocation.Pool.Get());
            structure.SetSparse(row, new Mana { Value = 1 }, 5);

            var revision = structure.ChangeSparseRevision<Mana>(row);

            Assert.AreEqual(1u, revision);
            Assert.AreEqual(1, observer.Changed.Count);
        }

        [Test]
        public void CopyDenseTo_CopiesSharedTypesWithVersionAndRevision()
        {
            var source = MakeStructure(IdOf<Position>(), IdOf<Velocity>());
            var target = MakeStructure(IdOf<Position>());

            var row = source.Append(1, EntityLocation.Pool.Get());
            source.SetDenseValue(row, new Position { X = 11 }, 7);
            source.SetDenseValue(row, new Velocity { Y = 22 }, 8);
            source.ChangeDenseRevision<Position>(row);

            var targetRow = target.Append(1, EntityLocation.Pool.Get());
            source.CopyDenseTo(target, row, targetRow);

            Assert.AreEqual(11, target.GetReadOnlyDenseColumn<Position>()[0].X);
            Assert.AreEqual(7u, target.GetDenseVersion<Position>(0));
            Assert.AreEqual(1u, target.GetDenseRevision<Position>(0));
            Assert.IsFalse(target.HasDense(IdOf<Velocity>()));
        }

        [Test]
        public void CopyTagsTo_TransfersRowTags()
        {
            var source = MakeStructure(IdOf<Position>());
            var target = MakeStructure(IdOf<Position>());

            var row = source.Append(1, EntityLocation.Pool.Get());
            source.AddTag(IdOf<Player>(), row);

            var targetRow = target.Append(1, EntityLocation.Pool.Get());
            source.CopyTagsTo(target, row, targetRow);

            Assert.IsTrue(target.HasTag(IdOf<Player>(), 0));
        }

        [Test]
        public void MoveSparseTo_TransfersDataVersionAndRevision()
        {
            var source = MakeStructure(IdOf<Position>());
            var target = MakeStructure(IdOf<Position>());

            var row = source.Append(1, EntityLocation.Pool.Get());
            source.SetSparse(row, new Mana { Value = 4 }, 9);
            source.ChangeSparseRevision<Mana>(row);

            var targetRow = target.Append(1, EntityLocation.Pool.Get());
            source.MoveSparseTo(target, row, targetRow);

            Assert.IsTrue(target.HasSparse(IdOf<Mana>(), 0));
            Assert.AreEqual(4, target.GetSparseRef<Mana>(0).Value);
            Assert.AreEqual(9u, target.GetSparseVersion<Mana>(0));
            Assert.AreEqual(1u, target.GetSparseRevision<Mana>(0));
        }

        [Test]
        public void SwapRemove_SwapsTagAndSparseRows()
        {
            var structure = MakeStructure(IdOf<Position>());
            var first = EntityLocation.Pool.Get();
            var second = EntityLocation.Pool.Get();
            structure.Append(1, first);
            structure.Append(2, second);
            structure.AddTag(IdOf<Player>(), 1);
            structure.SetSparse(1, new Mana { Value = 5 }, 3);

            structure.SwapRemove(0);

            Assert.AreEqual(1, structure.Count);
            Assert.AreEqual(2UL, structure.Entities[0]);
            Assert.IsTrue(structure.HasTag(IdOf<Player>(), 0));
            Assert.IsTrue(structure.HasSparse(IdOf<Mana>(), 0));
            Assert.AreEqual(5, structure.GetSparseRef<Mana>(0).Value);
            Assert.AreEqual(3u, structure.GetSparseVersion<Mana>(0));
        }

        [Test]
        public void LazySparse_GrowsToRowCountOnFirstSparseWrite()
        {
            var structure = MakeStructure(IdOf<Position>());
            structure.Append(1, EntityLocation.Pool.Get());
            structure.Append(2, EntityLocation.Pool.Get());
            structure.Append(3, EntityLocation.Pool.Get());

            structure.SetSparse(2, new Mana { Value = 7 }, 4);

            Assert.IsTrue(structure.HasSparse(IdOf<Mana>(), 2));
            Assert.AreEqual(7, structure.GetSparseRef<Mana>(2).Value);
            Assert.AreEqual(4u, structure.GetSparseVersion<Mana>(2));
            Assert.IsFalse(structure.HasSparse(IdOf<Mana>(), 0));
        }

        [Test]
        public void ChangeSparseRevision_OnAbsentComponent_ReturnsZeroWithoutNotification()
        {
            var structure = MakeStructure(IdOf<Position>());
            var observer = new RecordingObserver();
            structure.Observer = observer;
            var row = structure.Append(1, EntityLocation.Pool.Get());
            structure.SetSparse(row, new Mana { Value = 1 }, 1);
            structure.RemoveSparse(IdOf<Mana>(), row);

            var revision = structure.ChangeSparseRevision<Mana>(row);

            Assert.AreEqual(0u, revision);
            Assert.AreEqual(0, observer.Changed.Count);
        }

        [Test]
        public void MoveSparseTo_WhenSourceHasNoSparse_ClearsTargetRow()
        {
            var source = MakeStructure(IdOf<Position>());
            var target = MakeStructure(IdOf<Position>());

            var row = source.Append(1, EntityLocation.Pool.Get());
            var targetRow = target.Append(1, EntityLocation.Pool.Get());
            target.SetSparse(targetRow, new Mana { Value = 42 }, 1);

            source.MoveSparseTo(target, row, targetRow);

            Assert.IsFalse(target.HasSparse(IdOf<Mana>(), targetRow));
        }

        [Test]
        public void RemoveSparse_WhenNoSparse_IsNoOp()
        {
            var structure = MakeStructure(IdOf<Position>());
            var observer = new RecordingObserver();
            structure.Observer = observer;
            var row = structure.Append(1, EntityLocation.Pool.Get());

            structure.RemoveSparse(IdOf<Mana>(), row);

            Assert.AreEqual(0, observer.Removed.Count);
        }

        [Test]
        public void GetSparseRef_ThrowsForAbsentComponent()
        {
            var structure = MakeStructure(IdOf<Position>());
            var row = structure.Append(1, EntityLocation.Pool.Get());

            Assert.Throws<InvalidOperationException>(() =>
            {
                structure.GetSparseRef<Mana>(row);
            });
        }

        [Test]
        public void RemoveTag_ReturnsFalseForAbsentTagWithoutNotification()
        {
            var structure = MakeStructure(IdOf<Position>());
            var observer = new RecordingObserver();
            structure.Observer = observer;
            var row = structure.Append(1, EntityLocation.Pool.Get());

            Assert.IsFalse(structure.RemoveTag(IdOf<Player>(), row));
            Assert.AreEqual(0, observer.Removed.Count);
        }

        [Test]
        public void AddTag_ThrowsForDeadRow()
        {
            var structure = MakeStructure(IdOf<Position>());
            structure.Append(1, EntityLocation.Pool.Get());

            Assert.Throws<ArgumentOutOfRangeException>(() => structure.AddTag(IdOf<Player>(), 1));
        }

        [Test]
        public void Append_AfterSparseExists_KeepsSparseRowsAligned()
        {
            var structure = MakeStructure(IdOf<Position>());
            structure.Append(1, EntityLocation.Pool.Get());
            structure.SetSparse(0, new Mana { Value = 1 }, 1);

            structure.Append(2, EntityLocation.Pool.Get());

            Assert.IsFalse(structure.HasSparse(IdOf<Mana>(), 1));

            structure.SetSparse(1, new Mana { Value = 2 }, 2);
            Assert.IsTrue(structure.HasSparse(IdOf<Mana>(), 1));
        }
    }
}
