using System.Text.RegularExpressions;
using Terminal.Gui;
using Attribute = Terminal.Gui.Attribute;

namespace OpenFork.Cli.Tui;

/// <summary>
/// A custom Terminal.Gui view that renders markdown with colors similar to Glow/glamour.
/// Supports headers, code blocks, inline code, bold, italic, links, lists, and blockquotes.
/// </summary>
public class MarkdownView : View
{
    private string _text = "";
    private List<RenderedLine> _renderedLines = new();
    private int _topRow;
    private int _wrapWidth = 80;

    // Text selection support
    private bool _isSelecting;
    private int _selectionStartLine;
    private int _selectionStartCol;
    private int _selectionEndLine;
    private int _selectionEndCol;
    private bool _hasSelection;

    /// <summary>
    /// Color scheme for markdown elements (Glow-inspired dark theme)
    /// </summary>
    public static class MarkdownColors
    {
        // Base colors
        public static readonly Color Background = Color.Black;
        public static readonly Color Text = Color.White;

        // Headers - bright and prominent
        public static readonly Color H1 = Color.BrightMagenta;
        public static readonly Color H2 = Color.BrightCyan;
        public static readonly Color H3 = Color.BrightBlue;
        public static readonly Color H4 = Color.Blue;

        // Code - distinct background feel
        public static readonly Color CodeBlock = Color.BrightGreen;
        public static readonly Color CodeBlockBorder = Color.DarkGray;
        public static readonly Color InlineCode = Color.BrightYellow;

        // Emphasis
        public static readonly Color Bold = Color.White;
        public static readonly Color Italic = Color.Cyan;
        public static readonly Color BoldItalic = Color.BrightCyan;

        // Links and references
        public static readonly Color Link = Color.BrightBlue;
        public static readonly Color LinkUrl = Color.DarkGray;

        // Lists and quotes
        public static readonly Color ListBullet = Color.BrightMagenta;
        public static readonly Color BlockQuote = Color.Gray;
        public static readonly Color BlockQuoteBorder = Color.Magenta;

        // Horizontal rules
        public static readonly Color HorizontalRule = Color.DarkGray;

        // Selection
        public static readonly Color SelectionBackground = Color.Blue;
        public static readonly Color SelectionForeground = Color.White;

        // Create attributes
        public static Attribute GetAttribute(Color fg) => new(fg, Background);
        public static Attribute GetSelectionAttribute(Color fg) => new(SelectionForeground, SelectionBackground);
    }

    public new string Text
    {
        get => _text;
        set
        {
            _text = value ?? "";
            ParseAndRender();
            SetNeedsDisplay();
        }
    }

    public int TopRow
    {
        get => _topRow;
        set
        {
            var maxRow = Math.Max(0, _renderedLines.Count - Frame.Height);
            _topRow = Math.Max(0, Math.Min(value, maxRow));
            SetNeedsDisplay();
        }
    }

    public int TotalLines => _renderedLines.Count;

    public MarkdownView()
    {
        CanFocus = true;
        WantMousePositionReports = true;
        WantContinuousButtonPressed = true;  // Enable continuous mouse events during drag

        // Re-parse when layout changes (to get correct width)
        LayoutComplete += (e) =>
        {
            var newWidth = Frame.Width;
            if (newWidth > 0 && newWidth != _wrapWidth)
            {
                _wrapWidth = newWidth;
                ParseAndRender();
            }
        };
    }

    public override void Redraw(Rect bounds)
    {
        Clear();

        var driver = Application.Driver;
        var defaultAttr = MarkdownColors.GetAttribute(MarkdownColors.Text);
        var selectionAttr = new Attribute(MarkdownColors.SelectionForeground, MarkdownColors.SelectionBackground);

        for (int row = 0; row < bounds.Height; row++)
        {
            var lineIndex = _topRow + row;
            if (lineIndex >= _renderedLines.Count)
                break;

            var line = _renderedLines[lineIndex];
            Move(0, row);

            int col = 0;
            foreach (var span in line.Spans)
            {
                foreach (var ch in span.Text)
                {
                    if (col >= bounds.Width)
                        break;

                    // Check if this position is selected
                    if (IsPositionSelected(lineIndex, col))
                    {
                        driver.SetAttribute(selectionAttr);
                    }
                    else
                    {
                        driver.SetAttribute(span.Attribute);
                    }
                    driver.AddRune(ch);
                    col++;
                }
            }

            // Fill rest of line with spaces
            driver.SetAttribute(defaultAttr);
            while (col < bounds.Width)
            {
                if (IsPositionSelected(lineIndex, col))
                {
                    driver.SetAttribute(selectionAttr);
                }
                else
                {
                    driver.SetAttribute(defaultAttr);
                }
                driver.AddRune(' ');
                col++;
            }
        }
    }

    public override bool MouseEvent(MouseEvent me)
    {
        if (me.Flags.HasFlag(MouseFlags.WheeledUp))
        {
            TopRow = Math.Max(0, TopRow - 3);
            return true;
        }
        if (me.Flags.HasFlag(MouseFlags.WheeledDown))
        {
            TopRow = Math.Min(Math.Max(0, _renderedLines.Count - Frame.Height), TopRow + 3);
            return true;
        }

        // Double-click to select word (check before Button1Pressed as it includes that flag)
        if (me.Flags.HasFlag(MouseFlags.Button1DoubleClicked))
        {
            SelectWordAt(_topRow + me.Y, me.X);
            return true;
        }

        // Triple-click to select line
        if (me.Flags.HasFlag(MouseFlags.Button1TripleClicked))
        {
            SelectLineAt(_topRow + me.Y);
            return true;
        }

        // Handle text selection - Button1Pressed fires continuously with WantContinuousButtonPressed
        if (me.Flags.HasFlag(MouseFlags.Button1Pressed))
        {
            if (!_isSelecting)
            {
                // Start selection on first press
                _isSelecting = true;
                _selectionStartLine = _topRow + me.Y;
                _selectionStartCol = me.X;
                _selectionEndLine = _selectionStartLine;
                _selectionEndCol = _selectionStartCol;
                _hasSelection = false;
            }
            else
            {
                // Continue selection during drag (continuous press events)
                _selectionEndLine = _topRow + me.Y;
                _selectionEndCol = me.X;
                _hasSelection = _selectionStartLine != _selectionEndLine || _selectionStartCol != _selectionEndCol;
            }
            SetNeedsDisplay();
            return true;
        }

        // Handle release
        if (me.Flags.HasFlag(MouseFlags.Button1Released))
        {
            if (_isSelecting)
            {
                _selectionEndLine = _topRow + me.Y;
                _selectionEndCol = me.X;
                _hasSelection = _selectionStartLine != _selectionEndLine || _selectionStartCol != _selectionEndCol;
                _isSelecting = false;
                SetNeedsDisplay();
            }
            return true;
        }

        return base.MouseEvent(me);
    }

    private void SelectWordAt(int line, int col)
    {
        if (line < 0 || line >= _renderedLines.Count) return;

        var lineText = string.Concat(_renderedLines[line].Spans.Select(s => s.Text));
        if (col >= lineText.Length) return;

        // Find word boundaries
        int start = col;
        int end = col;

        // Expand backwards
        while (start > 0 && !char.IsWhiteSpace(lineText[start - 1]))
            start--;

        // Expand forwards
        while (end < lineText.Length && !char.IsWhiteSpace(lineText[end]))
            end++;

        _selectionStartLine = line;
        _selectionStartCol = start;
        _selectionEndLine = line;
        _selectionEndCol = end;
        _hasSelection = start != end;
        SetNeedsDisplay();
    }

    private void SelectLineAt(int line)
    {
        if (line < 0 || line >= _renderedLines.Count) return;

        _selectionStartLine = line;
        _selectionStartCol = 0;
        _selectionEndLine = line;
        _selectionEndCol = GetLineLength(line);
        _hasSelection = true;
        SetNeedsDisplay();
    }

    public override bool ProcessKey(KeyEvent keyEvent)
    {
        // Ctrl+C to copy selection
        if (keyEvent.Key == (Key.CtrlMask | Key.c) || keyEvent.Key == (Key.CtrlMask | Key.C))
        {
            CopySelectionToClipboard();
            return true;
        }

        // Escape to clear selection
        if (keyEvent.Key == Key.Esc && _hasSelection)
        {
            ClearSelection();
            return true;
        }

        // Ctrl+A to select all
        if (keyEvent.Key == (Key.CtrlMask | Key.a) || keyEvent.Key == (Key.CtrlMask | Key.A))
        {
            SelectAll();
            return true;
        }

        switch (keyEvent.Key)
        {
            case Key.PageUp:
                TopRow -= Frame.Height - 1;
                return true;
            case Key.PageDown:
                TopRow += Frame.Height - 1;
                return true;
            case Key.Home:
                TopRow = 0;
                return true;
            case Key.End:
                TopRow = Math.Max(0, _renderedLines.Count - Frame.Height);
                return true;
            case Key.CursorUp:
                TopRow--;
                return true;
            case Key.CursorDown:
                TopRow++;
                return true;
        }
        return base.ProcessKey(keyEvent);
    }

    private void ClearSelection()
    {
        _hasSelection = false;
        _isSelecting = false;
        SetNeedsDisplay();
    }

    private void SelectAll()
    {
        if (_renderedLines.Count == 0) return;

        _selectionStartLine = 0;
        _selectionStartCol = 0;
        _selectionEndLine = _renderedLines.Count - 1;
        _selectionEndCol = GetLineLength(_selectionEndLine);
        _hasSelection = true;
        SetNeedsDisplay();
    }

    private int GetLineLength(int lineIndex)
    {
        if (lineIndex < 0 || lineIndex >= _renderedLines.Count) return 0;
        return _renderedLines[lineIndex].Spans.Sum(s => s.Text.Length);
    }

    private void CopySelectionToClipboard()
    {
        if (!_hasSelection) return;

        var text = GetSelectedText();
        if (!string.IsNullOrEmpty(text))
        {
            try
            {
                Clipboard.TrySetClipboardData(text);
            }
            catch
            {
                // Clipboard might not be available in all terminals
            }
        }
    }

    private string GetSelectedText()
    {
        if (!_hasSelection || _renderedLines.Count == 0) return "";

        // Normalize selection (start before end)
        var (startLine, startCol, endLine, endCol) = NormalizeSelection();

        var sb = new System.Text.StringBuilder();

        for (int line = startLine; line <= endLine && line < _renderedLines.Count; line++)
        {
            var lineText = string.Concat(_renderedLines[line].Spans.Select(s => s.Text));

            int lineStartCol = (line == startLine) ? Math.Min(startCol, lineText.Length) : 0;
            int lineEndCol = (line == endLine) ? Math.Min(endCol, lineText.Length) : lineText.Length;

            if (lineStartCol < lineText.Length)
            {
                var selectedPart = lineText.Substring(lineStartCol, Math.Max(0, lineEndCol - lineStartCol));
                sb.Append(selectedPart);
            }

            if (line < endLine)
                sb.AppendLine();
        }

        return sb.ToString();
    }

    private (int startLine, int startCol, int endLine, int endCol) NormalizeSelection()
    {
        // Ensure start is before end
        if (_selectionStartLine < _selectionEndLine ||
            (_selectionStartLine == _selectionEndLine && _selectionStartCol <= _selectionEndCol))
        {
            return (_selectionStartLine, _selectionStartCol, _selectionEndLine, _selectionEndCol);
        }
        return (_selectionEndLine, _selectionEndCol, _selectionStartLine, _selectionStartCol);
    }

    private bool IsPositionSelected(int line, int col)
    {
        if (!_hasSelection) return false;

        var (startLine, startCol, endLine, endCol) = NormalizeSelection();

        if (line < startLine || line > endLine) return false;
        if (line == startLine && line == endLine) return col >= startCol && col < endCol;
        if (line == startLine) return col >= startCol;
        if (line == endLine) return col < endCol;
        return true;
    }

    public void ScrollToEnd()
    {
        TopRow = Math.Max(0, _renderedLines.Count - Frame.Height + 1);
    }

    private void ParseAndRender()
    {
        _renderedLines.Clear();
        if (string.IsNullOrEmpty(_text))
            return;

        _wrapWidth = Math.Max(20, Frame.Width > 0 ? Frame.Width : 80);

        var lines = _text.Split('\n');
        var inCodeBlock = false;
        var codeBlockLang = "";

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // Handle code blocks
            if (line.TrimStart().StartsWith("```"))
            {
                if (!inCodeBlock)
                {
                    inCodeBlock = true;
                    codeBlockLang = line.TrimStart()[3..].Trim();
                    AddCodeBlockStart(codeBlockLang);
                }
                else
                {
                    inCodeBlock = false;
                    codeBlockLang = "";
                    AddCodeBlockEnd();
                }
                continue;
            }

            if (inCodeBlock)
            {
                AddCodeLine(line);
                continue;
            }

            // Process markdown line
            RenderLine(line);
        }

        // Handle unclosed code block
        if (inCodeBlock)
        {
            AddCodeBlockEnd();
        }
    }

    private void AddCodeBlockStart(string lang)
    {
        var borderAttr = MarkdownColors.GetAttribute(MarkdownColors.CodeBlockBorder);
        var langAttr = MarkdownColors.GetAttribute(MarkdownColors.H3);

        var label = string.IsNullOrEmpty(lang) ? "code" : lang;
        var border = $"┌─ {label} " + new string('─', Math.Max(0, _wrapWidth - label.Length - 5));

        _renderedLines.Add(new RenderedLine(new TextSpan(border, borderAttr)));
    }

    private void AddCodeBlockEnd()
    {
        var borderAttr = MarkdownColors.GetAttribute(MarkdownColors.CodeBlockBorder);
        var border = "└" + new string('─', _wrapWidth - 1);
        _renderedLines.Add(new RenderedLine(new TextSpan(border, borderAttr)));
    }

    private void AddCodeLine(string line)
    {
        var borderAttr = MarkdownColors.GetAttribute(MarkdownColors.CodeBlockBorder);
        var codeAttr = MarkdownColors.GetAttribute(MarkdownColors.CodeBlock);

        var spans = new List<TextSpan>
        {
            new("│ ", borderAttr),
            new(line, codeAttr)
        };

        _renderedLines.Add(new RenderedLine(spans));
    }

    private void RenderLine(string line)
    {
        var trimmed = line.TrimStart();
        var indent = line.Length - trimmed.Length;
        var indentStr = new string(' ', indent);

        // Empty line
        if (string.IsNullOrWhiteSpace(line))
        {
            _renderedLines.Add(new RenderedLine(new TextSpan("", MarkdownColors.GetAttribute(MarkdownColors.Text))));
            return;
        }

        // Headers
        if (trimmed.StartsWith("#### "))
        {
            AddHeader(trimmed[5..], 4, indentStr);
            return;
        }
        if (trimmed.StartsWith("### "))
        {
            AddHeader(trimmed[4..], 3, indentStr);
            return;
        }
        if (trimmed.StartsWith("## "))
        {
            AddHeader(trimmed[3..], 2, indentStr);
            return;
        }
        if (trimmed.StartsWith("# "))
        {
            AddHeader(trimmed[2..], 1, indentStr);
            return;
        }

        // Horizontal rules
        if (trimmed == "---" || trimmed == "***" || trimmed == "___" ||
            Regex.IsMatch(trimmed, @"^[-*_]{3,}$"))
        {
            var hrAttr = MarkdownColors.GetAttribute(MarkdownColors.HorizontalRule);
            _renderedLines.Add(new RenderedLine(new TextSpan(new string('─', _wrapWidth), hrAttr)));
            return;
        }

        // Blockquotes
        if (trimmed.StartsWith("> "))
        {
            AddBlockQuote(trimmed[2..], indentStr);
            return;
        }
        if (trimmed == ">")
        {
            AddBlockQuote("", indentStr);
            return;
        }

        // Unordered lists
        var listMatch = Regex.Match(trimmed, @"^[-*+]\s+(.*)$");
        if (listMatch.Success)
        {
            AddListItem(listMatch.Groups[1].Value, false, "", indentStr);
            return;
        }

        // Ordered lists
        var orderedMatch = Regex.Match(trimmed, @"^(\d+)\.\s+(.*)$");
        if (orderedMatch.Success)
        {
            AddListItem(orderedMatch.Groups[2].Value, true, orderedMatch.Groups[1].Value, indentStr);
            return;
        }

        // Regular paragraph with inline formatting
        AddParagraph(line);
    }

    private void AddHeader(string text, int level, string indent)
    {
        var color = level switch
        {
            1 => MarkdownColors.H1,
            2 => MarkdownColors.H2,
            3 => MarkdownColors.H3,
            _ => MarkdownColors.H4
        };

        var attr = MarkdownColors.GetAttribute(color);
        var prefix = level switch
        {
            1 => "# ",
            2 => "## ",
            3 => "### ",
            _ => "#### "
        };

        var spans = new List<TextSpan>();
        if (!string.IsNullOrEmpty(indent))
            spans.Add(new TextSpan(indent, MarkdownColors.GetAttribute(MarkdownColors.Text)));

        spans.Add(new TextSpan(prefix, attr));
        spans.AddRange(ParseInlineFormatting(text, color));

        _renderedLines.Add(new RenderedLine(spans));

        // Add underline for H1 and H2
        if (level == 1)
        {
            var underline = new string('═', Math.Min(text.Length + prefix.Length + indent.Length, _wrapWidth));
            _renderedLines.Add(new RenderedLine(new TextSpan(indent + underline, attr)));
        }
        else if (level == 2)
        {
            var underline = new string('─', Math.Min(text.Length + prefix.Length + indent.Length, _wrapWidth));
            _renderedLines.Add(new RenderedLine(new TextSpan(indent + underline, attr)));
        }
    }

    private void AddBlockQuote(string text, string indent)
    {
        var borderAttr = MarkdownColors.GetAttribute(MarkdownColors.BlockQuoteBorder);
        var textAttr = MarkdownColors.GetAttribute(MarkdownColors.BlockQuote);

        var spans = new List<TextSpan>();
        if (!string.IsNullOrEmpty(indent))
            spans.Add(new TextSpan(indent, MarkdownColors.GetAttribute(MarkdownColors.Text)));

        spans.Add(new TextSpan("┃ ", borderAttr));
        spans.AddRange(ParseInlineFormatting(text, MarkdownColors.BlockQuote));

        _renderedLines.Add(new RenderedLine(spans));
    }

    private void AddListItem(string text, bool ordered, string number, string indent)
    {
        var bulletAttr = MarkdownColors.GetAttribute(MarkdownColors.ListBullet);

        var spans = new List<TextSpan>();
        if (!string.IsNullOrEmpty(indent))
            spans.Add(new TextSpan(indent, MarkdownColors.GetAttribute(MarkdownColors.Text)));

        if (ordered)
        {
            spans.Add(new TextSpan($"{number}. ", bulletAttr));
        }
        else
        {
            spans.Add(new TextSpan("• ", bulletAttr));
        }

        spans.AddRange(ParseInlineFormatting(text, MarkdownColors.Text));

        _renderedLines.Add(new RenderedLine(spans));
    }

    private void AddParagraph(string text)
    {
        // Word wrap
        var wrappedLines = WrapText(text, _wrapWidth);
        foreach (var wrappedLine in wrappedLines)
        {
            var spans = ParseInlineFormatting(wrappedLine, MarkdownColors.Text);
            _renderedLines.Add(new RenderedLine(spans));
        }
    }

    private List<TextSpan> ParseInlineFormatting(string text, Color defaultColor)
    {
        var spans = new List<TextSpan>();
        if (string.IsNullOrEmpty(text))
            return spans;

        var defaultAttr = MarkdownColors.GetAttribute(defaultColor);

        // Pattern for inline elements
        // Order matters: longer patterns first
        var pattern = @"
            (?<code>`[^`]+`)                           |  # Inline code
            (?<bolditalic>\*\*\*[^*]+\*\*\*)           |  # Bold italic
            (?<bold>\*\*[^*]+\*\*)                     |  # Bold
            (?<italic>(?<!\*)\*[^*]+\*(?!\*))          |  # Italic (single *)
            (?<italic2>(?<!_)_[^_]+_(?!_))             |  # Italic (single _)
            (?<strike>~~[^~]+~~)                       |  # Strikethrough
            (?<link>\[[^\]]+\]\([^)]+\))               |  # Links
            (?<text>[^`*_~\[]+)                           # Plain text
        ";

        var regex = new Regex(pattern, RegexOptions.IgnorePatternWhitespace);
        var matches = regex.Matches(text);
        int lastIndex = 0;

        foreach (Match match in matches)
        {
            // Add any text before this match
            if (match.Index > lastIndex)
            {
                spans.Add(new TextSpan(text[lastIndex..match.Index], defaultAttr));
            }

            if (match.Groups["code"].Success)
            {
                var code = match.Value[1..^1]; // Remove backticks
                spans.Add(new TextSpan($" {code} ", MarkdownColors.GetAttribute(MarkdownColors.InlineCode)));
            }
            else if (match.Groups["bolditalic"].Success)
            {
                var content = match.Value[3..^3];
                spans.Add(new TextSpan(content, MarkdownColors.GetAttribute(MarkdownColors.BoldItalic)));
            }
            else if (match.Groups["bold"].Success)
            {
                var content = match.Value[2..^2];
                spans.Add(new TextSpan(content, MarkdownColors.GetAttribute(MarkdownColors.Bold)));
            }
            else if (match.Groups["italic"].Success || match.Groups["italic2"].Success)
            {
                var content = match.Value[1..^1];
                spans.Add(new TextSpan(content, MarkdownColors.GetAttribute(MarkdownColors.Italic)));
            }
            else if (match.Groups["strike"].Success)
            {
                var content = match.Value[2..^2];
                spans.Add(new TextSpan($"~{content}~", MarkdownColors.GetAttribute(MarkdownColors.HorizontalRule)));
            }
            else if (match.Groups["link"].Success)
            {
                var linkMatch = Regex.Match(match.Value, @"\[([^\]]+)\]\(([^)]+)\)");
                if (linkMatch.Success)
                {
                    var linkText = linkMatch.Groups[1].Value;
                    var linkUrl = linkMatch.Groups[2].Value;
                    spans.Add(new TextSpan(linkText, MarkdownColors.GetAttribute(MarkdownColors.Link)));
                    spans.Add(new TextSpan($" ({linkUrl})", MarkdownColors.GetAttribute(MarkdownColors.LinkUrl)));
                }
            }
            else if (match.Groups["text"].Success)
            {
                spans.Add(new TextSpan(match.Value, defaultAttr));
            }

            lastIndex = match.Index + match.Length;
        }

        // Add remaining text
        if (lastIndex < text.Length)
        {
            spans.Add(new TextSpan(text[lastIndex..], defaultAttr));
        }

        return spans;
    }

    private static IEnumerable<string> WrapText(string text, int maxWidth)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxWidth)
        {
            yield return text;
            yield break;
        }

        var words = text.Split(' ');
        var currentLine = new System.Text.StringBuilder();

        foreach (var word in words)
        {
            if (word.Length > maxWidth)
            {
                if (currentLine.Length > 0)
                {
                    yield return currentLine.ToString().TrimEnd();
                    currentLine.Clear();
                }

                var remaining = word;
                while (remaining.Length > maxWidth)
                {
                    yield return remaining[..maxWidth];
                    remaining = remaining[maxWidth..];
                }
                if (remaining.Length > 0)
                    currentLine.Append(remaining);
            }
            else if (currentLine.Length + word.Length + 1 > maxWidth)
            {
                if (currentLine.Length > 0)
                {
                    yield return currentLine.ToString().TrimEnd();
                    currentLine.Clear();
                }
                currentLine.Append(word);
            }
            else
            {
                if (currentLine.Length > 0)
                    currentLine.Append(' ');
                currentLine.Append(word);
            }
        }

        if (currentLine.Length > 0)
            yield return currentLine.ToString().TrimEnd();
    }

    /// <summary>
    /// Represents a span of text with a specific color attribute.
    /// </summary>
    private class TextSpan
    {
        public string Text { get; }
        public Attribute Attribute { get; }

        public TextSpan(string text, Attribute attribute)
        {
            Text = text;
            Attribute = attribute;
        }
    }

    /// <summary>
    /// Represents a rendered line with multiple colored spans.
    /// </summary>
    private class RenderedLine
    {
        public List<TextSpan> Spans { get; }

        public RenderedLine(TextSpan span)
        {
            Spans = new List<TextSpan> { span };
        }

        public RenderedLine(List<TextSpan> spans)
        {
            Spans = spans;
        }
    }
}
