namespace PharmaPOS.Application.Common.Models;

/// <summary>Lightweight snapshot of the logged-in user held for the session.</summary>
public class UserSession
{
    public int UserId { get; init; }
    public string Username { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string RoleName { get; init; } = string.Empty;
    public int? BranchId { get; init; }
    public string? BranchName { get; init; }
    public IReadOnlyCollection<string> Permissions { get; init; } = Array.Empty<string>();
    public DateTime LoginTimeUtc { get; init; } = DateTime.UtcNow;
}
