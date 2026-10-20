namespace XMLToStatement;

/// <summary>
/// Human-readable descriptions for ISO 20022 codes (ExternalBankTransactionDomain/Family/SubFamily
/// and balance types). Unknown codes are shown as-is.
/// </summary>
public static class CodeDescriptions
{
    public static readonly IReadOnlyDictionary<string, string> Domains = new Dictionary<string, string>
    {
        ["ACMT"] = "Account Management",
        ["CAMT"] = "Cash Management",
        ["CMDT"] = "Commodities",
        ["DERV"] = "Derivatives",
        ["FORX"] = "Foreign Exchange",
        ["LDAS"] = "Loans, Deposits & Syndications",
        ["PMET"] = "Precious Metal",
        ["PMNT"] = "Payments",
        ["SECU"] = "Securities",
        ["TRAD"] = "Trade Services",
        ["XTND"] = "Extended Domain",
    };

    public static readonly IReadOnlyDictionary<string, string> Families = new Dictionary<string, string>
    {
        ["ACCB"] = "Account Balancing",
        ["CCRD"] = "Customer Card Transactions",
        ["CNTR"] = "Counter Transactions",
        ["DRFT"] = "Drafts / Bill of Orders",
        ["ICCN"] = "Issued Cash Concentration",
        ["ICDT"] = "Issued Credit Transfers",
        ["ICHQ"] = "Issued Cheques",
        ["IDDT"] = "Issued Direct Debits",
        ["IRCT"] = "Issued Real Time Credit Transfers",
        ["LBOX"] = "Lockbox Transactions",
        ["MCOP"] = "Miscellaneous Credit Operations",
        ["MCRD"] = "Merchant Card Transactions",
        ["MDOP"] = "Miscellaneous Debit Operations",
        ["NTAV"] = "Not Available",
        ["OTHR"] = "Other",
        ["RCCN"] = "Received Cash Concentration",
        ["RCDT"] = "Received Credit Transfers",
        ["RCHQ"] = "Received Cheques",
        ["RDDT"] = "Received Direct Debits",
        ["RRCT"] = "Received Real Time Credit Transfers",
        ["SETT"] = "Trade, Clearing and Settlement",
    };

    public static readonly IReadOnlyDictionary<string, string> SubFamilies = new Dictionary<string, string>
    {
        ["ACDT"] = "ACH Credit",
        ["ADJT"] = "Adjustments",
        ["ARET"] = "ACH Return",
        ["ASET"] = "ACH Settlement",
        ["ATXN"] = "ACH Transaction",
        ["AUTT"] = "Automatic Transfer",
        ["BCDP"] = "Branch Cheque Deposit",
        ["BCHQ"] = "Bank Cheque",
        ["BOOK"] = "Internal Book Transfer",
        ["CAJT"] = "Credit Adjustments",
        ["CHRG"] = "Charges",
        ["COMM"] = "Commission",
        ["CQRV"] = "Cheque Reversal",
        ["DAJT"] = "Debit Adjustments",
        ["DMCT"] = "Domestic Credit Transfer",
        ["ESCT"] = "SEPA Credit Transfer",
        ["FEES"] = "Fees",
        ["INTR"] = "Interest",
        ["LBDP"] = "Lockbox Deposit",
        ["NTAV"] = "Not Available",
        ["OTHR"] = "Other",
        ["PMDD"] = "Direct Debit Payment",
        ["RIMB"] = "Reimbursement",
        ["RPCR"] = "Reversal due to Payment Cancellation Request",
        ["RRTN"] = "Reversal due to Return",
        ["SALA"] = "Payroll / Salary Payment",
        ["SDVA"] = "Same Day Value Credit Transfer",
        ["STDO"] = "Standing Order",
        ["TAXE"] = "Taxes",
        ["UPCQ"] = "Unpaid Cheque",
        ["UPDD"] = "Unpaid Direct Debit",
        ["XBCT"] = "Cross-Border Credit Transfer",
    };

    public static readonly IReadOnlyDictionary<string, string> BalanceTypes = new Dictionary<string, string>
    {
        ["CLAV"] = "Closing Available Balance",
        ["CLBD"] = "Closing Booked Balance",
        ["FWAV"] = "Forward Available Balance",
        ["INFO"] = "Information Balance",
        ["ITAV"] = "Interim Available Balance",
        ["ITBD"] = "Interim Booked Balance",
        ["OPAV"] = "Opening Available Balance",
        ["OPBD"] = "Opening Booked Balance",
        ["PRCD"] = "Previously Closed Booked Balance",
        ["XPCD"] = "Expected Balance",
    };

    public static readonly IReadOnlyDictionary<string, string> AdditionalInfo = new Dictionary<string, string>
    {
        ["EODY"] = "End of Day",
        ["INTR"] = "Intraday",
    };

    public static string Describe(IReadOnlyDictionary<string, string> table, string code) =>
        table.TryGetValue(code, out var description) ? description : code;

    public static string WithDescription(IReadOnlyDictionary<string, string> table, string code) =>
        table.TryGetValue(code, out var description) ? $"{code} ({description})" : code;
}
