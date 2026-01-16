namespace OpenFork.Core.Lsp;

public static class LspLanguages
{
  public static readonly Dictionary<string, string> ExtensionToLanguageId = new(StringComparer.OrdinalIgnoreCase)
    {
        // C#
        { ".cs", "csharp" },
        
        // TypeScript/JavaScript
        { ".ts", "typescript" },
        { ".tsx", "typescriptreact" },
        { ".js", "javascript" },
        { ".jsx", "javascriptreact" },
        { ".mjs", "javascript" },
        { ".cjs", "javascript" },
        { ".mts", "typescript" },
        { ".cts", "typescript" },
        
        // Python
        { ".py", "python" },
        { ".pyi", "python" },
        
        // Go
        { ".go", "go" },
        
        // Rust
        { ".rs", "rust" },
        
        // Java
        { ".java", "java" },
        
        // C/C++
        { ".c", "c" },
        { ".cpp", "cpp" },
        { ".cc", "cpp" },
        { ".cxx", "cpp" },
        { ".h", "c" },
        { ".hpp", "cpp" },
        { ".hh", "cpp" },
        { ".hxx", "cpp" },
        
        // Ruby
        { ".rb", "ruby" },
        { ".rake", "ruby" },
        { ".gemspec", "ruby" },
        
        // PHP
        { ".php", "php" },
        
        // Swift
        { ".swift", "swift" },
        
        // Kotlin
        { ".kt", "kotlin" },
        { ".kts", "kotlin" },
        
        // Lua
        { ".lua", "lua" },
        
        // Elixir
        { ".ex", "elixir" },
        { ".exs", "elixir" },
        
        // Zig
        { ".zig", "zig" },
        { ".zon", "zig" },
        
        // F#
        { ".fs", "fsharp" },
        { ".fsi", "fsharp" },
        { ".fsx", "fsharp" },
        
        // Vue
        { ".vue", "vue" },
        
        // Svelte
        { ".svelte", "svelte" },
        
        // Astro
        { ".astro", "astro" },
        
        // YAML
        { ".yaml", "yaml" },
        { ".yml", "yaml" },
        
        // JSON
        { ".json", "json" },
        { ".jsonc", "jsonc" },
        
        // Markdown
        { ".md", "markdown" },
        
        // HTML
        { ".html", "html" },
        { ".htm", "html" },
        
        // CSS
        { ".css", "css" },
        { ".scss", "scss" },
        { ".sass", "sass" },
        { ".less", "less" },
        
        // Shell
        { ".sh", "shellscript" },
        { ".bash", "shellscript" },
        { ".zsh", "shellscript" },
        
        // SQL
        { ".sql", "sql" },
        
        // Terraform
        { ".tf", "terraform" },
        { ".tfvars", "terraform-vars" },
        
        // Docker
        { ".dockerfile", "dockerfile" },
        { "Dockerfile", "dockerfile" },
        
        // Nix
        { ".nix", "nix" },
        
        // Prisma
        { ".prisma", "prisma" },
    };

  public static string GetLanguageId(string filePath)
  {
    var ext = Path.GetExtension(filePath);
    if (string.IsNullOrEmpty(ext))
    {
      var fileName = Path.GetFileName(filePath);
      if (ExtensionToLanguageId.TryGetValue(fileName, out var langId))
        return langId;
      return "plaintext";
    }

    return ExtensionToLanguageId.TryGetValue(ext, out var languageId)
        ? languageId
        : "plaintext";
  }
}
