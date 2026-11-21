using System.Text;
using System.Text.Json;
using H2o.Core.Storage;

namespace H2o.Core.Rules;

/// <summary>The user's on/off state for each Trim Rule; a rule the user never touched follows its Rule Pack default.</summary>
public sealed class TrimRuleSettings(H2oDatabase db)
{
    static string Key(string id) => "trim." + id;

    public bool IsEnabled(TrimRule rule) => db.GetSetting(Key(rule.Id)) switch
    {
        "1" => true,
        "0" => false,
        _ => rule.Default,
    };

    public void SetEnabled(string ruleId, bool enabled) => db.SetSetting(Key(ruleId), enabled ? "1" : "0");
}

/// <summary>What the enabled Trim Rules add up to: CSS to hide, URL patterns to block, and ids for script behaviours.</summary>
public sealed record TrimPlan(string HideCss, IReadOnlyList<string> BlockPatterns, IReadOnlyList<string> EnabledRuleIds)
{
    public static TrimPlan From(RulePack pack, TrimRuleSettings settings)
    {
        var css = new StringBuilder();
        var block = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var enabled = new List<string>();

        foreach (var rule in pack.Rules)
        {
            if (!settings.IsEnabled(rule)) continue;
            enabled.Add(rule.Id);
            // One CSS rule per selector: an invalid selector then drops only itself, not the whole list.
            foreach (var selector in rule.Hide) css.Append(selector.Trim()).Append("{display:none!important}\n");
            foreach (var pattern in rule.Block) if (seen.Add(pattern)) block.Add(pattern);
        }
        return new TrimPlan(css.ToString(), block, enabled);
    }

    const string Placeholder = "/*H2O_TRIM*/null";

    /// <summary>The settings the page script needs, as JSON.</summary>
    public string ToScriptConfig() => JsonSerializer.Serialize(new { enabled = EnabledRuleIds, hideCss = HideCss });

    /// <summary>Fills the trim placeholder in the page script template.</summary>
    public string BuildScript(string template)
    {
        if (!template.Contains(Placeholder, StringComparison.Ordinal))
            throw new ArgumentException($"Template has no {Placeholder} placeholder.", nameof(template));
        return template.Replace(Placeholder, ToScriptConfig(), StringComparison.Ordinal);
    }
}
