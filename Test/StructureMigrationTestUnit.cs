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

        private struct Mana : IDiscreteComponent<Mana>
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
        public void Discrete_SetOverwriteRemove_NotifyObserver()
        {
            var structure = MakeStructure(IdOf<Position>());
            var observer = new RecordingObserver();
            structure.Observer = observer;
            var row = structure.Append(1, EntityLocation.Pool.Get());

            structure.SetDiscrete(row, new Mana { Value = 1 }, 5);
            structure.SetDiscrete(row, new Mana { Value = 2 }, 5);
            structure.RemoveDiscrete(IdOf<Mana>(), row);

            Assert.IsFalse(structure.HasDiscrete(IdOf<Mana>(), row));
            Assert.AreEqual(1, observer.Added.Count);
            Assert.AreEqual(1, observer.Changed.Count);
            Assert.AreEqual(1, observer.Removed.Count);
        }

        [Test]
        public void Discrete_ChangeRevision_NotifiesObserver()
        {
            var structure = MakeStructure(IdOf<Position>());
            var observer = new RecordingObserver();
            structure.Observer = observer;
            var row = structure.Append(1, EntityLocation.Pool.Get());
            structure.SetDiscrete(row, new Mana { Value = 1 }, 5);

            var revision = structure.ChangeDiscreteRevision<Mana>(row);

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

            Assert.AreEqual(11, target.RO<Position>()[0].X);
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
        public void MoveDiscreteTo_TransfersDataVersionAndRevision()
        {
            var source = MakeStructure(IdOf<Position>());
            var target = MakeStructure(IdOf<Position>());

            var row = source.Append(1, EntityLocation.Pool.Get());
            source.SetDiscrete(row, new Mana { Value = 4 }, 9);
            source.ChangeDiscreteRevision<Mana>(row);

            var targetRow = target.Append(1, EntityLocation.Pool.Get());
            source.MoveDiscreteTo(target, row, targetRow);

            Assert.IsTrue(target.HasDiscrete(IdOf<Mana>(), 0));
            Assert.AreEqual(4, target.GetDiscreteRef<Mana>(0).Value);
            Assert.AreEqual(9u, target.GetDiscreteVersion<Mana>(0));
            Assert.AreEqual(1u, target.GetDiscreteRevision<Mana>(0));
        }

        [Test]
        public void SwapRemove_SwapsTagAndDiscreteRows()
        {
            var structure = MakeStructure(IdOf<Position>());
            var first = EntityLocation.Pool.Get();
            var second = EntityLocation.Pool.Get();
            structure.Append(1, first);
            structure.Append(2, second);
            structure.AddTag(IdOf<Player>(), 1);
            structure.SetDiscrete(1, new Mana { Value = 5 }, 3);

            structure.SwapRemove(0);

            Assert.AreEqual(1, structure.Count);
            Assert.AreEqual(2UL, structure.Entities[0]);
            Assert.IsTrue(structure.HasTag(IdOf<Player>(), 0));
            Assert.IsTrue(structure.HasDiscrete(IdOf<Mana>(), 0));
            Assert.AreEqual(5, structure.GetDiscreteRef<Mana>(0).Value);
            Assert.AreEqual(3u, structure.GetDiscreteVersion<Mana>(0));
        }

        [Test]
        public void LazySpareSet_GrowsToRowCountOnFirstDiscreteWrite()
        {
            var structure = MakeStructure(IdOf<Position>());
            structure.Append(1, EntityLocation.Pool.Get());
            structure.Append(2, EntityLocation.Pool.Get());
            structure.Append(3, EntityLocation.Pool.Get());

            structure.SetDiscrete(2, new Mana { Value = 7 }, 4);

            Assert.IsTrue(structure.HasDiscrete(IdOf<Mana>(), 2));
            Assert.AreEqual(7, structure.GetDiscreteRef<Mana>(2).Value);
            Assert.AreEqual(4u, structure.GetDiscreteVersion<Mana>(2));
            Assert.IsFalse(structure.HasDiscrete(IdOf<Mana>(), 0));
        }

        [Test]
        public void ChangeDiscreteRevision_OnAbsentComponent_ReturnsZeroWithoutNotification()
        {
            var structure = MakeStructure(IdOf<Position>());
            var observer = new RecordingObserver();
            structure.Observer = observer;
            var row = structure.Append(1, EntityLocation.Pool.Get());
            structure.SetDiscrete(row, new Mana { Value = 1 }, 1);
            structure.RemoveDiscrete(IdOf<Mana>(), row);

            var revision = structure.ChangeDiscreteRevision<Mana>(row);

            Assert.AreEqual(0u, revision);
            Assert.AreEqual(0, observer.Changed.Count);
        }

        [Test]
        public void MoveDiscreteTo_WhenSourceHasNoSpareSet_ClearsTargetRow()
        {
            var source = MakeStructure(IdOf<Position>());
            var target = MakeStructure(IdOf<Position>());

            var row = source.Append(1, EntityLocation.Pool.Get());
            var targetRow = target.Append(1, EntityLocation.Pool.Get());
            target.SetDiscrete(targetRow, new Mana { Value = 42 }, 1);

            source.MoveDiscreteTo(target, row, targetRow);

            Assert.IsFalse(target.HasDiscrete(IdOf<Mana>(), targetRow));
        }

        [Test]
        public void RemoveDiscrete_WhenNoSpareSet_IsNoOp()
        {
            var structure = MakeStructure(IdOf<Position>());
            var observer = new RecordingObserver();
            structure.Observer = observer;
            var row = structure.Append(1, EntityLocation.Pool.Get());

            structure.RemoveDiscrete(IdOf<Mana>(), row);

            Assert.AreEqual(0, observer.Removed.Count);
        }

        [Test]
        public void GetDiscreteRef_ThrowsForAbsentComponent()
        {
            var structure = MakeStructure(IdOf<Position>());
            var row = structure.Append(1, EntityLocation.Pool.Get());

            Assert.Throws<InvalidOperationException>(() =>
            {
                structure.GetDiscreteRef<Mana>(row);
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
        public void Append_AfterSpareSetExists_KeepsDiscreteRowsAligned()
        {
            var structure = MakeStructure(IdOf<Position>());
            structure.Append(1, EntityLocation.Pool.Get());
            structure.SetDiscrete(0, new Mana { Value = 1 }, 1);

            structure.Append(2, EntityLocation.Pool.Get());

            Assert.IsFalse(structure.HasDiscrete(IdOf<Mana>(), 1));

            structure.SetDiscrete(1, new Mana { Value = 2 }, 2);
            Assert.IsTrue(structure.HasDiscrete(IdOf<Mana>(), 1));
        }
    }
}
