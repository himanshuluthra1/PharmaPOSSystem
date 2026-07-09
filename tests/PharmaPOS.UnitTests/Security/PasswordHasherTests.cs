using PharmaPOS.Infrastructure.Security;

namespace PharmaPOS.UnitTests.Security;

public class PasswordHasherTests
{
    private readonly BCryptPasswordHasher _hasher = new();

    [Fact]
    public void Hash_ProducesVerifiableHash()
    {
        var hash = _hasher.Hash("Admin@123");

        Assert.False(string.IsNullOrWhiteSpace(hash));
        Assert.NotEqual("Admin@123", hash);
        Assert.True(_hasher.Verify("Admin@123", hash));
    }

    [Fact]
    public void Verify_ReturnsFalse_ForWrongPassword()
    {
        var hash = _hasher.Hash("correct-horse");
        Assert.False(_hasher.Verify("battery-staple", hash));
    }

    [Fact]
    public void Verify_ReturnsFalse_ForMalformedHash()
    {
        Assert.False(_hasher.Verify("anything", "not-a-real-bcrypt-hash"));
    }

    [Fact]
    public void Hash_IsSalted_SoRepeatedHashesDiffer()
    {
        var a = _hasher.Hash("same-password");
        var b = _hasher.Hash("same-password");
        Assert.NotEqual(a, b);
        Assert.True(_hasher.Verify("same-password", a));
        Assert.True(_hasher.Verify("same-password", b));
    }
}
