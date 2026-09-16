using CoreECS.Defines;
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class StructureTestUnit
    {
        private struct Position : IComponent<Position>
        {
            public int X;
        }

        private struct Velocity : IComponent<Velocity>
        {
            public int Y;
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

        private static Structure MakePositionStructure()
        {
            return new Structure(new StructureKey(new[] { IdOf<Position>() }, 0));
        }

        [Test]
        public void Append_TracksEntityAndLocation()
        {
            var structure = MakePositionStructure();
            var location = EntityLocation.Pool.Get();

            var row = structure.Append(42, location);

            Assert.AreEqual(0, row);
            Assert.AreEqual(1, structure.Count);
            Assert.AreSame(structure, location.Structure);
            Assert.AreEqual(0, location.Row);
            Assert.AreEqual(42UL, structure.Entities[0]);
        }

        [Test]
        public void SwapRemove_MovesLastEntityAndFixesLocation()
        {
            var structure = MakePositionStructure();
            var a = EntityLocation.Pool.Get();
            var b = EntityLocation.Pool.Get();
            var c = EntityLocation.Pool.Get();
            structure.Append(1, a);
            structure.Append(2, b);
            structure.Append(3, c);

            structure.SwapRemove(0);

            Assert.AreEqual(2, structure.Count);
            Assert.AreEqual(3UL, structure.Entities[0]);
            Assert.AreEqual(0, c.Row);
            Assert.AreEqual(2UL, structure.Entities[1]);
            Assert.AreEqual(1, b.Row);
        }

        [Test]
        public void HasDense_ReflectsComposition()
        {
            var structure = MakePositionStructure();

            Assert.IsTrue(structure.HasDense(IdOf<Position>()));
            Assert.IsFalse(structure.HasDense(IdOf<Velocity>()));
        }

        [Test]
        public void RO_ReturnsRowAlignedSpan()
        {
            var structure = MakePositionStructure();
            var row = structure.Append(1, EntityLocation.Pool.Get());
            structure.SetDenseValue(row, new Position { X = 9 }, 1);

            var span = structure.RO<Position>();

            Assert.AreEqual(1, span.Length);
            Assert.AreEqual(9, span[0].X);
        }

        [Test]
        public void RW_BumpsRevisionAndNotifiesObserver()
        {
            var structure = MakePositionStructure();
            var observer = new RecordingObserver();
            structure.Observer = observer;
            var row = structure.Append(1, EntityLocation.Pool.Get());
            structure.SetDenseValue(row, default(Position), 1);

            var span = structure.RW<Position>();
            span[0].X = 5;

            Assert.AreEqual(5, structure.RO<Position>()[0].X);
            Assert.AreEqual(1u, structure.GetDenseRevision<Position>(0));
            Assert.AreEqual(1, observer.Changed.Count);
            Assert.AreEqual(IdOf<Position>(), observer.Changed[0].TypeId);
            Assert.AreEqual(0, observer.Changed[0].Row);
        }

        [Test]
        public void GetDenseRef_And_ChangeDenseRevision()
        {
            var structure = MakePositionStructure();
            var row = structure.Append(1, EntityLocation.Pool.Get());
            structure.SetDenseValue(row, new Position { X = 2 }, 3);

            ref var position = ref structure.GetDenseRef<Position>(row);
            position.X = 4;
            structure.ChangeDenseRevision<Position>(row);

            Assert.AreEqual(4, structure.RO<Position>()[0].X);
            Assert.AreEqual(3u, structure.GetDenseVersion<Position>(0));
            Assert.AreEqual(1u, structure.GetDenseRevision<Position>(0));
        }

        [Test]
        public void Growth_PreservesData()
        {
            var structure = MakePositionStructure();
            for (var i = 0; i < 100; i++)
            {
                var row = structure.Append((ulong)i, EntityLocation.Pool.Get());
                structure.SetDenseValue(row, new Position { X = i }, 1);
            }

            Assert.AreEqual(100, structure.Count);
            Assert.AreEqual(99, structure.RO<Position>()[99].X);
        }

        [Test]
        public void SlotOf_ThrowsForMissingDenseType()
        {
            var structure = MakePositionStructure();

            Assert.Throws<InvalidOperationException>(() => structure.RO<Velocity>());
        }

        [Test]
        public void SwapRemove_OnLastRow_DecrementsCount()
        {
            var structure = MakePositionStructure();
            var location = EntityLocation.Pool.Get();
            var row = structure.Append(1, location);
            structure.SetDenseValue(row, new Position { X = 3 }, 1);

            structure.SwapRemove(row);

            Assert.AreEqual(0, structure.Count);
            Assert.AreEqual(0, structure.RO<Position>().Length);
        }

        [Test]
        public void SwapRemove_MovesDenseValueVersionAndRevision()
        {
            var structure = MakePositionStructure();
            var first = EntityLocation.Pool.Get();
            var second = EntityLocation.Pool.Get();
            structure.Append(1, first);
            var lastRow = structure.Append(2, second);
            structure.SetDenseValue(lastRow, new Position { X = 8 }, 6);
            structure.ChangeDenseRevision<Position>(lastRow);

            structure.SwapRemove(0);

            Assert.AreEqual(1, structure.Count);
            Assert.AreEqual(2UL, structure.Entities[0]);
            Assert.AreEqual(8, structure.RO<Position>()[0].X);
            Assert.AreEqual(6u, structure.GetDenseVersion<Position>(0));
            Assert.AreEqual(1u, structure.GetDenseRevision<Position>(0));
            Assert.AreEqual(0, second.Row);
        }

        [Test]
        public void Append_AfterSwapRemove_ClearsRecycledDenseSlot()
        {
            var structure = MakePositionStructure();
            var first = EntityLocation.Pool.Get();
            var second = EntityLocation.Pool.Get();
            structure.Append(1, first);
            structure.Append(2, second);
            structure.SetDenseValue(1, new Position { X = 7 }, 6);

            structure.SwapRemove(1);

            var row = structure.Append(3, EntityLocation.Pool.Get());

            Assert.AreEqual(1, row);
            Assert.AreEqual(0, structure.RO<Position>()[1].X);
            Assert.AreEqual(0u, structure.GetDenseVersion<Position>(1));
            Assert.AreEqual(0u, structure.GetDenseRevision<Position>(1));
        }

        [Test]
        public void RepeatedSwapRemove_KeepsLocationsConsistent()
        {
            var structure = MakePositionStructure();
            var locations = new EntityLocation[4];
            for (var i = 0; i < locations.Length; i++)
            {
                locations[i] = EntityLocation.Pool.Get();
                var row = structure.Append((ulong)(i + 1), locations[i]);
                structure.SetDenseValue(row, new Position { X = i + 1 }, 1);
            }

            structure.SwapRemove(0);
            structure.SwapRemove(1);

            Assert.AreEqual(2, structure.Count);
            Assert.AreEqual(4UL, structure.Entities[0]);
            Assert.AreEqual(3UL, structure.Entities[1]);
            Assert.AreEqual(0, locations[3].Row);
            Assert.AreEqual(1, locations[2].Row);
            Assert.AreEqual(4, structure.RO<Position>()[0].X);
            Assert.AreEqual(3, structure.RO<Position>()[1].X);
        }

        [Test]
        public void Grow_PreservesVersionsAndRevisions()
        {
            var structure = MakePositionStructure();
            var row = structure.Append(1, EntityLocation.Pool.Get());
            structure.SetDenseValue(row, new Position { X = 4 }, 9);
            structure.ChangeDenseRevision<Position>(row);

            for (var i = 1; i < 20; i++)
            {
                structure.Append((ulong)(i + 1), EntityLocation.Pool.Get());
            }

            Assert.AreEqual(4, structure.RO<Position>()[0].X);
            Assert.AreEqual(9u, structure.GetDenseVersion<Position>(0));
            Assert.AreEqual(1u, structure.GetDenseRevision<Position>(0));
        }

        [Test]
        public void RW_OnEmptyStructure_ReturnsEmptySpanAndDoesNotNotify()
        {
            var structure = MakePositionStructure();
            var observer = new RecordingObserver();
            structure.Observer = observer;

            var span = structure.RW<Position>();

            Assert.AreEqual(0, span.Length);
            Assert.AreEqual(0, observer.Changed.Count);
        }

        [Test]
        public void SwapRemove_ThrowsForInvalidRow()
        {
            var structure = MakePositionStructure();
            var location = EntityLocation.Pool.Get();
            structure.Append(1, location);
            structure.SetDenseValue(0, new Position { X = 5 }, 3);

            Assert.Throws<ArgumentOutOfRangeException>(() => structure.SwapRemove(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => structure.SwapRemove(-1));

            Assert.AreEqual(1, structure.Count);
            Assert.AreEqual(1UL, structure.Entities[0]);
            Assert.AreEqual(0, location.Row);
            Assert.AreEqual(5, structure.RO<Position>()[0].X);
            Assert.AreEqual(3u, structure.GetDenseVersion<Position>(0));
        }
    }
}
