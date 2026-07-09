namespace PharmaPOS.Application.Common.Abstractions;

/// <summary>Abstracts the system clock so time-dependent logic is testable.</summary>
public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
    DateTime Now { get; }
    DateTime Today { get; }
}
