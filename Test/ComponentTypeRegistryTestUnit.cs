using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class ComponentTypeRegistryTestUnit
    {
        [Test]
        public void GetOrRegister_SameTypeAcrossContexts_ReturnsSameMetadataAndTypeId()
        {
            ComponentTypeMetadata first = RegisterStableTypeFromFirstContext();
            ComponentTypeMetadata second = RegisterStableTypeFromSecondContext();

            Assert.That(second, Is.SameAs(first));
            Assert.That(second.TypeId, Is.EqualTo(first.TypeId));
            Assert.That(second.ComponentType, Is.EqualTo(typeof(StableRegistryComponent)));
            Assert.That(second.Kind, Is.EqualTo(ComponentKind.Data));
        }

        [Test]
        public void GetOrRegister_DistinctTypes_AssignsDistinctTypeIds()
        {
            ComponentTypeMetadata first = ComponentTypeRegistry.GetOrRegister<DistinctRegistryComponentA>();
            ComponentTypeMetadata second = ComponentTypeRegistry.GetOrRegister<DistinctRegistryComponentB>();

            Assert.That(second.TypeId, Is.Not.EqualTo(first.TypeId));
            Assert.That(first.Kind, Is.EqualTo(ComponentKind.Data));
            Assert.That(second.Kind, Is.EqualTo(ComponentKind.Data));
        }

        [Test]
        public void ComponentType_GenericCache_UsesRegistryMetadataInstance()
        {
            ComponentTypeMetadata registered = ComponentTypeRegistry.GetOrRegister<CachedRegistryComponent>();

            Assert.That(ComponentType<CachedRegistryComponent>.Metadata, Is.SameAs(registered));
        }

        private static ComponentTypeMetadata RegisterStableTypeFromFirstContext()
        {
            return ComponentTypeRegistry.GetOrRegister<StableRegistryComponent>();
        }

        private static ComponentTypeMetadata RegisterStableTypeFromSecondContext()
        {
            return ComponentTypeRegistry.GetOrRegister<StableRegistryComponent>();
        }

        private struct StableRegistryComponent
        {
        }

        private struct DistinctRegistryComponentA
        {
        }

        private struct DistinctRegistryComponentB
        {
        }

        private struct CachedRegistryComponent
        {
        }
    }
}
