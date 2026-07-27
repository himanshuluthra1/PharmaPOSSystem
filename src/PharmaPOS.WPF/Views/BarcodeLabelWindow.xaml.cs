using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PharmaPOS.WPF.Views;

public partial class BarcodeLabelWindow : Window
{
    private readonly string _medicineName;
    private readonly string _barcode;
    private readonly BitmapSource _image;

    public BarcodeLabelWindow(string medicineName, string barcode, BitmapSource image)
    {
        InitializeComponent();
        _medicineName = medicineName;
        _barcode = barcode;
        _image = image;
        TitleText.Text = medicineName;
        BarcodeText.Text = barcode;
        BarcodeImage.Source = image;
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(QuantityBox.Text.Trim(), out var quantity) || quantity < 1)
        {
            MessageBox.Show(this, "Enter a valid number of barcodes to print (1 or more).",
                "Print barcodes", MessageBoxButton.OK, MessageBoxImage.Information);
            QuantityBox.Focus();
            return;
        }

        if (!int.TryParse(PerPageBox.Text.Trim(), out var perPage) || perPage < 1)
        {
            MessageBox.Show(this, "Enter how many barcodes to place on one A4 page (1 or more).",
                "Print barcodes", MessageBoxButton.OK, MessageBoxImage.Information);
            PerPageBox.Focus();
            return;
        }

        if (perPage > 48)
        {
            MessageBox.Show(this, "Maximum 48 barcodes per A4 page.",
                "Print barcodes", MessageBoxButton.OK, MessageBoxImage.Information);
            PerPageBox.Focus();
            return;
        }

        var dialog = new PrintDialog
        {
            PrintTicket =
            {
                PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4),
                PageOrientation = PageOrientation.Portrait
            }
        };
        if (dialog.ShowDialog() != true) return;

        var pageWidth = dialog.PrintableAreaWidth;
        var pageHeight = dialog.PrintableAreaHeight;
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new Size(pageWidth, pageHeight);

        var remaining = quantity;
        while (remaining > 0)
        {
            var countOnPage = Math.Min(perPage, remaining);
            var pageVisual = BuildPage(countOnPage, perPage, pageWidth, pageHeight);
            pageVisual.Measure(new Size(pageWidth, pageHeight));
            pageVisual.Arrange(new Rect(0, 0, pageWidth, pageHeight));
            pageVisual.UpdateLayout();

            var pageContent = new PageContent();
            var fixedPage = new FixedPage
            {
                Width = pageWidth,
                Height = pageHeight
            };
            fixedPage.Children.Add(pageVisual);
            ((System.Windows.Markup.IAddChild)pageContent).AddChild(fixedPage);
            document.Pages.Add(pageContent);

            remaining -= countOnPage;
        }

        dialog.PrintDocument(document.DocumentPaginator, $"Barcode {_barcode} x{quantity}");
    }

    private UIElement BuildPage(int labelsOnThisPage, int perPageCapacity, double pageWidth, double pageHeight)
    {
        var (columns, rows) = ChooseGrid(perPageCapacity);
        var margin = 18.0;
        var gap = 6.0;
        var usableWidth = pageWidth - (margin * 2);
        var usableHeight = pageHeight - (margin * 2);
        var cellWidth = (usableWidth - gap * (columns - 1)) / columns;
        var cellHeight = (usableHeight - gap * (rows - 1)) / rows;

        var canvas = new Canvas
        {
            Width = pageWidth,
            Height = pageHeight,
            Background = Brushes.White
        };

        for (var i = 0; i < labelsOnThisPage; i++)
        {
            var col = i % columns;
            var row = i / columns;
            var label = CreateLabel(cellWidth, cellHeight);
            Canvas.SetLeft(label, margin + col * (cellWidth + gap));
            Canvas.SetTop(label, margin + row * (cellHeight + gap));
            canvas.Children.Add(label);
        }

        return canvas;
    }

    private Border CreateLabel(double width, double height)
    {
        var image = new Image
        {
            Source = _image,
            Stretch = Stretch.Uniform,
            MaxHeight = Math.Max(28, height * 0.55),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var stack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4)
        };
        stack.Children.Add(new TextBlock
        {
            Text = _medicineName,
            FontSize = Math.Clamp(height * 0.09, 7, 11),
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Black,
            Margin = new Thickness(0, 0, 0, 2)
        });
        stack.Children.Add(image);
        stack.Children.Add(new TextBlock
        {
            Text = _barcode,
            FontSize = Math.Clamp(height * 0.08, 6, 10),
            FontFamily = new FontFamily("Consolas"),
            TextAlignment = TextAlignment.Center,
            Foreground = Brushes.Black,
            Margin = new Thickness(0, 2, 0, 0)
        });

        return new Border
        {
            Width = width,
            Height = height,
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(0.5),
            Child = stack
        };
    }

    /// <summary>Pick a near-square grid that can hold at least <paramref name="perPage"/> cells on portrait A4.</summary>
    private static (int Columns, int Rows) ChooseGrid(int perPage)
    {
        var columns = (int)Math.Ceiling(Math.Sqrt(perPage * (210.0 / 297.0)));
        columns = Math.Max(1, columns);
        var rows = (int)Math.Ceiling(perPage / (double)columns);
        while (columns * rows < perPage)
            rows++;

        // Prefer slightly wider grids for A4 portrait when counts are small.
        if (perPage <= 2) return (1, perPage);
        if (perPage <= 4) return (2, (int)Math.Ceiling(perPage / 2.0));
        if (perPage <= 6) return (2, 3);
        if (perPage <= 8) return (2, 4);
        if (perPage <= 9) return (3, 3);
        if (perPage <= 12) return (3, 4);
        if (perPage <= 16) return (4, 4);
        if (perPage <= 20) return (4, 5);
        if (perPage <= 24) return (4, 6);
        if (perPage <= 30) return (5, 6);
        if (perPage <= 36) return (6, 6);
        if (perPage <= 42) return (6, 7);
        return (columns, rows);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
