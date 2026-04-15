using FlowTracker.ViewModels;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using WpfButton = System.Windows.Controls.Button;
using WpfMessageBox = System.Windows.MessageBox;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace FlowTracker.Views;

/// <summary>
/// Hostet die Dashboard-UI und delegiert Datenoperationen an <see cref="DashboardViewModel"/>.
/// </summary>
public partial class DashboardWindow : Window
{
    private readonly DashboardViewModel _viewModel;
    private readonly Func<Task>? _afterDataChanged;

    public DashboardWindow(DashboardViewModel viewModel, Func<Task>? afterDataChanged = null)
    {
        _viewModel = viewModel;
        _afterDataChanged = afterDataChanged;
        InitializeComponent();
        ApplyWindowIcon();
        DataContext = _viewModel;
        Loaded += OnLoaded;
    }

    /// <summary>
    /// Lädt das Fenstericon mit Fallback-Strategie.
    /// </summary>
    private void ApplyWindowIcon()
    {
        var greenIconPath = Path.Combine(AppContext.BaseDirectory, "Icons", "TimeTrackerGreen.ico");
        var fallbackIconPath = Path.Combine(AppContext.BaseDirectory, "Icons", "TimeTrackerSchwarz.ico");
        var iconPath = File.Exists(greenIconPath)
            ? greenIconPath
            : File.Exists(fallbackIconPath)
                ? fallbackIconPath
                : null;

        if (iconPath is null)
        {
            return;
        }

        try
        {
            Icon = BitmapFrame.Create(new Uri(iconPath, UriKind.Absolute));
        }
        catch
        {
            // Bei ungültiger/fehlender Icon-Datei einfach Standard-Icon behalten.
        }
    }

    /// <summary>
    /// Lädt die initialen Dashboard-Daten beim Öffnen.
    /// </summary>
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadAsync();
    }

    private async void Load_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadAsync();
    }

    private async void SaveRow_Click(object sender, RoutedEventArgs e)
    {
        // EVA:
        // E: bearbeitete Zeile aus dem Button-DataContext.
        // V: ViewModel-Validierung und Save ausführen.
        // A: optional Callback triggern und UI-Status aktualisieren.
        if (sender is not WpfButton { DataContext: EditableTimeEntryRow row })
        {
            return;
        }

        var saved = await _viewModel.SaveAsync(row);
        if (!saved)
        {
            WpfMessageBox.Show(this, _viewModel.StatusMessage, "Ungültige Eingabe", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_afterDataChanged is not null)
        {
            await _afterDataChanged();
        }
    }

    private async void DeleteRow_Click(object sender, RoutedEventArgs e)
    {
        // EVA:
        // E: ausgewählte Zeile + Benutzerbestätigung.
        // V: Soft Delete über ViewModel ausführen.
        // A: Zeile aus UI entfernen und optionalen Refresh propagieren.
        if (sender is not WpfButton { DataContext: EditableTimeEntryRow row })
        {
            return;
        }

        var result = WpfMessageBox.Show(
            this,
            $"Eintrag #{row.Id} wirklich soft-löschen?",
            "Eintrag löschen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        await _viewModel.DeleteAsync(row);

        if (_afterDataChanged is not null)
        {
            await _afterDataChanged();
        }
    }

    private async void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        // EVA:
        // E: Zielpfad aus SaveFileDialog.
        // V: Export über ViewModel starten.
        // A: Erfolgsrückmeldung per MessageBox.
        var dialog = new WpfSaveFileDialog
        {
            Filter = "CSV-Datei (*.csv)|*.csv",
            FileName = $"flowtracker_{_viewModel.SelectedPeriod.ToString().ToLowerInvariant()}_{DateTime.Now:yyyyMMdd}.csv"
        };

        if (dialog.ShowDialog(this) is not true)
        {
            return;
        }

        await _viewModel.ExportCsvAsync(dialog.FileName);
        WpfMessageBox.Show(this, "CSV-Export erfolgreich.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        // EVA:
        // E: Zielpfad aus SaveFileDialog.
        // V: PDF-Export über ViewModel starten.
        // A: Erfolgsrückmeldung per MessageBox.
        var dialog = new WpfSaveFileDialog
        {
            Filter = "PDF-Datei (*.pdf)|*.pdf",
            FileName = $"flowtracker_{_viewModel.SelectedPeriod.ToString().ToLowerInvariant()}_{DateTime.Now:yyyyMMdd}.pdf"
        };

        if (dialog.ShowDialog(this) is not true)
        {
            return;
        }

        await _viewModel.ExportPdfAsync(dialog.FileName);
        WpfMessageBox.Show(this, "PDF-Export erfolgreich.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
