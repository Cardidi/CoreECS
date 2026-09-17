using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class EntityTableTestUnit
    {
        [Test]
        public void Create_AllocatesUniqueIncreasingIdsAndDistinctLocations()
        {
            var table = new EntityTable();

            var first = table.Create();
            var second = table.Create();

            Assert.AreEqual(1UL, first.EntityId);
            Assert.AreEqual(2UL, second.EntityId);
            Assert.Less(first.EntityId, second.EntityId);
            Assert.AreNotSame(first.Location, second.Location);
            Assert.AreEqual(2, table.Count);
        }

        [Test]
        public void TryGetLocation_ReturnsRegisteredLocationAndFalseForUnknownId()
        {
            var table = new EntityTable();
            var created = table.Create();

            Assert.IsTrue(table.TryGetLocation(created.EntityId, out var location));
            Assert.AreSame(created.Location, location);

            Assert.IsFalse(table.TryGetLocation(created.EntityId + 1UL, out var unknown));
            Assert.IsNull(unknown);
        }

        [Test]
        public void Destroy_RemovesEntryAndReleasesLocationToPool()
        {
            var table = new EntityTable();
            var created = table.Create();
            var generation = created.Location.Generation;

            table.Destroy(created.EntityId);

            Assert.AreEqual(0, table.Count);
            Assert.IsFalse(table.TryGetLocation(created.EntityId, out _));
            Assert.AreEqual(generation + 1U, created.Location.Generation);
        }

        [Test]
        public void Create_AfterDestroy_ReusesReleasedLocationWithNewerGeneration()
        {
            // Drain the shared pool so the released location is the only reuse candidate.
            EntityLocation.Pool.Clear();

            var table = new EntityTable();
            var first = table.Create();
            var staleLocation = first.Location;
            var staleGeneration = staleLocation.Generation;

            table.Destroy(first.EntityId);
            var second = table.Create();

            Assert.AreNotEqual(first.EntityId, second.EntityId);
            Assert.AreSame(staleLocation, second.Location);
            Assert.Greater(second.Location.Generation, staleGeneration);
            Assert.IsFalse(table.TryGetLocation(first.EntityId, out _));
            Assert.IsTrue(table.TryGetLocation(second.EntityId, out var current));
            Assert.AreSame(second.Location, current);
        }

        [Test]
        public void Destroy_UnknownId_IsNoOp()
        {
            var table = new EntityTable();
            var created = table.Create();

            table.Destroy(created.EntityId + 100UL);

            Assert.AreEqual(1, table.Count);
            Assert.IsTrue(table.TryGetLocation(created.EntityId, out var location));
            Assert.AreSame(created.Location, location);
        }
    }
}
