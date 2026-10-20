using Microsoft.Maui.ApplicationModel.DataTransfer;
using Jackett.Mobile.Core.Models;

namespace Jackett.Mobile.Pages;

// What can be done with a search result: open the magnet in a torrent app, share the .torrent file...
public static class ReleaseActions
{
    private const string OpenMagnet = "Open magnet link";
    private const string CopyMagnet = "Copy magnet link";
    private const string ShareTorrent = "Download .torrent file";
    private const string OpenDetails = "Open details page";
    private const string CopyTitle = "Copy title";

    public static async Task ShowAsync(Page page, SearchResult result)
    {
        var actions = new List<string>();
        if (result.HasMagnet)
        {
            actions.Add(OpenMagnet);
            actions.Add(CopyMagnet);
        }
        if (result.HasTorrentFile)
            actions.Add(ShareTorrent);
        if (result.Details != null)
            actions.Add(OpenDetails);
        actions.Add(CopyTitle);

        var choice = await page.DisplayActionSheetAsync(result.Title, "Cancel", null, actions.ToArray());
        try
        {
            switch (choice)
            {
                case OpenMagnet:
                    await OpenMagnetAsync(page, result.BestMagnet);
                    break;
                case CopyMagnet:
                    await Clipboard.Default.SetTextAsync(result.BestMagnet.AbsoluteUri);
                    break;
                case ShareTorrent:
                    await DownloadAsync(page, result);
                    break;
                case OpenDetails:
                    await Browser.Default.OpenAsync(result.Details, BrowserLaunchMode.SystemPreferred);
                    break;
                case CopyTitle:
                    await Clipboard.Default.SetTextAsync(result.Title);
                    break;
            }
        }
        catch (Exception e)
        {
            await page.DisplayAlertAsync("Something went wrong", e.Message, "OK");
        }
    }

    private static async Task OpenMagnetAsync(Page page, Uri magnet)
    {
        if (await Launcher.Default.CanOpenAsync(magnet) && await Launcher.Default.OpenAsync(magnet))
            return;

        await Clipboard.Default.SetTextAsync(magnet.AbsoluteUri);
        await page.DisplayAlertAsync("No torrent app found",
                                     "No installed app opens magnet links, so the link was copied instead.", "OK");
    }

    private static async Task DownloadAsync(Page page, SearchResult result)
    {
        var host = AppHost.Current;
        var outcome = await Task.Run(() => host.Downloads.DownloadAsync(result));

        if (outcome.IsMagnet)
        {
            await OpenMagnetAsync(page, outcome.MagnetUri);
            return;
        }

        var path = Path.Combine(FileSystem.CacheDirectory, outcome.FileName);
        await File.WriteAllBytesAsync(path, outcome.TorrentBytes);
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = result.Title,
            File = new ShareFile(path, "application/x-bittorrent")
        });
    }
}
