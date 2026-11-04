#if COREECS_ULONG_ENTITY
using EntityValue = System.UInt64;
#else
using EntityValue = System.UInt32;
#endif

using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class SparseIndexTestUnit
    {
        [Test]
        public void TryGet_AbsentEntity_ReturnsFalseAndMinusOne()
        {
            var sparse = new SparseIndex();

            bool found = sparse.TryGet(EntityId.Create(1, 1), out int packedSlot);

            Assert.That(found, Is.False);
            Assert.That(packedSlot, Is.EqualTo(-1));
            Assert.That(sparse.AllocatedPageCount, Is.Zero);
        }

        [Test]
        public void SetAndRemove_RoundTripPackedSlotZero()
        {
            var sparse = new SparseIndex();
            var entityId = EntityId.Create(17, 3);

            sparse.Set(entityId, 0);

            Assert.That(sparse.TryGet(entityId, out int packedSlot), Is.True);
            Assert.That(packedSlot, Is.Zero);

            sparse.Remove(entityId);

            Assert.That(sparse.TryGet(entityId, out packedSlot), Is.False);
            Assert.That(packedSlot, Is.EqualTo(-1));
        }

        [Test]
        public void TryGet_SameIndexWithDifferentGeneration_ReturnsFalse()
        {
            var sparse = new SparseIndex();
            var current = EntityId.Create(23, 5);
            var staleOrFuture = EntityId.Create(23, 6);
            sparse.Set(current, 41);

            bool found = sparse.TryGet(staleOrFuture, out int packedSlot);

            Assert.That(found, Is.False);
            Assert.That(packedSlot, Is.EqualTo(-1));
            Assert.That(sparse.TryGet(current, out int currentSlot), Is.True);
            Assert.That(currentSlot, Is.EqualTo(41));
        }

        [Test]
        public void Set_RejectsSameGenerationDuplicateAndOverwritesOlderGeneration()
        {
            var sparse = new SparseIndex();
            var first = EntityId.Create(31, 7);
            var replacement = EntityId.Create(31, 8);

            sparse.Set(first, 2);
            Assert.Throws<InvalidOperationException>(() => sparse.Set(first, 3));

            sparse.Set(replacement, 4);

            Assert.That(sparse.TryGet(first, out _), Is.False);
            Assert.That(sparse.TryGet(replacement, out int replacementSlot), Is.True);
            Assert.That(replacementSlot, Is.EqualTo(4));

            sparse.Remove(first);
            Assert.That(sparse.TryGet(replacement, out _), Is.True);
            sparse.Remove(replacement);
            Assert.That(sparse.TryGet(replacement, out _), Is.False);
        }

        [Test]
        public void Set_SupportsLowAndNearMaximumIndexes()
        {
            var sparse = new SparseIndex();
            var low = EntityId.Create(1, 1);
            var nearMaximum = EntityId.Create(EntityId.MaxIndex - (EntityValue)1, EntityId.MaxGeneration);

            sparse.Set(low, 11);
            sparse.Set(nearMaximum, 97);

            Assert.That(sparse.TryGet(low, out int lowSlot), Is.True);
            Assert.That(lowSlot, Is.EqualTo(11));
            Assert.That(sparse.TryGet(nearMaximum, out int highSlot), Is.True);
            Assert.That(highSlot, Is.EqualTo(97));
        }

        [Test]
        public void Set_SingleHighIndex_AllocatesOnlyItsFixedPage()
        {
            var sparse = new SparseIndex();
            var high = EntityId.Create(EntityId.MaxIndex, 1);

            sparse.Set(high, 123);

            Assert.That(sparse.AllocatedPageCount, Is.EqualTo(1));
            Assert.That(sparse.TryGet(high, out int packedSlot), Is.True);
            Assert.That(packedSlot, Is.EqualTo(123));
        }

        [Test]
        public void Set_InvalidEntityId_ThrowsWithoutAllocatingPage()
        {
            var sparse = new SparseIndex();

            Assert.Throws<ArgumentException>(() => sparse.Set(default(EntityId), 0));
            Assert.That(sparse.AllocatedPageCount, Is.Zero);
        }

        [Test]
        public void Set_NegativePackedSlot_ThrowsWithoutAllocatingPage()
        {
            var sparse = new SparseIndex();

            Assert.Throws<ArgumentOutOfRangeException>(() => sparse.Set(EntityId.Create(1, 1), -1));
            Assert.That(sparse.AllocatedPageCount, Is.Zero);
        }

        [Test]
        public void Clear_RemovesEntriesAndReleasesAllocatedPages()
        {
            var sparse = new SparseIndex();
            var low = EntityId.Create(1, 1);
            var high = EntityId.Create(EntityId.MaxIndex, EntityId.MaxGeneration);
            sparse.Set(low, 5);
            sparse.Set(high, 9);

            sparse.Clear();

            Assert.That(sparse.TryGet(low, out _), Is.False);
            Assert.That(sparse.TryGet(high, out _), Is.False);
            Assert.That(sparse.AllocatedPageCount, Is.Zero);
        }
    }
}
