using System.Diagnostics;
using System.Windows;

namespace RevReel.Views
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();

            VersionText.Text = $"Version {AppInfo.VersionText}";
            ReleaseText.Text = $"Released {AppInfo.ReleaseDateText}";
            CompanyText.Text = $"by {AppInfo.Company}";
            CopyrightText.Text = AppInfo.Copyright;
            LicenseText.Text =
                $"Licensed under the {AppInfo.LicenseName}. You may use, copy, modify and share it. " +
                "It is provided as is, without warranty of any kind.";
        }

        private void GitHubButton_Click(object sender, RoutedEventArgs e) => OpenUrl(AppInfo.RepositoryUrl);

        private void LicenseButton_Click(object sender, RoutedEventArgs e) => OpenUrl(AppInfo.LicenseUrl);

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                // No browser available. Nothing useful to do here.
            }
        }
    }
}
