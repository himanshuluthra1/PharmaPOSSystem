using PharmaPOS.Application.Common.Abstractions;

namespace PharmaPOS.Infrastructure.Services;

/// <summary>Default clock backed by the system time.</summary>
public class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
    public DateTime Now => DateTime.Now;
    public DateTime Today => DateTime.Today;
}
