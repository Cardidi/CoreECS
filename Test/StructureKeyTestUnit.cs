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
            Assert.AreEqual(0, a.DenseCount);
        }

        [Test]
        public void AddType_KeepsIdsSortedAndDeduplicates()
        {
            var ids = StructureKey.AddType(new uint[] { 2, 5 }, 3);
            CollectionAssert.AreEqual(new uint[] { 2, 3, 5 }, ids);

            var same = StructureKey.AddType(ids, 3);
            Assert.AreSame(ids, same);
        }

        [Test]
        public void AddType_HandlesEmptySource()
        {
            var ids = StructureKey.AddType(null, 9);
            CollectionAssert.AreEqual(new uint[] { 9 }, ids);
        }

        [Test]
        public void RemoveType_KeepsIdsSorted()
        {
            var ids = StructureKey.RemoveType(new uint[] { 2, 3, 5 }, 3);
            CollectionAssert.AreEqual(new uint[] { 2, 5 }, ids);
        }

        [Test]
        public void RemoveType_IgnoresMissingId()
        {
            var ids = StructureKey.RemoveType(new uint[] { 2, 5 }, 4);
            CollectionAssert.AreEqual(new uint[] { 2, 5 }, ids);
        }

        [Test]
        public void ToArray_ReturnsDefensiveCopy()
        {
            var key = new StructureKey(new uint[] { 1, 2 }, 0);
            var copy = key.ToArray();
            copy[0] = 99;

            Assert.AreEqual(1u, key.DenseTypeIds[0]);
        }
    }
}
