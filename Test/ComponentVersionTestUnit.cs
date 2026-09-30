using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class ComponentVersionTestUnit
    {
        [Test]
        public void Next_ReturnsUniqueIncreasingValues()
        {
            var first = ComponentVersion.Next();
            var second = ComponentVersion.Next();

            Assert.AreNotEqual(first, second);
            Assert.Greater(second, first);
        }
    }
}
