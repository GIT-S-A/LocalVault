using System.Text;
using System.Text.RegularExpressions;

namespace LocalVault.Engine;

public sealed record TemplateDraft(string Template, IReadOnlyList<CapturedValue> Captured);

public sealed record CapturedValue(EntryReference Reference, string? Value)
{
    public EntryName Name => Reference.Name;
}

public sealed record TemplateUpdate(string Template, IReadOnlyList<CapturedValue> ChangedSecrets);

public static partial class TemplateText
{
    public const int MinimumSecretLength = 6;
    public const string TemplateSuffix = ".template";
    public const string ExampleSuffix = ".example";

    public static IReadOnlyList<EntryReference> Placeholders(string template)
    {
        ArgumentNullException.ThrowIfNull(template);
        var names = new List<EntryReference>();
        foreach (Match match in Placeholder().Matches(template))
        {
            if (EntryReference.TryParse(match.Groups[1].Value, out var name) && !names.Contains(name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    public static string Render(string template, Func<EntryReference, string?> valueOf)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(valueOf);
        var missing = new List<string>();
        var rendered = Placeholder().Replace(template, match =>
        {
            if (!EntryReference.TryParse(match.Groups[1].Value, out var name))
            {
                return match.Value;
            }

            var value = valueOf(name);
            if (value is null)
            {
                missing.Add(name.ToString());
                return match.Value;
            }

            return value;
        });
        if (missing.Count > 0)
        {
            throw new VaultException($"No value for {string.Join(", ", missing.Distinct(StringComparer.OrdinalIgnoreCase))}. Nothing was written.");
        }

        return rendered;
    }

    public static TemplateDraft FromFile(string content, IReadOnlyDictionary<EntryReference, string> secrets, IReadOnlyList<string> capture)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(capture);
        var captured = new List<CapturedValue>();
        var lines = Lines(content).Select(line =>
        {
            foreach (var key in capture)
            {
                if (KeyLine(key).Match(Body(line)) is { Success: true } match)
                {
                    var name = NameOf(key);
                    captured.Add(new CapturedValue(new EntryReference(name), match.Groups["value"].Value));
                    return WithPlaceholder(line, match, name);
                }
            }

            return line;
        }).ToList();
        RequireAllCaptured(capture, captured);
        var known = secrets.Concat(captured.Select(c => KeyValuePair.Create(c.Reference, c.Value!)))
            .GroupBy(pair => pair.Key).ToDictionary(group => group.Key, group => group.Last().Value);
        var template = ReplaceSecrets(string.Concat(lines), known);
        RequireNoSecret(template, known);
        return new TemplateDraft(template, captured);
    }

    public static TemplateDraft FromExample(string example, ISet<EntryName> known, IReadOnlyList<string> capture)
    {
        ArgumentNullException.ThrowIfNull(example);
        ArgumentNullException.ThrowIfNull(known);
        ArgumentNullException.ThrowIfNull(capture);
        var captured = new List<CapturedValue>();
        var keys = capture.Concat(known.Where(name => name.Scope is null).Select(name => name.Key)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var lines = Lines(example).Select(line =>
        {
            foreach (var key in keys)
            {
                if (KeyLine(key).Match(Body(line)) is { Success: true } match)
                {
                    var name = NameOf(key);
                    if (capture.Contains(key, StringComparer.OrdinalIgnoreCase))
                    {
                        captured.Add(new CapturedValue(new EntryReference(name), null));
                    }

                    return WithPlaceholder(line, match, name);
                }
            }

            return line;
        }).ToList();
        RequireAllCaptured(capture, captured);
        return new TemplateDraft(string.Concat(lines), captured);
    }

    public static TemplateUpdate Update(string template, string content, IReadOnlyDictionary<EntryReference, string> secrets)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(secrets);
        var patterns = Lines(template)
            .Where(line => Placeholder().IsMatch(line))
            .Select(line => (Line: line, Names: Placeholder().Matches(line).Select(m => m.Groups[1].Value).ToList(), Pattern: LinePattern(Body(line)), Used: false))
            .ToList();
        var changed = new List<CapturedValue>();
        var output = new StringBuilder();
        foreach (var line in Lines(content))
        {
            var index = patterns.FindIndex(p => !p.Used && p.Pattern.IsMatch(Body(line)));
            if (index < 0)
            {
                output.Append(line);
                continue;
            }

            var pattern = patterns[index];
            patterns[index] = pattern with { Used = true };
            var match = pattern.Pattern.Match(Body(line));
            for (var i = 0; i < pattern.Names.Count; i++)
            {
                if (!EntryReference.TryParse(pattern.Names[i], out var name) || name.Part == EntryPart.Totp)
                {
                    continue;
                }

                var value = match.Groups[i + 1].Value;
                if (!secrets.TryGetValue(name, out var current) || current != value)
                {
                    changed.Add(new CapturedValue(name, value));
                }
            }

            output.Append(pattern.Line[..Body(pattern.Line).Length]).Append(line[Body(line).Length..]);
        }

        var lost = patterns.Where(p => !p.Used).SelectMany(p => p.Names).ToList();
        if (lost.Count > 0)
        {
            throw new VaultException(
                $"The lines holding {string.Join(", ", lost.Select(n => "{{" + n + "}}"))} changed in the file beyond the value. Edit the template itself for those lines. Nothing was written.");
        }

        var known = secrets.Concat(changed.Select(c => KeyValuePair.Create(c.Reference, c.Value!)))
            .GroupBy(pair => pair.Key).ToDictionary(group => group.Key, group => group.Last().Value);
        var updated = ReplaceSecrets(output.ToString(), known);
        RequireNoSecret(updated, known);
        return new TemplateUpdate(updated, changed);
    }

    public static void RequireNoSecret(string template, IReadOnlyDictionary<EntryReference, string> secrets)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(secrets);
        var leaked = secrets
            .Where(pair => pair.Key.IsSecret && pair.Value.Length >= MinimumSecretLength && template.Contains(pair.Value, StringComparison.Ordinal))
            .Select(pair => pair.Key.ToString())
            .ToList();
        if (leaked.Count > 0)
        {
            throw new VaultException(
                $"The template would contain the value of {string.Join(", ", leaked)}. Nothing was written.");
        }
    }

    private static string ReplaceSecrets(string text, IReadOnlyDictionary<EntryReference, string> secrets)
    {
        foreach (var (name, value) in secrets.Where(pair => pair.Key.Part == EntryPart.Password && pair.Value.Length >= MinimumSecretLength).OrderByDescending(pair => pair.Value.Length))
        {
            text = text.Replace(value, "{{" + name + "}}", StringComparison.Ordinal);
        }

        return text;
    }

    private static void RequireAllCaptured(IReadOnlyList<string> capture, List<CapturedValue> captured)
    {
        var absent = capture.Where(key => !captured.Any(c => string.Equals(c.Name.FullName, key, StringComparison.OrdinalIgnoreCase))).ToList();
        if (absent.Count > 0)
        {
            throw new VaultException($"No line sets {string.Join(", ", absent)} in the file. Nothing was written.");
        }
    }

    private static EntryName NameOf(string key) =>
        EntryName.TryParse(key, out var name)
            ? name
            : throw new VaultException($"'{key}' can't be a vault entry name. Use letters, digits and . _ : - @ + only.");

    private static string WithPlaceholder(string line, Match match, EntryName name)
    {
        var value = match.Groups["value"];
        return line[..value.Index] + "{{" + name.FullName + "}}" + line[(value.Index + value.Length)..];
    }

    private static Regex KeyLine(string key) => new(
        @"^\s*(?:export\s+)?" + Regex.Escape(key) + @"\s*[=:]\s*(?<quote>[""']?)(?<value>.*?)\k<quote>\s*(?:#.*)?$",
        RegexOptions.CultureInvariant);

    private static Regex LinePattern(string line)
    {
        var parts = Placeholder().Split(line);
        var pattern = new StringBuilder("^");
        for (var i = 0; i < parts.Length; i++)
        {
            pattern.Append(i % 2 == 0 ? Regex.Escape(parts[i]) : "(.*?)");
        }

        return new Regex(pattern.Append('$').ToString(), RegexOptions.CultureInvariant);
    }

    private static IEnumerable<string> Lines(string text) => LineBreak().Split(text).Where(line => line.Length > 0);

    private static string Body(string line) => line.TrimEnd('\r', '\n');

    [GeneratedRegex(@"\{\{([^{}\s]+)\}\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"(?<=\n)")]
    private static partial Regex LineBreak();
}
