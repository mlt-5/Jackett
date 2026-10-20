using System.Collections.ObjectModel;
using Jackett.Mobile.Core.Models;

namespace Jackett.Mobile.Pages;

public class IndexersPage : ContentPage
{
    private static readonly string[] Filters = { "Configured", "All", "Public", "Semi-private", "Private" };

    private readonly SearchBar _searchBar = new SearchBar { Placeholder = "Filter by name, language or tag" };
    private readonly Picker _filter = new Picker { Title = "Show" };
    private readonly Label _count = new Label { FontSize = 13, TextColor = Colors.Gray, VerticalOptions = LayoutOptions.Center };
    private readonly ObservableCollection<IndexerItem> _items = new ObservableCollection<IndexerItem>();
    private bool _filterChosen;

    public IndexersPage()
    {
        Title = "Indexers";

        foreach (var filter in Filters)
            _filter.Items.Add(filter);
        _filter.SelectedIndexChanged += (_, _) =>
        {
            _filterChosen = true;
            Refresh();
        };
        _searchBar.TextChanged += (_, _) => Refresh();

        var list = new CollectionView
        {
            ItemsSource = _items,
            SelectionMode = SelectionMode.Single,
            ItemTemplate = new DataTemplate(CreateCell),
            EmptyView = new Label { Text = "No indexers match.", TextColor = Colors.Gray, Margin = 24, HorizontalTextAlignment = TextAlignment.Center }
        };
        list.SelectionChanged += async (_, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is IndexerItem item)
            {
                list.SelectedItem = null;
                await Navigation.PushAsync(new IndexerConfigPage(item.Id));
            }
        };

        var filterRow = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            Padding = new Thickness(16, 0),
            ColumnSpacing = 12
        };
        filterRow.Add(_filter, 0, 0);
        filterRow.Add(_count, 1, 0);

        var layout = new Grid
        {
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) }
        };
        layout.Add(_searchBar, 0, 0);
        layout.Add(filterRow, 0, 1);
        layout.Add(list, 0, 2);
        Content = layout;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // start on "All" until the user has configured something
        if (!_filterChosen)
        {
            var anyConfigured = AppHost.Current?.Setup.GetIndexers().Any(i => i.IsConfigured) == true;
            _filter.SelectedIndex = anyConfigured ? 0 : 1;
            _filterChosen = false;
        }
        Refresh();
    }

    private void Refresh()
    {
        var host = AppHost.Current;
        if (host == null)
            return;

        var text = _searchBar.Text?.Trim() ?? "";
        var filter = Filters[Math.Max(_filter.SelectedIndex, 0)];

        var indexers = host.Setup.GetIndexers().Where(i => filter switch
        {
            "Configured" => i.IsConfigured,
            "Public" => i.Type == "public",
            "Semi-private" => i.Type == "semi-private",
            "Private" => i.Type == "private",
            _ => true
        });

        if (text.Length > 0)
            indexers = indexers.Where(i => Matches(i, text));

        _items.Clear();
        foreach (var indexer in indexers.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
            _items.Add(new IndexerItem(indexer));
        _count.Text = $"{_items.Count}";
    }

    private static bool Matches(IndexerSummary indexer, string text) =>
        indexer.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
        || (indexer.Language ?? "").Contains(text, StringComparison.OrdinalIgnoreCase)
        || indexer.Tags.Any(t => t.Contains(text, StringComparison.OrdinalIgnoreCase));

    private static View CreateCell()
    {
        var name = new Label { FontAttributes = FontAttributes.Bold };
        name.SetBinding(Label.TextProperty, nameof(IndexerItem.Name));

        var details = new Label { FontSize = 12, TextColor = Colors.Gray };
        details.SetBinding(Label.TextProperty, nameof(IndexerItem.Details));

        var status = new Label { FontSize = 13, VerticalOptions = LayoutOptions.Center };
        status.SetBinding(Label.TextProperty, nameof(IndexerItem.Status));

        var grid = new Grid
        {
            Padding = new Thickness(16, 10),
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
        };
        grid.Add(new VerticalStackLayout { Spacing = 2, Children = { name, details } }, 0, 0);
        grid.Add(status, 1, 0);
        return grid;
    }

    private class IndexerItem
    {
        public IndexerItem(IndexerSummary indexer)
        {
            Id = indexer.Id;
            Name = indexer.Name;
            Details = $"{indexer.Type} · {indexer.Language}";
            Status = !indexer.IsConfigured ? "" : indexer.HasError ? "⚠️" : "✅";
        }

        public string Id { get; }
        public string Name { get; }
        public string Details { get; }
        public string Status { get; }
    }
}
