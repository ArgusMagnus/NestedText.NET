using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

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
    public static async Task<JsonNode?> Deserialize(TextReader reader, NestedTextSerializerOptions? options = null, bool leaveOpen = false)
    {
        options ??= NestedTextSerializerOptions.Default;
        if (!options.Minimal)
            throw new NotSupportedException($"The current implementation does not support {nameof(NestedTextSerializerOptions)}.{nameof(NestedTextSerializerOptions.Minimal)} = false");
        var (_, node) = await Parse(reader, null, 0, options);
        return node;
    }

    public static async Task<JsonNode?> Deserialize(Stream stream, NestedTextSerializerOptions? options = null, bool leaveOpen = false)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: leaveOpen);
        return await Deserialize(reader, options);
    }

    public static async Task<JsonNode?> Deserialize(FileInfo file, NestedTextSerializerOptions? options = null)
    {
        using var stream = file.OpenRead();
        return await Deserialize(stream, options);
    }

    public static Task<JsonNode?> Deserialize(string nestedText, NestedTextSerializerOptions? options = null)
        => Deserialize(new StringReader(nestedText), options);

    abstract record ParseResult(string Line, int LineIndex, string Indent, string? Value);
    sealed record KeyParseResult(string Line, int LineIndex, string Indent, string Key, string? Value) : ParseResult(Line, LineIndex, Indent, Value);
    sealed record ListItemParseResult(string Line, int LineIndex, string Indent, string? Value) : ParseResult(Line, LineIndex, Indent, Value);
    sealed record MultilineParseResult(string Line, int LineIndex, string Indent, string? Value) : ParseResult(Line, LineIndex, Indent, Value);
    sealed record CommentParseResult(string Line, int LineIndex, string Comment) : ParseResult(Line, LineIndex, "", Comment);

    static async Task<(ParseResult? ParseResult, JsonNode? Node)> Parse(TextReader reader, ParseResult? parseResult, int indent, NestedTextSerializerOptions options)
    {
        ParseResult? prevParseResult = null;
        List<string>? items = null;
        var lineIdx = parseResult?.LineIndex ?? 0;
        JsonNode? node = null;

        static void AddPrevious(JsonNode? node, ParseResult? parseResult, ParseResult? prevParseResult)
        {
            if (prevParseResult is KeyParseResult key)
            {
                if (node is not JsonObject obj)
                    throw new NestedTextException("Invalid line", parseResult?.Line ?? "", parseResult?.LineIndex ?? -1);
                if (!obj.ContainsKey(key.Key))
                    obj[key.Key] = key.Value ?? "";
                else if (key.Value is not null)
                    throw new NestedTextException("Invalid line", parseResult?.Line ?? "", parseResult?.LineIndex ?? -1);
            }
            else if (prevParseResult is ListItemParseResult item && item.Value is null)
            {
                if (node is not JsonArray arr)
                    throw new NestedTextException("Invalid line", parseResult?.Line ?? "", parseResult?.LineIndex ?? -1);
                if (parseResult is null || prevParseResult.Indent.Length == parseResult.Indent.Length)
                    arr.Add("");
            }
        }

        static async Task<ParseResult?> NextResult(TextReader reader, ParseResult? parseResult)
        {
            if (await reader.ReadLineAsync() is not string line)
                return null;
            return ParseLine(line, (parseResult?.LineIndex + 1) ?? 0);
        }

        for (parseResult ??= await NextResult(reader, parseResult); parseResult is not null; (parseResult, prevParseResult) = (await NextResult(reader, parseResult), parseResult))
        {
            if (parseResult is CommentParseResult)
            {
                parseResult = prevParseResult;
                continue;
            }

            AddPrevious(node, parseResult, prevParseResult);

            if (parseResult.Indent.Length > indent)
            {
                var (nextParseResult, subNode) = await Parse(reader, parseResult, parseResult!.Indent.Length, options);
                if (subNode is null || prevParseResult?.Value is not null)
                    throw new NestedTextException("Invalid line", parseResult.Line, parseResult.LineIndex);

                if (node is JsonObject obj)
                {
                    if (prevParseResult is not KeyParseResult k)
                        throw new NestedTextException("Invalid line", parseResult.Line, parseResult.LineIndex);
                    obj[k.Key] = subNode;
                }
                else if (node is JsonArray arr)
                    arr.Add(subNode);
                else
                    throw new NestedTextException("Invalid line", parseResult.Line, parseResult.LineIndex);

                //(parseResult, prevParseResult) = (nextParseResult, parseResult);
                parseResult = nextParseResult;
                if (parseResult is null)
                    break;
            }

            if (parseResult.Indent.Length < indent)
            {
                if (prevParseResult is KeyParseResult k && node is JsonObject obj && !obj.ContainsKey(k.Key))
                    throw new NestedTextException("Invalid line", parseResult?.Line ?? "", parseResult?.LineIndex ?? -1);
                break;
            }

            if (parseResult is KeyParseResult key)
            {
                if (options.Minimal)
                {
                    if (key.Key[0] is ' ' or '[' or '{')
                        throw new NestedTextException($"invalid character '{key.Key[0]}' in key {key.Key}.", parseResult.Line, parseResult.LineIndex);
                    if (key.Key.StartsWith("- "))
                        throw new NestedTextException($"invalid substring '- ' in key {key.Key}.", parseResult.Line, parseResult.LineIndex);
                    if (key.Key.Contains(": "))
                        throw new NestedTextException($"invalid substring ': ' in key {key.Key}.", parseResult.Line, parseResult.LineIndex);
                }

                node ??= new JsonObject();
                if (node is not JsonObject obj)
                    throw new NestedTextException("Invalid line", parseResult.Line, parseResult.LineIndex);
                if (obj.ContainsKey(key.Key))
                    throw new NestedTextException($"duplicate key: {key.Key}.", parseResult.Line, parseResult.LineIndex);
                // added in AddPervious
            }
            else if (parseResult is ListItemParseResult)
            {
                node ??= new JsonArray();
                if (node is not JsonArray arr)
                    throw new NestedTextException("Invalid line", parseResult.Line, parseResult.LineIndex);
                if (parseResult.Value is not null)
                    arr.Add(parseResult.Value);
            }
            else if (parseResult is MultilineParseResult)
            {
                if (node is not null)
                    throw new NestedTextException("Invalid line", parseResult.Line, parseResult.LineIndex);
                (items ??= new()).Add(parseResult.Value ?? "");
            }
            else
                throw new NestedTextException("Invalid line", parseResult.Line, parseResult.LineIndex);
        }

        AddPrevious(node, parseResult, prevParseResult);

        if (items is not null)
        {
            if (node is not null)
                throw new NestedTextException("Invalid line", parseResult?.Line ?? "", parseResult?.LineIndex ?? -1);
            node = string.Join('\n', items);
        }

        return (parseResult, node);
    }

    static ParseResult ParseLine(string line, int lineIdx)
    {
        static ParseResult Core(string line, int lineIdx)
        {
            Match m;
            if ((m = Regex.Match(line, @"^(?<I>\s*)(?<T>>|-)(?: (?<V>.*))?$")).Success)
            {
                var indent = m.Groups["I"].Value;
                var value = m.Groups["V"].Success ? m.Groups["V"].Value : null;
                return m.Groups["T"].Value is "-" ? new ListItemParseResult(line, lineIdx, indent, value) : new MultilineParseResult(line, lineIdx, indent, value);
            }
            if ((m = Regex.Match(line, @"^(?<I>\s*)(?<K>.+?)\s*:(?: (?<V>.*))?$")).Success)
                return new KeyParseResult(line, lineIdx, m.Groups["I"].Value, m.Groups["K"].Value, m.Groups["V"].Success ? m.Groups["V"].Value : null);
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
