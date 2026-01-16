# OpenFork

OpenFork is a C# Spectre.Console TUI inspired by OpenCode, using OpenAI-compatible providers and a local SQLite database.

## Requirements

- .NET SDK 9 or later
- SQLite (bundled via Microsoft.Data.Sqlite)

## Configure

Edit `config/appsettings.json` and set an API key environment variable:

```
export ZAI_API_KEY="your_key"
```

## Run

```
dotnet run --project src/OpenFork.Cli/OpenFork.Cli.csproj
```

The app stores data in `data/openfork.db` by default.
