using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using MoergoLayerViz.App.Localization;

namespace MoergoLayerViz.App.Services;

/// <summary>
/// Checks the GitHub releases API for a newer tag than the assembly's
/// informational version, and surfaces the result as observable state for
/// the Settings window to bind against. Stateless across instances apart
/// from the in-flight HTTP request; the underlying socket pool is shared
/// through a static <see cref="HttpClient"/> so it outlives any one window.
/// </summary>
public sealed partial class UpdateChecker : ObservableObject
{
    private const string GitHubReleasesApi = "https://api.github.com/repos/ovandongen/moergo-layer-viz/releases/latest";
    private const string GitHubReleasesPage = "https://github.com/ovandongen/moergo-layer-viz/releases/latest";

    // Static singleton: this VM is recreated on every Settings reopen, but
    // the HTTP client should outlive the window — disposing per-window would
    // tear down the underlying socket pool.
    private static readonly HttpClient _httpClient = new();

    /// <summary>
    /// Display version pulled from <see cref="AssemblyInformationalVersionAttribute"/>.
    /// Strips the <c>+commitHash</c> suffix .NET source-link adds, so a CI build
    /// stamped <c>0.1.0+abc1234</c> renders as <c>v0.1.0</c>.
    /// </summary>
    public string AppVersion { get; } = ComputeAppVersion();

    [ObservableProperty]
    private string? _updateMessage;

    [ObservableProperty]
    private bool _isChecking;

    /// <summary>URL the update-link TextBlock opens. Null when no update is available.</summary>
    public string? UpdateUrl { get; private set; }

    /// <summary>
    /// Hits the GitHub releases API and sets <see cref="UpdateMessage"/> /
    /// <see cref="UpdateUrl"/> based on whether the latest tag is newer than
    /// the current build. Swallows network/parse errors into a generic
    /// "check failed" message — this is best-effort, not load-bearing.
    /// </summary>
    public async Task CheckAsync()
    {
        IsChecking = true;
        UpdateMessage = null;
        UpdateUrl = null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, GitHubReleasesApi);
            // GitHub's API rejects requests without a User-Agent.
            request.Headers.Add("User-Agent", "MoergoLayerViz");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            var tagName = doc.RootElement.GetProperty("tag_name").GetString();
            var htmlUrl = doc.RootElement.GetProperty("html_url").GetString();

            if (tagName is null)
            {
                UpdateMessage = Loc.Instance["Settings_UpdateCheckFailed"];
                return;
            }

            var latestStr = tagName.TrimStart('v');
            var currentStr = AppVersion.TrimStart('v');

            // Version.TryParse only accepts Major.Minor[.Build[.Revision]] —
            // a dev build stamped "0.0.0-dev" fails to parse, so we fall
            // through to the up-to-date branch silently. Real CI builds set
            // -p:Version=<semver> and parse cleanly.
            if (Version.TryParse(latestStr, out var latest) &&
                Version.TryParse(currentStr, out var current) &&
                latest > current)
            {
                UpdateMessage = Loc.Instance.Format("Settings_UpdateAvailable", tagName);
                UpdateUrl = htmlUrl ?? GitHubReleasesPage;
            }
            else
            {
                UpdateMessage = Loc.Instance["Settings_UpToDate"];
            }
        }
        catch
        {
            UpdateMessage = Loc.Instance["Settings_UpdateCheckFailed"];
        }
        finally
        {
            IsChecking = false;
        }
    }

    private static string ComputeAppVersion()
    {
        var info = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (info is not null)
        {
            var plus = info.IndexOf('+');
            return "v" + (plus >= 0 ? info[..plus] : info);
        }
        return "v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?");
    }
}
