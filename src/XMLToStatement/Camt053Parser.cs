using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace XMLToStatement;

/// <summary>
/// Parses ISO 20022 bank-to-customer cash management messages (camt.053 statements, and also
/// camt.052 reports / camt.054 notifications) of any schema version. Elements are matched by
/// local name so the parser is independent of the namespace/version in the file.
/// </summary>
public static class Camt053Parser
{
    private static readonly string[] StatementElementNames = ["Stmt", "Rpt", "Ntfctn"];

    public static StatementFile Load(string path)
    {
        using var stream = File.OpenRead(path);
        return Parse(stream);
    }

    public static StatementFile Parse(Stream stream)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var reader = XmlReader.Create(stream, settings);
        return Parse(XDocument.Load(reader));
    }

    public static StatementFile ParseXml(string xml)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
        return Parse(stream);
    }

    public static StatementFile Parse(XDocument doc)
    {
        var root = doc.Root ?? throw new InvalidDataException("The XML document is empty.");
        if (root.Name.LocalName == "File" && root.Child("Imports") is not null)
        {
            throw new InvalidDataException(
                "This XML is an import-processing log (<File><Imports>), not a bank statement. " +
                "Select the original ISO 20022 camt.053, camt.052 or camt.054 bank statement XML instead. " +
                "The import log cannot be converted into a bank statement because it contains no bank statement data.");
        }

        var message = root.Name.LocalName.StartsWith("BkToCstmr", StringComparison.Ordinal)
            ? root
            : root.Elements().FirstOrDefault(e => e.Name.LocalName.StartsWith("BkToCstmr", StringComparison.Ordinal))
              ?? throw new InvalidDataException(
                  "Not an ISO 20022 cash management file: no BkToCstmrStmt/BkToCstmrAcctRpt/BkToCstmrDbtCdtNtfctn element found.");

        var header = message.Child("GrpHdr");
        var statements = message.Elements()
            .Where(e => StatementElementNames.Contains(e.Name.LocalName))
            .Select(ParseStatement)
            .ToList();

        return new StatementFile(
            MessageTypeFromNamespace(root.Name.NamespaceName),
            header.Text("MsgId"),
            header.Text("CreDtTm"),
            header.Text("AddtlInf"),
            statements);
    }

    private static string MessageTypeFromNamespace(string ns)
    {
        var idx = ns.LastIndexOf(':');
        return idx >= 0 && idx < ns.Length - 1 ? ns[(idx + 1)..] : ns;
    }

    private static AccountStatement ParseStatement(XElement stmt)
    {
        var acct = stmt.Child("Acct");
        var id = acct.Child("Id");
        var iban = id.Text("IBAN");

        var account = new AccountInfo(
            Identifier: iban.Length > 0 ? iban : id.Text("Othr", "Id"),
            IsIban: iban.Length > 0,
            Currency: acct.Text("Ccy"),
            Name: acct.Text("Nm"),
            OwnerName: FirstNonEmpty(acct.Text("Ownr", "Nm"), acct.Text("Ownr", "Pty", "Nm")),
            Bic: FirstNonEmpty(acct.Text("Svcr", "FinInstnId", "BIC"), acct.Text("Svcr", "FinInstnId", "BICFI")),
            ServicerName: acct.Text("Svcr", "FinInstnId", "Nm"));

        return new AccountStatement(
            stmt.Text("Id"),
            stmt.Text("ElctrncSeqNb"),
            stmt.Text("CreDtTm"),
            stmt.Text("FrToDt", "FrDtTm"),
            stmt.Text("FrToDt", "ToDtTm"),
            account,
            stmt.Children("Bal").Select(ParseBalance).ToList(),
            ParseSummary(stmt.Child("TxsSummry")),
            stmt.Children("Ntry").Select(ParseEntry).ToList());
    }

    private static Balance ParseBalance(XElement bal)
    {
        var amt = bal.Child("Amt");
        return new Balance(
            FirstNonEmpty(bal.Text("Tp", "CdOrPrtry", "Cd"), bal.Text("Tp", "CdOrPrtry", "Prtry")),
            ParseDecimal(amt?.Value) ?? 0m,
            amt?.Attribute("Ccy")?.Value ?? "",
            ParseIndicator(bal.Text("CdtDbtInd")),
            FirstNonEmpty(bal.Text("Dt", "Dt"), bal.Text("Dt", "DtTm")));
    }

    private static TransactionSummary? ParseSummary(XElement? s)
    {
        if (s is null)
        {
            return null;
        }

        return new TransactionSummary(
            ParseInt(s.Text("TtlNtries", "NbOfNtries")),
            ParseDecimal(s.Text("TtlNtries", "Sum")),
            ParseDecimal(FirstNonEmpty(s.Text("TtlNtries", "TtlNetNtryAmt"), s.Text("TtlNtries", "TtlNetNtry", "Amt"))),
            ParseIndicator(FirstNonEmpty(s.Text("TtlNtries", "CdtDbtInd"), s.Text("TtlNtries", "TtlNetNtry", "CdtDbtInd"))),
            ParseInt(s.Text("TtlCdtNtries", "NbOfNtries")),
            ParseDecimal(s.Text("TtlCdtNtries", "Sum")),
            ParseInt(s.Text("TtlDbtNtries", "NbOfNtries")),
            ParseDecimal(s.Text("TtlDbtNtries", "Sum")));
    }

    private static Entry ParseEntry(XElement ntry)
    {
        var amt = ntry.Child("Amt");
        var sts = ntry.Child("Sts");
        var details = ntry.Children("NtryDtls")
            .SelectMany(d => d.Children("TxDtls"))
            .Select(ParseDetail)
            .ToList();

        return new Entry(
            ntry.Text("NtryRef"),
            ParseDecimal(amt?.Value) ?? 0m,
            amt?.Attribute("Ccy")?.Value ?? "",
            ParseIndicator(ntry.Text("CdtDbtInd")),
            sts is null ? "" : sts.HasElements ? sts.Text("Cd") : sts.Value.Trim(),
            FirstNonEmpty(ntry.Text("BookgDt", "Dt"), ntry.Text("BookgDt", "DtTm")),
            FirstNonEmpty(ntry.Text("ValDt", "Dt"), ntry.Text("ValDt", "DtTm")),
            ntry.Text("AcctSvcrRef"),
            ntry.Text("AddtlNtryInf"),
            ParseBankTxCode(ntry.Child("BkTxCd")) ?? BankTransactionCode.Empty,
            details);
    }

    private static TransactionDetail ParseDetail(XElement tx)
    {
        var references = new List<KeyValuePair<string, string>>();
        foreach (var r in tx.Child("Refs")?.Elements() ?? [])
        {
            if (r.HasElements)
            {
                var label = FirstNonEmpty(r.Text("Tp"), r.Name.LocalName);
                var value = FirstNonEmpty(r.Text("Ref"), string.Join(" ", r.Elements().Select(e => e.Value.Trim())));
                references.Add(new(label, value));
            }
            else if (!string.IsNullOrWhiteSpace(r.Value))
            {
                references.Add(new(r.Name.LocalName, r.Value.Trim()));
            }
        }

        var amt = tx.Child("AmtDtls", "TxAmt", "Amt") ?? tx.Child("Amt");
        var parties = tx.Child("RltdPties");

        var remittance = new List<string>();
        foreach (var rmt in tx.Children("RmtInf"))
        {
            remittance.AddRange(rmt.Children("Ustrd").Select(u => u.Value.Trim()).Where(v => v.Length > 0));
            remittance.AddRange(rmt.Children("Strd")
                .Select(s => s.Text("CdtrRefInf", "Ref"))
                .Where(v => v.Length > 0));
        }

        return new TransactionDetail(
            references,
            ParseDecimal(amt?.Value),
            amt?.Attribute("Ccy")?.Value ?? "",
            ParseParty(parties, "Dbtr", "DbtrAcct"),
            ParseParty(parties, "Cdtr", "CdtrAcct"),
            remittance,
            tx.Text("AddtlTxInf"),
            ParseBankTxCode(tx.Child("BkTxCd")));
    }

    private static Party? ParseParty(XElement? parties, string partyElement, string accountElement)
    {
        if (parties is null)
        {
            return null;
        }

        // v02 uses Dbtr/Nm; v08+ wraps the party as Dbtr/Pty/Nm.
        var name = FirstNonEmpty(parties.Text(partyElement, "Nm"), parties.Text(partyElement, "Pty", "Nm"));
        var account = FirstNonEmpty(parties.Text(accountElement, "Id", "IBAN"), parties.Text(accountElement, "Id", "Othr", "Id"));
        return name.Length == 0 && account.Length == 0 ? null : new Party(name, account);
    }

    private static BankTransactionCode? ParseBankTxCode(XElement? code)
    {
        if (code is null)
        {
            return null;
        }

        return new BankTransactionCode(
            code.Text("Domn", "Cd"),
            code.Text("Domn", "Fmly", "Cd"),
            code.Text("Domn", "Fmly", "SubFmlyCd"),
            code.Text("Prtry", "Cd"),
            code.Text("Prtry", "Issr"));
    }

    private static CreditDebit ParseIndicator(string value) => value switch
    {
        "CRDT" => CreditDebit.Credit,
        "DBIT" => CreditDebit.Debit,
        _ => CreditDebit.Unknown,
    };

    private static decimal? ParseDecimal(string? value) =>
        decimal.TryParse(value?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static int? ParseInt(string? value) =>
        int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? "";

    private static XElement? Child(this XElement? element, params string[] path)
    {
        var current = element;
        foreach (var name in path)
        {
            current = current?.Elements().FirstOrDefault(e => e.Name.LocalName == name);
        }

        return current;
    }

    private static IEnumerable<XElement> Children(this XElement? element, string name) =>
        element?.Elements().Where(e => e.Name.LocalName == name) ?? [];

    private static string Text(this XElement? element, params string[] path) =>
        element.Child(path)?.Value.Trim() ?? "";
}
