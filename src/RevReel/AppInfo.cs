using System.Reflection;

namespace RevReel
{
    /// <summary>
    /// App name, version and legal text in one place, so the About window
    /// and the update check always agree. Change ReleaseDateText when a new
    /// version is released. The version number itself comes from
    /// the Version setting in RevReel.csproj.
    /// </summary>
    public static class AppInfo
    {
        public const string Name = "RevReel";
        public const string Company = "Rev Creative Developer";
        public const string ReleaseDateText = "12 October 2026";
        public const string Copyright = "Copyright © 2026 Rev Creative Developer";
        public const string LicenseName = "MIT License";

        public const string RepositoryUrl = "https://github.com/revgamer/RevReel";
        public const string LicenseUrl = "https://github.com/revgamer/RevReel/blob/main/LICENSE";
        public const string IssuesUrl = "https://github.com/revgamer/RevReel/issues";
        public const string ReleasesUrl = "https://github.com/revgamer/RevReel/releases";
        public const string LatestReleaseApiUrl = "https://api.github.com/repos/revgamer/RevReel/releases/latest";

        /// <summary>Running version as Major.Minor.Build (for example 0.1.0).</summary>
        public static Version CurrentVersion
        {
            get
            {
                Version v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
                return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
            }
        }

        public static string VersionText => CurrentVersion.ToString();
    }
}
