using Foundation;
using UIKit;

namespace Jackett.Mobile;

public class Program
{
    private static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp()
    {
        AppHost.Platform = new IosPlatformServices();
        return MauiProgram.CreateMauiApp();
    }
}
