using CoreECS.Defines;
using CoreECS.Structures;
using CoreECS.Utils;

namespace CoreECS.Test
{
    [TestFixture]
    public class ComponentRefCorePoolTestUnit
    {
        [SetUp]
        public void Setup() => ComponentRefCorePool.Clear();

        [Test]
        public void Pool_ReusesReleasedCore_AndBumpsBindGeneration()
        {
            var location = EntityLocation.Pool.Get();
            var core = ComponentRefCorePool.Get();
            core.Bind(location, location.Generation, 7u, ComponentKind.Dense, 1u);
            var firstBind = core.BindGeneration;

            ComponentRefCorePool.Release(core);
            var reused = ComponentRefCorePool.Get();
            reused.Bind(location, location.Generation, 7u, ComponentKind.Dense, 2u);

            Assert.AreSame(core, reused);
            Assert.Greater(reused.BindGeneration, firstBind);
        }

        [Test]
        public void Handle_StaleAfterPoolRebind_IsNotNullFalse()
        {
            var location = EntityLocation.Pool.Get();
            var core = ComponentRefCorePool.Get();
            core.Bind(location, location.Generation, 7u, ComponentKind.Dense, 1u);
            var handle = new ComponentRef(core);

            ComponentRefCorePool.Release(core);
            var reused = ComponentRefCorePool.Get();
            reused.Bind(location, location.Generation, 7u, ComponentKind.Dense, 2u);

            Assert.IsFalse(handle.NotNull);
            Assert.AreEqual(0UL, handle.EntityId);
        }

        [Test]
        public void Handles_SameCoreAndGeneration_AreEqual_AndHashStable()
        {
            var location = EntityLocation.Pool.Get();
            var core = ComponentRefCorePool.Get();
            core.Bind(location, location.Generation, 7u, ComponentKind.Dense, 1u);

            var first = new ComponentRef(core);
            var second = new ComponentRef(core);

            Assert.IsTrue(first.Equals(second));
            Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
        }
    }
}
