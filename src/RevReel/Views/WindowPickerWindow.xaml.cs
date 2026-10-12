using System.Windows;
using System.Windows.Input;
using RevReel.Models;
using RevReel.Services;

namespace RevReel.Views
{
    /// <summary>Lets the person choose one open window to record. Works with the keyboard or the mouse.</summary>
    public partial class WindowPickerWindow : Window
    {
        /// <summary>The chosen window. Valid when ShowDialog returned true.</summary>
        public WindowSelection? Chosen { get; private set; }

        public WindowPickerWindow()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                LoadWindows();
                WindowList.Focus();
            };
        }

        private void LoadWindows()
        {
            IReadOnlyList<WindowSelection> windows = WindowService.GetOpenWindows();
            WindowList.ItemsSource = windows;
            EmptyText.Visibility = windows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (windows.Count > 0)
            {
                WindowList.SelectedIndex = 0;
            }
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) => LoadWindows();

        private void Select_Click(object sender, RoutedEventArgs e) => Confirm();

        private void WindowList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Confirm();

        private void Confirm()
        {
            if (WindowList.SelectedItem is WindowSelection window)
            {
                Chosen = window;
                DialogResult = true;
            }
        }
    }
}
