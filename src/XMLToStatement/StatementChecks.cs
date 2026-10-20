using System.Globalization;

namespace XMLToStatement;

/// <summary>
/// Reconciles a statement: entries against balances and against the bank-reported summary.
/// Checks whose inputs are absent from the file are skipped rather than failed.
/// </summary>
public static class StatementChecks
{
    public static IReadOnlyList<CheckResult> Run(AccountStatement s, AccountMap? map = null)
    {
        var results = new List<CheckResult>();
        var net = s.NetMovement;

        if (s.OpeningBalance is { } open && s.ClosingBalance is { } close)
        {
            var expected = open.SignedAmount + net;
            results.Add(new CheckResult(
                "Opening + net movement = Closing",
                expected == close.SignedAmount,
                $"{M(open.SignedAmount)} + {M(net)} = {M(expected)} vs closing {M(close.SignedAmount)}"));
        }

        if (s.Summary is { } sum)
        {
            if (sum.TotalCount is { } total)
            {
                results.Add(new CheckResult(
                    "Entry count matches summary",
                    total == s.Entries.Count,
                    $"{s.Entries.Count} entries vs {total} reported"));
            }

            if (sum.CreditCount is not null || sum.CreditSum is not null)
            {
                results.Add(new CheckResult(
                    "Credit count/sum matches summary",
                    (sum.CreditCount ?? s.CreditCount) == s.CreditCount && (sum.CreditSum ?? s.CreditTotal) == s.CreditTotal,
                    $"{s.CreditCount} / {M(s.CreditTotal)} vs {sum.CreditCount?.ToString() ?? "-"} / {M(sum.CreditSum)}"));
            }

            if (sum.DebitCount is not null || sum.DebitSum is not null)
            {
                results.Add(new CheckResult(
                    "Debit count/sum matches summary",
                    (sum.DebitCount ?? s.DebitCount) == s.DebitCount && (sum.DebitSum ?? s.DebitTotal) == s.DebitTotal,
                    $"{s.DebitCount} / {M(s.DebitTotal)} vs {sum.DebitCount?.ToString() ?? "-"} / {M(sum.DebitSum)}"));
            }

            if (sum.SignedNetAmount is { } reportedNet)
            {
                // A zero net amount may legitimately carry either indicator.
                var ok = reportedNet == net || (net == 0 && sum.NetAmount == 0);
                results.Add(new CheckResult(
                    "Net movement matches summary",
                    ok,
                    $"{M(net)} vs {M(sum.NetAmount)} {IndicatorCode(sum.NetIndicator)}"));
            }
        }

        var foreign = s.Entries.Count(e => e.Currency.Length > 0 && s.Account.Currency.Length > 0 && e.Currency != s.Account.Currency);
        if (s.Entries.Count > 0)
        {
            results.Add(new CheckResult(
                "All entries in account currency",
                foreign == 0,
                foreign == 0 ? $"All {s.Entries.Count} entries in {s.Account.Currency}" : $"{foreign} entries in another currency"));
        }

        if (map is not null)
        {
            var mapped = map.Lookup(s.Account.Identifier);
            results.Add(new CheckResult(
                "Account present in account map",
                mapped is not null,
                mapped is not null ? $"Maps to: {mapped}" : $"No map entry for {s.Account.Identifier}"));
        }

        return results;
    }

    public static string IndicatorCode(CreditDebit indicator) => indicator switch
    {
        CreditDebit.Credit => "CRDT",
        CreditDebit.Debit => "DBIT",
        _ => "",
    };

    private static string M(decimal? value) =>
        value?.ToString("N2", CultureInfo.InvariantCulture) ?? "-";
}
