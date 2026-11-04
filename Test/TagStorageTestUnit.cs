using CoreECS.Defines;
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class TagStorageTestUnit
    {
        private struct SleepingTag : ITagComponent<SleepingTag>
        {
        }

        [Test]
        public void Create_UsesNoPayloadPagesAndTracksRevisionPerEntity()
        {
            var entities = new EntityTable();
            var storage = (TagStorage<SleepingTag>)new ComponentStorageRegistry(entities)
                .GetOrCreate<SleepingTag>(ComponentKind.Tag);
            EntityId first = entities.Create(0);
            EntityId second = entities.Create(0);

            ComponentHandler firstHandler = storage.Create(first);
            ComponentHandler secondHandler = storage.Create(second);
            firstHandler.IncrementRevision();
            ref SleepingTag sharedDefault = ref storage.GetSharedDefault();

            Assert.That(storage.PayloadPageCount, Is.Zero);
            Assert.That(storage.Contains(first), Is.True);
            Assert.That(storage.Contains(second), Is.True);
            Assert.That(firstHandler.Storage, Is.SameAs(storage));
            Assert.That(firstHandler.Revision, Is.EqualTo(1));
            Assert.That(secondHandler.Revision, Is.Zero);
            Assert.That(sharedDefault, Is.EqualTo(default(SleepingTag)));
        }

        [Test]
        public void MarkRemoveAndCompact_RelocatesTailWithoutAllocatingPayload()
        {
            var entities = new EntityTable();
            var storage = (TagStorage<SleepingTag>)new ComponentStorageRegistry(entities)
                .GetOrCreate<SleepingTag>(ComponentKind.Tag);
            EntityId first = entities.Create(0);
            EntityId removed = entities.Create(0);
            EntityId moved = entities.Create(0);
            storage.Create(first);
            ComponentHandler removedHandler = storage.Create(removed);
            ComponentHandler movedHandler = storage.Create(moved);

            storage.MarkRemove(removed);

            Assert.That(storage.Contains(removed), Is.False);
            Assert.That(storage.GetEntityIdAt(1), Is.EqualTo(default(EntityId)));
            Assert.That(removedHandler.EntityId.IsValid, Is.False);
            Assert.That(storage.LiveCount, Is.EqualTo(2));
            Assert.That(storage.AllocatedCount, Is.EqualTo(3));

            storage.Compact();

            Assert.That(storage.AllocatedCount, Is.EqualTo(2));
            Assert.That(storage.GetEntityIdAt(1), Is.EqualTo(moved));
            Assert.That(storage.GetHandler(moved), Is.SameAs(movedHandler));
            Assert.That(movedHandler.PackedOffset, Is.EqualTo(1));
            Assert.That(storage.PayloadPageCount, Is.Zero);
        }

        [Test]
        public void Clear_DetachesHandlersAndResetsSharedDefault()
        {
            var entities = new EntityTable();
            var registry = new ComponentStorageRegistry(entities);
            var storage = (TagStorage<SleepingTag>)registry.GetOrCreate<SleepingTag>(ComponentKind.Tag);
            EntityId entityId = entities.Create(0);
            ComponentHandler handler = storage.Create(entityId);

            storage.Clear();

            Assert.That(handler.EntityId.IsValid, Is.False);
            Assert.That(entities.GetComponentHead(entityId), Is.Null);
            Assert.That(storage.LiveCount, Is.Zero);
            Assert.That(storage.AllocatedCount, Is.Zero);
            Assert.That(storage.GetSharedDefault(), Is.EqualTo(default(SleepingTag)));
        }
    }
}
