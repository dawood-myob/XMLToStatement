using System.Text.RegularExpressions;

namespace XMLToStatement;

/// <summary>
/// Optional external (bank file) to internal (e.g. Workday) account number map.
/// File format: one mapping per line, "external&lt;sep&gt;internal", where the separator is one or
/// more tabs, a comma or a semicolon. Blank lines and lines starting with '#' are ignored.
/// Spaces inside values are preserved (e.g. "00017566 0084").
/// </summary>
public sealed partial class AccountMap
{
    private readonly Dictionary<string, string> _map;

    public AccountMap(IDictionary<string, string> map)
    {
        _map = new Dictionary<string, string>(map, StringComparer.OrdinalIgnoreCase);
    }

    public int Count => _map.Count;

    public string? Lookup(string externalAccount) =>
        _map.TryGetValue(externalAccount.Trim(), out var value) ? value : null;

    public static AccountMap Load(string path) => Parse(File.ReadAllLines(path));

    public static AccountMap Parse(IEnumerable<string> lines)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lineNo = 0;
        foreach (var raw in lines)
        {
            lineNo++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var parts = Separator().Split(line, 2);
            if (parts.Length != 2 || parts[0].Trim().Length == 0 || parts[1].Trim().Length == 0)
            {
                throw new FormatException(
                    $"Account map line {lineNo} is invalid: '{raw}'. Expected 'external<TAB|,|;>internal'.");
            }

            map[parts[0].Trim()] = parts[1].Trim();
        }

        return new AccountMap(map);
    }

    [GeneratedRegex(@"\t+|,|;")]
    private static partial Regex Separator();
}
