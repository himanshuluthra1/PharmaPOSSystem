namespace PharmaPOS.WPF.Services;

/// <summary>Opens the bill search popup and shows the selected invoice viewer.</summary>
public interface IBillSearchService
{
    Task SearchAndViewAsync();
}
