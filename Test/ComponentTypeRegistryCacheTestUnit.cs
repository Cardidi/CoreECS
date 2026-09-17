using CoreECS.Defines;
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class ComponentTypeRegistryCacheTestUnit
    {
        private struct CachedDense : IComponent<CachedDense> { public int X; }
        private struct CachedSparse : ISparseComponent<CachedSparse> { public int X; }
        private struct CachedTag : ITagComponent<CachedTag> { }

        [Test]
        public void GetOrRegister_GenericAndTypeOverloads_Agree()
        {
            var generic = ComponentTypeRegistry.GetOrRegister<CachedDense>();
            var byType = ComponentTypeRegistry.GetOrRegister(typeof(CachedDense));

            Assert.AreEqual(generic.TypeId, byType.TypeId);
            Assert.AreEqual(ComponentKind.Dense, generic.Kind);
            Assert.AreEqual(ComponentKind.Sparse, ComponentTypeRegistry.GetOrRegister<CachedSparse>().Kind);
            Assert.AreEqual(ComponentKind.Tag, ComponentTypeRegistry.GetOrRegister<CachedTag>().Kind);
        }

        [Test]
        public void GetOrRegister_RepeatedCalls_ReturnSameInfoWithoutNewRegistration()
        {
            var first = ComponentTypeRegistry.GetOrRegister<CachedDense>();
            var before = ComponentTypeRegistry.RegisteredTypeCount;
            var second = ComponentTypeRegistry.GetOrRegister<CachedDense>();

            Assert.AreEqual(first.TypeId, second.TypeId);
            Assert.AreEqual(before, ComponentTypeRegistry.RegisteredTypeCount);
        }
    }
}
