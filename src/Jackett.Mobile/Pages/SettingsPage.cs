using Microsoft.Maui.ApplicationModel.DataTransfer;
namespace Jackett.Mobile.Pages;

public class SettingsPage : ContentPage
{
    private readonly Label _cookies = new Label { FontSize = 13, TextColor = Colors.Gray };

    public SettingsPage()
    {
        Title = "Settings";
        var host = AppHost.Current;
        var settings = host.Settings;

        var nativeNetworking = new Switch { IsToggled = settings.UseNativeNetworking };
        nativeNetworking.Toggled += (_, e) =>
        {
            settings.UseNativeNetworking = e.Value;
            settings.Save();
        };

        var solveChallenges = new Switch { IsToggled = settings.SolveChallengesInBrowser };
        solveChallenges.Toggled += (_, e) =>
        {
            settings.SolveChallengesInBrowser = e.Value;
            settings.Save();
            host.Challenges.ForgetDeclinedHosts();
        };

        var timeout = new Entry { Text = settings.SearchTimeoutSeconds.ToString(), Keyboard = Keyboard.Numeric };
        timeout.Unfocused += (_, _) =>
        {
            if (int.TryParse(timeout.Text, out var seconds) && seconds >= 10 && seconds <= 600)
            {
                settings.SearchTimeoutSeconds = seconds;
                settings.Save();
            }
            timeout.Text = settings.SearchTimeoutSeconds.ToString();
        };

        var flareSolverr = new Entry
        {
            Text = host.ServerConfig.FlareSolverrUrl,
            Placeholder = "http://192.168.1.10:8191 (optional)",
            Keyboard = Keyboard.Url
        };
        flareSolverr.Unfocused += (_, _) =>
        {
            host.ServerConfig.FlareSolverrUrl = flareSolverr.Text?.Trim();
            host.ConfigService.SaveConfig(host.ServerConfig);
        };

        var clearCookies = new Button { Text = "Forget browser cookies" };
        clearCookies.Clicked += (_, _) =>
        {
            host.Challenges.Store.Clear();
            host.Challenges.ForgetDeclinedHosts();
            RefreshCookies();
        };

        var clearCache = new Button { Text = "Clear search cache" };
        clearCache.Clicked += async (_, _) =>
        {
            host.CacheService.CleanCache();
            await DisplayAlertAsync("Done", "Cached search results were removed.", "OK");
        };

        var shareLog = new Button { Text = "Share log file" };
        shareLog.Clicked += async (_, _) =>
        {
            var log = Path.Combine(host.ConfigService.GetAppDataFolder(), "log.txt");
            if (File.Exists(log))
                await Share.Default.RequestAsync(new ShareFileRequest { Title = "Jackett log", File = new ShareFile(log, "text/plain") });
        };

        var indexerCount = host.Setup.GetIndexers().Count;

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 16,
                Spacing = 10,
                Children =
                {
                    Section("Cloudflare and logins"),
                    SwitchRow("Solve checks in the in-app browser", solveChallenges),
                    Note("When a site shows a \"checking your browser\" page, it opens in a browser sheet. " +
                         "Tap the checkbox if one appears; the cookies are reused for the next searches."),
                    SwitchRow("Use iOS networking", nativeNetworking),
                    Note("Sends requests like Safari does, which Cloudflare blocks less often. Restart the app to apply."),
                    _cookies,
                    clearCookies,

                    Section("Search"),
                    Field("Wait for slow indexers (seconds)", timeout),
                    clearCache,

                    Section("FlareSolverr"),
                    Field("FlareSolverr URL", flareSolverr),
                    Note("Optional: a FlareSolverr server on your network, used when the in-app browser cannot pass a check. " +
                         "Restart the app to apply."),

                    Section("Indexer definitions"),
                    Note($"{indexerCount} indexers are built in. To add or update one, copy its .yml file to " +
                         "Files → On My iPhone → Jackett → Definitions and restart the app."),

                    Section("About"),
                    Note($"Jackett for iOS {host.Options.AppVersion}"),
                    shareLog
                }
            }
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        RefreshCookies();
    }

    private void RefreshCookies()
    {
        var sites = AppHost.Current.Challenges.Store.All();
        _cookies.Text = sites.Count == 0
            ? "No saved browser cookies."
            : "Saved browser cookies for: " + string.Join(", ", sites.Select(s => s.Host));
    }

    private static Label Section(string text) =>
        new Label { Text = text, FontSize = 20, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 16, 0, 0) };

    private static Label Note(string text) => new Label { Text = text, FontSize = 13, TextColor = Colors.Gray };

    private static View Field(string label, View view) =>
        new VerticalStackLayout { Spacing = 4, Children = { new Label { Text = label }, view } };

    private static View SwitchRow(string label, Switch toggle)
    {
        var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        row.Add(new Label { Text = label, VerticalOptions = LayoutOptions.Center }, 0, 0);
        row.Add(toggle, 1, 0);
        return row;
    }
}
