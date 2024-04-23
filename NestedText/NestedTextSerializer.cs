using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NestedText;

public class NestedTextException(string message, string line, int lineIndex, int characterIndex = -1)
    : FormatException(characterIndex >= 0 ? $"Ln {lineIndex+1} Ch {characterIndex+1}: {message}" : $"Ln {lineIndex + 1}: {message}")
{
    public string Line { get; } = line;
    public int LineIndex { get; } = lineIndex;
    public int CharacterIndex { get; } = characterIndex;
}

public sealed record NestedTextSerializerOptions
{
    public static NestedTextSerializerOptions Default { get; } = new() { Minimal = true };
    public bool Minimal { get; init; }
}

public static class NestedTextSerializer
{
    public static async Task<JsonNode?> Deserialize(Stream stream, NestedTextSerializerOptions? options = null, bool leaveOpen = false)
    {
        if (options?.Minimal is false)
            throw new NotSupportedException($"The current implementation does not support {nameof(NestedTextSerializerOptions)}.{nameof(NestedTextSerializerOptions.Minimal)} = false");
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: leaveOpen);
        var (_, node) = await Parse(reader, null, 0);
        return node;
    }

    public static async Task<JsonNode?> Deserialize(string filename, NestedTextSerializerOptions? options = null)
    {
        using var stream = File.OpenRead(filename);
        return await Deserialize(stream, options);
    }

    abstract record ParseResult(string Line, int LineIndex, string Indent, string Value);
    sealed record KeyParseResult(string Line, int LineIndex, string Indent, string Key, string Value) : ParseResult(Line, LineIndex, Indent, Value);
    sealed record ListItemParseResult(string Line, int LineIndex, string Indent, string Value) : ParseResult(Line, LineIndex, Indent, Value);
    sealed record MultilineParseResult(string Line, int LineIndex, string Indent, string Value) : ParseResult(Line, LineIndex, Indent, Value);
    sealed record CommentParseResult(string Line, int LineIndex, string Comment) : ParseResult(Line, LineIndex, "", Comment);

    static async Task<(ParseResult? ParseResult, JsonNode? Node)> Parse(StreamReader reader, ParseResult? parseResult, int indent)
    {
        KeyParseResult? key = null;
        ParseResult? prevResult = null;
        List<string>? items = null;
        var lineIdx = parseResult?.LineIndex ?? 0;
        JsonNode? node = null;
        while (true)
        {
            if (parseResult is null)
            {
                if (await reader.ReadLineAsync() is not string line)
                    break;
                parseResult = ParseLine(line, (parseResult?.LineIndex + 1) ?? 0);
            }

            if (parseResult is CommentParseResult)
            {
                parseResult = null;
                continue;
            }

            if (parseResult.Indent.Length > indent)
            {
                var (nextParseResult, subNode) = await Parse(reader, parseResult, parseResult!.Indent.Length);
                if (key is null || subNode is null || node is not JsonObject obj)
                    throw new NestedTextException("Invalid line", parseResult.Line, parseResult.LineIndex);
                obj[key.Key] = subNode;
                key = null;
                parseResult = nextParseResult;
                if (parseResult is null)
                    continue;
            }

            if (parseResult.Indent.Length < indent)
                break;

            if ((key = parseResult as KeyParseResult) is not null)
            {
                node ??= new JsonObject();
                if (node is not JsonObject obj)
                    throw new NestedTextException("Invalid line", parseResult.Line, parseResult.LineIndex);
                obj[key.Key] = key.Value;
            }
            else if (parseResult is ListItemParseResult)
            {
                node ??= new JsonArray();
                if (node is not JsonArray arr)
                    throw new NestedTextException("Invalid line", parseResult.Line, parseResult.LineIndex);
                arr.Add(parseResult.Value);
            }
            else if (parseResult is MultilineParseResult)
            {
                if (node is not null)
                    throw new NestedTextException("Invalid line", parseResult.Line, parseResult.LineIndex);
                (items ??= new()).Add(parseResult.Value);
            }
            else
                throw new NestedTextException("Invalid line", parseResult.Line, parseResult.LineIndex);

            prevResult = parseResult;
            parseResult = null; // Read next line
        }

        if (items is not null)
        {
            if (node is not null)
                throw new NestedTextException("Invalid line", parseResult?.Line ?? "", parseResult?.LineIndex ?? -1);
            node = string.Join(Environment.NewLine, items);
        }
        
        return (parseResult, node);
    }

    static ParseResult ParseLine(string line, int lineIdx)
    {
        static ParseResult Core(string line, int lineIdx)
        {
            Match m;
            if ((m = Regex.Match(line, @"^(?<I>\s*)(?<T>>|-)(?: (?<V>.*))?$")).Success)
                return m.Groups["T"].Value is "-" ? new ListItemParseResult(line, lineIdx, m.Groups["I"].Value, m.Groups["V"].Value) : new MultilineParseResult(line, lineIdx, m.Groups["I"].Value, m.Groups["V"].Value);
            if ((m = Regex.Match(line, @"^(?<I>\s*)(?<K>.+?)\s*:(?: (?<V>.*))?$")).Success)
                return new KeyParseResult(line, lineIdx, m.Groups["I"].Value, m.Groups["K"].Value, m.Groups["V"].Value);
            if ((m = Regex.Match(line, @"^ *$")).Success)
                return new CommentParseResult(line, lineIdx, "");
            if ((m = Regex.Match(line, @"^ *# ?(?<C>.*)$")).Success)
                return new CommentParseResult(line, lineIdx, m.Groups["C"].Value);
            throw new NestedTextException("Unrecognized line", line, lineIdx);
        }

        var result = Core(line, lineIdx);
        var firstNonSpace = result.Indent.Select((c, i) => (c, i)).FirstOrDefault(x => x.c is not ' ');
        if (firstNonSpace.c != default)
            throw new NestedTextException("Invalid character in indentation, only simple spaces are allowed", line, lineIdx, firstNonSpace.i);
        return result;
    }
}
