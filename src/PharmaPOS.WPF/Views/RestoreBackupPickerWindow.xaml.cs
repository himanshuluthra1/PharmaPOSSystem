using System.Windows;
using System.Windows.Input;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.Views;

public partial class RestoreBackupPickerWindow : Window
{
    public RestoreBackupPickerWindow(IReadOnlyList<DriveBackupFile> files)
    {
        InitializeComponent();
        FileList.ItemsSource = files;
        if (files.Count > 0)
            FileList.SelectedIndex = 0;
    }

    public DriveBackupFile? SelectedFile => FileList.SelectedItem as DriveBackupFile;

    private void Restore_Click(object sender, RoutedEventArgs e) => Confirm();

    private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Confirm();

    private void Confirm()
    {
        if (SelectedFile is null) return;
        DialogResult = true;
        Close();
    }
}
