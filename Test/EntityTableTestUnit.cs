#if COREECS_ULONG_ENTITY
using EntityValue = System.UInt64;
#else
using EntityValue = System.UInt32;
#endif

using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class EntityTableTestUnit
    {
        [Test]
        public void DefaultEntityId_IsNeverAliveOrAcceptedForRecordAccess()
        {
            var table = new EntityTable();
            var handler = new ComponentHandler();

            Assert.That(table.IsAlive(default(EntityId)), Is.False);
            Assert.Throws<ArgumentException>(() => table.GetMask(default(EntityId)));
            Assert.Throws<ArgumentException>(() => table.GetComponentHead(default(EntityId)));
            Assert.Throws<ArgumentException>(() => table.SetComponentHead(default(EntityId), handler));
            Assert.That(table.MarkDestroyed(default(EntityId)), Is.False);
            Assert.That(table.AllocatedPageCount, Is.Zero);
        }

        [Test]
        public void Create_StartsAtIndexAndGenerationOneAndRetainsImmutableMask()
        {
            var table = new EntityTable();

            EntityId first = table.Create(0x25UL);
            EntityId second = table.Create(ulong.MaxValue);

            Assert.That(first.Index, Is.EqualTo((EntityValue)1));
            Assert.That(first.Generation, Is.EqualTo((EntityValue)1));
            Assert.That(second.Index, Is.EqualTo((EntityValue)2));
            Assert.That(table.IsAlive(first), Is.True);
            Assert.That(table.GetMask(first), Is.EqualTo(0x25UL));
            Assert.That(table.GetMask(second), Is.EqualTo(ulong.MaxValue));
            Assert.That(table.AllocatedPageCount, Is.EqualTo(1));
        }

        [Test]
        public void ComponentHead_RoundTripsAndCanBeClearedAfterLogicalDeath()
        {
            var table = new EntityTable();
            EntityId entityId = table.Create(7);
            var handler = new ComponentHandler();

            Assert.That(table.GetComponentHead(entityId), Is.Null);

            table.SetComponentHead(entityId, handler);
            Assert.That(table.GetComponentHead(entityId), Is.SameAs(handler));

            table.MarkDestroyed(entityId);
            table.SetComponentHead(entityId, null);

            Assert.That(table.GetComponentHead(entityId), Is.Null);
        }

        [Test]
        public void MarkDestroyed_MakesEntityLogicallyDeadBeforeCommitWithoutSamePhaseReuse()
        {
            var table = new EntityTable();
            EntityId destroyed = table.Create(3);

            Assert.That(table.MarkDestroyed(destroyed), Is.True);
            EntityId samePhase = table.Create(5);

            Assert.That(table.IsAlive(destroyed), Is.False);
            Assert.That(samePhase.Index, Is.EqualTo((EntityValue)2));
            Assert.That(samePhase.Generation, Is.EqualTo((EntityValue)1));
            Assert.That(table.MarkDestroyed(destroyed), Is.False);
        }

        [Test]
        public void CommitReleases_ReusesIndexWithNextGenerationAndRejectsStaleId()
        {
            var table = new EntityTable();
            EntityId destroyed = table.Create(11);
            table.MarkDestroyed(destroyed);

            table.CommitReleases();
            EntityId reused = table.Create(13);

            Assert.That(reused.Index, Is.EqualTo(destroyed.Index));
            Assert.That(reused.Generation, Is.EqualTo((EntityValue)2));
            Assert.That(table.IsAlive(destroyed), Is.False);
            Assert.That(table.IsAlive(reused), Is.True);
            Assert.Throws<ArgumentException>(() => table.GetMask(destroyed));
            Assert.That(table.GetMask(reused), Is.EqualTo(13UL));
        }

        [Test]
        public void EntityRecord_ActivationWrapsMaximumGenerationToOne()
        {
            var record = new EntityRecord(EntityId.MaxGeneration);

            record.Activate(17);

            Assert.That(record.Generation, Is.EqualTo((EntityValue)1));
            Assert.That(record.IsAlive, Is.True);
            Assert.That(record.Mask, Is.EqualTo(17UL));
        }

        [Test]
        public void MarkDestroyed_StaleGenerationCannotReleaseCurrentIncarnation()
        {
            var table = new EntityTable();
            EntityId original = table.Create(1);
            table.MarkDestroyed(original);
            table.CommitReleases();
            EntityId current = table.Create(2);

            Assert.That(table.MarkDestroyed(original), Is.False);
            table.CommitReleases();

            Assert.That(table.IsAlive(current), Is.True);
            Assert.That(table.Create(3).Index, Is.EqualTo((EntityValue)2));
        }

        [Test]
        public void ClearForRestart_DropsAllStateAndRestartsIdsAtOne()
        {
            var table = new EntityTable();
            EntityId oldFirst = table.Create(21);
            EntityId oldSecond = table.Create(22);
            table.MarkDestroyed(oldFirst);
            table.CommitReleases();

            table.ClearForRestart();
            EntityId restarted = table.Create(23);

            Assert.That(table.IsAlive(oldSecond), Is.False);
            Assert.That(restarted.Index, Is.EqualTo((EntityValue)1));
            Assert.That(restarted.Generation, Is.EqualTo((EntityValue)1));
            Assert.That(table.GetMask(restarted), Is.EqualTo(23UL));
            Assert.That(table.AllocatedPageCount, Is.EqualTo(1));
        }
    }
}
