using System.Windows;
using System.Windows.Input;

namespace FlowTracker.Views;

public partial class ReminderDotWindow : Window
{
    public event EventHandler? DotClicked;
    public bool IsDotVisible => IsVisible;

    public ReminderDotWindow()
    {
        InitializeComponent();
        Hide();
    }

    public void ShowAtBottomRight()
    {
        const double margin = 24;
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - margin;
        Top = workArea.Bottom - Height - margin;

        if (!IsVisible)
        {
            Show();
        }
    }

    public void HideDot()
    {
        if (IsVisible)
        {
            Hide();
        }
    }

    private void DotBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        DotClicked?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }
}
