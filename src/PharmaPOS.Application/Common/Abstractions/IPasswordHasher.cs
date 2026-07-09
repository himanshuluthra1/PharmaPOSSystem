namespace PharmaPOS.Application.Common.Abstractions;

/// <summary>Abstracts secure password hashing/verification (implemented with BCrypt).</summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}
