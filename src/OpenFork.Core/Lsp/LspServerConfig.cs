namespace OpenFork.Core.Lsp;

public class LspServerConfig
{
    public string Id { get; set; } = "";
    public string[] Extensions { get; set; } = Array.Empty<string>();
    public string[] RootPatterns { get; set; } = Array.Empty<string>();
    public string Command { get; set; } = "";
    public string[] Args { get; set; } = Array.Empty<string>();
    public Dictionary<string, object>? InitializationOptions { get; set; }
    public bool AutoInstall { get; set; }
    public string? InstallCommand { get; set; }
}

public static class LspServerConfigs
{
    public static readonly LspServerConfig CSharp = new()
    {
        Id = "csharp",
        Extensions = new[] { ".cs" },
        RootPatterns = new[] { "*.sln", "*.csproj", "global.json" },
        Command = "csharp-ls",
        Args = Array.Empty<string>(),
        AutoInstall = true,
        InstallCommand = "dotnet tool install --global csharp-ls"
    };

    public static readonly LspServerConfig TypeScript = new()
    {
        Id = "typescript",
        Extensions = new[] { ".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs", ".mts", ".cts" },
        RootPatterns = new[] { "package.json", "tsconfig.json", "jsconfig.json" },
        Command = "typescript-language-server",
        Args = new[] { "--stdio" },
        AutoInstall = true,
        InstallCommand = "npm install -g typescript-language-server typescript"
    };

    public static readonly LspServerConfig Python = new()
    {
        Id = "python",
        Extensions = new[] { ".py", ".pyi" },
        RootPatterns = new[] { "pyproject.toml", "setup.py", "requirements.txt", "Pipfile" },
        Command = "pyright-langserver",
        Args = new[] { "--stdio" },
        AutoInstall = true,
        InstallCommand = "npm install -g pyright"
    };

    public static readonly LspServerConfig Go = new()
    {
        Id = "go",
        Extensions = new[] { ".go" },
        RootPatterns = new[] { "go.mod", "go.sum", "go.work" },
        Command = "gopls",
        Args = Array.Empty<string>(),
        AutoInstall = true,
        InstallCommand = "go install golang.org/x/tools/gopls@latest"
    };

    public static readonly LspServerConfig Rust = new()
    {
        Id = "rust",
        Extensions = new[] { ".rs" },
        RootPatterns = new[] { "Cargo.toml", "Cargo.lock" },
        Command = "rust-analyzer",
        Args = Array.Empty<string>(),
        AutoInstall = false
    };

    public static readonly LspServerConfig Lua = new()
    {
        Id = "lua",
        Extensions = new[] { ".lua" },
        RootPatterns = new[] { ".luarc.json", ".luarc.jsonc", "stylua.toml" },
        Command = "lua-language-server",
        Args = Array.Empty<string>(),
        AutoInstall = false
    };

    public static readonly LspServerConfig Ruby = new()
    {
        Id = "ruby",
        Extensions = new[] { ".rb", ".rake", ".gemspec", ".ru" },
        RootPatterns = new[] { "Gemfile", ".ruby-version" },
        Command = "rubocop",
        Args = new[] { "--lsp" },
        AutoInstall = false
    };

    public static readonly LspServerConfig Clangd = new()
    {
        Id = "clangd",
        Extensions = new[] { ".c", ".cpp", ".cc", ".cxx", ".h", ".hpp", ".hh", ".hxx" },
        RootPatterns = new[] { "compile_commands.json", "compile_flags.txt", "CMakeLists.txt", "Makefile" },
        Command = "clangd",
        Args = new[] { "--background-index", "--clang-tidy" },
        AutoInstall = false
    };

    public static readonly LspServerConfig Java = new()
    {
        Id = "java",
        Extensions = new[] { ".java" },
        RootPatterns = new[] { "pom.xml", "build.gradle", "build.gradle.kts", ".project" },
        Command = "jdtls",
        Args = Array.Empty<string>(),
        AutoInstall = false
    };

    public static readonly LspServerConfig Yaml = new()
    {
        Id = "yaml",
        Extensions = new[] { ".yaml", ".yml" },
        RootPatterns = new[] { "*.yaml", "*.yml" },
        Command = "yaml-language-server",
        Args = new[] { "--stdio" },
        AutoInstall = true,
        InstallCommand = "npm install -g yaml-language-server"
    };

    public static readonly LspServerConfig Terraform = new()
    {
        Id = "terraform",
        Extensions = new[] { ".tf", ".tfvars" },
        RootPatterns = new[] { ".terraform.lock.hcl", "*.tf" },
        Command = "terraform-ls",
        Args = new[] { "serve" },
        AutoInstall = false
    };

    public static readonly LspServerConfig Bash = new()
    {
        Id = "bash",
        Extensions = new[] { ".sh", ".bash", ".zsh", ".ksh" },
        RootPatterns = Array.Empty<string>(),
        Command = "bash-language-server",
        Args = new[] { "start" },
        AutoInstall = true,
        InstallCommand = "npm install -g bash-language-server"
    };

    public static readonly LspServerConfig Dockerfile = new()
    {
        Id = "dockerfile",
        Extensions = new[] { ".dockerfile", "Dockerfile" },
        RootPatterns = new[] { "Dockerfile", "docker-compose.yml" },
        Command = "docker-langserver",
        Args = new[] { "--stdio" },
        AutoInstall = true,
        InstallCommand = "npm install -g dockerfile-language-server-nodejs"
    };

    public static IEnumerable<LspServerConfig> All => new[]
    {
        CSharp,
        TypeScript,
        Python,
        Go,
        Rust,
        Lua,
        Ruby,
        Clangd,
        Java,
        Yaml,
        Terraform,
        Bash,
        Dockerfile
    };

    public static LspServerConfig? GetForExtension(string extension)
    {
        return All.FirstOrDefault(c => c.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase));
    }

    public static LspServerConfig? GetForFile(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(ext))
        {
            var fileName = Path.GetFileName(filePath);
            return All.FirstOrDefault(c => c.Extensions.Contains(fileName, StringComparer.OrdinalIgnoreCase));
        }
        return GetForExtension(ext);
    }
}
