# Jackett for iOS

A standalone iOS app that runs Jackett on the phone: no server, no web UI.
It uses the same tracker code as the server (`Jackett.Common`), so all the YAML
definitions and C# indexers are included.

| Tab | What it does |
|-----|--------------|
| **Search** | Searches every configured indexer at once. Tap a result to open its magnet link in a torrent app, copy it, or share the `.torrent` file. |
| **Indexers** | Lists all indexers. Tap one to set it up (same fields as the web UI), test it or remove it. |
| **Settings** | Cloudflare options, search timeout, optional FlareSolverr address, clearing cookies or the search cache, and sharing the log file. |

## Cloudflare checks and captcha logins

* **"Checking your browser" pages** (Cloudflare, DDoS-Guard): when a tracker answers with one, the
  app opens the page in a browser sheet. It usually passes by itself; sometimes you tap the checkbox.
  The cookies and the browser User-Agent are saved (`browser-cookies.json`) and reused, so you are
  asked again only when they expire. If you cancel, or if the site still refuses the browser's cookies, that site is not asked again until
  the app restarts (or the setting is switched off and on).
* **Logins with captchas**: indexers that log in with a cookie get a **Log in with the in-app browser**
  button. Log in on the real site, tap **Done**, and the cookie (and User-Agent) fields are filled for you.
* **iOS networking** (on by default): requests go through `NSUrlSession`, the same network stack as
  Safari, which Cloudflare flags less often than .NET's own one. It can be turned off in Settings.
* **FlareSolverr** (optional): if a site still cannot be passed, a FlareSolverr server on your network
  can be set in Settings.

Sites that block whole IP ranges (mobile carriers, VPNs) or use stricter bot protection than a normal
check will still fail.

## Limits

* Searches run only while the app is open. iOS does not allow a background server, so Sonarr or Radarr
  cannot use the app as their indexer.
* The App Store usually rejects torrent indexer apps. Install it from Xcode, TestFlight or a
  sideloading tool such as AltStore.

## Build and run (needs a Mac)

1. Install Xcode (with an iOS simulator) and the [.NET 10 SDK](https://dotnet.microsoft.com/download).
2. Install the MAUI workload:
   ```sh
   sudo dotnet workload install maui-ios
   ```
3. Run in the simulator:
   ```sh
   cd src/Jackett.Mobile
   dotnet build -t:Run -f net10.0-ios
   ```
4. To run on an iPhone:
   1. Change `ApplicationId` in `Jackett.Mobile.csproj` to an identifier of your own, e.g. `com.yourname.jackett`.
   2. Create a signing profile for it: in Xcode, make a new empty iOS app with exactly that bundle identifier,
      choose your Apple ID under *Signing & Capabilities → Team*, and run it once on the iPhone (plugged in, with
      Developer Mode on). Xcode then creates the certificate and provisioning profile that .NET reuses.
   3. With the iPhone still connected, build and install:
      ```sh
      dotnet build -t:Run -f net10.0-ios -c Release -p:RuntimeIdentifier=ios-arm64
      ```
      If several certificates or profiles exist, name them with `-p:CodesignKey="Apple Development: ..."` and
      `-p:CodesignProvision="..."`.

   With a free Apple ID the app stops opening after 7 days and has to be installed again.

## Adding or updating tracker definitions

The definitions in `src/Jackett.Common/Definitions` are built into the app. To add or update one
without rebuilding, copy the `.yml` file to **Files → On My iPhone → Jackett → Definitions** and restart
the app. A file there replaces the built-in definition with the same `id`.

## Project layout

| Project | Contents |
|---------|----------|
| `Jackett.Mobile` | The MAUI app: pages (written in C#, no XAML) and the iOS-only code in `Platforms/iOS` (WKWebView browser sheet, `NSUrlSession` transport). |
| `Jackett.Mobile.Core` | Runs `Jackett.Common` without ASP.NET: startup, search, indexer setup, downloads, password encryption, and the Cloudflare challenge handling. Builds on any OS. |
| `Jackett.Mobile.Core.Test` | Tests for the core, including starting the full host with every definition. Run with `dotnet test src/Jackett.Mobile.Core.Test`. |

The app data (indexer settings, logs, cookies) is stored in the app's `Library` folder. The key
that encrypts saved passwords is stored in the iOS Keychain (or, if the Keychain is not available, in a
file next to the data). As in the server, login cookies are saved unencrypted in the indexer settings.
