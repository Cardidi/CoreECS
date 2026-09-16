using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class TagContainerTestUnit
    {
        [Test]
        public void Add_Has_Remove_WorkOnSameRow()
        {
            var tags = new TagContainer();
            tags.AddRow();

            Assert.IsTrue(tags.Add(0, 1));
            Assert.IsTrue(tags.Has(0, 1));
            Assert.IsFalse(tags.Add(0, 1));
            Assert.IsTrue(tags.Remove(0, 1));
            Assert.IsFalse(tags.Has(0, 1));
        }

        [Test]
        public void Has_ReturnsFalseForUnknownRowOrTag()
        {
            var tags = new TagContainer();

            Assert.IsFalse(tags.Has(0, 1));

            tags.AddRow();
            Assert.IsFalse(tags.Has(0, 1));
        }

        [Test]
        public void Add_SupportsTagIdsBeyond64Bits()
        {
            var tags = new TagContainer();
            tags.AddRow();

            Assert.IsTrue(tags.Add(0, 130));
            Assert.IsTrue(tags.Has(0, 130));
            Assert.IsFalse(tags.Has(0, 129));
        }

        [Test]
        public void WidthGrowth_PreservesExistingRows()
        {
            var tags = new TagContainer();
            tags.AddRow();
            tags.AddRow();
            tags.Add(0, 1);

            tags.Add(1, 65);

            Assert.IsTrue(tags.Has(0, 1));
            Assert.IsTrue(tags.Has(1, 65));
        }

        [Test]
        public void RemoveRowSwap_MovesLastRowIntoRemovedSlot()
        {
            var tags = new TagContainer();
            tags.AddRow();
            tags.AddRow();
            tags.Add(1, 5);

            tags.RemoveRowSwap(0);

            Assert.AreEqual(1, tags.Count);
            Assert.IsTrue(tags.Has(0, 5));
        }

        [Test]
        public void CopyRowTo_TransfersWordsAcrossDifferentWidths()
        {
            var source = new TagContainer();
            source.AddRow();
            source.Add(0, 130);

            var target = new TagContainer();
            target.AddRow();

            source.CopyRowTo(0, target, 0);

            Assert.IsTrue(target.Has(0, 130));
        }
    }
}
