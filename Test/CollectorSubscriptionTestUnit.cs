using CoreECS.Defines;
using CoreECS.Managers;
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public sealed class CollectorSubscriptionTestUnit
    {
        [Test]
        public void InvertedIndexes_DeliverOnlyEligibleStructuralRevisionAndLifecycleReasons()
        {
            var subscriptions = new CollectorSubscriptions();
            var position = Register(subscriptions,
                EntityMatcher.With.OfAll<Position>(), EntityCollectorFlag.Default);
            var velocity = Register(subscriptions,
                EntityMatcher.With.OfAll<Velocity>(), EntityCollectorFlag.Default);
            var wildcard = Register(subscriptions,
                EntityMatcher.With.OfAll<Position>(), EntityCollectorFlag.RevisionAsChange);
            var empty = Register(subscriptions, EntityMatcher.With, EntityCollectorFlag.Default);
            var noneOnly = Register(subscriptions,
                EntityMatcher.With.OfNone<Velocity>(), EntityCollectorFlag.Default);
            EntityId entityId = EntityId.Create(7, 3);
            uint positionTypeId = ComponentTypeRegistry.GetOrRegister<Position>().TypeId;

            subscriptions.DispatchStructural(positionTypeId, entityId);

            Assert.Multiple(() =>
            {
                AssertReason(position, entityId, CollectorPendingReason.Structural);
                AssertReason(wildcard, entityId, CollectorPendingReason.Structural);
                AssertReason(empty, entityId, CollectorPendingReason.Structural);
                Assert.That(velocity.Pending.Count, Is.Zero);
                Assert.That(noneOnly.Pending.Count, Is.Zero);
                Assert.That(subscriptions.LastDeliveredCountForTesting, Is.EqualTo(3));
                Assert.That(subscriptions.LastUnrelatedCountForTesting, Is.EqualTo(2));
            });

            Clear(position, velocity, wildcard, empty, noneOnly);
            subscriptions.DispatchRevision(
                new ComponentChangeJournal.Entry(1, entityId, positionTypeId));

            Assert.Multiple(() =>
            {
                AssertReason(position, entityId, CollectorPendingReason.Revision);
                AssertReason(wildcard, entityId, CollectorPendingReason.Revision);
                AssertReason(empty, entityId, CollectorPendingReason.Revision);
                Assert.That(velocity.Pending.Count, Is.Zero);
                Assert.That(noneOnly.Pending.Count, Is.Zero);
                Assert.That(subscriptions.LastDeliveredCountForTesting, Is.EqualTo(3));
                Assert.That(subscriptions.LastUnrelatedCountForTesting, Is.EqualTo(2));
            });

            Clear(position, velocity, wildcard, empty, noneOnly);
            subscriptions.DispatchLifecycle(entityId);

            Assert.Multiple(() =>
            {
                AssertReason(empty, entityId, CollectorPendingReason.Lifecycle);
                AssertReason(noneOnly, entityId, CollectorPendingReason.Lifecycle);
                Assert.That(position.Pending.Count, Is.Zero);
                Assert.That(velocity.Pending.Count, Is.Zero);
                Assert.That(wildcard.Pending.Count, Is.Zero);
                Assert.That(subscriptions.LastDeliveredCountForTesting, Is.EqualTo(2));
                Assert.That(subscriptions.LastUnrelatedCountForTesting, Is.EqualTo(3));
            });
        }

        [Test]
        public void Unregister_UnlinksCollectorFromEverySubscriptionList()
        {
            var subscriptions = new CollectorSubscriptions();
            var target = Register(subscriptions,
                EntityMatcher.With.OfNone<Position>(), EntityCollectorFlag.Default);
            EntityId entityId = EntityId.Create(8, 4);
            uint positionTypeId = ComponentTypeRegistry.GetOrRegister<Position>().TypeId;

            subscriptions.Unregister(target);
            subscriptions.DispatchStructural(positionTypeId, entityId);
            subscriptions.DispatchRevision(
                new ComponentChangeJournal.Entry(1, entityId, positionTypeId));
            subscriptions.DispatchLifecycle(entityId);

            Assert.Multiple(() =>
            {
                Assert.That(target.Pending.Count, Is.Zero);
                Assert.That(subscriptions.RegisteredCountForTesting, Is.Zero);
                Assert.That(subscriptions.SubscriptionLinkCountForTesting, Is.Zero);
            });
        }

        [Test]
        public void CollectorDispose_UnlinksItsStructuralRevisionAndLifecycleRegistrations()
        {
            using var fixture = new WorldFixture();
            EntityCollector collector = fixture.World.CreateCollector(
                EntityMatcher.With.OfNone<Position>(), EntityCollectorFlag.Default);

            Assert.That(fixture.Matches.RegisteredCollectorCountForTesting, Is.EqualTo(1));
            Assert.That(fixture.Matches.SubscriptionLinkCountForTesting, Is.GreaterThan(0));

            collector.Dispose();

            Assert.Multiple(() =>
            {
                Assert.That(fixture.Matches.RegisteredCollectorCountForTesting, Is.Zero);
                Assert.That(fixture.Matches.SubscriptionLinkCountForTesting, Is.Zero);
            });
        }

        [Test]
        public void PendingSet_OrsReasonsPerFullEntityIdAndSeparatesReusedGeneration()
        {
            var pending = new CollectorPendingSet();
            EntityId oldGeneration = EntityId.Create(9, 2);
            EntityId newGeneration = EntityId.Create(9, 3);

            pending.Mark(oldGeneration, CollectorPendingReason.Structural);
            pending.Mark(oldGeneration, CollectorPendingReason.Revision);
            pending.Mark(newGeneration, CollectorPendingReason.Lifecycle);

            Assert.Multiple(() =>
            {
                Assert.That(pending.Count, Is.EqualTo(2));
                Assert.That(pending.TryGet(oldGeneration, out CollectorPendingReason oldReason), Is.True);
                Assert.That(oldReason, Is.EqualTo(
                    CollectorPendingReason.Structural | CollectorPendingReason.Revision));
                Assert.That(pending.TryGet(newGeneration, out CollectorPendingReason newReason), Is.True);
                Assert.That(newReason, Is.EqualTo(CollectorPendingReason.Lifecycle));
            });
        }

        [Test]
        public void PendingSet_DeduplicatesRepeatedStructuralReasons()
        {
            var pending = new CollectorPendingSet();
            EntityId entityId = EntityId.Create(10, 4);

            pending.Mark(entityId, CollectorPendingReason.Structural);
            pending.Mark(entityId, CollectorPendingReason.Structural);

            Assert.That(pending.TryGet(entityId, out CollectorPendingReason reason), Is.True);
            Assert.That(reason, Is.EqualTo(CollectorPendingReason.Structural));
        }

        [Test]
        public void StructuralDispatch_UsesOnlyIndexedPendingWithoutProvenanceOrChainScan()
        {
            var subscriptions = new CollectorSubscriptions();
            var all = Register(subscriptions,
                EntityMatcher.With.OfAll<Position>(), EntityCollectorFlag.Default);
            var any = Register(subscriptions,
                EntityMatcher.With.OfAny<Position>(), EntityCollectorFlag.Default);
            var none = Register(subscriptions,
                EntityMatcher.With.OfNone<Position>(), EntityCollectorFlag.Default);
            EntityId entityId = EntityId.Create(11, 2);
            uint positionTypeId = ComponentTypeRegistry.GetOrRegister<Position>().TypeId;

            subscriptions.DispatchStructural(positionTypeId, entityId);

            Assert.Multiple(() =>
            {
                AssertReason(all, entityId, CollectorPendingReason.Structural);
                AssertReason(any, entityId, CollectorPendingReason.Structural);
                AssertReason(none, entityId, CollectorPendingReason.Structural);
                Assert.That(subscriptions.LastDeliveredCountForTesting, Is.EqualTo(3));
                Assert.That(Enum.GetNames(typeof(CollectorPendingReason)),
                    Does.Not.Contain("StructuralAdded")
                        .And.Not.Contain("StructuralRemoved")
                        .And.Not.Contain("StructuralClashed"));
                Assert.That(typeof(CollectorSubscriptions)
                    .GetMethods(System.Reflection.BindingFlags.Instance |
                                System.Reflection.BindingFlags.NonPublic)
                    .Any(method => method.Name == "DispatchStructural" &&
                                   method.GetParameters().Any(parameter =>
                                       parameter.ParameterType == typeof(ComponentHandler))),
                    Is.False);
            });
        }

        [Test]
        public void LateCollector_ReceivesOnlyPostCreationRevisionAfterAnotherCollectorPumps()
        {
            using var fixture = new WorldFixture();
            Entity pre = fixture.World.CreateEntity();
            Entity post = fixture.World.CreateEntity();
            ComponentRef<Position> prePosition = pre.CreateComponent<Position>();
            ComponentRef<Position> postPosition = post.CreateComponent<Position>();
            EntityCollector pump = fixture.World.CreateCollector(
                EntityMatcher.With, EntityCollectorFlag.RevisionAsChange);

            _ = prePosition.RW;
            EntityCollector late = fixture.World.CreateCollector(
                EntityMatcher.With.OfAll<Position>(), EntityCollectorFlag.Default);
            ulong createdSequence = fixture.Matches.GetCreatedSequenceForTesting(late);
            _ = postPosition.RW;

            pump.Flush();

            Assert.Multiple(() =>
            {
                Assert.That(createdSequence, Is.EqualTo(2));
                Assert.That(fixture.Matches.HasPendingReasonForTesting(
                    late, pre.EntityId, CollectorPendingReason.Revision), Is.False);
                Assert.That(fixture.Matches.HasPendingReasonForTesting(
                    late, post.EntityId, CollectorPendingReason.Revision), Is.True);
                Assert.That(fixture.Matches.LastDeliveredCountForTesting, Is.EqualTo(3));
                Assert.That(fixture.Matches.LastUnrelatedCountForTesting, Is.EqualTo(1));
            });
        }

        [Test]
        public void LateCollector_AdvanceEpochMakesSameComponentPostCreationWriteDeliverable()
        {
            using var fixture = new WorldFixture();
            Entity entity = fixture.World.CreateEntity();
            ComponentRef<Position> position = entity.CreateComponent<Position>();
            EntityCollector pump = fixture.World.CreateCollector(
                EntityMatcher.With, EntityCollectorFlag.RevisionAsChange);

            _ = position.RW;
            EntityCollector late = fixture.World.CreateCollector(
                EntityMatcher.With.OfAll<Position>(), EntityCollectorFlag.Default);
            _ = position.RW;

            pump.Flush();

            Assert.Multiple(() =>
            {
                Assert.That(fixture.Components.ChangeJournal.NextSequence, Is.EqualTo(3));
                Assert.That(fixture.Matches.HasPendingReasonForTesting(
                    late, entity.EntityId, CollectorPendingReason.Revision), Is.True);
                Assert.That(fixture.Matches.PendingCountForTesting(late), Is.EqualTo(1));
            });
        }

        [Test]
        public void PumpedOldGenerationReason_NeverMarksEntityReusingItsIndex()
        {
            using var fixture = new WorldFixture();
            EntityCollector observer = fixture.World.CreateCollector(
                EntityMatcher.With, EntityCollectorFlag.RevisionAsChange);
            EntityCollector pump = fixture.World.CreateCollector(
                EntityMatcher.With, EntityCollectorFlag.RevisionAsChange);
            Entity removed = fixture.World.CreateEntity();
            ComponentRef<Position> removedPosition = removed.CreateComponent<Position>();
            _ = removedPosition.RW;

            fixture.World.DestroyEntity(removed);
            fixture.Entities.CommitReleases();
            Entity reused = fixture.World.CreateEntity();
            reused.CreateComponent<Position>();

            pump.Flush();

            Assert.Multiple(() =>
            {
                Assert.That(reused.EntityId.Index, Is.EqualTo(removed.EntityId.Index));
                Assert.That(reused.EntityId.Generation, Is.Not.EqualTo(removed.EntityId.Generation));
                Assert.That(fixture.Matches.HasPendingReasonForTesting(
                    observer, removed.EntityId, CollectorPendingReason.Revision), Is.True);
                Assert.That(fixture.Matches.HasPendingReasonForTesting(
                    observer, reused.EntityId, CollectorPendingReason.Revision), Is.False);
            });
        }

        private static TestTarget Register(
            CollectorSubscriptions subscriptions,
            EntityMatcher matcher,
            EntityCollectorFlag flag)
        {
            var target = new TestTarget();
            subscriptions.Register(target, matcher.Bind(), flag);
            return target;
        }

        private static void AssertReason(
            TestTarget target,
            EntityId entityId,
            CollectorPendingReason expected)
        {
            Assert.That(target.Pending.TryGet(entityId, out CollectorPendingReason actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        private static void Clear(params TestTarget[] targets)
        {
            foreach (TestTarget target in targets)
            {
                target.Pending.Clear();
            }
        }

        private sealed class TestTarget : ICollectorSubscriptionTarget
        {
            public ulong CreatedSequence => 1;
            public CollectorPendingSet Pending { get; } = new CollectorPendingSet();

            public void MarkPending(EntityId entityId, CollectorPendingReason reason)
            {
                Pending.Mark(entityId, reason);
            }
        }

        private sealed class WorldFixture : IDisposable
        {
            public World World { get; } = new World();
            public ComponentManager Components { get; private set; } = null!;
            public EntityManager Entities { get; private set; } = null!;
            public EntityMatchManager Matches { get; private set; } = null!;

            public WorldFixture()
            {
                World.Startup();
                Components = World.GetManager<ComponentManager>();
                Entities = World.GetManager<EntityManager>();
                Matches = World.GetManager<EntityMatchManager>();
            }

            public void Dispose()
            {
                World.Shutdown();
            }
        }

        private struct Position : IComponent<Position>
        {
            public void OnCreate(EntityId entityId) { }
            public void OnDestroy(EntityId entityId) { }
        }

        private struct Velocity : IComponent<Velocity>
        {
            public void OnCreate(EntityId entityId) { }
            public void OnDestroy(EntityId entityId) { }
        }
    }
}
