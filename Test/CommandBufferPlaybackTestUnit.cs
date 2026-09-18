using CoreECS.Defines;
using CoreECS.Managers;
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class CommandBufferPlaybackTestUnit
    {
        private struct Position : IComponent<Position>
        {
            public int X;
        }

        private struct Mana : ISparseComponent<Mana>
        {
            public int Value;
        }

        private struct PlayerTag : ITagComponent<PlayerTag>
        {
        }

        private struct LifecyclePosition : IComponent<LifecyclePosition>
        {
            public int X;

            public void OnCreate(ulong entityId)
            {
                CreateCount += 1;
                LastCreatedX = X;
            }

            public static int CreateCount;
            public static int LastCreatedX;
        }

        private World m_world;

        [SetUp]
        public void SetUp()
        {
            LifecyclePosition.CreateCount = 0;
            LifecyclePosition.LastCreatedX = 0;
            m_world = new World();
            m_world.Startup();
        }

        [TearDown]
        public void TearDown()
        {
            m_world?.Shutdown();
        }

        private EntityTable Table => m_world.GetManager<EntityManager>().Table;

        private static ulong SingleEntityId(World world)
        {
            ulong result = 0;
            foreach (var entityId in world.GetManager<EntityManager>().Table.EntityIds)
            {
                result = entityId;
            }

            return result;
        }

        private static ulong OtherEntityId(World world, ulong excluded)
        {
            foreach (var entityId in world.GetManager<EntityManager>().Table.EntityIds)
            {
                if (entityId != excluded) return entityId;
            }

            return 0;
        }

        [Test]
        public void Playback_CreatesEntityAndComponents_ForPlaceholder()
        {
            using var commandBuffer = m_world.CreateCommandBuffer();
            var placeholder = commandBuffer.CreateEntity(0b01);
            commandBuffer.CreateComponent<Position>(placeholder, new Position { X = 7 });
            commandBuffer.CreateComponent<Mana>(placeholder, new Mana { Value = 3 });
            commandBuffer.CreateComponent<PlayerTag>(placeholder);

            commandBuffer.Playback();

            var entity = m_world.GetEntity(SingleEntityId(m_world));
            Assert.IsTrue(entity.IsValid);
            Assert.AreEqual(0b01UL, entity.Mask);
            Assert.AreEqual(7, entity.GetComponent<Position>().RO.X);
            Assert.AreEqual(3, entity.GetComponent<Mana>().RO.Value);
            Assert.IsTrue(entity.HasComponent<PlayerTag>());
        }

        [Test]
        public void Playback_AppliesCommandsInRecordingOrder()
        {
            var entity = m_world.CreateEntity();
            entity.CreateComponent(new Position { X = 1 });

            using var commandBuffer = m_world.CreateCommandBuffer();
            commandBuffer.DestroyComponent<Position>(entity);
            commandBuffer.CreateComponent<Position>(entity, new Position { X = 9 });

            commandBuffer.Playback();

            Assert.IsTrue(entity.IsValid);
            Assert.AreEqual(9, entity.GetComponent<Position>().RO.X);
        }

        [Test]
        public void Playback_CreateThenDestroyComponent_LeavesEntityWithoutIt()
        {
            var entity = m_world.CreateEntity();

            using var commandBuffer = m_world.CreateCommandBuffer();
            commandBuffer.CreateComponent<Position>(entity, new Position { X = 1 });
            commandBuffer.DestroyComponent<Position>(entity);

            commandBuffer.Playback();

            Assert.IsTrue(entity.IsValid);
            Assert.IsFalse(entity.HasComponent<Position>());
        }

        [Test]
        public void Playback_CreateComponentWithValue_RunsOnCreateWithValue()
        {
            using var commandBuffer = m_world.CreateCommandBuffer();
            var placeholder = commandBuffer.CreateEntity();
            commandBuffer.CreateComponent<LifecyclePosition>(placeholder, new LifecyclePosition { X = 42 });

            commandBuffer.Playback();

            Assert.AreEqual(1, LifecyclePosition.CreateCount);
            Assert.AreEqual(42, LifecyclePosition.LastCreatedX);
        }

        [Test]
        public void Playback_DestroysPlaceholderAndRealEntities()
        {
            var real = m_world.CreateEntity();

            using var commandBuffer = m_world.CreateCommandBuffer();
            var placeholder = commandBuffer.CreateEntity();
            commandBuffer.CreateComponent<Position>(placeholder);
            commandBuffer.DestroyEntity(placeholder);
            commandBuffer.DestroyEntity(real);

            commandBuffer.Playback();

            Assert.AreEqual(0, Table.Count);
            Assert.IsFalse(real.IsValid);
        }

        [Test]
        public void Playback_SetMask_MigratesExistingAndCreatedEntities()
        {
            var existing = m_world.CreateEntity(0b01);
            existing.CreateComponent(new Position { X = 5 });
            existing.CreateComponent(new Mana { Value = 2 });

            using var commandBuffer = m_world.CreateCommandBuffer();
            var placeholder = commandBuffer.CreateEntity(0b10);
            commandBuffer.CreateComponent<Position>(placeholder, new Position { X = 8 });
            commandBuffer.SetMask(existing, 0b100);

            commandBuffer.Playback();

            Assert.AreEqual(0b100UL, existing.Mask);
            Assert.AreEqual(5, existing.GetComponent<Position>().RO.X);
            Assert.AreEqual(2, existing.GetComponent<Mana>().RO.Value);

            var created = m_world.GetEntity(OtherEntityId(m_world, existing.EntityId));
            Assert.AreEqual(0b10UL, created.Mask);
            Assert.AreEqual(8, created.GetComponent<Position>().RO.X);
        }

        [Test]
        public void Playback_ThenReuse_AppliesSecondBatchAndConsumesOldPlaceholders()
        {
            using var commandBuffer = m_world.CreateCommandBuffer();
            var first = commandBuffer.CreateEntity();
            commandBuffer.CreateComponent<Position>(first, new Position { X = 1 });

            commandBuffer.Playback();
            Assert.AreEqual(1, Table.Count);
            Assert.Throws<InvalidOperationException>(() => commandBuffer.CreateComponent<Position>(first));

            var second = commandBuffer.CreateEntity();
            commandBuffer.CreateComponent<Position>(second, new Position { X = 2 });
            commandBuffer.Playback();

            Assert.AreEqual(2, Table.Count);
        }

        [Test]
        public void Playback_EmptyBuffer_IsNoOpAndRepeatable()
        {
            using var commandBuffer = m_world.CreateCommandBuffer();

            commandBuffer.Playback();
            commandBuffer.Playback();

            Assert.AreEqual(0, Table.Count);
        }

        [Test]
        public void Playback_InsideTick_AppliesImmediately()
        {
            m_world.BeginTick();
            try
            {
                using var commandBuffer = m_world.CreateCommandBuffer();
                var placeholder = commandBuffer.CreateEntity();
                commandBuffer.CreateComponent<Position>(placeholder, new Position { X = 1 });
                commandBuffer.Playback();
            }
            finally
            {
                m_world.EndTick();
            }

            Assert.AreEqual(1, Table.Count);
        }

        [Test]
        public void Playback_AfterDispose_Throws()
        {
            var commandBuffer = m_world.CreateCommandBuffer();
            commandBuffer.Dispose();

            Assert.Throws<InvalidOperationException>(() => commandBuffer.Playback());
        }

        [Test]
        public void Playback_DeadRealTarget_Throws_AndBufferRemainsReusable()
        {
            var doomed = m_world.CreateEntity();

            using var commandBuffer = m_world.CreateCommandBuffer();
            commandBuffer.CreateComponent<Position>(doomed, new Position { X = 1 });
            m_world.DestroyEntity(doomed);

            Assert.Throws<InvalidOperationException>(() => commandBuffer.Playback());

            var placeholder = commandBuffer.CreateEntity();
            commandBuffer.CreateComponent<Position>(placeholder, new Position { X = 2 });
            commandBuffer.Playback();

            Assert.AreEqual(1, Table.Count);
            Assert.AreEqual(2, m_world.GetEntity(SingleEntityId(m_world)).GetComponent<Position>().RO.X);
        }

        [Test]
        public void Playback_CommandOnDestroyedPlaceholder_Throws()
        {
            using var commandBuffer = m_world.CreateCommandBuffer();
            var placeholder = commandBuffer.CreateEntity();
            commandBuffer.DestroyEntity(placeholder);
            commandBuffer.CreateComponent<Position>(placeholder);

            Assert.Throws<InvalidOperationException>(() => commandBuffer.Playback());
        }
    }
}
