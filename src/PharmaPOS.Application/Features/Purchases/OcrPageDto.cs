namespace PharmaPOS.Application.Features.Purchases;

/// <summary>One OCR word with page-relative bounds (pixels).</summary>
public sealed class OcrWordDto
{
    public string Text { get; init; } = string.Empty;
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public double CenterY => Y + Height / 2;
    public double Right => X + Width;
}

/// <summary>Full OCR page used by the purchase-bill layout parser.</summary>
public sealed class OcrPageDto
{
    public string FullText { get; init; } = string.Empty;
    public IReadOnlyList<OcrWordDto> Words { get; init; } = Array.Empty<OcrWordDto>();
}
