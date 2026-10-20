# Jackett – Repository Mind Map

Jackett is a proxy server: it takes Torznab/TorrentPotato queries from apps
(Sonarr, Radarr, Lidarr…), turns them into tracker-specific HTTP requests,
scrapes/parses the tracker's response, and returns results in a common format.

```mermaid
mindmap
  root((Jackett))
    src/ – .NET solution Jackett.sln
      Jackett.Common – core library
        Definitions/ – 586 YAML tracker definitions + schema.json
        Indexers/
          CardigannIndexer – runs the YAML definitions
          Definitions/ – ~60 hand-written C# trackers
          Abstract/ – Gazelle, Avistaz, SpeedApp… base classes
          Feeds/ – Newznab/RSS feed base
          Meta/ – aggregate "all" indexer
        Services/ – config, indexer manager, cache, updates
        Models/ – TorznabQuery, ReleaseInfo, configs, DTOs
        Utils/ – HTTP clients, parsing, logging, filters
        Content/ – Web UI HTML/JS/CSS
      Jackett.Server – ASP.NET host, entry point
        Controllers/ – Torznab API, UI, download, blackhole
        Middleware / Services – auth, security, server
      DateTimeRoutines – date parsing helper
      Jackett.Tray – Windows tray app
      Jackett.Service – Windows service
      Jackett.Updater – self updater
      Jackett.Test – unit tests
      Jackett.IntegrationTests – integration tests
      Jackett.Mobile – iOS app (MAUI)
      Jackett.Mobile.Core – runs Common without the web server
      Jackett.Mobile.Core.Test – tests of the mobile core
    docs/ – generated YAML schema docs
    .github/ – issue templates, workflows, screenshots
    Root install scripts
      systemd / macOS / Windows launchers
      Installer.iss – Windows installer
      azure-pipelines.yml – CI/release build
```

## Directory tree (top level)

```
Jackett/
├── src/
│   ├── Jackett.sln                 Solution file (lists all projects)
│   ├── Directory.Build.props       Shared build settings
│   ├── Jackett.Common/             ★ CORE – everything else depends on it
│   │   ├── Definitions/*.yml       586 YAML trackers (Cardigann engine) + schema.json
│   │   ├── Indexers/               Indexer engine + ~60 C# trackers
│   │   ├── Services/               ConfigurationService, IndexerManagerService, CacheService, UpdateService…
│   │   ├── Models/                 Torznab models, indexer config models
│   │   ├── Utils/                  WebClient(s), parsers, logging, filter funcs
│   │   └── Content/                Web dashboard (index.html, custom.js, bootstrap…)
│   ├── Jackett.Server/             ★ Main executable (ASP.NET Core): Program.cs, Startup.cs, Controllers
│   ├── DateTimeRoutines/           Used by Jackett.Common for date parsing
│   ├── Jackett.Tray/               Windows-only system tray app
│   ├── Jackett.Service/            Windows-only service wrapper
│   ├── Jackett.Updater/            Self-update helper
│   ├── Jackett.Test/               Unit tests (NUnit)
│   ├── Jackett.IntegrationTests/   Integration tests
│   ├── Jackett.Mobile.sln          Solution for the iOS app
│   ├── Jackett.Mobile/             iOS app (MAUI): search, indexer setup, in-app browser for Cloudflare
│   ├── Jackett.Mobile.Core/        Runs Jackett.Common without ASP.NET (used by the app)
│   └── Jackett.Mobile.Core.Test/   Tests of the mobile core
├── docs/                           ~250 auto-generated schema docs (from schema.json)
├── .github/                        Issue/PR templates, dependabot, workflows
│   └── workflows/                  codeql, definitions-update, issuebot, schema-docs, winget
├── azure-pipelines.yml             Build/release pipeline for all platforms
├── Installer.iss                   Inno Setup Windows installer
├── install_service_*.sh / *_macos  Linux/macOS service install/uninstall scripts
├── jackett_launcher.{sh,bat}       Launchers used by the updater
├── Upstart.config                  Legacy Upstart service config
└── README.md, CONTRIBUTING.md, SECURITY.md, LICENSE
```

## Project dependency graph

```mermaid
graph TD
  DT[DateTimeRoutines] --> C[Jackett.Common]
  C --> S[Jackett.Server]
  C --> T[Jackett.Tray]
  C --> SV[Jackett.Service]
  C --> U[Jackett.Updater]
  C --> TE[Jackett.Test]
  S --> TE
  C --> MC[Jackett.Mobile.Core]
  MC --> M[Jackett.Mobile iOS app]
  MC --> MT[Jackett.Mobile.Core.Test]
```

## Request flow

```
Sonarr/Radarr ──► Jackett.Server Controllers/ResultsController (Torznab API)
                    └─► IndexerManagerService ─► Indexer (C# class or CardigannIndexer + YAML)
                          └─► Utils/Clients/WebClient ─► tracker website
                    ◄── ReleaseInfo list ◄── parse HTML/JSON
```

## What can be removed? (quick guide)

| Item | Safe to remove? | Notes |
|------|-----------------|-------|
| Individual `Definitions/*.yml` | ✅ Yes | Just that tracker disappears. Check `Jackett.Test` for references. |
| Individual C# tracker in `Indexers/Definitions/` | ✅ Mostly | Remove any tests that reference it; abstract base classes are shared. |
| `docs/` | ✅ Yes | Generated docs; also remove `.github/workflows/schema-docs.yml`. |
| `.github/` screenshots, issue templates, workflows | ✅ Yes | Only affects GitHub UX/automation. |
| `Jackett.Tray`, `Jackett.Service` | ⚠️ Yes, if not on Windows | Remove from `Jackett.sln`, `Installer.iss`, `azure-pipelines.yml`. |
| `Jackett.Updater` + `jackett_launcher.*` | ⚠️ Yes, if no self-update | `UpdateService` in Common calls it; disable updates. |
| Install scripts / `Upstart.config` / `Installer.iss` | ⚠️ Per platform | Only packaging; referenced by `azure-pipelines.yml`. |
| `Jackett.Test`, `Jackett.IntegrationTests` | ⚠️ Possible | Lose test coverage; remove from `.sln`/pipeline. |
| `Jackett.Common`, `Jackett.Server`, `DateTimeRoutines` | ❌ No | Core of the app. |
