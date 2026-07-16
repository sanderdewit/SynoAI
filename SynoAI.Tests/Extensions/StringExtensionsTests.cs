using SynoAI.Extensions;
using Xunit;

namespace SynoAI.Tests.Extensions
{
    public class StringExtensionsTests
    {
        [Theory]
        [InlineData("person", "Person")]
        [InlineData("Person", "Person")]
        [InlineData("p", "P")]
        [InlineData("dog cat", "Dog cat")]
        [InlineData("123abc", "123abc")]
        public void FirstCharToUpper_CapitalisesOnlyTheFirstCharacter(string input, string expected)
        {
            Assert.Equal(expected, input.FirstCharToUpper());
        }

        [Fact]
        public void FirstCharToUpper_WhenNull_ThrowsArgumentNullException()
        {
            string? input = null;
            Assert.Throws<ArgumentNullException>(() => input!.FirstCharToUpper());
        }

        [Fact]
        public void FirstCharToUpper_WhenEmpty_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => string.Empty.FirstCharToUpper());
        }
    }
}
