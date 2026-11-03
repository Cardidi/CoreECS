using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class ComponentStorageTestUnit
    {
        private struct Position
        {
            internal int X;
            internal int Y;
        }

        private struct Velocity
        {
            internal int Value;
        }

        [Test]
        public void Registry_CreatesOneCallerSelectedStoragePerWorldAndTypeId()
        {
            var firstRegistry = new ComponentStorageRegistry(new EntityTable());
            var secondRegistry = new ComponentStorageRegistry(new EntityTable());

            var first = (ComponentStorage<Position>)firstRegistry.GetOrCreate<Position>(ComponentKind.Data);
            var repeated = (ComponentStorage<Position>)firstRegistry.GetOrCreate<Position>(ComponentKind.Data);
            var otherWorld = (ComponentStorage<Position>)secondRegistry.GetOrCreate<Position>(ComponentKind.Data);

            Assert.That(repeated, Is.SameAs(first));
            Assert.That(otherWorld, Is.Not.SameAs(first));
            Assert.That(firstRegistry.Count, Is.EqualTo(1));
            Assert.That(firstRegistry.TryGet(first.TypeId, out IComponentStorage storage), Is.True);
            Assert.That(storage, Is.SameAs(first));
            Assert.Throws<InvalidOperationException>(() =>
                firstRegistry.GetOrCreate<Position>(ComponentKind.Tag));
        }

        [Test]
        public void Create_StoresDefaultOrInitialPayloadAndAttachesUniqueHandlers()
        {
            var entities = new EntityTable();
            var registry = new ComponentStorageRegistry(entities);
            var positions = (ComponentStorage<Position>)registry.GetOrCreate<Position>(ComponentKind.Data);
            var velocities = (TagStorage<Velocity>)registry.GetOrCreate<Velocity>(ComponentKind.Tag);
            EntityId first = entities.Create(1);
            EntityId second = entities.Create(2);
            var initial = new Position { X = 7, Y = -3 };

            ComponentHandler firstHandler = positions.Create(first);
            ComponentHandler secondHandler = positions.Create(second, in initial);
            ComponentHandler tagHandler = velocities.Create(first);

            Assert.That(positions.Contains(first), Is.True);
            Assert.That(positions.Contains(second), Is.True);
            Assert.That(positions.Get(first).X, Is.Zero);
            Assert.That(positions.Get(second).X, Is.EqualTo(7));
            Assert.That(positions.Get(second).Y, Is.EqualTo(-3));
            Assert.That(positions.GetHandler(first), Is.SameAs(firstHandler));
            Assert.That(firstHandler.Storage, Is.SameAs(positions));
            Assert.That(firstHandler.TypeId, Is.EqualTo(positions.TypeId));
            Assert.That(firstHandler.PackedOffset, Is.Zero);
            Assert.That(secondHandler.PackedOffset, Is.EqualTo(1));
            Assert.That(positions.LiveCount, Is.EqualTo(2));
            Assert.That(positions.AllocatedCount, Is.EqualTo(2));
            Assert.That(entities.GetComponentHead(first), Is.SameAs(tagHandler));
            Assert.That(tagHandler.Next, Is.SameAs(firstHandler));
            Assert.That(firstHandler.Previous, Is.SameAs(tagHandler));
            Assert.Throws<InvalidOperationException>(() => positions.Create(first));
        }

        [Test]
        public void PayloadReference_RemainsValidWhenPackedPagesGrow()
        {
            var entities = new EntityTable();
            var storage = (ComponentStorage<Position>)new ComponentStorageRegistry(entities)
                .GetOrCreate<Position>(ComponentKind.Data);
            EntityId first = entities.Create(0);
            storage.Create(first, new Position { X = 1, Y = 2 });
            ref Position captured = ref storage.Get(first);

            for (int i = 0; i < 28; i++)
            {
                EntityId entityId = entities.Create(0);
                storage.Create(entityId, new Position { X = i + 10 });
            }

            captured.X = 99;

            Assert.That(storage.Get(first).X, Is.EqualTo(99));
            Assert.That(storage.PayloadPageCount, Is.EqualTo(4));
            Assert.That(storage.Capacity, Is.EqualTo(60));
        }

        [Test]
        public void MarkRemove_InvalidatesMembershipAndHandlerButLeavesPayloadUntilCompact()
        {
            var entities = new EntityTable();
            var registry = new ComponentStorageRegistry(entities);
            var storage = (ComponentStorage<Position>)registry.GetOrCreate<Position>(ComponentKind.Data);
            EntityId first = entities.Create(0);
            EntityId removed = entities.Create(0);
            EntityId third = entities.Create(0);
            storage.Create(first, new Position { X = 1 });
            ComponentHandler removedHandler = storage.Create(removed, new Position { X = 2 });
            storage.Create(third, new Position { X = 3 });
            ulong removedBindGeneration = removedHandler.BindGeneration;

            Assert.That(storage.MarkRemove(removed), Is.True);

            Assert.That(storage.Contains(removed), Is.False);
            Assert.That(storage.GetEntityIdAt(1), Is.EqualTo(default(EntityId)));
            Assert.That(storage.GetPayloadAt(1).X, Is.EqualTo(2));
            Assert.That(storage.LiveCount, Is.EqualTo(2));
            Assert.That(storage.AllocatedCount, Is.EqualTo(3));
            Assert.That(removedHandler.EntityId.IsValid, Is.False);
            Assert.That(removedHandler.Storage, Is.Null);
            Assert.That(removedHandler.PackedOffset, Is.EqualTo(-1));
            Assert.Throws<InvalidOperationException>(() => storage.GetHandler(removed));

            EntityId appended = entities.Create(0);
            ComponentHandler appendedHandler = storage.Create(appended, new Position { X = 4 });

            Assert.That(appendedHandler.PackedOffset, Is.EqualTo(3));
            Assert.That(appendedHandler.BindGeneration, Is.Not.EqualTo(removedBindGeneration));
            Assert.That(storage.AllocatedCount, Is.EqualTo(4));
            Assert.That(storage.GetPayloadAt(1).X, Is.EqualTo(2));
        }

        [Test]
        public void Compact_MovesLiveTailIntoHolesAndRelocatesSparseAndHandlers()
        {
            var entities = new EntityTable();
            var storage = (ComponentStorage<Position>)new ComponentStorageRegistry(entities)
                .GetOrCreate<Position>(ComponentKind.Data);
            var ids = new EntityId[7];
            var handlers = new ComponentHandler[7];
            for (int i = 0; i < ids.Length; i++)
            {
                ids[i] = entities.Create(0);
                handlers[i] = storage.Create(ids[i], new Position { X = i });
            }

            int capacity = storage.Capacity;
            storage.MarkRemove(ids[1]);
            storage.MarkRemove(ids[4]);

            storage.Compact();

            Assert.That(storage.LiveCount, Is.EqualTo(5));
            Assert.That(storage.AllocatedCount, Is.EqualTo(5));
            Assert.That(storage.Capacity, Is.EqualTo(capacity));
            Assert.That(storage.Get(ids[6]).X, Is.EqualTo(6));
            Assert.That(storage.Get(ids[5]).X, Is.EqualTo(5));
            Assert.That(handlers[6].PackedOffset, Is.EqualTo(1));
            Assert.That(handlers[5].PackedOffset, Is.EqualTo(4));
            Assert.That(storage.GetHandler(ids[6]), Is.SameAs(handlers[6]));
            Assert.That(storage.GetHandler(ids[5]), Is.SameAs(handlers[5]));
            Assert.That(storage.GetEntityIdAt(1), Is.EqualTo(ids[6]));
            Assert.That(storage.GetEntityIdAt(4), Is.EqualTo(ids[5]));
            for (int slot = 0; slot < storage.AllocatedCount; slot++)
            {
                Assert.That(storage.GetEntityIdAt(slot).IsValid, Is.True);
            }
        }

        [Test]
        public void Contains_RejectsStaleGenerationAfterEntityIndexReuse()
        {
            var entities = new EntityTable();
            var storage = (ComponentStorage<Position>)new ComponentStorageRegistry(entities)
                .GetOrCreate<Position>(ComponentKind.Data);
            EntityId stale = entities.Create(0);
            storage.Create(stale, new Position { X = 1 });
            storage.MarkRemove(stale);
            entities.MarkDestroyed(stale);
            entities.CommitReleases();
            EntityId current = entities.Create(0);

            storage.Create(current, new Position { X = 2 });

            Assert.That(current.Index, Is.EqualTo(stale.Index));
            Assert.That(current.Generation, Is.Not.EqualTo(stale.Generation));
            Assert.That(storage.Contains(stale), Is.False);
            Assert.That(storage.Contains(current), Is.True);
            Assert.Throws<InvalidOperationException>(() => storage.Get(stale));
            Assert.That(storage.Get(current).X, Is.EqualTo(2));
        }

        [Test]
        public void Clear_DetachesOnlyThisStorageAndAllowsReuseFromSlotZero()
        {
            var entities = new EntityTable();
            var registry = new ComponentStorageRegistry(entities);
            var positions = (ComponentStorage<Position>)registry.GetOrCreate<Position>(ComponentKind.Data);
            var velocities = (ComponentStorage<Velocity>)registry.GetOrCreate<Velocity>(ComponentKind.Data);
            EntityId entityId = entities.Create(0);
            ComponentHandler position = positions.Create(entityId, new Position { X = 1 });
            ComponentHandler velocity = velocities.Create(entityId, new Velocity { Value = 2 });

            positions.Clear();

            Assert.That(position.EntityId.IsValid, Is.False);
            Assert.That(entities.GetComponentHead(entityId), Is.SameAs(velocity));
            Assert.That(velocity.Next, Is.Null);
            Assert.That(positions.LiveCount, Is.Zero);
            Assert.That(positions.AllocatedCount, Is.Zero);

            ComponentHandler recreated = positions.Create(entityId, new Position { X = 3 });
            Assert.That(recreated.PackedOffset, Is.Zero);
            Assert.That(positions.Get(entityId).X, Is.EqualTo(3));
        }

        [Test]
        public void Create_RejectsDefaultAndDeadEntitiesWithoutAllocatingSlots()
        {
            var entities = new EntityTable();
            var storage = (ComponentStorage<Position>)new ComponentStorageRegistry(entities)
                .GetOrCreate<Position>(ComponentKind.Data);
            EntityId dead = entities.Create(0);
            entities.MarkDestroyed(dead);

            Assert.Throws<ArgumentException>(() => storage.Create(default(EntityId)));
            Assert.Throws<ArgumentException>(() => storage.Create(dead));
            Assert.That(storage.LiveCount, Is.Zero);
            Assert.That(storage.AllocatedCount, Is.Zero);
        }

        [Test]
        public void Create_CommonPageAllocationFailureDoesNotPublishComponent()
        {
            var entities = new EntityTable();
            var registry = new ComponentStorageRegistry(entities);
            var positions = (ComponentStorage<Position>)registry.GetOrCreate<Position>(ComponentKind.Data);
            var velocities = (ComponentStorage<Velocity>)registry.GetOrCreate<Velocity>(ComponentKind.Data);
            EntityId entityId = entities.Create(0);
            ComponentHandler originalHead = velocities.Create(entityId, new Velocity { Value = 4 });
            positions.BeforeEntityPageAllocationForTesting = ThrowInjectedFailure;

            Assert.Throws<InvalidOperationException>(() =>
                positions.Create(entityId, new Position { X = 7 }));

            AssertCreateWasNotPublished(positions, entities, entityId, originalHead);
            Assert.That(registry.PooledHandlerCountForTesting, Is.Zero);

            positions.BeforeEntityPageAllocationForTesting = null;
            positions.BeforeHandlerPageAllocationForTesting = ThrowInjectedFailure;

            Assert.Throws<InvalidOperationException>(() =>
                positions.Create(entityId, new Position { X = 8 }));

            AssertCreateWasNotPublished(positions, entities, entityId, originalHead);
            Assert.That(registry.PooledHandlerCountForTesting, Is.Zero);
        }

        [Test]
        public void Create_HandlerReservationFailureReturnsHandlerWithoutPublishingComponent()
        {
            var entities = new EntityTable();
            var registry = new ComponentStorageRegistry(entities);
            var positions = (ComponentStorage<Position>)registry.GetOrCreate<Position>(ComponentKind.Data);
            EntityId entityId = entities.Create(0);
            positions.AfterHandlerReservedForTesting = _ => ThrowInjectedFailure();

            Assert.Throws<InvalidOperationException>(() => positions.Create(entityId));

            AssertCreateWasNotPublished(positions, entities, entityId, null);
            Assert.That(registry.PooledHandlerCountForTesting, Is.EqualTo(1));

            positions.AfterHandlerReservedForTesting = null;
            ComponentHandler reused = positions.Create(entityId, new Position { X = 8 });

            Assert.That(reused.PackedOffset, Is.Zero);
            Assert.That(positions.Get(entityId).X, Is.EqualTo(8));
            Assert.That(registry.PooledHandlerCountForTesting, Is.Zero);
        }

        [Test]
        public void Create_PayloadPageAllocationFailureReturnsHandlerWithoutPublishingComponent()
        {
            var entities = new EntityTable();
            var registry = new ComponentStorageRegistry(entities);
            var positions = (ComponentStorage<Position>)registry.GetOrCreate<Position>(ComponentKind.Data);
            EntityId entityId = entities.Create(0);
            positions.BeforePayloadPageAllocationForTesting = ThrowInjectedFailure;

            Assert.Throws<InvalidOperationException>(() =>
                positions.Create(entityId, new Position { X = 9 }));

            AssertCreateWasNotPublished(positions, entities, entityId, null);
            Assert.That(registry.PooledHandlerCountForTesting, Is.EqualTo(1));

            positions.BeforePayloadPageAllocationForTesting = null;
            ComponentHandler reused = positions.Create(entityId, new Position { X = 10 });

            Assert.That(reused.PackedOffset, Is.Zero);
            Assert.That(positions.Get(entityId).X, Is.EqualTo(10));
            Assert.That(registry.PooledHandlerCountForTesting, Is.Zero);
        }

        [Test]
        public void Create_AttachFailureRollsBackSparsePackedSlotsAndHandler()
        {
            var entities = new EntityTable();
            var registry = new ComponentStorageRegistry(entities);
            var positions = (ComponentStorage<Position>)registry.GetOrCreate<Position>(ComponentKind.Data);
            var velocities = (ComponentStorage<Velocity>)registry.GetOrCreate<Velocity>(ComponentKind.Data);
            EntityId entityId = entities.Create(0);
            ComponentHandler originalHead = velocities.Create(entityId, new Velocity { Value = 5 });
            positions.BeforeAttachForTesting = ThrowInjectedFailure;

            Assert.Throws<InvalidOperationException>(() =>
                positions.Create(entityId, new Position { X = 11 }));

            AssertCreateWasNotPublished(positions, entities, entityId, originalHead);
            Assert.That(registry.PooledHandlerCountForTesting, Is.EqualTo(1));

            positions.BeforeAttachForTesting = null;
            ComponentHandler reused = positions.Create(entityId, new Position { X = 12 });

            Assert.That(reused.PackedOffset, Is.Zero);
            Assert.That(positions.Get(entityId).X, Is.EqualTo(12));
            Assert.That(entities.GetComponentHead(entityId), Is.SameAs(reused));
            Assert.That(reused.Next, Is.SameAs(originalHead));
            Assert.That(registry.PooledHandlerCountForTesting, Is.Zero);
        }

        private static void AssertCreateWasNotPublished(
            ComponentStorage<Position> storage,
            EntityTable entities,
            EntityId entityId,
            ComponentHandler? expectedHead)
        {
            Assert.That(storage.Contains(entityId), Is.False);
            Assert.That(storage.LiveCount, Is.Zero);
            Assert.That(storage.AllocatedCount, Is.Zero);
            Assert.That(entities.GetComponentHead(entityId), Is.SameAs(expectedHead));
        }

        private static void ThrowInjectedFailure()
        {
            throw new InvalidOperationException("Injected allocation/publication failure.");
        }
    }
}
