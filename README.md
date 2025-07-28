# Party Finder Reborn - Dalamud Plugin

A modern party matching system for Final Fantasy XIV, built as a Dalamud plugin.

## Features

- Hello World ImGui window to demonstrate basic functionality
- Configuration system for plugin settings
- Modern .NET 9 architecture
- Integration with ECommons for easy Dalamud service access

## Development Setup

### Prerequisites

- .NET 9 SDK
- XIVLauncher with Dalamud installed
- Final Fantasy XIV (for testing)

### Building

1. Clone the repository
2. Navigate to the plugin directory:
   ```bash
   cd party-finder-plugin/PartyFinderReborn
   ```
3. Restore packages and build:
   ```bash
   dotnet restore
   dotnet build
   ```

### Installing for Development

1. Build the plugin (see above)
2. Copy the output files from `bin/Debug/` to your Dalamud dev plugins folder
3. In-game, use `/xlsettings` → Experimental → Dev Plugin Locations to add the path
4. Enable the plugin in `/xlplugins` → Dev Tools → Installed Dev Plugins

### Usage

- Use `/pfreborn` command to open the main window
- Access configuration through the Dalamud plugin settings

## Project Structure

```
PartyFinderReborn/
├── Plugin.cs              # Main plugin class
├── Configuration.cs       # Plugin configuration
├── Constants.cs          # API endpoints and constants
├── Windows/
│   ├── MainWindow.cs     # Main plugin window
│   └── ConfigWindow.cs   # Configuration window
├── PartyFinderReborn.json # Plugin manifest
└── PartyFinderReborn.csproj # Project file
```

## Dependencies

- **Dalamud.NET.Sdk**: 12.0.2
- **ECommons**: 3.0.0.7 (by NightmareXIV)

## License

[Your license here]
