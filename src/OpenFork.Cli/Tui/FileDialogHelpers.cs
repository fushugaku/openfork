using Terminal.Gui;

namespace OpenFork.Cli.Tui;

public static class FileDialogHelpers
{
    public static string? SelectFile(string startDirectory, string title = "Select File")
    {
        var dialog = new OpenDialog(title, "Select a file")
        {
            AllowsMultipleSelection = false,
            DirectoryPath = startDirectory,
            CanChooseDirectories = false,
            CanChooseFiles = true
        };

        Application.Run(dialog);

        if (dialog.Canceled || dialog.FilePaths.Count == 0)
            return null;

        return dialog.FilePaths[0];
    }

    /// <summary>
    /// Opens a folder browser dialog that allows selecting the current folder.
    /// </summary>
    public static string? SelectFolder(string startDirectory, string title = "Select Folder")
    {
        var currentPath = Directory.Exists(startDirectory)
            ? Path.GetFullPath(startDirectory)
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var dialog = new Dialog(title)
        {
            Width = Dim.Percent(80),
            Height = Dim.Percent(80),
            ColorScheme = Theme.Schemes.Dialog
        };

        // Current path display
        var pathLabel = new Label("Current folder:")
        {
            X = 1,
            Y = 0,
            ColorScheme = Theme.Schemes.Muted
        };

        var pathField = new TextField(currentPath)
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill() - 2,
            ReadOnly = true,
            ColorScheme = Theme.Schemes.Input
        };

        // Folder list
        var listFrame = new FrameView("Contents")
        {
            X = 1,
            Y = 3,
            Width = Dim.Fill() - 2,
            Height = Dim.Fill() - 6,
            ColorScheme = Theme.Schemes.Panel
        };

        var listView = new ListView()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ColorScheme = Theme.Schemes.List
        };
        listFrame.Add(listView);

        string? result = null;

        // Populate folder list
        void RefreshList()
        {
            pathField.Text = currentPath;

            var items = new List<string>();

            // Add parent directory option
            var parent = Directory.GetParent(currentPath);
            if (parent != null)
            {
                items.Add("📁 ..");
            }

            try
            {
                // Add subdirectories
                var dirs = Directory.GetDirectories(currentPath)
                    .Select(d => new DirectoryInfo(d))
                    .Where(d => !d.Name.StartsWith('.')) // Hide hidden folders
                    .OrderBy(d => d.Name);

                foreach (var dir in dirs)
                {
                    items.Add($"📁 {dir.Name}");
                }

                // Add files (for context, but not selectable)
                var files = Directory.GetFiles(currentPath)
                    .Select(f => new FileInfo(f))
                    .Where(f => !f.Name.StartsWith('.'))
                    .OrderBy(f => f.Name)
                    .Take(50); // Limit file display

                foreach (var file in files)
                {
                    items.Add($"📄 {file.Name}");
                }
            }
            catch (UnauthorizedAccessException)
            {
                items.Add("(access denied)");
            }
            catch (Exception ex)
            {
                items.Add($"(error: {ex.Message})");
            }

            listView.SetSource(items);
        }

        // Navigate to folder
        void NavigateTo(string path)
        {
            if (Directory.Exists(path))
            {
                currentPath = Path.GetFullPath(path);
                RefreshList();
            }
        }

        // Handle double-click or Enter on list item
        listView.OpenSelectedItem += (e) =>
        {
            var selected = listView.SelectedItem;
            var items = listView.Source.ToList();

            if (selected < 0 || selected >= items.Count)
                return;

            var item = items[selected]?.ToString() ?? "";

            if (item == "📁 ..")
            {
                // Go up
                var parent = Directory.GetParent(currentPath);
                if (parent != null)
                {
                    NavigateTo(parent.FullName);
                }
            }
            else if (item.StartsWith("📁 "))
            {
                // Enter subfolder
                var folderName = item[3..]; // Remove "📁 " prefix
                var newPath = Path.Combine(currentPath, folderName);
                NavigateTo(newPath);
            }
            // Ignore file clicks
        };

        // Hint
        var hintLabel = new Label("Enter/DblClick: Navigate | Use current folder to select")
        {
            X = 1,
            Y = Pos.AnchorEnd(3),
            ColorScheme = Theme.Schemes.Muted
        };

        // Buttons
        var useButton = new Button("✓ _Use This Folder")
        {
            ColorScheme = Theme.Schemes.Button
        };
        useButton.Clicked += () =>
        {
            result = currentPath;
            Application.RequestStop();
        };

        var cancelButton = new Button("_Cancel")
        {
            ColorScheme = Theme.Schemes.Button
        };
        cancelButton.Clicked += () =>
        {
            result = null;
            Application.RequestStop();
        };

        // Up button for quick navigation
        var upButton = new Button("⬆️ _Up")
        {
            ColorScheme = Theme.Schemes.Button
        };
        upButton.Clicked += () =>
        {
            var parent = Directory.GetParent(currentPath);
            if (parent != null)
            {
                NavigateTo(parent.FullName);
            }
        };

        // Home button
        var homeButton = new Button("🏠 _Home")
        {
            ColorScheme = Theme.Schemes.Button
        };
        homeButton.Clicked += () =>
        {
            NavigateTo(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        };

        dialog.Add(pathLabel, pathField, listFrame, hintLabel);
        dialog.AddButton(useButton);
        dialog.AddButton(upButton);
        dialog.AddButton(homeButton);
        dialog.AddButton(cancelButton);

        // Initial population
        RefreshList();

        // Focus the list
        listView.SetFocus();

        Application.Run(dialog);

        return result;
    }

    public static string? SaveFile(string startDirectory, string defaultFileName = "", string title = "Save File")
    {
        var dialog = new SaveDialog(title, "Choose file to save")
        {
            DirectoryPath = startDirectory,
            FilePath = defaultFileName
        };

        Application.Run(dialog);

        if (dialog.Canceled)
            return null;

        return dialog.FilePath?.ToString();
    }
}
