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

        private struct OwnedA
        {
            internal int Value;
        }

        private struct OwnedB
        {
            internal int Value;
        }

        private struct OwnedC
        {
            internal int Value;
        }

        private struct ReuseProbe
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

        [Test]
        public void Compact_ConsecutiveInternalHolesRelocatesEveryLiveEntry()
        {
            ComponentStorage<Position> storage = CreatePositionStorage(
                8,
                out _,
                out EntityId[] ids,
                out ComponentHandler[] handlers,
                out ulong[] bindGenerations);
            ComponentHandler deletedTwo = handlers[2];
            ComponentHandler deletedThree = handlers[3];
            ulong deletedTwoGeneration = bindGenerations[2];
            ulong deletedThreeGeneration = bindGenerations[3];

            storage.MarkRemove(ids[2]);
            storage.MarkRemove(ids[3]);
            storage.Compact();

            AssertLiveStorage(storage,
                Expected(ids, handlers, bindGenerations, 0),
                Expected(ids, handlers, bindGenerations, 1),
                Expected(ids, handlers, bindGenerations, 4),
                Expected(ids, handlers, bindGenerations, 5),
                Expected(ids, handlers, bindGenerations, 6),
                Expected(ids, handlers, bindGenerations, 7));
            AssertStalePair(deletedTwo, deletedTwoGeneration);
            AssertStalePair(deletedThree, deletedThreeGeneration);
        }

        [TestCase(1)]
        [TestCase(2)]
        public void Compact_InternalHoleAndTailHolesSkipsTrailingTombstones(int tailHoleCount)
        {
            ComponentStorage<Position> storage = CreatePositionStorage(
                8,
                out _,
                out EntityId[] ids,
                out ComponentHandler[] handlers,
                out ulong[] bindGenerations);
            ComponentHandler deletedInternal = handlers[1];
            ulong deletedInternalGeneration = bindGenerations[1];
            ComponentHandler deletedTail = handlers[7];
            ulong deletedTailGeneration = bindGenerations[7];

            storage.MarkRemove(ids[1]);
            storage.MarkRemove(ids[7]);
            if (tailHoleCount == 2)
            {
                storage.MarkRemove(ids[6]);
            }

            storage.Compact();

            if (tailHoleCount == 1)
            {
                AssertLiveStorage(storage,
                    Expected(ids, handlers, bindGenerations, 0),
                    Expected(ids, handlers, bindGenerations, 2),
                    Expected(ids, handlers, bindGenerations, 3),
                    Expected(ids, handlers, bindGenerations, 4),
                    Expected(ids, handlers, bindGenerations, 5),
                    Expected(ids, handlers, bindGenerations, 6));
            }
            else
            {
                AssertLiveStorage(storage,
                    Expected(ids, handlers, bindGenerations, 0),
                    Expected(ids, handlers, bindGenerations, 2),
                    Expected(ids, handlers, bindGenerations, 3),
                    Expected(ids, handlers, bindGenerations, 4),
                    Expected(ids, handlers, bindGenerations, 5));
                AssertStalePair(handlers[6], bindGenerations[6]);
            }

            AssertStalePair(deletedInternal, deletedInternalGeneration);
            AssertStalePair(deletedTail, deletedTailGeneration);
        }

        [Test]
        public void Compact_HandlerReusedBeforeCompactionKeepsOldPairStaleAndRelocatesNewBinding()
        {
            ComponentStorage<Position> storage = CreatePositionStorage(
                4,
                out EntityTable entities,
                out EntityId[] ids,
                out ComponentHandler[] handlers,
                out ulong[] bindGenerations);
            ComponentHandler released = handlers[1];
            ulong releasedGeneration = bindGenerations[1];

            storage.MarkRemove(ids[1]);
            EntityId appended = entities.Create(0);
            ComponentHandler rebound = storage.Create(appended, new Position { X = 999 });
            ulong reboundGeneration = rebound.BindGeneration;

            Assert.That(rebound, Is.SameAs(released));
            Assert.That(reboundGeneration, Is.Not.EqualTo(releasedGeneration));

            storage.Compact();

            AssertLiveStorage(storage,
                Expected(ids, handlers, bindGenerations, 0),
                new LiveExpectation(appended, 999, rebound, reboundGeneration),
                Expected(ids, handlers, bindGenerations, 2),
                Expected(ids, handlers, bindGenerations, 3));
            Assert.That(storage.Contains(ids[1]), Is.False);
            Assert.Throws<InvalidOperationException>(() => storage.Get(ids[1]));
            Assert.Throws<InvalidOperationException>(() => storage.GetHandler(ids[1]));
            AssertStalePair(rebound, releasedGeneration);
        }

        [Test]
        public void IntrusiveList_RemoveHeadPreservesBidirectionalNeighborsAndExcludesReusedHandler()
        {
            AssertIntrusiveRemoval(IntrusiveRemoval.Head);
        }

        [Test]
        public void IntrusiveList_RemoveMiddlePreservesBidirectionalNeighborsAndExcludesReusedHandler()
        {
            AssertIntrusiveRemoval(IntrusiveRemoval.Middle);
        }

        [Test]
        public void IntrusiveList_RemoveTailPreservesBidirectionalNeighborsAndExcludesReusedHandler()
        {
            AssertIntrusiveRemoval(IntrusiveRemoval.Tail);
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

        private static ComponentStorage<Position> CreatePositionStorage(
            int count,
            out EntityTable entities,
            out EntityId[] ids,
            out ComponentHandler[] handlers,
            out ulong[] bindGenerations)
        {
            entities = new EntityTable();
            var registry = new ComponentStorageRegistry(entities);
            var storage = (ComponentStorage<Position>)registry.GetOrCreate<Position>(ComponentKind.Data);
            ids = new EntityId[count];
            handlers = new ComponentHandler[count];
            bindGenerations = new ulong[count];
            for (int i = 0; i < count; i++)
            {
                ids[i] = entities.Create(0);
                handlers[i] = storage.Create(ids[i], new Position { X = 100 + i });
                bindGenerations[i] = handlers[i].BindGeneration;
            }

            return storage;
        }

        private static LiveExpectation Expected(
            EntityId[] ids,
            ComponentHandler[] handlers,
            ulong[] bindGenerations,
            int index)
        {
            return new LiveExpectation(ids[index], 100 + index, handlers[index], bindGenerations[index]);
        }

        private static void AssertLiveStorage(
            ComponentStorage<Position> storage,
            params LiveExpectation[] expected)
        {
            Assert.That(storage.LiveCount, Is.EqualTo(expected.Length));
            Assert.That(storage.AllocatedCount, Is.EqualTo(expected.Length));
            var packedOffsets = new HashSet<int>();
            for (int packedSlot = 0; packedSlot < storage.LiveCount; packedSlot++)
            {
                Assert.That(storage.GetEntityIdAt(packedSlot).IsValid, Is.True,
                    $"Packed slot {packedSlot} must not be a tombstone after Compact.");
            }

            foreach (LiveExpectation item in expected)
            {
                Assert.That(storage.Contains(item.EntityId), Is.True);
                Assert.That(storage.Get(item.EntityId).X, Is.EqualTo(item.Payload));
                Assert.That(storage.GetHandler(item.EntityId), Is.SameAs(item.Handler));
                Assert.That(item.Handler.BindGeneration, Is.EqualTo(item.BindGeneration));
                Assert.That(item.Handler.PackedOffset, Is.InRange(0, storage.LiveCount - 1));
                Assert.That(storage.GetEntityIdAt(item.Handler.PackedOffset), Is.EqualTo(item.EntityId));
                Assert.That(packedOffsets.Add(item.Handler.PackedOffset), Is.True,
                    "Each live handler must own a distinct packed slot.");
            }

            Assert.That(packedOffsets.Count, Is.EqualTo(expected.Length));
        }

        private static void AssertStalePair(ComponentHandler handler, ulong capturedGeneration)
        {
            bool pairIsCurrent = handler.EntityId.IsValid && handler.BindGeneration == capturedGeneration;
            Assert.That(pairIsCurrent, Is.False,
                "A removed (handler, BindGeneration) pair must not become current again.");
        }

        private static void AssertIntrusiveRemoval(IntrusiveRemoval removal)
        {
            var entities = new EntityTable();
            var registry = new ComponentStorageRegistry(entities);
            var storageA = (ComponentStorage<OwnedA>)registry.GetOrCreate<OwnedA>(ComponentKind.Data);
            var storageB = (ComponentStorage<OwnedB>)registry.GetOrCreate<OwnedB>(ComponentKind.Data);
            var storageC = (ComponentStorage<OwnedC>)registry.GetOrCreate<OwnedC>(ComponentKind.Data);
            var reuseStorage = (ComponentStorage<ReuseProbe>)registry.GetOrCreate<ReuseProbe>(ComponentKind.Data);
            EntityId owner = entities.Create(0);
            EntityId other = entities.Create(0);
            ComponentHandler a = storageA.Create(owner, new OwnedA { Value = 11 });
            ComponentHandler b = storageB.Create(owner, new OwnedB { Value = 22 });
            ComponentHandler c = storageC.Create(owner, new OwnedC { Value = 33 });
            ComponentHandler deleted;
            ComponentHandler[] expectedChain;
            ulong deletedGeneration;

            switch (removal)
            {
                case IntrusiveRemoval.Head:
                    deleted = c;
                    deletedGeneration = deleted.BindGeneration;
                    expectedChain = new[] { b, a };
                    storageC.MarkRemove(owner);
                    break;
                case IntrusiveRemoval.Middle:
                    deleted = b;
                    deletedGeneration = deleted.BindGeneration;
                    expectedChain = new[] { c, a };
                    storageB.MarkRemove(owner);
                    break;
                default:
                    deleted = a;
                    deletedGeneration = deleted.BindGeneration;
                    expectedChain = new[] { c, b };
                    storageA.MarkRemove(owner);
                    break;
            }

            Assert.That(deleted.EntityId.IsValid, Is.False);
            Assert.That(deleted.Storage, Is.Null);
            Assert.That(deleted.PackedOffset, Is.EqualTo(-1));
            Assert.That(deleted.Previous, Is.Null);
            Assert.That(deleted.Next, Is.Null);
            Assert.That(registry.PooledHandlerCountForTesting, Is.EqualTo(1));
            AssertHandlerChain(entities, owner, expectedChain);
            AssertRemainingOwnedComponents(storageA, storageB, storageC, owner, removal, a, b, c);

            ComponentHandler reused = reuseStorage.Create(other, new ReuseProbe { Value = 44 });

            Assert.That(reused, Is.SameAs(deleted));
            Assert.That(reused.BindGeneration, Is.Not.EqualTo(deletedGeneration));
            Assert.That(reuseStorage.Contains(other), Is.True);
            Assert.That(reuseStorage.Get(other).Value, Is.EqualTo(44));
            Assert.That(reuseStorage.GetHandler(other), Is.SameAs(reused));
            Assert.That(registry.PooledHandlerCountForTesting, Is.Zero);
            Assert.That(expectedChain, Does.Not.Contain(reused));
            AssertHandlerChain(entities, owner, expectedChain);
            AssertRemainingOwnedComponents(storageA, storageB, storageC, owner, removal, a, b, c);
            AssertStalePair(reused, deletedGeneration);
        }

        private static void AssertHandlerChain(
            EntityTable entities,
            EntityId owner,
            ComponentHandler[] expected)
        {
            Assert.That(entities.GetComponentHead(owner), Is.SameAs(expected[0]));
            for (int i = 0; i < expected.Length; i++)
            {
                ComponentHandler? previous = i == 0 ? null : expected[i - 1];
                ComponentHandler? next = i == expected.Length - 1 ? null : expected[i + 1];
                Assert.That(expected[i].Previous, Is.SameAs(previous));
                Assert.That(expected[i].Next, Is.SameAs(next));
            }
        }

        private static void AssertRemainingOwnedComponents(
            ComponentStorage<OwnedA> storageA,
            ComponentStorage<OwnedB> storageB,
            ComponentStorage<OwnedC> storageC,
            EntityId owner,
            IntrusiveRemoval removal,
            ComponentHandler a,
            ComponentHandler b,
            ComponentHandler c)
        {
            if (removal == IntrusiveRemoval.Tail)
            {
                Assert.That(storageA.Contains(owner), Is.False);
                Assert.Throws<InvalidOperationException>(() => storageA.GetHandler(owner));
            }
            else
            {
                Assert.That(storageA.Contains(owner), Is.True);
                Assert.That(storageA.Get(owner).Value, Is.EqualTo(11));
                Assert.That(storageA.GetHandler(owner), Is.SameAs(a));
            }

            if (removal == IntrusiveRemoval.Middle)
            {
                Assert.That(storageB.Contains(owner), Is.False);
                Assert.Throws<InvalidOperationException>(() => storageB.GetHandler(owner));
            }
            else
            {
                Assert.That(storageB.Contains(owner), Is.True);
                Assert.That(storageB.Get(owner).Value, Is.EqualTo(22));
                Assert.That(storageB.GetHandler(owner), Is.SameAs(b));
            }

            if (removal == IntrusiveRemoval.Head)
            {
                Assert.That(storageC.Contains(owner), Is.False);
                Assert.Throws<InvalidOperationException>(() => storageC.GetHandler(owner));
            }
            else
            {
                Assert.That(storageC.Contains(owner), Is.True);
                Assert.That(storageC.Get(owner).Value, Is.EqualTo(33));
                Assert.That(storageC.GetHandler(owner), Is.SameAs(c));
            }
        }

        private readonly struct LiveExpectation
        {
            internal readonly EntityId EntityId;
            internal readonly int Payload;
            internal readonly ComponentHandler Handler;
            internal readonly ulong BindGeneration;

            internal LiveExpectation(
                EntityId entityId,
                int payload,
                ComponentHandler handler,
                ulong bindGeneration)
            {
                EntityId = entityId;
                Payload = payload;
                Handler = handler;
                BindGeneration = bindGeneration;
            }
        }

        private enum IntrusiveRemoval
        {
            Head,
            Middle,
            Tail
        }
    }
}
