using FlowTracker.Domain;
using System.Windows;
using System.Windows.Controls;

namespace FlowTracker.Views;

/// <summary>
/// Bearbeitet Reminder-Profil und Fokus-Unterdrückungseinträge.
/// </summary>
public partial class SettingsWindow : Window
{
    public AppSettings? ResultSettings { get; private set; }

    public SettingsWindow(AppSettings currentSettings)
    {
        InitializeComponent();

        var profile = string.IsNullOrWhiteSpace(currentSettings.ReminderProfile)
            ? "balanced"
            : currentSettings.ReminderProfile.Trim().ToLowerInvariant();

        foreach (var item in ProfileComboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), profile, StringComparison.OrdinalIgnoreCase))
            {
                ProfileComboBox.SelectedItem = item;
                break;
            }
        }

        if (ProfileComboBox.SelectedItem is null && ProfileComboBox.Items.Count > 0)
        {
            ProfileComboBox.SelectedIndex = 0;
        }

        ProcessesTextBox.Text = string.Join(
            Environment.NewLine,
            currentSettings.FocusSuppressedProcesses
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var selected = ProfileComboBox.SelectedItem as ComboBoxItem;
        var profile = selected?.Tag?.ToString() ?? "balanced";
        var processes = ProcessesTextBox.Text
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(static value => value.Trim())
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        ResultSettings = new AppSettings
        {
            ReminderProfile = profile,
            FocusSuppressedProcesses = processes
        };

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
