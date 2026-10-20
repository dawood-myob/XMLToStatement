using XMLToStatement;

namespace XMLToStatement.Tests;

public class ParserAndRendererTests
{
    private static readonly string SamplePath = Path.Combine(AppContext.BaseDirectory, "samples", "sample-camt053-nz.xml");

    [Fact]
    public void Parses_sample_header_accounts_balances_and_entries()
    {
        var file = Camt053Parser.Load(SamplePath);

        Assert.Equal("camt.053.001.02", file.MessageType);
        Assert.Equal("01012026010101", file.MessageId);
        Assert.Equal("EODY", file.AdditionalInfo);
        Assert.Equal(2, file.Statements.Count);

        var s = file.Statements[0];
        Assert.Equal("030001001234500", s.Account.Identifier);
        Assert.Equal("WPACNZ2W", s.Account.Bic);
        Assert.Equal("NZD", s.Account.Currency);
        Assert.Equal(1000.00m, s.OpeningBalance!.SignedAmount);
        Assert.Equal(1075.50m, s.ClosingBalance!.SignedAmount);
        Assert.Equal(2, s.Entries.Count);
        Assert.Equal(100.00m, s.CreditTotal);
        Assert.Equal(24.50m, s.DebitTotal);

        var credit = s.Entries[0];
        Assert.Equal("BP50", credit.BankTransactionCode.ProprietaryCode);
        Assert.Equal("SMITH & CO", credit.Details[0].Debtor!.Name);
        Assert.Contains(credit.Details[0].References, r => r.Key == "Add.Ref" && r.Value == "12345");
        Assert.Equal("WESTPAC FEES", s.Entries[1].Details[0].Creditor!.Name);
    }

    [Fact]
    public void All_checks_pass_for_balanced_sample()
    {
        var file = Camt053Parser.Load(SamplePath);
        var map = AccountMap.Parse(File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "samples", "sample-account-map.txt")));

        var checks = file.Statements.SelectMany(s => StatementChecks.Run(s, map)).ToList();

        Assert.NotEmpty(checks);
        Assert.All(checks, c => Assert.True(c.Passed, $"{c.Name}: {c.Detail}"));
    }

    [Fact]
    public void Detects_closing_balance_mismatch()
    {
        var xml = File.ReadAllText(SamplePath).Replace("<Amt Ccy=\"NZD\">1075.50</Amt>", "<Amt Ccy=\"NZD\">1075.51</Amt>");
        var file = Camt053Parser.ParseXml(xml);

        var checks = StatementChecks.Run(file.Statements[0]);

        Assert.Contains(checks, c => c.Name.StartsWith("Opening") && !c.Passed);
    }

    [Fact]
    public void Reports_unmapped_account()
    {
        var file = Camt053Parser.Load(SamplePath);
        var map = AccountMap.Parse(["030001001234500\t001234500"]);

        var checks = StatementChecks.Run(file.Statements[1], map);

        Assert.Contains(checks, c => c.Name.Contains("account map") && !c.Passed);
    }

    [Fact]
    public void Parses_newer_version_with_iban_and_nested_party()
    {
        const string xml = """
            <Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.053.001.08">
              <BkToCstmrStmt>
                <GrpHdr><MsgId>M1</MsgId><CreDtTm>2026-01-01T00:00:00</CreDtTm></GrpHdr>
                <Stmt>
                  <Id>S1</Id>
                  <Acct>
                    <Id><IBAN>GB29NWBK60161331926819</IBAN></Id>
                    <Ccy>GBP</Ccy>
                    <Ownr><Nm>Owner Ltd</Nm></Ownr>
                    <Svcr><FinInstnId><BICFI>NWBKGB2L</BICFI></FinInstnId></Svcr>
                  </Acct>
                  <Bal><Tp><CdOrPrtry><Cd>PRCD</Cd></CdOrPrtry></Tp><Amt Ccy="GBP">10.00</Amt><CdtDbtInd>DBIT</CdtDbtInd><Dt><Dt>2026-01-01</Dt></Dt></Bal>
                  <Bal><Tp><CdOrPrtry><Cd>CLBD</Cd></CdOrPrtry></Tp><Amt Ccy="GBP">5.00</Amt><CdtDbtInd>CRDT</CdtDbtInd><Dt><Dt>2026-01-01</Dt></Dt></Bal>
                  <Ntry>
                    <Amt Ccy="GBP">15.00</Amt><CdtDbtInd>CRDT</CdtDbtInd><Sts><Cd>BOOK</Cd></Sts>
                    <BookgDt><Dt>2026-01-01</Dt></BookgDt>
                    <NtryDtls><TxDtls>
                      <RltdPties><Dbtr><Pty><Nm>Payer Plc</Nm></Pty></Dbtr><DbtrAcct><Id><IBAN>DE89370400440532013000</IBAN></Id></DbtrAcct></RltdPties>
                      <RmtInf><Ustrd>Invoice 42</Ustrd></RmtInf>
                    </TxDtls></NtryDtls>
                  </Ntry>
                </Stmt>
              </BkToCstmrStmt>
            </Document>
            """;

        var file = Camt053Parser.ParseXml(xml);
        var s = file.Statements.Single();

        Assert.Equal("camt.053.001.08", file.MessageType);
        Assert.True(s.Account.IsIban);
        Assert.Equal("NWBKGB2L", s.Account.Bic);
        Assert.Equal("Owner Ltd", s.Account.OwnerName);
        Assert.Equal(-10.00m, s.OpeningBalance!.SignedAmount);
        Assert.Equal("BOOK", s.Entries[0].Status);
        Assert.Equal("Payer Plc", s.Entries[0].Details[0].Debtor!.Name);
        Assert.Equal("DE89370400440532013000", s.Entries[0].Details[0].Debtor!.Account);
        Assert.Equal(["Invoice 42"], s.Entries[0].Details[0].RemittanceInfo);
        Assert.All(StatementChecks.Run(s), c => Assert.True(c.Passed, c.Detail));

        var html = HtmlStatementRenderer.Render(file);
        Assert.Contains("GB29 NWBK 6016 1331 9268 19", html);
    }

    [Theory]
    [InlineData("")]
    [InlineData("urn:example:import-log")]
    public void Rejects_import_processing_log_with_actionable_error(string xmlNamespace)
    {
        var xml = $"""
            <File xmlns="{xmlNamespace}">
              <Props><Prop name="omitted.import.lines.count"><Value>0</Value></Prop></Props>
              <Imports><Import><HeaderLine><Ref>Import number: 1</Ref></HeaderLine><Lines/></Import></Imports>
            </File>
            """;

        var error = Assert.Throws<InvalidDataException>(() => Camt053Parser.ParseXml(xml));

        Assert.Contains("import-processing log", error.Message);
        Assert.Contains("Select the original ISO 20022", error.Message);
        Assert.Contains("contains no bank statement data", error.Message);
    }

    [Fact]
    public void Rejects_non_camt_xml()
    {
        var error = Assert.Throws<InvalidDataException>(() => Camt053Parser.ParseXml("<root><a/></root>"));

        Assert.Contains("Not an ISO 20022 cash management file", error.Message);
    }

    [Fact]
    public void Rejects_dtd_to_prevent_entity_expansion()
    {
        const string xml = "<!DOCTYPE d [<!ENTITY x \"y\">]><Document><BkToCstmrStmt/></Document>";
        Assert.Throws<System.Xml.XmlException>(() => Camt053Parser.ParseXml(xml));
    }

    [Theory]
    [InlineData("030802018336300", "WPACNZ2W", AccountFormat.Auto, "03-0802-0183363-00")]
    [InlineData("0308020183363001", "WPACNZ2W", AccountFormat.Auto, "03-0802-0183363-001")]
    [InlineData("032001123456", "WPACAU2S", AccountFormat.Auto, "032-001 123456")]
    [InlineData("12345678", "DEUTDEFF", AccountFormat.Auto, "12345678")]
    [InlineData("030802018336300", "WPACNZ2W", AccountFormat.None, "030802018336300")]
    public void Formats_account_numbers(string id, string bic, AccountFormat format, string expected)
    {
        Assert.Equal(expected, AccountNumberFormatter.Format(id, bic, isIban: false, format));
    }

    [Fact]
    public void Account_map_supports_tabs_commas_and_values_with_spaces()
    {
        var map = AccountMap.Parse(["# comment", "", "030502001756684\t\t00017566 0084", "A,B", "C;D"]);

        Assert.Equal(3, map.Count);
        Assert.Equal("00017566 0084", map.Lookup("030502001756684"));
        Assert.Equal("B", map.Lookup("A"));
        Assert.Equal("D", map.Lookup("C"));
        Assert.Throws<FormatException>(() => AccountMap.Parse(["onlyonevalue"]));
    }

    [Fact]
    public void Html_is_encoded_and_contains_running_balance()
    {
        var file = Camt053Parser.Load(SamplePath);

        var html = HtmlStatementRenderer.Render(file, new RenderOptions { Title = "T <x>" });

        Assert.Contains("SMITH &amp; CO", html);
        Assert.Contains("T &lt;x&gt;", html);
        Assert.DoesNotContain("<x>", html);
        Assert.Contains("1,075.50", html);
        Assert.Contains("03-0001-0012345-00", html);
        Assert.Contains("No transactions for this period.", html);
    }

    [Fact]
    public void Cli_writes_output_and_returns_success()
    {
        var outFile = Path.Combine(Path.GetTempPath(), $"xmltostatement-{Guid.NewGuid():N}.html");
        try
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var code = Cli.Run([SamplePath, "-o", outFile, "--strict"], stdout, stderr);

            Assert.Equal(Cli.Success, code);
            Assert.True(File.Exists(outFile));
            Assert.Contains("0 failed", stdout.ToString());
            Assert.Equal("", stderr.ToString());
        }
        finally
        {
            File.Delete(outFile);
        }
    }

    [Fact]
    public void Cli_rejects_import_log_without_writing_statement()
    {
        var dir = Directory.CreateTempSubdirectory("xmltostatement-");
        try
        {
            var input = Path.Combine(dir.FullName, "PostProcessing-pp-data.xml");
            var output = Path.Combine(dir.FullName, "statement.html");
            File.WriteAllText(input, "<File><Props/><Imports><Import><Lines/></Import></Imports></File>");
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var code = Cli.Run([input, "-o", output], stdout, stderr);

            Assert.Equal(Cli.ProcessingError, code);
            Assert.False(File.Exists(output));
            Assert.Equal("", stdout.ToString());
            Assert.Contains($"Error processing '{input}'", stderr.ToString());
            Assert.Contains("import-processing log", stderr.ToString());
            Assert.Contains("Select the original ISO 20022", stderr.ToString());
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Cli_strict_returns_checks_failed_when_unmapped()
    {
        var dir = Directory.CreateTempSubdirectory("xmltostatement-");
        try
        {
            var mapFile = Path.Combine(dir.FullName, "map.txt");
            File.WriteAllText(mapFile, "999\t1\n");

            var code = Cli.Run([SamplePath, "-o", dir.FullName + Path.DirectorySeparatorChar, "-m", mapFile, "--strict"],
                new StringWriter(), new StringWriter());

            Assert.Equal(Cli.ChecksFailed, code);
            Assert.True(File.Exists(Path.Combine(dir.FullName, "sample-camt053-nz.html")));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData]
    [InlineData("--bogus")]
    [InlineData("x.xml", "--account-format", "xx")]
    [InlineData("x.xml", "-o")]
    public void Cli_returns_usage_error_for_bad_arguments(params string[] args)
    {
        Assert.Equal(Cli.UsageError, Cli.Run(args, new StringWriter(), new StringWriter()));
    }
}
