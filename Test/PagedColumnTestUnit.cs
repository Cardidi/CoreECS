using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class PagedColumnTestUnit
    {
        [TestCase(0, 4)]
        [TestCase(1, 8)]
        [TestCase(2, 16)]
        [TestCase(3, 32)]
        public void PageSize_GrowsByExactPowersOfTwo(int page, int expectedSize)
        {
            Assert.That(PackedPageLayout.PageSize(page), Is.EqualTo(expectedSize));
        }

        [TestCase(1, 4)]
        [TestCase(2, 12)]
        [TestCase(3, 28)]
        [TestCase(4, 60)]
        public void CapacityBeforePage_SumsAllEarlierPageSizes(int page, int expectedCapacity)
        {
            Assert.That(PackedPageLayout.CapacityBeforePage(page), Is.EqualTo(expectedCapacity));
        }

        [TestCase(0, 0, 0)]
        [TestCase(3, 0, 3)]
        [TestCase(4, 1, 0)]
        [TestCase(11, 1, 7)]
        [TestCase(12, 2, 0)]
        [TestCase(27, 2, 15)]
        [TestCase(28, 3, 0)]
        public void Locate_MapsPackedSlotBoundaries(int slot, int expectedPage, int expectedOffset)
        {
            PackedPageLayout.Locate(slot, out int page, out int offset);

            Assert.That(page, Is.EqualTo(expectedPage));
            Assert.That(offset, Is.EqualTo(expectedOffset));
        }

        [Test]
        public void EnsureSlot_GrowsInOrderWithoutReplacingExistingPagesOrValues()
        {
            var column = new PagedColumn<string>();
            column.EnsureSlot(27);
            column[0] = "page zero";
            column[4] = "page one";
            column[12] = "page two";
            string[] pageZero = column.GetPageForTesting(0);
            string[] pageOne = column.GetPageForTesting(1);
            string[] pageTwo = column.GetPageForTesting(2);

            column.EnsureSlot(28);

            Assert.That(column.CountPages, Is.EqualTo(4));
            Assert.That(column.Capacity, Is.EqualTo(60));
            Assert.That(column.GetPageForTesting(0), Is.SameAs(pageZero));
            Assert.That(column.GetPageForTesting(1), Is.SameAs(pageOne));
            Assert.That(column.GetPageForTesting(2), Is.SameAs(pageTwo));
            Assert.That(column.GetPageForTesting(3), Has.Length.EqualTo(32));
            Assert.That(column[0], Is.EqualTo("page zero"));
            Assert.That(column[4], Is.EqualTo("page one"));
            Assert.That(column[12], Is.EqualTo("page two"));
        }

        [Test]
        public void ClearSlot_ResetsOnlyTheRequestedPackedSlot()
        {
            var column = new PagedColumn<string>();
            column.EnsureSlot(4);
            column[3] = "keep";
            column[4] = "clear";

            column.ClearSlot(4);

            Assert.That(column[3], Is.EqualTo("keep"));
            Assert.That(column[4], Is.Null);
        }
    }
}
