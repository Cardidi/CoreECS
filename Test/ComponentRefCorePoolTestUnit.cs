using CoreECS.Defines;
using CoreECS.Structures;
using CoreECS.Utils;

namespace CoreECS.Test
{
    [TestFixture]
    public class ComponentRefCorePoolTestUnit
    {
        private struct PositionComponent : IComponent<PositionComponent>
        {
            public int X;
        }

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
        public void Handle_StaleAfterLifecycleRebind_IsDead()
        {
            var world = new World();
            world.Startup();
            try
            {
                var first = world.CreateEntity();
                var position = first.CreateComponent<PositionComponent>();
                var staleUntyped = position.Untyped();
                var core = position.Core;
                Assert.IsTrue(position.NotNull);

                // Real lifecycle: removing the component releases its core to the pool.
                first.DestroyComponent(position);

                // A later component of the same type reuses the released core and rebinds it,
                // so the old handle points at a live core that belongs to another instance.
                var second = world.CreateEntity();
                var rebound = second.CreateComponent<PositionComponent>();
                Assert.AreSame(core, rebound.Core);
                Assert.IsTrue(rebound.NotNull);

                Assert.IsFalse(position.NotNull);
                Assert.AreEqual(0UL, position.EntityId);

                // The stale handle must throw before touching the rebound instance's revision.
                Assert.Throws<NullReferenceException>(() => { _ = position.RW.X; });
                Assert.AreEqual(0UL, rebound.Revision);

                // And it must stay dead even when the safe checks are skipped.
                Assert.IsFalse(staleUntyped.Typed<PositionComponent>(noSafeCheck: true).NotNull);
            }
            finally
            {
                world.Shutdown();
            }
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
