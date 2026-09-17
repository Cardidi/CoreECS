using System;
using CoreECS.Defines;
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class EntityMatcherStructureTestUnit
    {
        private struct Position : IComponent<Position>
        {
            public int X;
        }

        private struct OtherDense : IComponent<OtherDense>
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

        private struct EvalDense : IComponent<EvalDense>
        {
            public int X;
        }

        private struct EvalDiscrete : IDiscreteComponent<EvalDiscrete>
        {
            public int Value;
        }

        private struct EvalTag : ITagComponent<EvalTag>
        {
        }

        private static uint IdOf<T>() where T : struct, IComponent<T>
            => ComponentTypeRegistry.GetOrRegister<T>().TypeId;

        private static Structure MakeStructure(ulong mask, params uint[] denseTypeIds)
        {
            Array.Sort(denseTypeIds);
            return new Structure(new StructureKey(denseTypeIds, mask));
        }

        private static int AppendRow(Structure structure, ulong entityId)
        {
            var location = EntityLocation.Pool.Get();
            return structure.Append(entityId, location);
        }

        [Test]
        public void ComponentFilter_AllOfDense_MatchesOnlyStructuresCarryingTheType()
        {
            var matching = MakeStructure(ulong.MaxValue, IdOf<Position>());
            var matchingRow = AppendRow(matching, 1UL);
            var missing = MakeStructure(ulong.MaxValue);
            var missingRow = AppendRow(missing, 2UL);

            var matcher = (EntityMatcher)EntityMatcher.With.OfAll<Position>();

            Assert.IsTrue(matcher.ComponentFilter(matching, matchingRow));
            Assert.IsFalse(matcher.ComponentFilter(missing, missingRow));
        }

        [Test]
        public void ComponentFilter_AllOfTag_MatchesOnlyRowsCarryingTheTag()
        {
            var structure = MakeStructure(ulong.MaxValue);
            var taggedRow = AppendRow(structure, 1UL);
            var plainRow = AppendRow(structure, 2UL);
            structure.AddTag(IdOf<PlayerTag>(), taggedRow);

            var matcher = (EntityMatcher)EntityMatcher.With.OfAll<PlayerTag>();

            Assert.IsTrue(matcher.ComponentFilter(structure, taggedRow));
            Assert.IsFalse(matcher.ComponentFilter(structure, plainRow));
        }

        [Test]
        public void ComponentFilter_AllOfDiscrete_MatchesOnlyRowsCarryingTheComponent()
        {
            var structure = MakeStructure(ulong.MaxValue);
            var withManaRow = AppendRow(structure, 1UL);
            var withoutManaRow = AppendRow(structure, 2UL);
            structure.SetDiscrete(withManaRow, new Mana { Value = 3 }, ComponentVersion.Next());

            var matcher = (EntityMatcher)EntityMatcher.With.OfAll<Mana>();

            Assert.IsTrue(matcher.ComponentFilter(structure, withManaRow));
            Assert.IsFalse(matcher.ComponentFilter(structure, withoutManaRow));
        }

        [Test]
        public void ComponentFilter_NoneOf_RejectsPresenceAcrossDenseTagAndDiscrete()
        {
            var denseStructure = MakeStructure(ulong.MaxValue, IdOf<Position>());
            var denseRow = AppendRow(denseStructure, 1UL);

            var tagStructure = MakeStructure(ulong.MaxValue);
            var taggedRow = AppendRow(tagStructure, 2UL);
            var untaggedRow = AppendRow(tagStructure, 3UL);
            tagStructure.AddTag(IdOf<PlayerTag>(), taggedRow);

            var discreteStructure = MakeStructure(ulong.MaxValue);
            var withManaRow = AppendRow(discreteStructure, 4UL);
            var plainRow = AppendRow(discreteStructure, 5UL);
            discreteStructure.SetDiscrete(withManaRow, new Mana { Value = 1 }, ComponentVersion.Next());

            var matcher = (EntityMatcher)EntityMatcher.With
                .OfNone<Position>()
                .OfNone<PlayerTag>()
                .OfNone<Mana>();

            Assert.IsFalse(matcher.ComponentFilter(denseStructure, denseRow));
            Assert.IsFalse(matcher.ComponentFilter(tagStructure, taggedRow));
            Assert.IsFalse(matcher.ComponentFilter(discreteStructure, withManaRow));
            Assert.IsTrue(matcher.ComponentFilter(tagStructure, untaggedRow));
            Assert.IsTrue(matcher.ComponentFilter(discreteStructure, plainRow));
        }

        [Test]
        public void ComponentFilter_AnyOf_MixedKinds_SatisfiedByAnyPresentCondition()
        {
            var denseStructure = MakeStructure(ulong.MaxValue, IdOf<Position>());
            var denseRow = AppendRow(denseStructure, 1UL);

            var rowStructure = MakeStructure(ulong.MaxValue);
            var row = AppendRow(rowStructure, 2UL);

            var matcher = (EntityMatcher)EntityMatcher.With
                .OfAny<Position>()
                .OfAny<PlayerTag>()
                .OfAny<Mana>();

            Assert.IsTrue(matcher.ComponentFilter(denseStructure, denseRow));
            Assert.IsFalse(matcher.ComponentFilter(rowStructure, row));

            rowStructure.AddTag(IdOf<PlayerTag>(), row);
            Assert.IsTrue(matcher.ComponentFilter(rowStructure, row));
            rowStructure.RemoveTag(IdOf<PlayerTag>(), row);

            rowStructure.SetDiscrete(row, new Mana { Value = 2 }, ComponentVersion.Next());
            Assert.IsTrue(matcher.ComponentFilter(rowStructure, row));
        }

        [Test]
        public void ComponentFilter_MaskMismatch_RejectsOtherwiseMatchingRow()
        {
            var structure = MakeStructure(0b0010UL, IdOf<Position>());
            var row = AppendRow(structure, 1UL);

            var rejected = EntityMatcher.WithMask(0b0001UL);
            var matching = EntityMatcher.WithMask(0b0010UL);
            var overlapping = EntityMatcher.WithMask(0b0011UL);

            Assert.IsFalse(rejected.ComponentFilter(structure, row));
            Assert.IsTrue(matching.ComponentFilter(structure, row));
            Assert.IsTrue(overlapping.ComponentFilter(structure, row));
        }

        [Test]
        public void ComponentFilter_EmptyMatcher_MatchesAnyRowWithIntersectingMask()
        {
            var structure = MakeStructure(0b100UL);
            var row = AppendRow(structure, 1UL);

            Assert.IsTrue(EntityMatcher.With.ComponentFilter(structure, row));
            Assert.IsTrue(EntityMatcher.WithMask(0b100UL).ComponentFilter(structure, row));
            Assert.IsFalse(EntityMatcher.WithMask(0b011UL).ComponentFilter(structure, row));
        }

        [Test]
        public void ComponentFilter_AllAnyNone_CombinedSemantics()
        {
            var matching = MakeStructure(ulong.MaxValue, IdOf<Position>());
            var matchingRow = AppendRow(matching, 1UL);
            matching.AddTag(IdOf<PlayerTag>(), matchingRow);

            var anyMissing = MakeStructure(ulong.MaxValue, IdOf<Position>());
            var anyMissingRow = AppendRow(anyMissing, 2UL);

            var nonePresent = MakeStructure(ulong.MaxValue, IdOf<Position>(), IdOf<OtherDense>());
            var nonePresentRow = AppendRow(nonePresent, 3UL);
            nonePresent.AddTag(IdOf<PlayerTag>(), nonePresentRow);

            var matcher = (EntityMatcher)EntityMatcher.With
                .OfAll<Position>()
                .OfAny<PlayerTag>()
                .OfAny<Mana>()
                .OfNone<OtherDense>();

            Assert.IsTrue(matcher.ComponentFilter(matching, matchingRow));
            Assert.IsFalse(matcher.ComponentFilter(anyMissing, anyMissingRow));
            Assert.IsFalse(matcher.ComponentFilter(nonePresent, nonePresentRow));
        }

        [Test]
        public void IsRelevantComponent_UnchangedForTagAndDiscreteTypes()
        {
            var matcher = EntityMatcher.With
                .OfAll<Position>()
                .OfAny<PlayerTag>()
                .OfNone<Mana>();

            Assert.IsTrue(matcher.IsRelevantComponent(typeof(Position)));
            Assert.IsTrue(matcher.IsRelevantComponent(typeof(PlayerTag)));
            Assert.IsTrue(matcher.IsRelevantComponent(typeof(Mana)));
            Assert.IsFalse(matcher.IsRelevantComponent(typeof(OtherDense)));
        }

        [Test]
        public void ComponentFilter_Evaluation_DoesNotTouchTheRegistry()
        {
            var matcher = (EntityMatcher)EntityMatcher.With
                .OfAll<EvalDense>()
                .OfAny<EvalTag>()
                .OfNone<EvalDiscrete>();
            var structure = MakeStructure(ulong.MaxValue, IdOf<EvalDense>());
            var row = AppendRow(structure, 1UL);
            structure.AddTag(IdOf<EvalTag>(), row);
            var registeredBefore = ComponentTypeRegistry.RegisteredTypeCount;

            for (var i = 0; i < 64; i++)
            {
                matcher.ComponentFilter(structure, row);
            }

            Assert.AreEqual(registeredBefore, ComponentTypeRegistry.RegisteredTypeCount);
        }
    }
}
