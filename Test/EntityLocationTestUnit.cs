using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class EntityLocationTestUnit
    {
        [Test]
        public void Release_ResetsStructureAndRow()
        {
            var location = EntityLocation.Pool.Get();
            location.Row = 7;
            EntityLocation.Pool.Release(location);

            var reused = EntityLocation.Pool.Get();
            Assert.IsNull(reused.Structure);
            Assert.AreEqual(-1, reused.Row);
        }

        [Test]
        public void Release_AdvancesGenerationOfReleasedInstance()
        {
            var location = EntityLocation.Pool.Get();
            var generation = location.Generation;

            EntityLocation.Pool.Release(location);

            Assert.AreEqual(generation + 1, location.Generation);
        }
    }
}
