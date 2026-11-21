namespace Tubiqo.Core.Navigation;

internal static class QueryString
{
    /// <summary>First value of <paramref name="key"/> in the URI's query, decoded; null when absent.</summary>
    public static string? Get(Uri uri, string key)
    {
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var name = eq < 0 ? part : part[..eq];
            if (name != key) continue;
            return eq < 0 ? "" : Uri.UnescapeDataString(part[(eq + 1)..].Replace('+', ' '));
        }
        return null;
    }
}
