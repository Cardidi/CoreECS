using CoreECS.Defines;
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class StructureRegistryTestUnit
    {
        private struct Position : IComponent<Position>
        {
        }

        private struct Velocity : IComponent<Velocity>
        {
        }

        private static uint IdOf<T>() where T : struct, IComponent<T>
            => ComponentTypeRegistry.GetOrRegister<T>().TypeId;

        [Test]
        public void GetOrCreate_DeduplicatesByCompositionAndMask()
        {
            var registry = new StructureRegistry();
            var positionId = IdOf<Position>();
            var velocityId = IdOf<Velocity>();

            var first = registry.GetOrCreate(new[] { positionId }, 7);
            var second = registry.GetOrCreate(new[] { positionId }, 7);
            var differentMask = registry.GetOrCreate(new[] { positionId }, 8);
            var differentComposition = registry.GetOrCreate(new[] { positionId, velocityId }, 7);

            Assert.AreSame(first, second);
            Assert.AreNotSame(first, differentMask);
            Assert.AreNotSame(first, differentComposition);
            Assert.AreEqual(3, registry.Count);
        }

        [Test]
        public void GetOrCreate_KeyMatchesEquivalentKeyInstance()
        {
            var registry = new StructureRegistry();
            var positionId = IdOf<Position>();

            var created = registry.GetOrCreate(new[] { positionId }, 0);
            var fetched = registry.GetOrCreate(new StructureKey(new[] { positionId }, 0));

            Assert.AreSame(created, fetched);
        }

        [Test]
        public void GetOrCreate_OwnsKeyCopy_ExternalArrayMutationDoesNotCorruptRegistry()
        {
            var registry = new StructureRegistry();
            var positionId = IdOf<Position>();
            var ids = new[] { positionId };

            var created = registry.GetOrCreate(ids, 0);
            ids[0] = positionId + 1000;

            var fetched = registry.GetOrCreate(new[] { positionId }, 0);

            Assert.AreSame(created, fetched);
            Assert.AreEqual(1, registry.Count);
        }
    }
}
