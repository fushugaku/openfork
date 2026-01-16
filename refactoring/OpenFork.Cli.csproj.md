# Refactoring: OpenFork.Cli.csproj

## Overview
Update project dependencies from Spectre.Console to Terminal.Gui.

## Current Dependencies
```xml
<PackageReference Include="Spectre.Console" Version="0.49.1" />
<PackageReference Include="Spectre.Console.Cli" Version="0.49.1" />
```

## Target Dependencies
```xml
<PackageReference Include="Terminal.Gui" Version="2.0.0-alpha.*" />
```

## Migration Steps

### 1. Remove Spectre.Console packages
- Remove `Spectre.Console` (v0.49.1)
- Remove `Spectre.Console.Cli` (v0.49.1)

### 2. Add Terminal.Gui package
- Add `Terminal.Gui` v2.0.0-alpha (latest alpha)

### 3. Keep existing dependencies
- `Microsoft.Extensions.Configuration.Json` (9.0.0)
- `Microsoft.Extensions.Hosting` (9.0.0)
- `Microsoft.Extensions.Http` (9.0.0)
- `Microsoft.Extensions.Options.ConfigurationExtensions` (9.0.0)

## Final csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\OpenFork.Core\OpenFork.Core.csproj" />
    <ProjectReference Include="..\OpenFork.Providers\OpenFork.Providers.csproj" />
    <ProjectReference Include="..\OpenFork.Search\OpenFork.Search.csproj" />
    <ProjectReference Include="..\OpenFork.Storage\OpenFork.Storage.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="9.0.0" />
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="9.0.0" />
    <PackageReference Include="Microsoft.Extensions.Http" Version="9.0.0" />
    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="9.0.0" />
    <PackageReference Include="Terminal.Gui" Version="2.0.0-alpha.*" />
  </ItemGroup>

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

</Project>
```

## Testing
After updating:
```bash
cd src/OpenFork.Cli
dotnet restore
dotnet build
```

Verify no Spectre.Console references remain in bin directory.
