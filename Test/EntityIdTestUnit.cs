#if COREECS_ULONG_ENTITY
using EntityValue = System.UInt64;
#else
using EntityValue = System.UInt32;
#endif

namespace CoreECS.Test
{
    [TestFixture]
    public class EntityIdTestUnit
    {
#if COREECS_ULONG_ENTITY
        private const EntityValue ExpectedMaxIndex = uint.MaxValue;
        private const EntityValue ExpectedMaxGeneration = uint.MaxValue;
#else
        private const EntityValue ExpectedMaxIndex = (1U << 20) - 1;
        private const EntityValue ExpectedMaxGeneration = (1U << 12) - 1;
#endif

        [Test]
        public void EntityId_Default_IsInvalid()
        {
            var entityId = default(EntityId);

            Assert.That(entityId.IsValid, Is.False);
            Assert.That(entityId.Index, Is.Zero);
            Assert.That(entityId.Generation, Is.Zero);
            Assert.That(entityId.Raw, Is.Zero);
        }

        [Test]
        public void EntityId_Create_RoundTripsIndexAndGeneration()
        {
            var entityId = EntityId.Create(123, 7);

            Assert.That(entityId.Index, Is.EqualTo((EntityValue)123));
            Assert.That(entityId.Generation, Is.EqualTo((EntityValue)7));
            Assert.That(entityId.IsValid, Is.True);
        }

        [Test]
        public void EntityId_MaxValues_RoundTripWithoutOverlap()
        {
            var maximum = EntityId.Create(ExpectedMaxIndex, ExpectedMaxGeneration);
            var maximumIndex = EntityId.Create(ExpectedMaxIndex, 1);
            var maximumGeneration = EntityId.Create(1, ExpectedMaxGeneration);

            Assert.That(maximum.Index, Is.EqualTo(ExpectedMaxIndex));
            Assert.That(maximum.Generation, Is.EqualTo(ExpectedMaxGeneration));
            Assert.That(maximumIndex.Index, Is.EqualTo(ExpectedMaxIndex));
            Assert.That(maximumIndex.Generation, Is.EqualTo((EntityValue)1));
            Assert.That(maximumGeneration.Index, Is.EqualTo((EntityValue)1));
            Assert.That(maximumGeneration.Generation, Is.EqualTo(ExpectedMaxGeneration));
            Assert.That(EntityId.MaxIndex, Is.EqualTo(ExpectedMaxIndex));
            Assert.That(EntityId.MaxGeneration, Is.EqualTo(ExpectedMaxGeneration));
        }

        [Test]
        public void EntityId_Equality_UsesIndexAndGeneration()
        {
            var entityId = EntityId.Create(123, 7);
            var same = EntityId.Create(123, 7);
            var differentIndex = EntityId.Create(124, 7);
            var differentGeneration = EntityId.Create(123, 8);

            Assert.That(entityId.Equals(same), Is.True);
            Assert.That(entityId.Equals((object)same), Is.True);
            Assert.That(entityId == same, Is.True);
            Assert.That(entityId != same, Is.False);
            Assert.That(entityId.GetHashCode(), Is.EqualTo(same.GetHashCode()));
            Assert.That(entityId, Is.Not.EqualTo(differentIndex));
            Assert.That(entityId, Is.Not.EqualTo(differentGeneration));
            Assert.That(entityId != differentIndex, Is.True);
            Assert.That(entityId != differentGeneration, Is.True);
        }

        [Test]
        public void EntityId_NextGeneration_SkipsZeroAfterWrap()
        {
            Assert.That(EntityId.NextGeneration(1), Is.EqualTo((EntityValue)2));
            Assert.That(EntityId.NextGeneration(ExpectedMaxGeneration), Is.EqualTo((EntityValue)1));
        }

        [Test]
        public void EntityId_ToString_ContainsIndexAndGeneration()
        {
            var text = EntityId.Create(123, 7).ToString();

            Assert.That(text, Does.Contain("123"));
            Assert.That(text, Does.Contain("7"));
        }

        [Test]
        public void EntityId_Create_RejectsZeroIndex()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => EntityId.Create(0, 1));
        }

        [Test]
        public void EntityId_Create_RejectsZeroGeneration()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => EntityId.Create(1, 0));
        }

        [Test]
        public void EntityId_Create_RejectsValuesAboveMasks()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => EntityId.Create(ExpectedMaxIndex + 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => EntityId.Create(1, ExpectedMaxGeneration + 1));
        }
    }
}
