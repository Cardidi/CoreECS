using CoreECS.Defines;
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class StructureBatchAccessTestUnit
    {
        private struct Position : IComponent<Position>
        {
            public int X;
        }

        private struct Velocity : IComponent<Velocity>
        {
            public int Y;
        }

        private struct ManaComponent : IDiscreteComponent<ManaComponent>
        {
        }

        private struct PlayerTag : ITagComponent<PlayerTag>
        {
        }

        private sealed class RecordingObserver : IStructureObserver
        {
            public readonly List<(uint TypeId, int Row)> Changed = new();

            public void OnComponentAdded(Structure structure, int row, uint typeId)
            {
            }

            public void OnComponentRemoved(Structure structure, int row, uint typeId)
            {
            }

            public void OnComponentChanged(Structure structure, int row, uint typeId)
                => Changed.Add((typeId, row));
        }

        private static uint IdOf<T>() where T : struct, IComponent<T>
            => ComponentTypeRegistry.GetOrRegister<T>().TypeId;

        private static Structure MakeStructure(params uint[] typeIds)
        {
            Array.Sort(typeIds);
            return new Structure(new StructureKey(typeIds, 0));
        }

        private static int AppendPosition(Structure structure, ulong entityId, int value)
        {
            var row = structure.Append(entityId, EntityLocation.Pool.Get());
            structure.SetDenseValue(row, new Position { X = value }, ComponentVersion.Next());
            return row;
        }

        [Test]
        public void RO_ReturnsLiveRowsWithoutMarkingOrNotifying()
        {
            var structure = MakeStructure(IdOf<Position>());
            var observer = new RecordingObserver();
            structure.Observer = observer;
            AppendPosition(structure, 11, 1);
            AppendPosition(structure, 22, 2);
            AppendPosition(structure, 33, 3);

            var span = structure.RO<Position>();

            Assert.AreEqual(structure.Count, span.Length);
            Assert.AreEqual(1, span[0].X);
            Assert.AreEqual(2, span[1].X);
            Assert.AreEqual(3, span[2].X);
            Assert.AreEqual(11UL, structure.Entities[0]);
            Assert.AreEqual(22UL, structure.Entities[1]);
            Assert.AreEqual(33UL, structure.Entities[2]);
            for (var row = 0; row < structure.Count; row++)
            {
                Assert.AreEqual(0u, structure.GetDenseRevision<Position>(row));
            }

            Assert.AreEqual(0, observer.Changed.Count);
        }

        [Test]
        public void RO_ReflectsWritesMadeThroughPreviousRWSpan()
        {
            var structure = MakeStructure(IdOf<Position>());
            AppendPosition(structure, 1, 1);
            AppendPosition(structure, 2, 2);

            var rw = structure.RW<Position>();
            rw[0] = new Position { X = 10 };
            rw[1] = new Position { X = 20 };

            var ro = structure.RO<Position>();

            Assert.AreEqual(2, ro.Length);
            Assert.AreEqual(10, ro[0].X);
            Assert.AreEqual(20, ro[1].X);
        }

        [Test]
        public void RW_WritesPersistAndAreVisibleThroughGetDenseRef()
        {
            var structure = MakeStructure(IdOf<Position>());
            var first = AppendPosition(structure, 1, 1);
            var second = AppendPosition(structure, 2, 2);

            var span = structure.RW<Position>();
            span[0] = new Position { X = 7 };
            span[1] = new Position { X = 8 };

            Assert.AreEqual(7, structure.GetDenseRef<Position>(first).X);
            Assert.AreEqual(8, structure.GetDenseRef<Position>(second).X);
        }

        [Test]
        public void RW_MarksEveryLiveRowOnceAndOnlyItsOwnColumn()
        {
            var structure = MakeStructure(IdOf<Position>(), IdOf<Velocity>());
            var observer = new RecordingObserver();
            structure.Observer = observer;

            for (var i = 0; i < 3; i++)
            {
                var row = structure.Append((ulong)(i + 1), EntityLocation.Pool.Get());
                structure.SetDenseValue(row, new Position { X = i }, ComponentVersion.Next());
                structure.SetDenseValue(row, new Velocity { Y = i * 10 }, ComponentVersion.Next());
            }

            var span = structure.RW<Position>();

            Assert.AreEqual(3, span.Length);
            for (var row = 0; row < structure.Count; row++)
            {
                Assert.AreEqual(1u, structure.GetDenseRevision<Position>(row));
                Assert.AreEqual(0u, structure.GetDenseRevision<Velocity>(row));
            }

            Assert.AreEqual(3, observer.Changed.Count);
            Assert.AreEqual((IdOf<Position>(), 0), observer.Changed[0]);
            Assert.AreEqual((IdOf<Position>(), 1), observer.Changed[1]);
            Assert.AreEqual((IdOf<Position>(), 2), observer.Changed[2]);
        }

        [Test]
        public void RO_And_RW_ThrowForDenseTypeAbsentFromStructure()
        {
            var structure = MakeStructure(IdOf<Position>());
            structure.Append(1, EntityLocation.Pool.Get());

            Assert.Throws<InvalidOperationException>(() => structure.RO<Velocity>());
            Assert.Throws<InvalidOperationException>(() => structure.RW<Velocity>());
        }

        [Test]
        public void RO_And_RW_ThrowForDiscreteAndTagTypes()
        {
            var structure = MakeStructure(IdOf<Position>());
            structure.Append(1, EntityLocation.Pool.Get());

            Assert.Throws<InvalidOperationException>(() => structure.RO<ManaComponent>());
            Assert.Throws<InvalidOperationException>(() => structure.RW<ManaComponent>());
            Assert.Throws<InvalidOperationException>(() => structure.RO<PlayerTag>());
            Assert.Throws<InvalidOperationException>(() => structure.RW<PlayerTag>());
        }

        [Test]
        public void RO_And_RW_OnEmptyStructure_ReturnEmptySpansWithoutNotifying()
        {
            var structure = MakeStructure(IdOf<Position>());
            var observer = new RecordingObserver();
            structure.Observer = observer;

            Assert.AreEqual(0, structure.RO<Position>().Length);
            Assert.AreEqual(0, structure.RW<Position>().Length);
            Assert.AreEqual(0, observer.Changed.Count);
        }

        [Test]
        public void Spans_ExposeLiveRowsOnlyWhenCapacityExceedsCount()
        {
            var structure = MakeStructure(IdOf<Position>());
            for (var i = 0; i < 20; i++)
            {
                AppendPosition(structure, (ulong)(i + 1), i);
            }

            for (var row = 19; row >= 3; row--)
            {
                structure.SwapRemove(row);
            }

            Assert.AreEqual(3, structure.Count);

            var ro = structure.RO<Position>();
            var rw = structure.RW<Position>();

            Assert.AreEqual(3, ro.Length);
            Assert.AreEqual(3, rw.Length);

            for (var row = 0; row < structure.Count; row++)
            {
                rw[row] = new Position { X = (int)structure.Entities[row] };
            }

            for (var row = 0; row < structure.Count; row++)
            {
                Assert.AreEqual((int)structure.Entities[row], ro[row].X);
                Assert.AreEqual((int)structure.Entities[row], structure.GetDenseRef<Position>(row).X);
                Assert.AreEqual(1u, structure.GetDenseRevision<Position>(row));
            }
        }
    }
}
