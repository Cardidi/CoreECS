using CoreECS.Defines;

namespace CoreECS.Test
{
    [TestFixture]
    public class EntityExtensionTestUnit
    {
        private World _world;

        [SetUp]
        public void Setup()
        {
            _world = new World();
            _world.Startup();
        }

        [TearDown]
        public void TearDown()
        {
            _world?.Shutdown();
        }

        [Test]
        public void TryGetComponent_Dense_ReturnsRefAndTrueWhenPresent()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();

            Assert.IsTrue(entity.TryGetComponent<PositionComponent>(out var componentRef));
            Assert.IsTrue(componentRef.NotNull);
            Assert.AreEqual(entity.EntityId, componentRef.EntityId);
        }

        [Test]
        public void TryGetComponent_Absent_ReturnsFalseAndDefault()
        {
            var entity = _world.CreateEntity();

            Assert.IsFalse(entity.TryGetComponent<PositionComponent>(out var componentRef));
            Assert.IsFalse(componentRef.NotNull);
        }

        [Test]
        public void TryGetComponent_Sparse_ReturnsRefAndTrueWhenPresent()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent(new ManaComponent { Value = 4 });

            Assert.IsTrue(entity.TryGetComponent<ManaComponent>(out var componentRef));
            Assert.IsTrue(componentRef.NotNull);
            Assert.AreEqual(4, componentRef.RO.Value);
        }

        [Test]
        public void TryGetComponent_Tag_ReturnsTrueWithDefaultRef()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<PlayerTag>();

            Assert.IsTrue(entity.TryGetComponent<PlayerTag>(out var componentRef));
            Assert.IsFalse(componentRef.NotNull);
            Assert.IsTrue(entity.HasComponent<PlayerTag>());
        }

        [Test]
        public void GetOrCreateComponent_Dense_CreatesWhenAbsentAndReturnsExistingWhenPresent()
        {
            var entity = _world.CreateEntity();

            Assert.IsFalse(entity.GetOrCreateComponent<PositionComponent>(out var created));
            Assert.IsTrue(created.NotNull);

            Assert.IsTrue(entity.GetOrCreateComponent<PositionComponent>(out var existing));
            Assert.AreEqual(created, existing);
        }

        [Test]
        public void GetOrCreateComponent_Sparse_CreatesWhenAbsent()
        {
            var entity = _world.CreateEntity();

            Assert.IsFalse(entity.GetOrCreateComponent<ManaComponent>(out var created));
            Assert.IsTrue(created.NotNull);
            Assert.AreEqual(entity.EntityId, created.EntityId);
        }

        [Test]
        public void GetOrCreateComponent_Tag_ReturnsFalseAndDefaultRef()
        {
            var entity = _world.CreateEntity();

            Assert.IsFalse(entity.GetOrCreateComponent<PlayerTag>(out var created));
            Assert.IsFalse(created.NotNull);
            Assert.IsTrue(entity.HasComponent<PlayerTag>());
        }

        [Test]
        public void GetComponent_Tag_ReturnsDefaultWhileHasComponentIsTrue()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<PlayerTag>();

            Assert.IsTrue(entity.HasComponent<PlayerTag>());
            Assert.IsFalse(entity.GetComponent<PlayerTag>().NotNull);
            Assert.AreEqual(0, entity.GetComponents<PlayerTag>().Length);
        }

        [Test]
        public void GetComponents_OmitsTagsAndCollectionOverloadAddsDenseAndSparse()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();
            entity.CreateComponent(new ManaComponent { Value = 1 });
            entity.CreateComponent<PlayerTag>();

            var components = entity.GetComponents();
            Assert.AreEqual(2, components.Length);
            foreach (var componentRef in components)
            {
                Assert.IsTrue(componentRef.NotNull);
                Assert.AreNotEqual(typeof(PlayerTag), componentRef.RuntimeType);
            }

            var results = new List<ComponentRef>();
            var added = entity.GetComponents(results);
            Assert.AreEqual(2, added);
            Assert.AreEqual(2, results.Count);
        }

        // Test components
        private struct PositionComponent : IComponent<PositionComponent>
        {
            public float X;
        }

        private struct ManaComponent : ISparseComponent<ManaComponent>
        {
            public int Value;
        }

        private struct PlayerTag : ITagComponent<PlayerTag>
        {
        }
    }
}
