using Jackett.Mobile.Core.Models;

namespace Jackett.Mobile.Pages;

// Setup form of one indexer, built from the same configuration items as the web UI.
public class IndexerConfigPage : ContentPage
{
    private readonly string _indexerId;
    private readonly VerticalStackLayout _form = new VerticalStackLayout { Spacing = 14 };
    private readonly ActivityIndicator _busy = new ActivityIndicator();
    private readonly Label _error = new Label { TextColor = Colors.OrangeRed, IsVisible = false };
    private readonly Button _save = new Button { Text = "Save and test" };
    private readonly Button _test = new Button { Text = "Test" };
    private readonly Button _delete = new Button { Text = "Remove indexer", TextColor = Colors.Red, BackgroundColor = Colors.Transparent };
    private List<ConfigField> _fields;
    private bool _loaded;

    public IndexerConfigPage(string indexerId)
    {
        _indexerId = indexerId;
        var indexer = AppHost.Current.Setup.GetIndexer(indexerId);
        Title = indexer.Name;

        _save.Clicked += async (_, _) => await SaveAsync();
        _test.Clicked += async (_, _) => await TestAsync();
        _delete.Clicked += async (_, _) => await DeleteAsync();

        ToolbarItems.Add(new ToolbarItem("Reload", null, async () => await LoadAsync()));

        var siteLink = new Button { Text = "Open website", BackgroundColor = Colors.Transparent, Padding = 0, HorizontalOptions = LayoutOptions.Start };
        siteLink.Clicked += async (_, _) => await Browser.Default.OpenAsync(indexer.SiteLink, BrowserLaunchMode.SystemPreferred);

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 16,
                Spacing = 12,
                Children =
                {
                    new Label { Text = indexer.Description, FontSize = 13, TextColor = Colors.Gray },
                    new Label { Text = $"{indexer.Type} · {indexer.Language}", FontSize = 13, TextColor = Colors.Gray },
                    siteLink,
                    _error,
                    _form,
                    _busy,
                    _save,
                    _test,
                    _delete
                }
            }
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded)
            return;
        _loaded = true;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        await RunBusyAsync(async () =>
        {
            _fields = await AppHost.Current.Setup.GetFormAsync(_indexerId);
            BuildForm();
        });
    }

    private void RefreshButtons()
    {
        var indexer = AppHost.Current.Setup.GetIndexer(_indexerId);
        _test.IsVisible = _delete.IsVisible = indexer.IsConfigured;
        _error.Text = indexer.LastError;
        _error.IsVisible = indexer.HasError;
    }

    private void BuildForm()
    {
        _form.Children.Clear();
        foreach (var field in _fields)
        {
            if (field.IsCookie && AppHost.Current.Setup.CanLogInWithBrowser)
            {
                var login = new Button { Text = "Log in with the in-app browser" };
                login.Clicked += async (_, _) => await LogInWithBrowserAsync();
                _form.Children.Add(login);
            }

            var view = CreateFieldView(field);
            if (view != null)
                _form.Children.Add(view);
        }
        RefreshButtons();
    }

    private static View CreateFieldView(ConfigField field)
    {
        switch (field.Type)
        {
            case ConfigField.TypeInfo:
                var info = new VerticalStackLayout { Spacing = 4 };
                if (!string.IsNullOrWhiteSpace(field.Name))
                    info.Children.Add(new Label { Text = field.Name, FontAttributes = FontAttributes.Bold });
                info.Children.Add(new Label { Text = field.Value, TextType = TextType.Html, FontSize = 13 });
                return info;

            case ConfigField.TypeImage:
                if (field.ImageBytes == null)
                    return null;
                var bytes = field.ImageBytes;
                return Labeled(field.Name, new Image
                {
                    Source = ImageSource.FromStream(() => new MemoryStream(bytes)),
                    HeightRequest = 80,
                    Aspect = Aspect.AspectFit,
                    HorizontalOptions = LayoutOptions.Start
                });

            case ConfigField.TypeBool:
                var toggle = new Switch { IsToggled = field.BoolValue, HorizontalOptions = LayoutOptions.End };
                toggle.Toggled += (_, e) => field.BoolValue = e.Value;
                var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
                row.Add(new Label { Text = field.Name, VerticalOptions = LayoutOptions.Center }, 0, 0);
                row.Add(toggle, 1, 0);
                return row;

            case ConfigField.TypeSelect:
                var picker = new Picker();
                foreach (var option in field.Options)
                    picker.Items.Add(option.Value);
                picker.SelectedIndex = field.Options.FindIndex(o => o.Key == field.Value);
                picker.SelectedIndexChanged += (_, _) =>
                {
                    if (picker.SelectedIndex >= 0)
                        field.Value = field.Options[picker.SelectedIndex].Key;
                };
                return Labeled(field.Name, picker);

            case ConfigField.TypeCheckbox:
                var boxes = new VerticalStackLayout { Spacing = 2 };
                foreach (var option in field.Options)
                {
                    var box = new CheckBox { IsChecked = field.Values.Contains(option.Key) };
                    box.CheckedChanged += (_, e) =>
                    {
                        field.Values.Remove(option.Key);
                        if (e.Value)
                            field.Values.Add(option.Key);
                    };
                    boxes.Children.Add(new HorizontalStackLayout
                    {
                        Children = { box, new Label { Text = option.Value, VerticalOptions = LayoutOptions.Center } }
                    });
                }
                return Labeled(field.Name, boxes);

            default: // inputstring, password, inputtags
                var isPassword = field.Type == ConfigField.TypePassword
                                 || string.Equals(field.Name, "password", StringComparison.OrdinalIgnoreCase);
                var entry = new Entry
                {
                    Text = field.Value,
                    IsPassword = isPassword,
                    Placeholder = field.Type == ConfigField.TypeTags ? "comma separated" : null,
                    Keyboard = Keyboard.Plain,
                    IsSpellCheckEnabled = false,
                    IsTextPredictionEnabled = false
                };
                entry.TextChanged += (_, e) => field.Value = e.NewTextValue;
                return Labeled(field.Name, entry);
        }
    }

    private static View Labeled(string label, View view) =>
        new VerticalStackLayout
        {
            Spacing = 4,
            Children = { new Label { Text = label, FontSize = 13, FontAttributes = FontAttributes.Bold }, view }
        };

    private async Task LogInWithBrowserAsync()
    {
        var ok = await AppHost.Current.Setup.LogInWithBrowserAsync(_indexerId, _fields);
        if (!ok)
            return;

        BuildForm();
        await DisplayAlertAsync("Logged in", "The cookies were copied from the browser. Tap \"Save and test\" to finish.", "OK");
    }

    private async Task SaveAsync()
    {
        var saved = false;
        await RunBusyAsync(async () =>
        {
            await Task.Run(() => AppHost.Current.Setup.SaveAsync(_indexerId, _fields));
            saved = true;
        }, onError: async () =>
        {
            // captchas are single use: show a fresh one
            if (_fields.Any(f => f.Type == ConfigField.TypeImage))
                await LoadAsync();
        });

        if (saved)
        {
            await DisplayAlertAsync("Indexer added", "The test search worked.", "OK");
            await Navigation.PopAsync();
        }
    }

    private Task TestAsync() =>
        RunBusyAsync(async () =>
        {
            await Task.Run(() => AppHost.Current.Setup.TestAsync(_indexerId));
            await DisplayAlertAsync("Test passed", "The indexer returned results.", "OK");
        });

    private async Task DeleteAsync()
    {
        if (!await DisplayAlertAsync("Remove indexer?", "Its settings and login will be deleted.", "Remove", "Cancel"))
            return;

        AppHost.Current.Setup.Delete(_indexerId);
        await Navigation.PopAsync();
    }

    private async Task RunBusyAsync(Func<Task> action, Func<Task> onError = null)
    {
        _busy.IsRunning = true;
        _save.IsEnabled = _test.IsEnabled = false;
        try
        {
            await action();
        }
        catch (Exception e)
        {
            await DisplayAlertAsync("It did not work", e.Message, "OK");
            if (onError != null)
                await onError();
        }
        finally
        {
            _busy.IsRunning = false;
            _save.IsEnabled = _test.IsEnabled = true;
            RefreshButtons();
        }
    }
}
