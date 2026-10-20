using XMLToStatement;

return Cli.Run(args, Console.Out, Console.Error);

public static class Cli
{
    public const int Success = 0;
    public const int UsageError = 1;
    public const int ProcessingError = 2;
    public const int ChecksFailed = 3;

    private const string Usage = """
        XMLToStatement - convert ISO 20022 camt.053 (also camt.052/054) bank statement XML to a readable HTML statement.

        Usage:
          XMLToStatement <input.xml | input-folder> [options]

        Options:
          -o, --output <path>           Output .html file, or folder (default: next to input, same name with .html)
          -m, --map <file>              Optional account map file: 'external<TAB|,|;>internal' per line
              --map-label <text>        Column label for mapped account (default: "Mapped account number")
          -t, --title <text>            Report title (default: "Bank Statement")
              --account-format <fmt>    auto | nz | au | none (default: auto, based on bank BIC country)
              --strict                  Exit with code 3 if any integrity check fails
          -h, --help                    Show this help

        Exit codes: 0 success, 1 usage error, 2 processing error, 3 integrity checks failed (--strict)
        """;

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        string? input = null, output = null, mapPath = null, title = null, mapLabel = null;
        var format = AccountFormat.Auto;
        var strict = false;

        try
        {
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-h" or "--help" or "/?":
                        stdout.WriteLine(Usage);
                        return Success;
                    case "-o" or "--output":
                        output = NextValue(args, ref i);
                        break;
                    case "-m" or "--map":
                        mapPath = NextValue(args, ref i);
                        break;
                    case "--map-label":
                        mapLabel = NextValue(args, ref i);
                        break;
                    case "-t" or "--title":
                        title = NextValue(args, ref i);
                        break;
                    case "--account-format":
                        var value = NextValue(args, ref i);
                        if (!Enum.TryParse(value, ignoreCase: true, out format) || !Enum.IsDefined(format))
                        {
                            throw new ArgumentException($"Invalid --account-format '{value}'. Use auto, nz, au or none.");
                        }

                        break;
                    case "--strict":
                        strict = true;
                        break;
                    default:
                        if (args[i].StartsWith('-') || input is not null)
                        {
                            throw new ArgumentException($"Unexpected argument '{args[i]}'.");
                        }

                        input = args[i];
                        break;
                }
            }

            if (input is null)
            {
                throw new ArgumentException("No input file or folder specified.");
            }
        }
        catch (ArgumentException ex)
        {
            stderr.WriteLine($"Error: {ex.Message}");
            stderr.WriteLine();
            stderr.WriteLine(Usage);
            return UsageError;
        }

        AccountMap? map = null;
        if (mapPath is not null)
        {
            try
            {
                map = AccountMap.Load(mapPath);
                stdout.WriteLine($"Loaded {map.Count} account map entries from {mapPath}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
            {
                stderr.WriteLine($"Error: cannot load account map '{mapPath}': {ex.Message}");
                return ProcessingError;
            }
        }

        List<(string Input, string Output)> jobs;
        if (Directory.Exists(input))
        {
            var outDir = output ?? input;
            if (File.Exists(outDir))
            {
                stderr.WriteLine("Error: when the input is a folder, --output must be a folder.");
                return UsageError;
            }

            jobs = Directory.GetFiles(input, "*.xml")
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .Select(f => (f, Path.Combine(outDir, Path.GetFileNameWithoutExtension(f) + ".html")))
                .ToList();
            if (jobs.Count == 0)
            {
                stderr.WriteLine($"Error: no .xml files found in '{input}'.");
                return ProcessingError;
            }
        }
        else if (File.Exists(input))
        {
            jobs = [(input, ResolveOutputFile(input, output))];
        }
        else
        {
            stderr.WriteLine($"Error: input '{input}' does not exist.");
            return UsageError;
        }

        var errors = 0;
        var failedChecks = 0;
        foreach (var (inFile, outFile) in jobs)
        {
            try
            {
                var file = Camt053Parser.Load(inFile);
                var options = new RenderOptions
                {
                    Title = title ?? "Bank Statement",
                    SourceFileName = Path.GetFileName(inFile),
                    AccountFormat = format,
                    AccountMap = map,
                    AccountMapLabel = mapLabel ?? "Mapped account number",
                };

                var dir = Path.GetDirectoryName(Path.GetFullPath(outFile));
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(outFile, HtmlStatementRenderer.Render(file, options));

                var results = file.Statements
                    .SelectMany(s => StatementChecks.Run(s, map).Select(c => (s.Account.Identifier, Check: c)))
                    .ToList();
                var failed = results.Where(r => !r.Check.Passed).ToList();
                failedChecks += failed.Count;

                stdout.WriteLine($"{Path.GetFileName(inFile)} -> {outFile}");
                stdout.WriteLine($"  {file.MessageType}, message {file.MessageId}, {file.Statements.Count} account(s), " +
                                 $"{file.Statements.Sum(s => s.Entries.Count)} entries, checks: {results.Count - failed.Count} passed, {failed.Count} failed");
                foreach (var (account, check) in failed)
                {
                    stdout.WriteLine($"  FAIL [{account}] {check.Name}: {check.Detail}");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Xml.XmlException)
            {
                errors++;
                stderr.WriteLine($"Error processing '{inFile}': {ex.Message}");
            }
        }

        if (errors > 0)
        {
            return ProcessingError;
        }

        return strict && failedChecks > 0 ? ChecksFailed : Success;
    }

    private static string ResolveOutputFile(string input, string? output)
    {
        var defaultName = Path.GetFileNameWithoutExtension(input) + ".html";
        if (output is null)
        {
            return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(input))!, defaultName);
        }

        var looksLikeFolder = Directory.Exists(output)
            || output.EndsWith(Path.DirectorySeparatorChar)
            || output.EndsWith(Path.AltDirectorySeparatorChar);
        return looksLikeFolder ? Path.Combine(output, defaultName) : output;
    }

    private static string NextValue(string[] args, ref int i)
    {
        if (i + 1 >= args.Length)
        {
            throw new ArgumentException($"Option '{args[i]}' requires a value.");
        }

        return args[++i];
    }
}
