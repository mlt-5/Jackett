using Jackett.Mobile.Pages;

namespace Jackett.Mobile;

public class App : Application
{
    protected override Window CreateWindow(IActivationState activationState) =>
        new Window(new LoadingPage()) { Title = "Jackett" };

    // Called by LoadingPage once Jackett is running.
    public static void ShowMainUi()
    {
        var window = Current?.Windows.FirstOrDefault();
        if (window != null)
            window.Page = new AppShell();
    }
}

public class AppShell : Shell
{
    public AppShell()
    {
        var tabs = new TabBar();
        tabs.Items.Add(Tab<SearchPage>("Search", "tab_search.png"));
        tabs.Items.Add(Tab<IndexersPage>("Indexers", "tab_indexers.png"));
        tabs.Items.Add(Tab<SettingsPage>("Settings", "tab_settings.png"));
        Items.Add(tabs);
    }

    private static ShellContent Tab<TPage>(string title, string icon) where TPage : Page =>
        new ShellContent
        {
            Title = title,
            Icon = icon,
            ContentTemplate = new DataTemplate(typeof(TPage))
        };
}
