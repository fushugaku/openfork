using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace OpenFork.Core.Tools;

public class WebFetchTool : ITool
{
    private const int MaxResponseSize = 5 * 1024 * 1024;
    private const int DefaultTimeoutSeconds = 30;
    private const int MaxTimeoutSeconds = 120;

    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders =
        {
            { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36" },
            { "Accept-Language", "en-US,en;q=0.9" }
        }
    };

    public string Name => "webfetch";

    public string Description => PromptLoader.Load("webfetch",
        "Fetches content from a specified URL and returns it in the requested format.");

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            url = new
            {
                type = "string",
                description = "The URL to fetch content from"
            },
            format = new
            {
                type = "string",
                @enum = new[] { "text", "markdown", "html" },
                description = "The format to return the content in (text, markdown, or html). Defaults to markdown."
            },
            timeout = new
            {
                type = "integer",
                description = "Optional timeout in seconds (max 120)"
            }
        },
        required = new[] { "url" }
    };

    public async Task<ToolResult> ExecuteAsync(string arguments, ToolContext context)
    {
        try
        {
            var args = JsonSerializer.Deserialize<WebFetchArgs>(arguments, JsonHelper.Options);
            if (string.IsNullOrWhiteSpace(args?.Url))
                return new ToolResult(false, "Missing required parameter: url");

            var url = args.Url;
            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                return new ToolResult(false, "URL must start with http:// or https://");

            var format = args.Format ?? "markdown";
            var timeout = Math.Min(args.Timeout ?? DefaultTimeoutSeconds, MaxTimeoutSeconds);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));

            var acceptHeader = format switch
            {
                "markdown" => "text/markdown;q=1.0, text/x-markdown;q=0.9, text/plain;q=0.8, text/html;q=0.7, */*;q=0.1",
                "text" => "text/plain;q=1.0, text/markdown;q=0.9, text/html;q=0.8, */*;q=0.1",
                "html" => "text/html;q=1.0, application/xhtml+xml;q=0.9, text/plain;q=0.8, */*;q=0.1",
                _ => "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"
            };

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Accept", acceptHeader);

            using var response = await HttpClient.SendAsync(request, cts.Token);

            if (!response.IsSuccessStatusCode)
                return new ToolResult(false, $"Request failed with status code: {(int)response.StatusCode}");

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength > MaxResponseSize)
                return new ToolResult(false, "Response too large (exceeds 5MB limit)");

            var content = await response.Content.ReadAsStringAsync(cts.Token);
            if (content.Length > MaxResponseSize)
                return new ToolResult(false, "Response too large (exceeds 5MB limit)");

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";

            var result = format switch
            {
                "markdown" when contentType.Contains("html") => ConvertHtmlToMarkdown(content),
                "text" when contentType.Contains("html") => ExtractTextFromHtml(content),
                _ => content
            };

            return new ToolResult(true, result);
        }
        catch (TaskCanceledException)
        {
            return new ToolResult(false, "Request timed out");
        }
        catch (HttpRequestException ex)
        {
            return new ToolResult(false, $"HTTP request failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new ToolResult(false, $"Error fetching URL: {ex.Message}");
        }
    }

    private static string ConvertHtmlToMarkdown(string html)
    {
        var result = html;

        result = Regex.Replace(result, @"<script[^>]*>[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"<style[^>]*>[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"<noscript[^>]*>[\s\S]*?</noscript>", "", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"<!--[\s\S]*?-->", "");

        result = Regex.Replace(result, @"<h1[^>]*>(.*?)</h1>", "# $1\n\n", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        result = Regex.Replace(result, @"<h2[^>]*>(.*?)</h2>", "## $1\n\n", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        result = Regex.Replace(result, @"<h3[^>]*>(.*?)</h3>", "### $1\n\n", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        result = Regex.Replace(result, @"<h4[^>]*>(.*?)</h4>", "#### $1\n\n", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        result = Regex.Replace(result, @"<h5[^>]*>(.*?)</h5>", "##### $1\n\n", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        result = Regex.Replace(result, @"<h6[^>]*>(.*?)</h6>", "###### $1\n\n", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        result = Regex.Replace(result, @"<p[^>]*>(.*?)</p>", "$1\n\n", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        result = Regex.Replace(result, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);

        result = Regex.Replace(result, @"<strong[^>]*>(.*?)</strong>", "**$1**", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        result = Regex.Replace(result, @"<b[^>]*>(.*?)</b>", "**$1**", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        result = Regex.Replace(result, @"<em[^>]*>(.*?)</em>", "*$1*", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        result = Regex.Replace(result, @"<i[^>]*>(.*?)</i>", "*$1*", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        result = Regex.Replace(result, @"<a[^>]*href=[""']([^""']+)[""'][^>]*>(.*?)</a>", "[$2]($1)", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        result = Regex.Replace(result, @"<code[^>]*>(.*?)</code>", "`$1`", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        result = Regex.Replace(result, @"<pre[^>]*>(.*?)</pre>", "\n```\n$1\n```\n", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        result = Regex.Replace(result, @"<li[^>]*>(.*?)</li>", "- $1\n", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        result = Regex.Replace(result, @"<[ou]l[^>]*>", "\n", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"</[ou]l>", "\n", RegexOptions.IgnoreCase);

        result = Regex.Replace(result, @"<hr\s*/?>", "\n---\n", RegexOptions.IgnoreCase);

        result = Regex.Replace(result, @"<[^>]+>", "");

        result = System.Net.WebUtility.HtmlDecode(result);

        result = Regex.Replace(result, @"\n{3,}", "\n\n");
        result = result.Trim();

        return result;
    }

    private static string ExtractTextFromHtml(string html)
    {
        var result = html;

        result = Regex.Replace(result, @"<script[^>]*>[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"<style[^>]*>[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"<noscript[^>]*>[\s\S]*?</noscript>", "", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"<!--[\s\S]*?-->", "");

        result = Regex.Replace(result, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"</p>", "\n\n", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"</div>", "\n", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"</li>", "\n", RegexOptions.IgnoreCase);

        result = Regex.Replace(result, @"<[^>]+>", "");

        result = System.Net.WebUtility.HtmlDecode(result);

        result = Regex.Replace(result, @"[ \t]+", " ");
        result = Regex.Replace(result, @"\n{3,}", "\n\n");
        result = result.Trim();

        return result;
    }

    private record WebFetchArgs(
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("format")] string? Format,
        [property: JsonPropertyName("timeout")] int? Timeout
    );
}
