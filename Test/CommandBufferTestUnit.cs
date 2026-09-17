using CoreECS.Defines;
using CoreECS.Managers;

namespace CoreECS.Test
{
    [TestFixture]
    public class CommandBufferTestUnit
    {
        private struct Position : IComponent<Position>
        {
            public int X;
        }

        private struct Mana : IDiscreteComponent<Mana>
        {
            public int Value;
        }

        private struct PlayerTag : ITagComponent<PlayerTag>
        {
        }

        private World m_world;

        [SetUp]
        public void SetUp()
        {
            m_world = new World();
            m_world.Startup();
        }

        [TearDown]
        public void TearDown()
        {
            m_world?.Shutdown();
        }

        [Test]
        public void CreateCommandBuffer_RequiresReadyWorld()
        {
            var world = new World();

            Assert.Throws<InvalidOperationException>(() => world.CreateCommandBuffer());

            world.Startup();
            using (var commandBuffer = world.CreateCommandBuffer())
            {
                Assert.IsNotNull(commandBuffer);
            }

            world.Shutdown();
            Assert.Throws<InvalidOperationException>(() => world.CreateCommandBuffer());
        }

        [Test]
        public void CreateEntity_ReturnsDistinctPlaceholderHandles()
        {
            using var commandBuffer = m_world.CreateCommandBuffer();

            var first = commandBuffer.CreateEntity(0b01);
            var second = commandBuffer.CreateEntity();

            Assert.AreNotEqual(first, second);
            Assert.AreNotEqual(first.EntityId, second.EntityId);
            Assert.IsFalse(first.IsValid);
            Assert.IsFalse(second.IsValid);
            Assert.AreEqual(m_world, first.World);
        }

        [Test]
        public void RecordingCommands_DoesNotChangeWorldState()
        {
            var entity = m_world.CreateEntity();
            entity.CreateComponent(new Position { X = 1 });
            var componentManager = m_world.GetManager<ComponentManager>();
            var table = m_world.GetManager<EntityManager>().Table;
            var structureCount = componentManager.Structures.Count;

            using var commandBuffer = m_world.CreateCommandBuffer();
            var placeholder = commandBuffer.CreateEntity();
            commandBuffer.CreateComponent<Position>(placeholder, new Position { X = 9 });
            commandBuffer.CreateComponent<Mana>(placeholder);
            commandBuffer.CreateComponent<PlayerTag>(placeholder);
            commandBuffer.DestroyComponent<PlayerTag>(placeholder);
            commandBuffer.CreateComponent<Position>(entity, new Position { X = 2 });
            commandBuffer.DestroyComponent<Position>(entity);
            commandBuffer.DestroyEntity(entity);

            Assert.AreEqual(1, table.Count);
            Assert.AreEqual(structureCount, componentManager.Structures.Count);
            Assert.IsTrue(entity.IsValid);
            Assert.IsTrue(entity.HasComponent<Position>());
            Assert.AreEqual(1, entity.GetComponent<Position>().RO.X);
        }

        [Test]
        public void Dispose_DiscardsPendingCommands()
        {
            var componentManager = m_world.GetManager<ComponentManager>();
            var structureCount = componentManager.Structures.Count;

            using (var commandBuffer = m_world.CreateCommandBuffer())
            {
                var placeholder = commandBuffer.CreateEntity();
                commandBuffer.CreateComponent<Position>(placeholder, new Position { X = 9 });
                commandBuffer.CreateComponent<Mana>(placeholder);
            }

            Assert.AreEqual(0, m_world.GetManager<EntityManager>().Table.Count);
            Assert.AreEqual(structureCount, componentManager.Structures.Count);
        }

        [Test]
        public void Dispose_IsIdempotent()
        {
            var commandBuffer = m_world.CreateCommandBuffer();

            commandBuffer.Dispose();
            commandBuffer.Dispose();
        }

        [Test]
        public void AfterDispose_RecordingMethodsThrow()
        {
            var entity = m_world.CreateEntity();
            var commandBuffer = m_world.CreateCommandBuffer();
            commandBuffer.Dispose();

            Assert.Throws<InvalidOperationException>(() => commandBuffer.CreateEntity());
            Assert.Throws<InvalidOperationException>(() => commandBuffer.CreateComponent<Position>(entity));
            Assert.Throws<InvalidOperationException>(() => commandBuffer.CreateComponent(entity, new Position()));
            Assert.Throws<InvalidOperationException>(() => commandBuffer.DestroyComponent<Position>(entity));
            Assert.Throws<InvalidOperationException>(() => commandBuffer.DestroyEntity(entity));
        }

        [Test]
        public void Recording_RejectsDefaultAndForeignEntities()
        {
            using var commandBuffer = m_world.CreateCommandBuffer();

            Assert.Throws<InvalidOperationException>(() => commandBuffer.CreateComponent<Position>(default));

            var otherWorld = new World();
            otherWorld.Startup();
            try
            {
                var foreign = otherWorld.CreateEntity();
                Assert.Throws<InvalidOperationException>(() => commandBuffer.CreateComponent<Position>(foreign));
                Assert.Throws<InvalidOperationException>(() => commandBuffer.DestroyEntity(foreign));
            }
            finally
            {
                otherWorld.Shutdown();
            }
        }

        [Test]
        public void Recording_RejectsPlaceholderFromAnotherBuffer()
        {
            using var first = m_world.CreateCommandBuffer();
            using var second = m_world.CreateCommandBuffer();
            var placeholder = first.CreateEntity();

            Assert.Throws<InvalidOperationException>(() => second.CreateComponent<Position>(placeholder));
            Assert.Throws<InvalidOperationException>(() => second.DestroyEntity(placeholder));
        }
    }
}
