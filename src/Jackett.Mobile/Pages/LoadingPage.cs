namespace Jackett.Mobile.Pages;

public class LoadingPage : ContentPage
{
    private readonly ActivityIndicator _spinner = new ActivityIndicator { IsRunning = true };
    private readonly Label _message = new Label { Text = "Loading indexers…", HorizontalTextAlignment = TextAlignment.Center };
    private readonly Button _retry = new Button { Text = "Try again", IsVisible = false };
    private bool _started;

    public LoadingPage()
    {
        _retry.Clicked += async (_, _) => await StartAsync();

        Content = new VerticalStackLayout
        {
            Spacing = 16,
            Padding = 32,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = "Jackett", FontSize = 32, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center },
                _spinner,
                _message,
                _retry
            }
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_started)
            return;
        _started = true;
        await StartAsync();
    }

    private async Task StartAsync()
    {
        _retry.IsVisible = false;
        _spinner.IsRunning = true;
        _message.Text = "Loading indexers…";

        try
        {
            await AppHost.StartAsync();
            App.ShowMainUi();
        }
        catch (Exception e)
        {
            _spinner.IsRunning = false;
            _message.Text = "Jackett could not start:\n" + e.Message;
            _retry.IsVisible = true;
        }
    }
}
