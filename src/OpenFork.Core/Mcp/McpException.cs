namespace OpenFork.Core.Mcp;

/// <summary>
/// Exception from MCP protocol error.
/// </summary>
public class McpException : Exception
{
    /// <summary>MCP error code.</summary>
    public int Code { get; }

    public McpException(string message, int code = -1)
        : base(message)
    {
        Code = code;
    }

    public McpException(string message, int code, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }
}
