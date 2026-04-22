using System;
using System.Collections.Generic;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace AudioClient.GUI.Helpers;

// Must inherit AvaloniaObject so RegisterAttached<TOwner,...> constraint is satisfied.
public class ResoniteRichText : AvaloniaObject
{
    public static readonly AttachedProperty<string?> TextProperty =
        AvaloniaProperty.RegisterAttached<ResoniteRichText, TextBlock, string?>("Text");

    static ResoniteRichText()
    {
        TextProperty.Changed.AddClassHandler<TextBlock>(OnTextChanged);
    }

    public static string? GetText(TextBlock element) => element.GetValue(TextProperty);
    public static void SetText(TextBlock element, string? value) => element.SetValue(TextProperty, value);

    private static void OnTextChanged(TextBlock tb, AvaloniaPropertyChangedEventArgs e)
    {
        tb.Inlines ??= new InlineCollection();
        tb.Inlines.Clear();
        var text = (string?)e.NewValue;
        if (!string.IsNullOrEmpty(text))
            foreach (var inline in ParseInlines(text))
                tb.Inlines.Add(inline);
    }

    /// <summary>Returns the plain text content with all markup tags removed.</summary>
    public static string StripTags(string text)
    {
        if (!text.Contains('<')) return text;
        var sb = new StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] == '<')
            {
                int j = i + 1;
                while (j < text.Length && text[j] != '>') j++;
                if (j < text.Length) i = j + 1;
                else sb.Append(text[i++]);
            }
            else sb.Append(text[i++]);
        }
        return sb.ToString();
    }

    // ----- Parser -----

    private enum TextTransform { None, Uppercase, Lowercase }

    private sealed class ParserState
    {
        public Stack<bool> Bold          { get; } = new();
        public Stack<bool> Italic        { get; } = new();
        public Stack<bool> Underline     { get; } = new();
        public Stack<bool> Strikethrough { get; } = new();
        public Stack<Color?> Foreground  { get; } = new();
        public Stack<byte?> Alpha        { get; } = new();
        public Stack<double> FontSize    { get; } = new();
        public Stack<Color?> Mark        { get; } = new();
        public Stack<TextTransform> Transform { get; } = new();

        public bool IsBold          => Bold.Count          > 0 && Bold.Peek();
        public bool IsItalic        => Italic.Count        > 0 && Italic.Peek();
        public bool IsUnderline     => Underline.Count     > 0 && Underline.Peek();
        public bool IsStrikethrough => Strikethrough.Count > 0 && Strikethrough.Peek();
        public Color?         CurrentForeground => Foreground.Count > 0 ? Foreground.Peek() : null;
        public byte?          CurrentAlpha      => Alpha.Count     > 0 ? Alpha.Peek()     : null;
        public double         CurrentFontSize   => FontSize.Count  > 0 ? FontSize.Peek()  : double.NaN;
        public Color?         CurrentMark       => Mark.Count      > 0 ? Mark.Peek()      : null;
        public TextTransform  CurrentTransform  => Transform.Count > 0 ? Transform.Peek() : TextTransform.None;
    }

    private static IEnumerable<Inline> ParseInlines(string text)
    {
        var state    = new ParserState();
        bool noparse = false;
        int  i       = 0;
        var  sb      = new StringBuilder();

        while (i < text.Length)
        {
            if (text[i] == '<')
            {
                // In noparse mode only </noparse> exits it; everything else is literal text.
                if (noparse && !MatchesAt(text, i, "</noparse>"))
                {
                    sb.Append(text[i++]);
                    continue;
                }

                if (sb.Length > 0)
                {
                    if (MakeRun(ApplyTransform(sb.ToString(), state.CurrentTransform), state) is { } run)
                        yield return run;
                    sb.Clear();
                }

                if (TryParseTag(text, ref i, state, ref noparse, out var emitInline))
                {
                    if (emitInline != null) yield return emitInline;
                }
                else
                {
                    sb.Append('<');
                    i++;
                }
            }
            else
            {
                sb.Append(text[i++]);
            }
        }

        if (sb.Length > 0 &&
            MakeRun(ApplyTransform(sb.ToString(), state.CurrentTransform), state) is { } lastRun)
            yield return lastRun;
    }

    private static bool MatchesAt(string text, int pos, string pattern)
        => string.Compare(text, pos, pattern, 0, pattern.Length,
                          StringComparison.OrdinalIgnoreCase) == 0;

    private static string ApplyTransform(string text, TextTransform transform) =>
        transform switch
        {
            TextTransform.Uppercase => text.ToUpperInvariant(),
            TextTransform.Lowercase => text.ToLowerInvariant(),
            _                       => text
        };

    private static Run? MakeRun(string content, ParserState state)
    {
        if (content.Length == 0) return null;
        var run = new Run(content);

        var color = state.CurrentForeground;
        var alpha = state.CurrentAlpha;
        if (color.HasValue || alpha.HasValue)
        {
            var c = color ?? Colors.White;
            if (alpha.HasValue) c = Color.FromArgb(alpha.Value, c.R, c.G, c.B);
            run.Foreground = new SolidColorBrush(c);
        }

        if (state.IsBold)          run.FontWeight = FontWeight.Bold;
        if (state.IsItalic)        run.FontStyle  = FontStyle.Italic;

        if (state.IsUnderline || state.IsStrikethrough)
        {
            var decors = new TextDecorationCollection();
            if (state.IsUnderline)     decors.Add(TextDecorations.Underline[0]);
            if (state.IsStrikethrough) decors.Add(TextDecorations.Strikethrough[0]);
            run.TextDecorations = decors;
        }

        var size = state.CurrentFontSize;
        if (!double.IsNaN(size)) run.FontSize = size;

        if (state.CurrentMark is { } mark) run.Background = new SolidColorBrush(mark);

        return run;
    }

    private static bool TryParseTag(
        string text, ref int i, ParserState state, ref bool noparse, out Inline? emitInline)
    {
        emitInline = null;
        int start = i++;   // skip '<'

        bool closing = i < text.Length && text[i] == '/';
        if (closing) i++;

        int nameStart = i;
        while (i < text.Length && text[i] != '=' && text[i] != '>' &&
               text[i] != ' ' && text[i] != '\t')
            i++;

        if (i >= text.Length || nameStart == i) { i = start + 1; return false; }

        string tagName = text[nameStart..i].Trim().ToLowerInvariant();

        string? value = null;
        if (i < text.Length && text[i] == '=')
        {
            i++;
            bool quoted = i < text.Length && text[i] == '"';
            if (quoted) i++;
            int valStart = i;
            char endChar = quoted ? '"' : '>';
            while (i < text.Length && text[i] != endChar && text[i] != '>')
                i++;
            value = text[valStart..i];
            if (quoted && i < text.Length && text[i] == '"') i++;
        }

        while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) i++;
        if (i >= text.Length || text[i] != '>') { i = start + 1; return false; }
        i++;   // skip '>'

        if (tagName == "noparse") { noparse = !closing; return true; }

        if (closing) PopTag(tagName, state);
        else         PushTag(tagName, value, state, out emitInline);

        return true;
    }

    private static void PushTag(string tag, string? value, ParserState state, out Inline? emit)
    {
        emit = null;
        switch (tag)
        {
            case "b":           state.Bold.Push(true);                                   break;
            case "i":           state.Italic.Push(true);                                 break;
            case "u":           state.Underline.Push(true);                              break;
            case "s":           state.Strikethrough.Push(true);                          break;
            case "color":       state.Foreground.Push(ParseColor(value));                break;
            case "alpha":       state.Alpha.Push(ParseAlpha(value));                     break;
            case "size":        state.FontSize.Push(ParseFontSize(value,
                                    state.CurrentFontSize));                             break;
            case "mark":        state.Mark.Push(ParseColor(value));                      break;
            case "uppercase":
            case "allcaps":     state.Transform.Push(TextTransform.Uppercase);           break;
            case "lowercase":   state.Transform.Push(TextTransform.Lowercase);           break;
            case "smallcaps":   state.Transform.Push(TextTransform.Uppercase);           break;
            case "br":          emit = new LineBreak();                                  break;
            case "closeallblock":
                state.Bold.Clear(); state.Italic.Clear(); state.Underline.Clear();
                state.Strikethrough.Clear(); state.Foreground.Clear(); state.Alpha.Clear();
                state.FontSize.Clear(); state.Mark.Clear(); state.Transform.Clear();
                break;
            // font, sprite/glyph, gradient, line-height, align, nobr, sub, sup → ignored
        }
    }

    private static void PopTag(string tag, ParserState state)
    {
        switch (tag)
        {
            case "b":           if (state.Bold.Count          > 0) state.Bold.Pop();          break;
            case "i":           if (state.Italic.Count        > 0) state.Italic.Pop();        break;
            case "u":           if (state.Underline.Count     > 0) state.Underline.Pop();     break;
            case "s":           if (state.Strikethrough.Count > 0) state.Strikethrough.Pop(); break;
            case "color":       if (state.Foreground.Count    > 0) state.Foreground.Pop();    break;
            case "alpha":       if (state.Alpha.Count         > 0) state.Alpha.Pop();         break;
            case "size":        if (state.FontSize.Count      > 0) state.FontSize.Pop();      break;
            case "mark":        if (state.Mark.Count          > 0) state.Mark.Pop();          break;
            case "uppercase":
            case "allcaps":
            case "lowercase":
            case "smallcaps":   if (state.Transform.Count     > 0) state.Transform.Pop();     break;
        }
    }

    private static Color? ParseColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string v = value.Trim();
        if (v.StartsWith('#'))
        {
            string hex = v[1..];
            try
            {
                return hex.Length switch
                {
                    6 => Color.FromRgb(
                            Convert.ToByte(hex[0..2], 16),
                            Convert.ToByte(hex[2..4], 16),
                            Convert.ToByte(hex[4..6], 16)),
                    8 => Color.FromArgb(
                            Convert.ToByte(hex[6..8], 16),
                            Convert.ToByte(hex[0..2], 16),
                            Convert.ToByte(hex[2..4], 16),
                            Convert.ToByte(hex[4..6], 16)),
                    _ => null
                };
            }
            catch { return null; }
        }
        return v.ToLowerInvariant() switch
        {
            "red"            => Colors.Red,
            "green"          => Color.FromRgb(0, 128, 0),
            "blue"           => Colors.Blue,
            "yellow"         => Colors.Yellow,
            "cyan"           => Colors.Cyan,
            "magenta"        => Colors.Magenta,
            "orange"         => Colors.Orange,
            "purple"         => Color.FromRgb(128, 0, 128),
            "lime"           => Colors.Lime,
            "pink"           => Colors.Pink,
            "brown"          => Colors.Brown,
            "white"          => Colors.White,
            "gray" or "grey" => Colors.Gray,
            "black"          => Colors.Black,
            "clear"          => Colors.Transparent,
            _                => null
        };
    }

    private static byte? ParseAlpha(string? value)
    {
        if (value is null) return null;
        string v = value.Trim();
        if (v.StartsWith('#') && v.Length == 3)
            try { return Convert.ToByte(v[1..], 16); } catch { }
        return null;
    }

    private static double ParseFontSize(string? value, double currentSize)
    {
        if (string.IsNullOrWhiteSpace(value)) return double.NaN;
        double baseSize = double.IsNaN(currentSize) ? 14.0 : currentSize;
        string v = value.Trim();
        if (v.EndsWith('%') &&
            double.TryParse(v[..^1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double pct))
        {
            return Math.Max(1.0, baseSize * pct / 100.0);
        }
        if (double.TryParse(v, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double abs))
        {
            return Math.Max(1.0, abs);
        }
        return double.NaN;
    }
}
