using System.Collections.ObjectModel;
using Jackett.Mobile.Core.Models;
using Jackett.Mobile.Core.Services;

namespace Jackett.Mobile.Pages;

public class SearchPage : ContentPage
{
    private static readonly (string Name, int[] Categories)[] CategoryChoices =
    {
        ("All categories", Array.Empty<int>()),
        ("Movies", new[] { 2000 }),
        ("TV", new[] { 5000 }),
        ("Anime", new[] { 5070 }),
        ("Music", new[] { 3000 }),
        ("Audiobooks", new[] { 3030 }),
        ("Books", new[] { 7000 }),
        ("PC & Games", new[] { 4000 }),
        ("Console", new[] { 1000 }),
        ("Other", new[] { 8000 })
    };

    private static readonly (string Name, ResultSort Sort)[] SortChoices =
    {
        ("Most seeders", ResultSort.Seeders),
        ("Newest", ResultSort.Date),
        ("Largest", ResultSort.Size),
        ("Title", ResultSort.Title)
    };

    private readonly SearchBar _searchBar = new SearchBar { Placeholder = "Search all configured indexers" };
    private readonly Picker _category = new Picker { Title = "Category" };
    private readonly Picker _sort = new Picker { Title = "Sort" };
    private readonly Label _status = new Label { FontSize = 13, TextColor = Colors.Gray };
    private readonly Button _failures = new Button { IsVisible = false, FontSize = 13, Padding = new Thickness(8, 0), HeightRequest = 32 };
    private readonly ActivityIndicator _spinner = new ActivityIndicator { IsRunning = false, IsVisible = false, HeightRequest = 20 };
    private readonly ObservableCollection<ResultItem> _items = new ObservableCollection<ResultItem>();

    private List<SearchResult> _results = new List<SearchResult>();
    private List<IndexerSearchStatus> _failed = new List<IndexerSearchStatus>();
    private CancellationTokenSource _searchCancellation;

    public SearchPage()
    {
        Title = "Search";

        foreach (var choice in CategoryChoices)
            _category.Items.Add(choice.Name);
        _category.SelectedIndex = 0;

        foreach (var choice in SortChoices)
            _sort.Items.Add(choice.Name);
        _sort.SelectedIndex = 0;
        _sort.SelectedIndexChanged += (_, _) => ShowResults();

        _searchBar.SearchButtonPressed += async (_, _) => await SearchAsync();
        _failures.Clicked += async (_, _) => await ShowFailuresAsync();

        var list = new CollectionView
        {
            ItemsSource = _items,
            SelectionMode = SelectionMode.Single,
            ItemTemplate = new DataTemplate(CreateResultCell),
            EmptyView = new Label
            {
                Text = "Type a search and press Search.\nAdd indexers in the Indexers tab first.",
                HorizontalTextAlignment = TextAlignment.Center,
                TextColor = Colors.Gray,
                Margin = new Thickness(24, 48)
            }
        };
        list.SelectionChanged += async (_, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is ResultItem item)
            {
                list.SelectedItem = null;
                await ReleaseActions.ShowAsync(this, item.Result);
            }
        };

        var filters = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 8,
            Padding = new Thickness(12, 0)
        };
        filters.Add(_category, 0, 0);
        filters.Add(_sort, 1, 0);

        var statusRow = new HorizontalStackLayout
        {
            Spacing = 8,
            Padding = new Thickness(16, 0),
            Children = { _spinner, _status, _failures }
        };

        var layout = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            }
        };
        layout.Add(_searchBar, 0, 0);
        layout.Add(filters, 0, 1);
        layout.Add(statusRow, 0, 2);
        layout.Add(list, 0, 3);
        Content = layout;
    }

    private async Task SearchAsync()
    {
        var host = AppHost.Current;
        var query = _searchBar.Text?.Trim();
        if (host == null || string.IsNullOrEmpty(query))
            return;

        var configured = host.Setup.GetIndexers().Count(i => i.IsConfigured);
        if (configured == 0)
        {
            await DisplayAlertAsync("No indexers", "Add at least one indexer in the Indexers tab.", "OK");
            return;
        }

        _searchCancellation?.Cancel();
        _searchCancellation = new CancellationTokenSource();
        var token = _searchCancellation.Token;

        _searchBar.Unfocus();
        _items.Clear();
        _failures.IsVisible = false;
        _spinner.IsVisible = _spinner.IsRunning = true;
        var done = 0;
        _status.Text = $"Searching {configured} indexers…";

        // created on the UI thread, so the callbacks come back on it
        var progress = new Progress<IndexerSearchStatus>(_ =>
        {
            if (!token.IsCancellationRequested)
                _status.Text = $"Searching… {++done}/{configured} indexers answered";
        });

        var request = new SearchRequest
        {
            Query = query,
            Categories = CategoryChoices[Math.Max(_category.SelectedIndex, 0)].Categories
        };
        var timeout = TimeSpan.FromSeconds(Math.Max(host.Settings.SearchTimeoutSeconds, 10));

        try
        {
            var response = await Task.Run(() => host.Search.SearchAsync(request, timeout, progress, token), token);
            if (token.IsCancellationRequested)
                return;

            _results = response.Results;
            _failed = response.Indexers.Where(i => !i.Succeeded).ToList();
            ShowResults();

            var succeeded = response.Indexers.Count - _failed.Count;
            _status.Text = $"{_results.Count} results from {succeeded} indexers";
            _failures.Text = $"⚠ {_failed.Count} failed";
            _failures.IsVisible = _failed.Count > 0;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            _status.Text = "Search failed: " + e.Message;
        }
        finally
        {
            if (!token.IsCancellationRequested)
                _spinner.IsVisible = _spinner.IsRunning = false;
        }
    }

    private void ShowResults()
    {
        var sort = SortChoices[Math.Max(_sort.SelectedIndex, 0)].Sort;
        _items.Clear();
        foreach (var result in SearchService.Sort(_results, sort))
            _items.Add(new ResultItem(result));
    }

    private Task ShowFailuresAsync()
    {
        var text = string.Join("\n\n", _failed.Select(f => $"{f.IndexerName}: {f.Error}"));
        return DisplayAlertAsync("Indexers that failed", text, "OK");
    }

    private static View CreateResultCell()
    {
        var title = new Label { FontAttributes = FontAttributes.Bold, MaxLines = 3, LineBreakMode = LineBreakMode.TailTruncation };
        title.SetBinding(Label.TextProperty, nameof(ResultItem.Title));

        var stats = new Label { FontSize = 13 };
        stats.SetBinding(Label.TextProperty, nameof(ResultItem.Stats));

        var source = new Label { FontSize = 12, TextColor = Colors.Gray };
        source.SetBinding(Label.TextProperty, nameof(ResultItem.Source));

        return new VerticalStackLayout
        {
            Padding = new Thickness(16, 10),
            Spacing = 3,
            Children = { title, stats, source }
        };
    }

    private class ResultItem
    {
        public ResultItem(SearchResult result)
        {
            Result = result;
            Title = result.Title;
            Stats = $"▲ {result.Seeders?.ToString() ?? "?"}   ▼ {result.Peers?.ToString() ?? "?"}   {result.SizeText}" +
                    (result.IsFreeleech ? "   FREELEECH" : "");
            var age = result.PublishDate == default ? "" : " · " + FormatAge(DateTime.Now - result.PublishDate);
            Source = result.IndexerName + age + (string.IsNullOrEmpty(result.Category) ? "" : " · " + result.Category);
        }

        public SearchResult Result { get; }
        public string Title { get; }
        public string Stats { get; }
        public string Source { get; }

        private static string FormatAge(TimeSpan age) =>
            age.TotalDays >= 365 ? $"{(int)(age.TotalDays / 365)}y ago" :
            age.TotalDays >= 1 ? $"{(int)age.TotalDays}d ago" :
            age.TotalHours >= 1 ? $"{(int)age.TotalHours}h ago" :
            "just now";
    }
}
