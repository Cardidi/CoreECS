using System.Collections.Generic;
using System.Reflection;
using CoreECS.Defines;
using CoreECS.Managers;
using CoreECS.Structures;

#if COREECS_ULONG_ENTITY
using RevisionValue = System.UInt64;
#else
using RevisionValue = System.UInt32;
#endif

namespace CoreECS.Test
{
    [TestFixture]
    public class ComponentChangeJournalTestUnit
    {
        private World m_world;
        private ComponentManager m_components;
        private EntityManager m_entities;

        [SetUp]
        public void SetUp()
        {
            m_world = new World();
            m_world.Startup();
            m_components = m_world.GetManager<ComponentManager>();
            m_entities = m_world.GetManager<EntityManager>();
        }

        [TearDown]
        public void TearDown()
        {
            m_world?.Shutdown();
        }

        [Test]
        public void Append_RetainsEntriesAcrossExplicitEpochAdvanceWithMonotonicSequences()
        {
            var journal = new ComponentChangeJournal();
            EntityId first = EntityId.Create(7, 3);
            EntityId second = EntityId.Create(8, 4);

            Assert.That(journal.CurrentEpoch, Is.EqualTo(1));
            Assert.That(journal.NextSequence, Is.EqualTo(1));

            journal.Append(first, 11);
            journal.AdvanceEpoch();
            journal.Append(second, 12);

            var entries = new List<ComponentChangeJournal.Entry>();
            journal.Pump(entries.Add);

            Assert.That(entries, Has.Count.EqualTo(2));
            Assert.That(entries[0].Sequence, Is.EqualTo(1));
            Assert.That(entries[0].EntityId, Is.EqualTo(first));
            Assert.That(entries[0].TypeId, Is.EqualTo(11));
            Assert.That(entries[1].Sequence, Is.EqualTo(2));
            Assert.That(entries[1].EntityId, Is.EqualTo(second));
            Assert.That(entries[1].TypeId, Is.EqualTo(12));
            Assert.That(journal.NextSequence, Is.EqualTo(3));
            Assert.That(journal.CurrentEpoch, Is.EqualTo(3));
        }

        [Test]
        public void Append_WhenNextSequenceIsMaxValue_FailsBeforeSequenceCanWrap()
        {
            var journal = new ComponentChangeJournal();
            PropertyInfo nextSequence = typeof(ComponentChangeJournal).GetProperty(
                nameof(ComponentChangeJournal.NextSequence),
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            nextSequence.SetValue(journal, ulong.MaxValue);

            InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(() =>
                journal.Append(EntityId.Create(7, 3), 11));

            Assert.Multiple(() =>
            {
                Assert.That(exception!.Message, Does.Contain("sequence"));
                Assert.That(journal.NextSequence, Is.EqualTo(ulong.MaxValue));
                var entries = new List<ComponentChangeJournal.Entry>();
                journal.Pump(entries.Add);
                Assert.That(entries, Is.Empty);
            });
        }

        [Test]
        public void Pump_CapturesTailAndLeavesEntriesAppendedByVisitorForNextPump()
        {
            var journal = new ComponentChangeJournal();
            EntityId first = EntityId.Create(9, 2);
            EntityId appendedDuringPump = EntityId.Create(10, 5);
            journal.Append(first, 21);
            var firstPump = new List<ComponentChangeJournal.Entry>();

            journal.Pump(entry =>
            {
                firstPump.Add(entry);
                journal.Append(appendedDuringPump, 22);
            });

            Assert.That(firstPump, Has.Count.EqualTo(1));
            Assert.That(firstPump[0].EntityId, Is.EqualTo(first));
            Assert.That(journal.CurrentEpoch, Is.EqualTo(2));

            var secondPump = new List<ComponentChangeJournal.Entry>();
            journal.Pump(secondPump.Add);

            Assert.That(secondPump, Has.Count.EqualTo(1));
            Assert.That(secondPump[0].Sequence, Is.EqualTo(2));
            Assert.That(secondPump[0].EntityId, Is.EqualTo(appendedDuringPump));
            Assert.That(journal.CurrentEpoch, Is.EqualTo(3));

            var emptyPump = new List<ComponentChangeJournal.Entry>();
            journal.Pump(emptyPump.Add);
            Assert.That(emptyPump, Is.Empty);
            Assert.That(journal.CurrentEpoch, Is.EqualTo(4));
        }

        [Test]
        public void Rw_IncrementsEveryRevisionButJournalsOncePerHandlerAndEpoch()
        {
            EntityId first = m_entities.Create(0);
            EntityId second = m_entities.Create(0);
            ComponentRef<JournalPosition> firstPosition = m_components.Create<JournalPosition>(first);
            ComponentRef<JournalVelocity> firstVelocity = m_components.Create<JournalVelocity>(first);
            ComponentRef<JournalPosition> secondPosition = m_components.Create<JournalPosition>(second);

            _ = firstPosition.RW;
            _ = firstPosition.RW;
            Assert.That(firstPosition.Revision, Is.EqualTo((RevisionValue)2));

            var withoutInterest = new List<ComponentChangeJournal.Entry>();
            m_components.ChangeJournal.Pump(withoutInterest.Add);
            Assert.That(withoutInterest, Is.Empty);

            m_components.AcquireRevisionInterest();
            _ = firstPosition.RW;
            _ = firstPosition.RW;
            _ = firstPosition.RW;
            _ = firstVelocity.RW;
            _ = secondPosition.RW;

            Assert.That(firstPosition.Revision, Is.EqualTo((RevisionValue)5));
            Assert.That(firstVelocity.Revision, Is.EqualTo((RevisionValue)1));
            Assert.That(secondPosition.Revision, Is.EqualTo((RevisionValue)1));

            var entries = new List<ComponentChangeJournal.Entry>();
            m_components.ChangeJournal.Pump(entries.Add);

            Assert.That(entries, Has.Count.EqualTo(3));
            Assert.That(entries[0].EntityId, Is.EqualTo(first));
            Assert.That(entries[0].TypeId, Is.EqualTo(firstPosition.Handler.TypeId));
            Assert.That(entries[1].EntityId, Is.EqualTo(first));
            Assert.That(entries[1].TypeId, Is.EqualTo(firstVelocity.Handler.TypeId));
            Assert.That(entries[2].EntityId, Is.EqualTo(second));
            Assert.That(entries[2].TypeId, Is.EqualTo(secondPosition.Handler.TypeId));

            _ = firstPosition.RW;
            var nextEpoch = new List<ComponentChangeJournal.Entry>();
            m_components.ChangeJournal.Pump(nextEpoch.Add);

            Assert.That(firstPosition.Revision, Is.EqualTo((RevisionValue)6));
            Assert.That(nextEpoch, Has.Count.EqualTo(1));
            Assert.That(nextEpoch[0].EntityId, Is.EqualTo(first));
        }

        [Test]
        public void AdvanceChangeEpoch_DoesNotConsumeEntriesAndAllowsNextRwToAppend()
        {
            EntityId entityId = m_entities.Create(0);
            ComponentRef<JournalPosition> component = m_components.Create<JournalPosition>(entityId);
            m_components.AcquireRevisionInterest();

            _ = component.RW;
            _ = component.RW;
            ulong epochBeforeAdvance = m_components.ChangeJournal.CurrentEpoch;

            m_components.AdvanceChangeEpoch();
            _ = component.RW;

            Assert.That(m_components.ChangeJournal.CurrentEpoch, Is.EqualTo(epochBeforeAdvance + 1));
            var entries = new List<ComponentChangeJournal.Entry>();
            m_components.ChangeJournal.Pump(entries.Add);

            Assert.That(component.Revision, Is.EqualTo((RevisionValue)3));
            Assert.That(entries, Has.Count.EqualTo(2));
            Assert.That(entries[0].EntityId, Is.EqualTo(entityId));
            Assert.That(entries[1].EntityId, Is.EqualTo(entityId));
            Assert.That(entries[1].Sequence, Is.EqualTo(entries[0].Sequence + 1));
        }

        [Test]
        public void RemovedAndReusedIndex_JournalEntriesRetainFullEntityGeneration()
        {
            m_components.AcquireRevisionInterest();
            EntityId removed = m_entities.Create(0);
            ComponentRef<JournalPosition> removedComponent = m_components.Create<JournalPosition>(removed);
            _ = removedComponent.RW;

            m_entities.Destroy(removed);
            m_entities.CommitReleases();
            EntityId reused = m_entities.Create(0);
            ComponentRef<JournalPosition> reusedComponent = m_components.Create<JournalPosition>(reused);
            _ = reusedComponent.RW;

            var entries = new List<ComponentChangeJournal.Entry>();
            m_components.ChangeJournal.Pump(entries.Add);

            Assert.That(reused.Index, Is.EqualTo(removed.Index));
            Assert.That(reused.Generation, Is.Not.EqualTo(removed.Generation));
            Assert.That(entries, Has.Count.EqualTo(2));
            Assert.That(entries[0].EntityId, Is.EqualTo(removed));
            Assert.That(entries[1].EntityId, Is.EqualTo(reused));
        }

        private struct JournalPosition : IComponent<JournalPosition>
        {
            public void OnCreate(EntityId entityId) { }
            public void OnDestroy(EntityId entityId) { }
        }

        private struct JournalVelocity : IComponent<JournalVelocity>
        {
            public void OnCreate(EntityId entityId) { }
            public void OnDestroy(EntityId entityId) { }
        }
    }
}
