using CoreECS.Defines;

namespace CoreECS.Test
{
    [TestFixture]
    public class ComponentInterfaceTestUnit
    {
        private struct DenseComponent : IComponent<DenseComponent>
        {
            public int Value;
        }

        private struct DiscreteComponent : IDiscreteComponent<DiscreteComponent>
        {
            public int Value;
        }

        private struct TagComponent : ITagComponent<TagComponent>
        {
        }

        private static T RequireComponent<T>(T value) where T : struct, IComponent<T>
        {
            return value;
        }

        [Test]
        public void DenseComponent_SatisfiesIComponentConstraint()
        {
            var component = RequireComponent(new DenseComponent { Value = 3 });
            Assert.AreEqual(3, component.Value);
        }

        [Test]
        public void DiscreteComponent_SatisfiesIComponentConstraint()
        {
            var component = RequireComponent(new DiscreteComponent { Value = 5 });
            Assert.AreEqual(5, component.Value);
        }

        [Test]
        public void TagComponent_SatisfiesIComponentConstraint()
        {
            RequireComponent(default(TagComponent));
            Assert.Pass();
        }

        [Test]
        public void DefaultLifecycleHooks_CanBeCalledOnTag()
        {
            IComponent<TagComponent> tag = default(TagComponent);
            tag.OnCreate(1UL);
            tag.OnDestroy(1UL);
            Assert.Pass();
        }
    }
}
