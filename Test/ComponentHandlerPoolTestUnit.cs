using CoreECS.Defines;
using CoreECS.Structures;
using CoreECS.Utils;

namespace CoreECS.Test
{
    [TestFixture]
    public class ComponentHandlerPoolTestUnit
    {
        private struct PositionComponent : IComponent<PositionComponent>
        {
            public int X;
        }

        [SetUp]
        public void Setup() => ComponentHandlerPool.Clear();

        [Test]
        public void Pool_ReusesReleasedCore_AndBumpsBindGeneration()
        {
            var location = EntityLocation.Pool.Get();
            var handler = ComponentHandlerPool.Get();
            handler.Bind(location, location.Generation, 7u, ComponentKind.Dense, 1u);
            var firstBind = handler.BindGeneration;

            ComponentHandlerPool.Release(handler);
            var reused = ComponentHandlerPool.Get();
            reused.Bind(location, location.Generation, 7u, ComponentKind.Dense, 2u);

            Assert.AreSame(handler, reused);
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
                var handler = position.Handler;
                Assert.IsTrue(position.NotNull);

                // Real lifecycle: removing the component releases its handler to the pool.
                first.DestroyComponent(position);

                // A later component of the same type reuses the released handler and rebinds it,
                // so the old handle points at a live handler that belongs to another instance.
                var second = world.CreateEntity();
                var rebound = second.CreateComponent<PositionComponent>();
                Assert.AreSame(handler, rebound.Handler);
                Assert.IsTrue(rebound.NotNull);

                Assert.IsFalse(position.NotNull);
                Assert.AreEqual(0UL, position.EntityId);

                // The stale handle must throw before touching the rebound instance's revision.
                Assert.Throws<NullReferenceException>(() => { _ = position.RW.X; });
                Assert.AreEqual(0UL, rebound.Revision);

                // And it must stay dead even when the safe checks are skipped.
                Assert.IsFalse(staleUntyped.Typed<PositionComponent>(@unsafe: true).NotNull);
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
            var handler = ComponentHandlerPool.Get();
            handler.Bind(location, location.Generation, 7u, ComponentKind.Dense, 1u);

            var first = new ComponentRef(handler);
            var second = new ComponentRef(handler);

            Assert.IsTrue(first.Equals(second));
            Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
        }
    }
}
