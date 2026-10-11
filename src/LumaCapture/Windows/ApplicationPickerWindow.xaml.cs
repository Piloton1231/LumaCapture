using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LumaCapture.Infrastructure;

namespace LumaCapture.Windows;

public partial class ApplicationPickerWindow : Window
{
    private readonly string _selectedProcessName;
    public string? SelectedProcessName { get; private set; }
    public bool SelectionCleared { get; private set; }

    public ApplicationPickerWindow(string selectedProcessName)
    {
        InitializeComponent();
        _selectedProcessName = selectedProcessName;
        LocalizationService.Apply(this);
        ClearSelectionButton.IsEnabled = !string.IsNullOrWhiteSpace(selectedProcessName);
        Populate();
        SourceInitialized += (_, _) => WindowProtection.ExcludeFromCapture(this);
    }

    private void Populate()
    {
        ProcessPanel.Children.Clear();
        foreach (var process in ProcessCatalog.ListAvailable())
        {
            var selected = string.Equals(process.ProcessName, _selectedProcessName, StringComparison.OrdinalIgnoreCase);
            var label = new StackPanel();
            label.Children.Add(new TextBlock { Text = (selected ? "✓  " : "") + process.DisplayName, FontWeight = FontWeights.SemiBold });
            label.Children.Add(new TextBlock { Text = process.ProcessName, FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(153, 162, 179)), Margin = new Thickness(0, 4, 0, 0) });
            var button = new Button
            {
                Content = label,
                ToolTip = process.DisplayName,
                Tag = process.ProcessName,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(2, 3, 2, 3),
                Padding = new Thickness(12, 10, 12, 10),
                BorderBrush = selected ? new SolidColorBrush(Color.FromRgb(255, 82, 109)) : new SolidColorBrush(Color.FromRgb(48, 54, 70)),
                Background = selected ? new SolidColorBrush(Color.FromRgb(48, 31, 42)) : new SolidColorBrush(Color.FromRgb(32, 36, 49))
            };
            button.Click += SelectProcess_Click;
            ProcessPanel.Children.Add(button);
        }
        ApplySearch();
        if (ProcessPanel.Children.Count == 0)
            ProcessPanel.Children.Add(new TextBlock { Text = LocalizationService.T("選択できるアプリが見つかりません。"), Foreground = new SolidColorBrush(Color.FromRgb(153, 162, 179)), Margin = new Thickness(8) });
    }
    private void Search_Changed(object sender, TextChangedEventArgs e) => ApplySearch();
    private void ApplySearch()
    {
        if (ProcessPanel is null || SearchBox is null) return;
        foreach (var button in ProcessPanel.Children.OfType<Button>())
            button.Visibility = ($"{button.Tag} {button.ToolTip}").Contains(SearchBox.Text.Trim(), StringComparison.CurrentCultureIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SelectProcess_Click(object sender, RoutedEventArgs e)
    {
        SelectedProcessName = (sender as Button)?.Tag?.ToString();
        DialogResult = !string.IsNullOrWhiteSpace(SelectedProcessName);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Populate();
    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        SelectionCleared = true;
        SelectedProcessName = null;
        DialogResult = true;
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
}
