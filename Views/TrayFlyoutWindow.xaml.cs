using System.Windows;

namespace FlowTracker.Views;

public partial class TrayFlyoutWindow : Window
{
    private readonly Action _openDashboard;
    private readonly Func<Task> _stopTrackingAsync;
    private readonly Func<Task> _toggleGhostModeAsync;

    public TrayFlyoutWindow(Action openDashboard, Func<Task> stopTrackingAsync, Func<Task> toggleGhostModeAsync)
    {
        _openDashboard = openDashboard;
        _stopTrackingAsync = stopTrackingAsync;
        _toggleGhostModeAsync = toggleGhostModeAsync;

        InitializeComponent();
        Deactivated += (_, _) => Hide();
    }

    public void UpdateSummary(double hoursToday, string status, bool isGhostMode, bool canStopTracking)
    {
        SummaryTextBlock.Text = $"{hoursToday:F2}h • {status}";
        GhostModeTextBlock.Visibility = isGhostMode ? Visibility.Visible : Visibility.Collapsed;
        GhostModeButton.Content = isGhostMode ? "Ghost Mode deaktivieren" : "Ghost Mode aktivieren";
        StopTrackingButton.IsEnabled = canStopTracking;
    }

    private void OpenDashboard_Click(object sender, RoutedEventArgs e)
    {
        _openDashboard();
        Hide();
    }

    private async void StopTracking_Click(object sender, RoutedEventArgs e)
    {
        await _stopTrackingAsync();
    }

    private async void GhostMode_Click(object sender, RoutedEventArgs e)
    {
        await _toggleGhostModeAsync();
        Activate();
    }
}
