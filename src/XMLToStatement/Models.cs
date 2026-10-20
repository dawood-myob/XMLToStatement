namespace XMLToStatement;

public enum CreditDebit
{
    Unknown,
    Credit,
    Debit,
}

public sealed record StatementFile(
    string MessageType,
    string MessageId,
    string CreatedAt,
    string AdditionalInfo,
    IReadOnlyList<AccountStatement> Statements);

public sealed record AccountInfo(
    string Identifier,
    bool IsIban,
    string Currency,
    string Name,
    string OwnerName,
    string Bic,
    string ServicerName);

public sealed record Balance(
    string Code,
    decimal Amount,
    string Currency,
    CreditDebit Indicator,
    string Date)
{
    public decimal SignedAmount => Indicator == CreditDebit.Debit ? -Amount : Amount;
}

public sealed record TransactionSummary(
    int? TotalCount,
    decimal? TotalSum,
    decimal? NetAmount,
    CreditDebit NetIndicator,
    int? CreditCount,
    decimal? CreditSum,
    int? DebitCount,
    decimal? DebitSum)
{
    public decimal? SignedNetAmount => NetAmount is null
        ? null
        : NetIndicator == CreditDebit.Debit ? -NetAmount : NetAmount;
}

public sealed record BankTransactionCode(
    string Domain,
    string Family,
    string SubFamily,
    string ProprietaryCode,
    string ProprietaryIssuer)
{
    public static readonly BankTransactionCode Empty = new("", "", "", "", "");
}

public sealed record Party(string Name, string Account);

public sealed record TransactionDetail(
    IReadOnlyList<KeyValuePair<string, string>> References,
    decimal? Amount,
    string AmountCurrency,
    Party? Debtor,
    Party? Creditor,
    IReadOnlyList<string> RemittanceInfo,
    string AdditionalInfo,
    BankTransactionCode? BankTransactionCode);

public sealed record Entry(
    string EntryReference,
    decimal Amount,
    string Currency,
    CreditDebit Indicator,
    string Status,
    string BookingDate,
    string ValueDate,
    string AccountServicerReference,
    string AdditionalEntryInfo,
    BankTransactionCode BankTransactionCode,
    IReadOnlyList<TransactionDetail> Details)
{
    public decimal SignedAmount => Indicator == CreditDebit.Debit ? -Amount : Amount;
}

public sealed record AccountStatement(
    string Id,
    string ElectronicSequenceNumber,
    string CreatedAt,
    string FromDateTime,
    string ToDateTime,
    AccountInfo Account,
    IReadOnlyList<Balance> Balances,
    TransactionSummary? Summary,
    IReadOnlyList<Entry> Entries)
{
    public Balance? FindBalance(params string[] codes) =>
        codes.Select(c => Balances.FirstOrDefault(b => b.Code == c)).FirstOrDefault(b => b is not null);

    public Balance? OpeningBalance => FindBalance("OPBD", "PRCD", "OPAV");

    public Balance? ClosingBalance => FindBalance("CLBD");

    public int CreditCount => Entries.Count(e => e.Indicator == CreditDebit.Credit);

    public int DebitCount => Entries.Count(e => e.Indicator == CreditDebit.Debit);

    public decimal CreditTotal => Entries.Where(e => e.Indicator == CreditDebit.Credit).Sum(e => e.Amount);

    public decimal DebitTotal => Entries.Where(e => e.Indicator == CreditDebit.Debit).Sum(e => e.Amount);

    public decimal NetMovement => CreditTotal - DebitTotal;
}

public sealed record CheckResult(string Name, bool Passed, string Detail);
