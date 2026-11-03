#if COREECS_ULONG_ENTITY
using RevisionValue = System.UInt64;
#else
using RevisionValue = System.UInt32;
#endif

using CoreECS.Structures;
using CoreECS.Utils;

namespace CoreECS.Test
{
    [TestFixture]
    public class ComponentHandlerTestUnit
    {
        [Test]
        public void Bind_InitializesIdentityLocationAndChangeTracking()
        {
            var handler = new ComponentHandler();
            var storage = new object();
            EntityId entityId = EntityId.Create(17, 3);

            handler.Bind(entityId, 42, storage, 9);
            RevisionValue initialRevision = handler.Revision;

            Assert.That(handler.EntityId, Is.EqualTo(entityId));
            Assert.That(handler.TypeId, Is.EqualTo(42U));
            Assert.That(handler.Storage, Is.SameAs(storage));
            Assert.That(handler.PackedOffset, Is.EqualTo(9));
            Assert.That(initialRevision, Is.Zero);
            Assert.That(handler.LastJournalEpoch, Is.Zero);
            Assert.That(handler.Previous, Is.Null);
            Assert.That(handler.Next, Is.Null);
            Assert.That(handler.BindGeneration, Is.Not.Zero);
        }

        [Test]
        public void IncrementRevision_AdvancesAndWrapsWithoutReturningZero()
        {
            var handler = new ComponentHandler();
            handler.Bind(EntityId.Create(1, 1), 1, new object(), 0);

            handler.IncrementRevision();

            Assert.That(handler.Revision, Is.EqualTo((RevisionValue)1));

            handler.Revision = RevisionValue.MaxValue;
            handler.IncrementRevision();

            Assert.That(handler.Revision, Is.EqualTo((RevisionValue)1));
        }

        [Test]
        public void Invalidate_ClearsBoundStateAndRetainedReferences()
        {
            var handler = new ComponentHandler();
            var previous = new ComponentHandler();
            var next = new ComponentHandler();
            handler.Bind(EntityId.Create(5, 2), 7, new object(), 13);
            handler.Revision = 8;
            handler.LastJournalEpoch = 21;
            handler.Previous = previous;
            handler.Next = next;

            handler.Invalidate();

            Assert.That(handler.EntityId.IsValid, Is.False);
            Assert.That(handler.TypeId, Is.Zero);
            Assert.That(handler.Storage, Is.Null);
            Assert.That(handler.PackedOffset, Is.EqualTo(-1));
            Assert.That(handler.Revision, Is.Zero);
            Assert.That(handler.LastJournalEpoch, Is.Zero);
            Assert.That(handler.Previous, Is.Null);
            Assert.That(handler.Next, Is.Null);
        }

        [Test]
        public void ReleaseAndReuse_IncrementsGenerationAndKeepsCapturedGenerationStale()
        {
            var pool = new ComponentHandlerPool();
            ComponentHandler handler = pool.Get();
            handler.Bind(EntityId.Create(11, 4), 3, new object(), 6);
            ulong capturedGeneration = handler.BindGeneration;
            handler.Previous = new ComponentHandler();
            handler.Next = new ComponentHandler();

            pool.Release(handler);

            Assert.That(handler.Storage, Is.Null);
            Assert.That(handler.Previous, Is.Null);
            Assert.That(handler.Next, Is.Null);

            ComponentHandler reused = pool.Get();
            reused.Bind(EntityId.Create(12, 5), 4, new object(), 7);

            Assert.That(reused, Is.SameAs(handler));
            Assert.That(reused.BindGeneration, Is.Not.EqualTo(capturedGeneration));
            Assert.That(capturedGeneration == reused.BindGeneration, Is.False,
                "A ComponentRef-style (handler, generation) pair must remain stale after reuse.");
        }
    }
}
