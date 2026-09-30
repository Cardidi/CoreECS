using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class StructureKeyTestUnit
    {
        [Test]
        public void Equality_ComparesMaskAndSortedIds()
        {
            var a = new StructureKey(new uint[] { 1, 2, 3 }, 0b101);
            var b = new StructureKey(new uint[] { 1, 2, 3 }, 0b101);
            var differentIds = new StructureKey(new uint[] { 1, 2, 4 }, 0b101);
            var differentMask = new StructureKey(new uint[] { 1, 2, 3 }, 0b110);

            Assert.AreEqual(a, b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
            Assert.AreNotEqual(a, differentIds);
            Assert.AreNotEqual(a, differentMask);
        }

        [Test]
        public void Equality_HandlesEmptyComposition()
        {
            var a = new StructureKey(null, 7);
            var b = new StructureKey(System.Array.Empty<uint>(), 7);

            Assert.AreEqual(a, b);
        }

        [Test]
        public void Constructor_DoesNotRetainInputArray()
        {
            var ids = new uint[] { 1, 2 };
            var key = new StructureKey(ids, 0);
            var expected = new StructureKey(new uint[] { 1, 2 }, 0);
            ids[0] = 99;

            Assert.AreEqual(expected, key);
        }
    }
}
