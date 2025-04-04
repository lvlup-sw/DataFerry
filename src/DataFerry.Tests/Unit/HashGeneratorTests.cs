using System.Text;

using lvlup.DataFerry.Utilities;

namespace lvlup.DataFerry.Tests.Unit;

[TestClass]
public class HashGeneratorTests
{
    [TestMethod]
    public void EmptyInput_ReturnsCorrectHash()
    {
        // Arrange
        ReadOnlySpan<byte> bytes = default;
        uint seed = 12345;

        // Act
        uint result = HashGenerator.Hash32(ref bytes, seed);

        // Assert
        Assert.AreEqual(seed, result);
    }

    [TestMethod]
    public void DifferentSeeds_HashesAreDistinct()
    {
        // Arrange
        var random = new Random();
        var bytes = new byte[64];
        random.NextBytes(bytes);
        var bytesSpan1 = new ReadOnlySpan<byte>(bytes);
        random.NextBytes(bytes);
        var bytesSpan2 = new ReadOnlySpan<byte>(bytes);

        // Act
        uint seed1 = 42;
        uint hash1 = HashGenerator.Hash32(ref bytesSpan1, seed1);

        uint seed2 = 12345;
        uint hash2 = HashGenerator.Hash32(ref bytesSpan2, seed2);

        // Assert
        Assert.AreNotEqual(hash1, hash2);
    }

    [TestMethod]
    public void SameSeeds_AndValues_HashesAreIdentical()
    {
        // Arrange
        var random = new Random();
        var bytes = new byte[64];
        random.NextBytes(bytes);
        var bytesSpan1 = new ReadOnlySpan<byte>(bytes);
        var bytesSpan2 = new ReadOnlySpan<byte>(bytes);

        // Act
        uint seed = 5;
        uint hash1 = HashGenerator.Hash32(ref bytesSpan1, seed);
        uint hash2 = HashGenerator.Hash32(ref bytesSpan2, seed);

        // Assert
        Assert.AreEqual(hash1, hash2);
    }

    [DataTestMethod]
    [DataRow("key", 293U, 2495785535U)]
    [DataRow("Hello World!", 420U, 1535517821U)]
    [DataRow("a$6ajXViSAfFw5pR2kkz3Q28YGrDx$jeaLJ5HFPe", 69U, 3131871211U)]
    [DataRow("!zhgt#HVY#tV%kPPZ$LXYEo@EqyKjqRJPzUb3*hhASWpdyZAF3!t$V96j9Eb9ivzMH2w4jvuyHaXRxd&YbHz*W8yZGJ#CXjXfqMzNGgf@YMfh*RdZpRXtPQ3mV$N9N!%", 23485U, 4240136436U)]
    [TestMethod]
    public void KnownInputs_ReturnCorrectHashes(string input, uint seed, uint expected)
    {
        // Arrange
        ReadOnlySpan<byte> inputSpan = Encoding.UTF8.GetBytes(input).AsSpan();

        // Act
        var actual = HashGenerator.Hash32(ref inputSpan, seed);

        // Assert
        Assert.AreEqual(expected, actual);
    }

    [DataTestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(100)]
    [DataRow(1024)]
    [TestMethod]
    public void DifferentInputLengths_HashesAreDistinct(int length)
    {
        // Arrange
        var random = new Random();
        var bytes = new byte[length];
        random.NextBytes(bytes);
        uint seed = 0;
        var bytesSpan = new ReadOnlySpan<byte>(bytes);

        // Act
        uint hash1 = HashGenerator.Hash32(ref bytesSpan, seed);
        bytes[0] = (byte)(bytes[0] ^ 0xFF);
        bytesSpan = new ReadOnlySpan<byte>(bytes);
        uint hash2 = HashGenerator.Hash32(ref bytesSpan, seed);

        // Assert
        Assert.AreNotEqual(hash1, hash2);
    }
}