using CoreECS.Defines;
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class ComponentTypeRegistryTestUnit
    {
        private struct RegistryDense : IComponent<RegistryDense>
        {
        }

        private struct RegistryDiscrete : IDiscreteComponent<RegistryDiscrete>
        {
        }

        private struct RegistryTag : ITagComponent<RegistryTag>
        {
        }

        private struct RegistryConcurrent : IComponent<RegistryConcurrent>
        {
        }

        [Test]
        public void ResolveKind_DetectsDense()
        {
            Assert.AreEqual(ComponentKind.Dense, ComponentTypeRegistry.ResolveKind(typeof(RegistryDense)));
        }

        [Test]
        public void ResolveKind_DetectsDiscrete()
        {
            Assert.AreEqual(ComponentKind.Discrete, ComponentTypeRegistry.ResolveKind(typeof(RegistryDiscrete)));
        }

        [Test]
        public void ResolveKind_DetectsTag()
        {
            Assert.AreEqual(ComponentKind.Tag, ComponentTypeRegistry.ResolveKind(typeof(RegistryTag)));
        }

        [Test]
        public void ResolveKind_RejectsNonComponentType()
        {
            Assert.Throws<ArgumentException>(() => ComponentTypeRegistry.ResolveKind(typeof(int)));
        }

        [Test]
        public void GetOrRegister_IsIdempotent()
        {
            var first = ComponentTypeRegistry.GetOrRegister<RegistryDense>();
            var second = ComponentTypeRegistry.GetOrRegister<RegistryDense>();

            Assert.AreEqual(first.TypeId, second.TypeId);
            Assert.AreEqual(ComponentKind.Dense, first.Kind);
            Assert.AreEqual(typeof(RegistryDense), first.Type);
        }

        [Test]
        public void GetOrRegister_AssignsUniqueIds()
        {
            var a = ComponentTypeRegistry.GetOrRegister<RegistryDense>();
            var b = ComponentTypeRegistry.GetOrRegister<RegistryDiscrete>();
            var c = ComponentTypeRegistry.GetOrRegister<RegistryTag>();

            Assert.AreNotEqual(a.TypeId, b.TypeId);
            Assert.AreNotEqual(b.TypeId, c.TypeId);
            Assert.AreNotEqual(a.TypeId, c.TypeId);
        }

        [Test]
        public void GetById_ReturnsRegisteredInfo()
        {
            var registered = ComponentTypeRegistry.GetOrRegister<RegistryTag>();
            var fetched = ComponentTypeRegistry.GetById(registered.TypeId);

            Assert.AreEqual(typeof(RegistryTag), fetched.Type);
            Assert.AreEqual(ComponentKind.Tag, fetched.Kind);
        }

        [Test]
        public void GetById_ThrowsForUnknownId()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ComponentTypeRegistry.GetById(uint.MaxValue));
        }

        [Test]
        public void GetOrRegister_ConcurrentFirstRegistration_KeepsIdsConsistent()
        {
            const int threadCount = 32;
            using var barrier = new Barrier(threadCount);
            var results = new ComponentTypeInfo[threadCount];
            var threads = new Thread[threadCount];

            for (var i = 0; i < threadCount; i++)
            {
                var index = i;
                threads[i] = new Thread(() =>
                {
                    barrier.SignalAndWait();
                    results[index] = ComponentTypeRegistry.GetOrRegister<RegistryConcurrent>();
                });
                threads[i].Start();
            }

            foreach (var thread in threads) thread.Join();

            var canonical = results[0];
            for (var i = 0; i < threadCount; i++)
            {
                Assert.AreEqual(canonical.TypeId, results[i].TypeId);
                Assert.AreEqual(canonical.TypeId, ComponentTypeRegistry.GetById(results[i].TypeId).TypeId);
            }

            Assert.AreEqual(ComponentTypeRegistry.RegisteredTypeCount, ComponentTypeRegistry.RegisteredIdCount);
        }
    }
}
