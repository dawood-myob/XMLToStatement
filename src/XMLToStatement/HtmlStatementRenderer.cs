using System.Globalization;
using System.Net;
using System.Text;

namespace XMLToStatement;

public sealed record RenderOptions
{
    public string Title { get; init; } = "Bank Statement";

    public string SourceFileName { get; init; } = "";

    public AccountFormat AccountFormat { get; init; } = AccountFormat.Auto;

    public AccountMap? AccountMap { get; init; }

    public string AccountMapLabel { get; init; } = "Mapped account number";
}

public static class HtmlStatementRenderer
{
    public static string Render(StatementFile file, RenderOptions? options = null)
    {
        options ??= new RenderOptions();
        var sb = new StringBuilder();

        sb.Append("<!DOCTYPE html>\n<html lang=\"en\"><head><meta charset=\"utf-8\">\n")
          .Append("<title>").Append(E(options.Title)).Append(" - ").Append(E(file.MessageId)).Append("</title>\n")
          .Append(Css)
          .Append("</head>\n<body id=\"top\">\n")
          .Append("<h1>").Append(E(options.Title)).Append(" &mdash; ISO 20022 ").Append(E(file.MessageType)).Append("</h1>\n");

        if (options.SourceFileName.Length > 0)
        {
            sb.Append("<p class=\"small\">Generated from ").Append(E(options.SourceFileName))
              .Append(" on ").Append(E(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))).Append("</p>\n");
        }

        sb.Append("<table class=\"kv\">\n");
        KeyValue(sb, "Message Id", file.MessageId, mono: true);
        KeyValue(sb, "File created", file.CreatedAt);
        KeyValue(sb, "Statement type", CodeDescriptions.WithDescription(CodeDescriptions.AdditionalInfo, file.AdditionalInfo));
        KeyValue(sb, "Number of accounts", file.Statements.Count.ToString(CultureInfo.InvariantCulture));
        sb.Append("</table>\n");

        RenderIndex(sb, file, options);

        for (var i = 0; i < file.Statements.Count; i++)
        {
            RenderStatement(sb, file.Statements[i], i + 1, options);
        }

        sb.Append("</body></html>\n");
        return sb.ToString();
    }

    private static void RenderIndex(StringBuilder sb, StatementFile file, RenderOptions options)
    {
        var showMap = options.AccountMap is not null;
        sb.Append("<h3>Accounts in this file</h3>\n<table class=\"list\"><thead><tr><th>Account</th><th>Name</th><th>Ccy</th>")
          .Append("<th>Opening</th><th>Closing</th><th>Entries</th>");
        if (showMap)
        {
            sb.Append("<th>").Append(E(options.AccountMapLabel)).Append("</th>");
        }

        sb.Append("</tr></thead><tbody>\n");
        for (var i = 0; i < file.Statements.Count; i++)
        {
            var s = file.Statements[i];
            sb.Append("<tr><td><a href=\"#acct").Append(i + 1).Append("\" class=\"mono\">")
              .Append(E(FormatAccount(s.Account, options))).Append("</a></td>")
              .Append("<td>").Append(E(AccountName(s.Account))).Append("</td>")
              .Append("<td>").Append(E(s.Account.Currency)).Append("</td>")
              .Append("<td class=\"num\">").Append(M(s.OpeningBalance?.SignedAmount)).Append("</td>")
              .Append("<td class=\"num\">").Append(M(s.ClosingBalance?.SignedAmount)).Append("</td>")
              .Append("<td class=\"num\">").Append(s.Entries.Count).Append("</td>");
            if (showMap)
            {
                sb.Append("<td class=\"mono\">").Append(E(options.AccountMap!.Lookup(s.Account.Identifier) ?? "-")).Append("</td>");
            }

            sb.Append("</tr>\n");
        }

        sb.Append("</tbody></table>\n");
    }

    private static void RenderStatement(StringBuilder sb, AccountStatement s, int index, RenderOptions options)
    {
        var a = s.Account;
        var ccy = a.Currency;

        sb.Append("<section id=\"acct").Append(index).Append("\">\n<h2>").Append(index).Append(". ")
          .Append(E(FormatAccount(a, options))).Append(" &mdash; ").Append(E(AccountName(a))).Append("</h2>\n");

        sb.Append("<div class=\"grid\"><table class=\"kv\">\n");
        KeyValue(sb, a.IsIban ? "IBAN" : "Account (as in file)", a.Identifier, mono: true);
        KeyValue(sb, "Account (formatted)", FormatAccount(a, options), mono: true);
        if (options.AccountMap is not null)
        {
            KeyValue(sb, options.AccountMapLabel, options.AccountMap.Lookup(a.Identifier) ?? "- (not mapped)", mono: true);
        }

        KeyValue(sb, "Account name", a.Name);
        if (a.OwnerName.Length > 0)
        {
            KeyValue(sb, "Account owner", a.OwnerName);
        }

        KeyValue(sb, "Currency", ccy);
        KeyValue(sb, "Bank (BIC)", a.Bic, mono: true);
        if (a.ServicerName.Length > 0)
        {
            KeyValue(sb, "Bank name", a.ServicerName);
        }

        sb.Append("</table><table class=\"kv\">\n");
        KeyValue(sb, "Statement Id", s.Id, mono: true);
        KeyValue(sb, "Electronic Seq No", s.ElectronicSequenceNumber);
        KeyValue(sb, "Created", s.CreatedAt);
        KeyValue(sb, "Period from", s.FromDateTime);
        KeyValue(sb, "Period to", s.ToDateTime);
        sb.Append("</table></div>\n");

        RenderBalances(sb, s, ccy);
        RenderSummary(sb, s.Summary, ccy);
        RenderEntries(sb, s);
        RenderChecks(sb, StatementChecks.Run(s, options.AccountMap));

        sb.Append("<p class=\"top\"><a href=\"#top\">Back to top</a></p>\n</section>\n");
    }

    private static void RenderBalances(StringBuilder sb, AccountStatement s, string ccy)
    {
        sb.Append("<h3>Balances</h3>\n");
        if (s.Balances.Count == 0)
        {
            sb.Append("<p class=\"muted\">No balances reported.</p>\n");
            return;
        }

        sb.Append("<table class=\"list\"><thead><tr><th>Type</th><th>Amount (").Append(E(ccy))
          .Append(")</th><th>Cr/Dr</th><th>Date</th></tr></thead><tbody>\n");
        foreach (var b in s.Balances)
        {
            sb.Append("<tr><td>").Append(E(CodeDescriptions.WithDescription(CodeDescriptions.BalanceTypes, b.Code))).Append("</td>")
              .Append("<td class=\"num\">").Append(M(b.Amount)).Append("</td>")
              .Append("<td>").Append(StatementChecks.IndicatorCode(b.Indicator)).Append("</td>")
              .Append("<td>").Append(E(b.Date)).Append("</td></tr>\n");
        }

        sb.Append("</tbody></table>\n");
    }

    private static void RenderSummary(StringBuilder sb, TransactionSummary? sum, string ccy)
    {
        if (sum is null)
        {
            return;
        }

        sb.Append("<h3>Transaction summary (as reported by bank)</h3>\n<table class=\"list\"><thead><tr><th></th><th>Count</th><th>Amount (")
          .Append(E(ccy)).Append(")</th></tr></thead><tbody>\n")
          .Append("<tr><td>Total entries</td><td class=\"num\">").Append(sum.TotalCount).Append("</td><td class=\"num\">").Append(M(sum.TotalSum)).Append("</td></tr>\n")
          .Append("<tr><td>Credits</td><td class=\"num\">").Append(sum.CreditCount).Append("</td><td class=\"num cr\">").Append(M(sum.CreditSum)).Append("</td></tr>\n")
          .Append("<tr><td>Debits</td><td class=\"num\">").Append(sum.DebitCount).Append("</td><td class=\"num dr\">").Append(M(sum.DebitSum)).Append("</td></tr>\n")
          .Append("<tr><td>Net movement</td><td></td><td class=\"num\">").Append(M(sum.NetAmount)).Append(' ')
          .Append(StatementChecks.IndicatorCode(sum.NetIndicator)).Append("</td></tr>\n</tbody></table>\n");
    }

    private static void RenderEntries(StringBuilder sb, AccountStatement s)
    {
        sb.Append("<h3>Transactions</h3>\n");
        if (s.Entries.Count == 0)
        {
            sb.Append("<p class=\"muted\">No transactions for this period.</p>\n");
            return;
        }

        var opening = s.OpeningBalance?.SignedAmount;
        decimal? running = opening;

        sb.Append("<table class=\"tx\"><thead><tr><th>#</th><th>Date</th><th>Entry Ref / Status</th><th>Type</th>")
          .Append("<th>Counterparty</th><th>References / Narrative</th><th>Debit</th><th>Credit</th><th>Balance</th></tr></thead><tbody>\n")
          .Append("<tr class=\"sub\"><td colspan=\"8\">Opening balance</td><td class=\"num\">").Append(M(opening)).Append("</td></tr>\n");

        for (var i = 0; i < s.Entries.Count; i++)
        {
            var e = s.Entries[i];
            running += e.SignedAmount;

            sb.Append("<tr><td class=\"num\">").Append(i + 1).Append("</td>")
              .Append("<td>").Append(E(e.BookingDate)).Append("<br><span class=\"small\">Value ").Append(E(e.ValueDate)).Append("</span></td>")
              .Append("<td class=\"mono\">").Append(E(e.EntryReference)).Append("<br><span class=\"small\">").Append(E(e.Status)).Append("</span></td>")
              .Append("<td>").Append(TypeCell(e.BankTransactionCode)).Append("</td>")
              .Append("<td>").Append(CounterpartyCell(e)).Append("</td>")
              .Append("<td class=\"small\">").Append(ReferencesCell(e)).Append("</td>")
              .Append("<td class=\"num dr\">").Append(e.Indicator == CreditDebit.Debit ? M(e.Amount) : "").Append("</td>")
              .Append("<td class=\"num cr\">").Append(e.Indicator == CreditDebit.Credit ? M(e.Amount) : "").Append("</td>")
              .Append("<td class=\"num\">").Append(M(running)).Append("</td></tr>\n");
        }

        sb.Append("<tr class=\"sub\"><td colspan=\"6\">Totals / Closing balance</td>")
          .Append("<td class=\"num dr\">").Append(M(s.DebitTotal)).Append("</td>")
          .Append("<td class=\"num cr\">").Append(M(s.CreditTotal)).Append("</td>")
          .Append("<td class=\"num\">").Append(M(running)).Append("</td></tr>\n</tbody></table>\n");
    }

    private static string TypeCell(BankTransactionCode c)
    {
        if (c == BankTransactionCode.Empty)
        {
            return "";
        }

        var sb = new StringBuilder();
        if (c.ProprietaryCode.Length > 0)
        {
            sb.Append("<b>").Append(E(c.ProprietaryCode)).Append("</b><br>");
        }

        if (c.Domain.Length > 0)
        {
            sb.Append("<span class=\"small\">")
              .Append(E(CodeDescriptions.Describe(CodeDescriptions.Domains, c.Domain))).Append(" / ")
              .Append(E(CodeDescriptions.Describe(CodeDescriptions.Families, c.Family))).Append(" / ")
              .Append(E(CodeDescriptions.Describe(CodeDescriptions.SubFamilies, c.SubFamily)))
              .Append("<br><span class=\"mono\">").Append(E($"{c.Domain}-{c.Family}-{c.SubFamily}")).Append("</span></span>");
        }

        return sb.ToString();
    }

    private static string CounterpartyCell(Entry e)
    {
        var parts = new List<string>();
        foreach (var d in e.Details)
        {
            AddParty(parts, "Payer", d.Debtor);
            AddParty(parts, "Payee", d.Creditor);
        }

        return string.Join("<br>", parts);
    }

    private static void AddParty(List<string> parts, string label, Party? p)
    {
        if (p is null)
        {
            return;
        }

        var html = $"<b>{label}:</b> {E(p.Name)}";
        if (p.Account.Length > 0)
        {
            html += $"<br><span class=\"mono\">{E(AccountNumberFormatter.Format(p.Account, "", false, InferFormat(p.Account)))}</span>";
        }

        parts.Add(html);
    }

    // Counterparty accounts have no BIC; a 15/16 digit number is treated as NZ, which is harmless elsewhere.
    private static AccountFormat InferFormat(string account) =>
        account.Length is 15 or 16 && account.All(char.IsAsciiDigit) ? AccountFormat.NZ : AccountFormat.None;

    private static string ReferencesCell(Entry e)
    {
        var lines = new List<string>();
        if (e.AccountServicerReference.Length > 0)
        {
            lines.Add($"<b>AcctSvcrRef:</b> {E(e.AccountServicerReference)}");
        }

        var multiple = e.Details.Count > 1;
        for (var i = 0; i < e.Details.Count; i++)
        {
            var d = e.Details[i];
            if (multiple)
            {
                var amount = d.Amount is { } amt ? $" &mdash; {M(amt)} {E(d.AmountCurrency)}" : "";
                lines.Add($"<u>Detail {i + 1}</u>{amount}");
            }

            lines.AddRange(d.References.Select(r => $"<b>{E(r.Key)}:</b> {E(r.Value)}"));
            lines.AddRange(d.RemittanceInfo.Select(E));
            if (d.AdditionalInfo.Length > 0)
            {
                lines.Add(E(d.AdditionalInfo));
            }
        }

        if (e.AdditionalEntryInfo.Length > 0)
        {
            lines.Add(E(e.AdditionalEntryInfo));
        }

        return string.Join("<br>", lines);
    }

    private static void RenderChecks(StringBuilder sb, IReadOnlyList<CheckResult> checks)
    {
        sb.Append("<h3>Integrity checks</h3>\n");
        if (checks.Count == 0)
        {
            sb.Append("<p class=\"muted\">No checks applicable.</p>\n");
            return;
        }

        sb.Append("<table class=\"list\"><thead><tr><th>Check</th><th>Result</th><th>Detail</th></tr></thead><tbody>\n");
        foreach (var c in checks)
        {
            sb.Append("<tr><td>").Append(E(c.Name)).Append("</td><td class=\"").Append(c.Passed ? "ok\">PASS" : "bad\">FAIL")
              .Append("</td><td>").Append(E(c.Detail)).Append("</td></tr>\n");
        }

        sb.Append("</tbody></table>\n");
    }

    private static string FormatAccount(AccountInfo a, RenderOptions o) =>
        AccountNumberFormatter.Format(a.Identifier, a.Bic, a.IsIban, o.AccountFormat);

    private static string AccountName(AccountInfo a) =>
        a.Name.Length > 0 ? a.Name : a.OwnerName.Length > 0 ? a.OwnerName : "(no name)";

    private static void KeyValue(StringBuilder sb, string key, string value, bool mono = false) =>
        sb.Append("<tr><th>").Append(E(key)).Append("</th><td").Append(mono ? " class=\"mono\"" : "").Append('>')
          .Append(E(value.Length > 0 ? value : "-")).Append("</td></tr>\n");

    private static string E(string value) => WebUtility.HtmlEncode(value);

    private static string M(decimal? value) =>
        value?.ToString("N2", CultureInfo.InvariantCulture) ?? "-";

    private const string Css = """
        <style>
         body { font-family: Segoe UI, Arial, sans-serif; margin: 24px; color: #222; font-size: 13px; }
         h1 { color: #da1710; margin-bottom: 4px; }
         h2 { background: #1f1c4f; color: #fff; padding: 8px 12px; margin-top: 36px; }
         h3 { border-bottom: 2px solid #da1710; padding-bottom: 2px; margin-top: 20px; }
         table { border-collapse: collapse; margin: 6px 0; }
         th, td { border: 1px solid #ccc; padding: 4px 8px; vertical-align: top; text-align: left; }
         th { background: #f0f0f5; }
         table.tx { width: 100%; }
         table.tx tbody tr:nth-child(even) { background: #fafafa; }
         tr.sub td { background: #e8e8f0; font-weight: bold; }
         .grid { display: flex; gap: 24px; flex-wrap: wrap; }
         .kv th { width: 210px; }
         .num { text-align: right; white-space: nowrap; }
         .mono { font-family: Consolas, monospace; }
         .small { font-size: 11px; color: #555; }
         .muted { color: #777; font-style: italic; }
         .cr { color: #0a7a2f; } .dr { color: #b00020; }
         .ok { color: #0a7a2f; font-weight: bold; } .bad { color: #b00020; font-weight: bold; }
         @media print { h2 { page-break-before: always; } .top { display: none; } }
        </style>

        """;
}
