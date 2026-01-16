using Spectre.Console;

namespace OpenFork.Cli.Tui;

public class Browser
{
    public bool DisplayIcons { get; set; } = true;
    public bool IsWindows { get; }
    public int PageSize { get; set; } = 15;
    public bool CanCreateFolder { get; set; } = true;
    public string ActualFolder { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public string SelectedFile { get; set; } = "";
    public string LevelUpText { get; set; } = "Go up";
    public string ActualFolderText { get; set; } = "Current";
    public string MoreChoicesText { get; set; } = "↑↓ navigate";
    public string CreateNewText { get; set; } = "New folder";
    public string SelectFileText { get; set; } = "Select File";
    public string SelectFolderText { get; set; } = "Select Folder";
    public string SelectDriveText { get; set; } = "Select Drive";
    public string SelectActualText { get; set; } = "Use this folder";
    public string[]? Drives { get; set; }

    private string _lastFolder;

    public Browser()
    {
        var os = Environment.OSVersion.Platform.ToString();
        IsWindows = os[..3].Equals("win", StringComparison.OrdinalIgnoreCase);
        _lastFolder = ActualFolder;
    }

    public async Task<string> GetPath(string actualFolder, bool selectFile)
    {
        _lastFolder = actualFolder;

        while (true)
        {
            var headerText = selectFile ? SelectFileText : SelectFolderText;
            string[] directoriesInFolder;

            try
            {
                Directory.SetCurrentDirectory(actualFolder);
            }
            catch
            {
                actualFolder = _lastFolder;
                Directory.SetCurrentDirectory(actualFolder);
            }

            AnsiConsole.Clear();
            RenderBrowserHeader(headerText, actualFolder);

            var folders = new Dictionary<string, string>();

            try
            {
                directoriesInFolder = Directory.GetDirectories(Directory.GetCurrentDirectory());
                _lastFolder = actualFolder;
            }
            catch
            {
                actualFolder = actualFolder == _lastFolder
                    ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                    : _lastFolder;

                Directory.SetCurrentDirectory(actualFolder);
                directoriesInFolder = Directory.GetDirectories(Directory.GetCurrentDirectory());
            }

            if (IsWindows)
            {
                folders.Add(FormatChoice(Icons.Folder, SelectDriveText, Theme.Secondary), "/////");
            }

            try
            {
                var parentDir = new DirectoryInfo(actualFolder).Parent;
                if (parentDir != null)
                {
                    folders.Add(FormatChoice("↑", LevelUpText, Theme.Muted), parentDir.FullName);
                }
            }
            catch { }

            if (!selectFile)
            {
                folders.Add(FormatChoice(Icons.Check, SelectActualText, Theme.Success), Directory.GetCurrentDirectory());
            }

            if (CanCreateFolder)
            {
                folders.Add(FormatChoice(Icons.Add, CreateNewText, Theme.Accent), "///new");
            }

            foreach (var d in directoriesInFolder)
            {
                var cut = new DirectoryInfo(actualFolder).Parent != null ? 1 : 0;
                var folderName = d[(actualFolder.Length + cut)..];
                folders.Add(FormatChoice(Icons.Folder, folderName, Theme.Primary), d);
            }

            if (selectFile)
            {
                var fileList = Directory.GetFiles(actualFolder);
                foreach (var file in fileList)
                {
                    var fileName = Path.GetFileName(file);
                    folders.Add(FormatChoice(Icons.File, fileName, Theme.Secondary), file);
                }
            }

            var selected = AnsiConsole.Prompt(
                Prompts.Selection<string>(selectFile ? SelectFileText : SelectFolderText)
                    .AddChoices(folders.Keys));

            _lastFolder = actualFolder;
            var record = folders.FirstOrDefault(s => s.Key == selected).Value ?? actualFolder;

            if (record == "/////")
            {
                record = SelectDrive();
                actualFolder = record;
                continue;
            }

            if (record == "///new")
            {
                var folderName = AnsiConsole.Prompt(Prompts.RequiredText("Folder name"));
                try
                {
                    Directory.CreateDirectory(folderName);
                    record = Path.Combine(actualFolder, folderName);
                }
                catch (Exception ex)
                {
                    AnsiConsole.Write(Panels.Error(ex.Message));
                    AnsiConsole.Prompt(new TextPrompt<string>("Press Enter").AllowEmpty());
                    continue;
                }
            }

            if (record == Directory.GetCurrentDirectory())
                return actualFolder;

            if (Directory.Exists(record))
            {
                actualFolder = record;
            }
            else
            {
                return record;
            }
        }
    }

    public Task<string> GetFilePath(string folder) => GetPath(folder, true);
    public Task<string> GetFilePath() => GetPath(ActualFolder, true);
    public Task<string> GetFolderPath(string folder) => GetPath(folder, false);
    public Task<string> GetFolderPath() => GetPath(ActualFolder, false);

    private void RenderBrowserHeader(string title, string path)
    {
        AnsiConsole.Write(new Rule($"[{Theme.Primary.ToMarkup()}]{title}[/]").LeftJustified().RuleStyle(Theme.MutedStyle));
        AnsiConsole.WriteLine();

        var textPath = new TextPath(path)
        {
            RootStyle = new Style(Theme.Success),
            SeparatorStyle = new Style(Theme.Muted),
            StemStyle = new Style(Theme.Secondary),
            LeafStyle = new Style(Theme.Accent)
        };

        var grid = new Grid()
            .AddColumn()
            .AddColumn()
            .AddRow(
                new Markup($"[{Theme.Muted.ToMarkup()}]{ActualFolderText}:[/]"),
                textPath
            );

        AnsiConsole.Write(grid);
        AnsiConsole.WriteLine();
    }

    private string SelectDrive()
    {
        Drives = Directory.GetLogicalDrives();
        var result = new Dictionary<string, string>();

        foreach (var drive in Drives)
        {
            result.Add(FormatChoice(Icons.Folder, drive, Theme.Primary), drive);
        }

        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule($"[{Theme.Primary.ToMarkup()}]{SelectDriveText}[/]").LeftJustified().RuleStyle(Theme.MutedStyle));
        AnsiConsole.WriteLine();

        var selected = AnsiConsole.Prompt(
            Prompts.Selection<string>(SelectDriveText)
                .AddChoices(result.Keys));

        return result.FirstOrDefault(s => s.Key == selected).Value ?? Drives.FirstOrDefault() ?? "C:\\";
    }

    private string FormatChoice(string icon, string text, Color color)
    {
        return DisplayIcons
            ? $"{icon} [{color.ToMarkup()}]{Markup.Escape(text)}[/]"
            : $"[{color.ToMarkup()}]{Markup.Escape(text)}[/]";
    }
}
