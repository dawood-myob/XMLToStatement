# XMLToStatement

Converts ISO 20022 bank-to-customer cash management XML (camt.053 statements, and also camt.052 /
camt.054) into a readable, printable HTML bank statement with reconciliation checks.

The parser matches elements by local name, so it works with any camt message version
(e.g. `camt.053.001.02` as used by Westpac NZ/AU, through `camt.053.001.08+`), BBAN (`Othr/Id`) or IBAN accounts.

## Output

For each file:

- Group header (message id, creation time, statement type) and an index of all accounts.
- Per account: account details (raw, formatted, optional mapped number), BIC, statement id, sequence
  number and period; balances; the bank-reported transaction summary; every entry with booking/value
  date, entry reference, status, transaction code (proprietary + ISO domain/family/sub-family with
  descriptions), payer/payee and account, all references, remittance/narrative, debit/credit and a
  running balance.
- Integrity checks per account:
  - Opening balance + net movement = closing balance
  - Entry count, credit and debit count/sum match `TxsSummry`
  - Net movement matches `TxsSummry`
  - All entries in the account currency
  - Account present in the account map (only when `--map` is supplied)

## Build

Requires the .NET 10 SDK.

```powershell
dotnet build
dotnet test
```

## Usage

### Windows Forms application

On Windows, start the form with:

```powershell
dotnet run --project src\XMLToStatement.WinForms
```

Use **Browse...** to select an input XML file and an output folder, then click **Convert**. The HTML report is saved in the selected folder with the same base name as the XML file. Conversion results and errors appear in the window.

In Visual Studio, set **XMLToStatement.WinForms** as the startup project to launch the form.

### Web application (Docker / Kubernetes)

A lightweight ASP.NET Core minimal API (`src\XMLToStatement.Web`) with an upload page: choose the XML,
click **Convert and download**, and the HTML statement is downloaded. Optional settings (title, account
format, account map file) are under **Options**. Files are processed in memory only; nothing is stored
or logged, and any file content is accepted regardless of extension (it is validated by parsing).

```powershell
dotnet run --project src\XMLToStatement.Web --urls http://localhost:8080
```

Endpoints: `GET /` (upload page), `POST /convert` (multipart field `file`, optional `map`, `title`,
`accountFormat`, `mapLabel`; returns the HTML as an attachment, or `400` with a message), `GET /healthz`.
The maximum upload size defaults to 50 MB and can be changed with the `MAX_UPLOAD_MB` environment variable.

```powershell
# Command-line example
curl.exe -F "file=@samples\sample-camt053-nz.xml" -o statement.html http://localhost:8080/convert
```

Build the image (from the repo root) and deploy to Kubernetes:

```powershell
docker build -t your-registry.example.com/xml-to-statement-web:1.0.0 .
docker push your-registry.example.com/xml-to-statement-web:1.0.0
# edit the image name in deploy\k8s\xml-to-statement.yaml, then:
kubectl apply -f deploy\k8s\xml-to-statement.yaml
kubectl port-forward svc/xml-to-statement 8080:80   # quick test without an ingress
# optional: edit the host in deploy\k8s\ingress.yaml, then:
kubectl apply -f deploy\k8s\ingress.yaml
```

The container runs as a non-root user (UID 1654) on port 8080 with a read-only root filesystem and
`/healthz` readiness/liveness probes. The web app has no authentication; expose it only inside your
network or put it behind an authenticating ingress/proxy, as statements contain sensitive data.

### Command-line application

```powershell
dotnet run --project src\XMLToStatement -- <input.xml | input-folder> [options]
```

| Option | Description |
|---|---|
| `-o, --output <path>` | Output `.html` file or folder. Default: next to the input with the same name and `.html`. |
| `-m, --map <file>` | Optional account map: `external<TAB\|,\|;>internal` per line, `#` for comments. |
| `--map-label <text>` | Label for the mapped account column (default `Mapped account number`). |
| `-t, --title <text>` | Report title (default `Bank Statement`). |
| `--account-format <fmt>` | `auto` (from BIC country), `nz`, `au` or `none`. |
| `--strict` | Exit with code 3 if any integrity check fails (useful in automation). |

Exit codes: `0` success, `1` usage error, `2` processing error, `3` integrity checks failed (`--strict`).

### Examples

```powershell
# Single file, output next to it
dotnet run --project src\XMLToStatement -- samples\sample-camt053-nz.xml

# With a Workday-style account map and a custom title
dotnet run --project src\XMLToStatement -- samples\sample-camt053-nz.xml `
  -o out\statement.html -m samples\sample-account-map.txt `
  --map-label "Workday account number" -t "Westpac NZ Bank Statement" --strict

# Every *.xml in a folder
dotnet run --project src\XMLToStatement -- C:\statements -o C:\statements\html
```

To produce a standalone executable:

```powershell
dotnet publish src\XMLToStatement -c Release -r win-x64 --self-contained false -o publish
publish\XMLToStatement.exe input.xml
```

## Account number formatting

| Format | Rule | Example |
|---|---|---|
| NZ | 15/16 digits → bank-branch-account-suffix | `030802018336300` → `03-0802-0183363-00` |
| AU | BSB + account | `032001123456` → `032-001 123456` |
| IBAN | groups of 4 | `GB29 NWBK 6016 1331 9268 19` |

`auto` picks NZ/AU from characters 5–6 of the servicer BIC (e.g. `WPACNZ2W` → NZ).

## Notes

- Select the original bank statement XML, not an import-processing log such as
  `PostProcessing-pp-data.xml`. Logs with a `<File><Imports>` structure contain import status,
  not bank statement data, and cannot be converted into a statement. The original statement
  contains `BkToCstmrStmt`, `BkToCstmrAcctRpt` or `BkToCstmrDbtCdtNtfctn`.
- Bank proprietary transaction codes (e.g. Westpac NZ `AP50`, `BP50`, `DC50`) are shown as supplied.
- DTD processing is disabled when reading XML.
- Statement files contain sensitive financial data; generated HTML should be handled accordingly.
  Sample files in `samples\` are synthetic.

## Project layout

```
src/XMLToStatement/
  Program.cs                 CLI
  Camt053Parser.cs           XML -> model (version-agnostic)
  Models.cs                  Statement model records
  StatementChecks.cs         Reconciliation checks
  HtmlStatementRenderer.cs   Model -> HTML
  AccountNumberFormatter.cs  NZ/AU/IBAN display formatting
  AccountMap.cs              Optional external -> internal account map
  CodeDescriptions.cs        ISO code descriptions
src/XMLToStatement.WinForms/ Windows Forms app
src/XMLToStatement.Web/      Web upload/download app (Dockerfile at repo root)
deploy/k8s/                  Kubernetes Deployment, Service and Ingress manifests
tests/XMLToStatement.Tests/  xUnit tests
samples/                     Synthetic sample statement and map
```
