using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tubiqo.Core.Rules;

/// <summary>One Trim Rule: what it hides (CSS selectors) and what it blocks (URL patterns). Never code.</summary>
public sealed record TrimRule(string Id, string Title, bool Default, IReadOnlyList<string> Hide, IReadOnlyList<string> Block);

public sealed class RulePackException(string message) : Exception(message);

/// <summary>
/// The data-only bundle that defines the Trim Rules (ADR 0003). Parsing is strict: anything
/// that could be more than "hide this element" or "block this specific URL" is refused.
/// </summary>
public sealed partial record RulePack(string Version, IReadOnlyList<TrimRule> Rules)
{
    public const int SupportedFormat = 1;
    const int MaxRules = 100, MaxEntries = 200, MaxSelectorLength = 300, MaxTitleLength = 100, MaxVersionLength = 40;
    const int MinBlockLiteralLength = 8;

    // URL text a block pattern must never cover: video segments, thumbnails, the player and the Lean Player.
    static readonly string[] ProtectedFragments =
    [
        "googlevideo.com", "ytimg.com", "youtube-nocookie.com/embed", "player.tubiqo.test",
        "youtubei/v1/player", "youtube.com/youtubei/", "youtube.com/watch", "youtube.com/s/",
    ];
    const string YouTubeRoot = "https://www.youtube.com/";

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,39}$")]
    private static partial Regex IdPattern();

    public static RulePack Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { throw new RulePackException("Rule Pack is not valid JSON."); }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new RulePackException("Rule Pack must be a JSON object.");

            if (!root.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.Number ||
                !format.TryGetInt32(out var f) || f != SupportedFormat)
                throw new RulePackException($"Unsupported Rule Pack format (expected {SupportedFormat}).");

            var version = RequiredString(root, "version", MaxVersionLength, "Rule Pack version");

            if (!root.TryGetProperty("rules", out var rulesEl) || rulesEl.ValueKind != JsonValueKind.Array)
                throw new RulePackException("Rule Pack has no rules list.");
            if (rulesEl.GetArrayLength() > MaxRules) throw new RulePackException($"Rule Pack has more than {MaxRules} rules.");

            var rules = new List<TrimRule>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in rulesEl.EnumerateArray())
            {
                if (r.ValueKind != JsonValueKind.Object) throw new RulePackException("Each rule must be an object.");
                var id = RequiredString(r, "id", 40, "Rule id");
                if (!IdPattern().IsMatch(id)) throw new RulePackException($"Rule id '{id}' must be lowercase letters, digits and dashes.");
                if (!ids.Add(id)) throw new RulePackException($"Duplicate rule id '{id}'.");
                var title = RequiredString(r, "title", MaxTitleLength, $"Rule '{id}' title");
                if (!r.TryGetProperty("default", out var def) || def.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new RulePackException($"Rule '{id}' needs a true/false default.");

                var hide = StringList(r, "hide", id);
                foreach (var s in hide)
                    if (!IsPlainSelector(s)) throw new RulePackException($"Rule '{id}' hide entry is not a plain CSS selector: {s}");
                var block = StringList(r, "block", id);
                foreach (var p in block)
                    if (!IsSpecificPattern(p)) throw new RulePackException($"Rule '{id}' block pattern is too broad or covers playback: {p}");

                rules.Add(new TrimRule(id, title, def.GetBoolean(), hide, block));
            }
            return new RulePack(version, rules);
        }
    }

    static string RequiredString(JsonElement o, string name, int maxLength, string what)
    {
        if (!o.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.String)
            throw new RulePackException($"{what} is missing (property '{name}').");
        var s = p.GetString()!.Trim();
        if (s.Length == 0 || s.Length > maxLength) throw new RulePackException($"{what} must be 1–{maxLength} characters ('{name}').");
        return s;
    }

    static IReadOnlyList<string> StringList(JsonElement rule, string name, string id)
    {
        if (!rule.TryGetProperty(name, out var p)) return [];
        if (p.ValueKind != JsonValueKind.Array) throw new RulePackException($"Rule '{id}' {name} must be a list.");
        if (p.GetArrayLength() > MaxEntries) throw new RulePackException($"Rule '{id}' {name} has more than {MaxEntries} entries.");
        var list = new List<string>();
        foreach (var e in p.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.String) throw new RulePackException($"Rule '{id}' {name} entries must be strings.");
            list.Add(e.GetString()!);
        }
        return list;
    }

    /// <summary>
    /// A selector that can only select: no braces, semicolons, at-rules, escapes, comments, markup or url(),
    /// and balanced brackets and quotes, so it can't end its own CSS rule or swallow the next one.
    /// </summary>
    internal static bool IsPlainSelector(string s)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Length > MaxSelectorLength) return false;
        if (s.IndexOfAny(['{', '}', ';', '@', '\\', '<']) >= 0) return false;
        if (s.Contains("/*") || s.Contains("*/") || s.Contains("url(", StringComparison.OrdinalIgnoreCase)) return false;

        char quote = '\0';
        int brackets = 0, parens = 0;
        foreach (var c in s)
        {
            if (char.IsControl(c)) return false;
            if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
            switch (c)
            {
                case '"' or '\'': quote = c; break;
                case '[': brackets++; break;
                case ']': if (--brackets < 0) return false; break;
                case '(': parens++; break;
                case ')': if (--parens < 0) return false; break;
            }
        }
        return quote == '\0' && brackets == 0 && parens == 0;
    }

    /// <summary>A WebView2 filter of the form <c>*literal*</c> or <c>*literal</c> naming one specific URL family.</summary>
    internal static bool IsSpecificPattern(string p)
    {
        if (!p.StartsWith('*')) return false;
        var literal = p.TrimStart('*');
        if (literal.EndsWith('*')) literal = literal[..^1];
        if (literal.Contains('*') || literal.Length < MinBlockLiteralLength || !literal.Contains('.')) return false;
        if (literal.Any(char.IsWhiteSpace) || literal.Any(char.IsControl)) return false;

        var lower = literal.ToLowerInvariant();
        if (YouTubeRoot.Contains(lower, StringComparison.Ordinal)) return false; // e.g. "youtube.com/", "www.youtube.com"
        return !ProtectedFragments.Any(f => lower.Contains(f, StringComparison.Ordinal));
    }
}
