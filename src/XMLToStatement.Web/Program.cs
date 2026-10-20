using System.Xml;
using Microsoft.AspNetCore.Http.Features;
using XMLToStatement;

var builder = WebApplication.CreateBuilder(args);

// Uploads are processed in memory only; nothing is written to disk or logged.
var maxUploadMb = builder.Configuration.GetValue("MAX_UPLOAD_MB", 50);
var maxUploadBytes = maxUploadMb * 1024L * 1024L;
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = maxUploadBytes);
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = maxUploadBytes);

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/healthz", () => Results.Ok(new { status = "healthy" }));

app.MapPost("/convert", async (HttpRequest request, HttpResponse response) =>
{
    if (!request.HasFormContentType)
    {
        return Results.Text("Send the statement as multipart/form-data.", statusCode: 400);
    }

    IFormCollection form;
    try
    {
        form = await request.ReadFormAsync();
    }
    catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException or IOException)
    {
        return Results.Text($"The upload could not be read (maximum size is {maxUploadMb} MB).", statusCode: 400);
    }

    var upload = form.Files.GetFile("file");
    if (upload is null || upload.Length == 0)
    {
        return Results.Text("Select a camt.053 XML file to convert.", statusCode: 400);
    }

    try
    {
        AccountMap? map = null;
        var mapFile = form.Files.GetFile("map");
        if (mapFile is { Length: > 0 })
        {
            using var mapReader = new StreamReader(mapFile.OpenReadStream());
            var lines = new List<string>();
            while (await mapReader.ReadLineAsync() is { } line)
            {
                lines.Add(line);
            }

            map = AccountMap.Parse(lines);
        }

        var format = Enum.TryParse<AccountFormat>(form["accountFormat"], ignoreCase: true, out var f) && Enum.IsDefined(f)
            ? f
            : AccountFormat.Auto;

        StatementFile statements;
        await using (var stream = upload.OpenReadStream())
        {
            statements = Camt053Parser.Parse(stream);
        }

        var title = form["title"].ToString().Trim();
        var label = form["mapLabel"].ToString().Trim();
        var html = HtmlStatementRenderer.Render(statements, new RenderOptions
        {
            Title = title.Length > 0 ? title : "Bank Statement",
            SourceFileName = Path.GetFileName(upload.FileName),
            AccountFormat = format,
            AccountMap = map,
            AccountMapLabel = label.Length > 0 ? label : "Mapped account number",
        });

        var checks = statements.Statements.SelectMany(s => StatementChecks.Run(s, map)).ToList();
        var failed = checks.Count(c => !c.Passed);
        response.Headers["X-Conversion-Summary"] =
            $"{statements.Statements.Count} account(s), {statements.Statements.Sum(s => s.Entries.Count)} entries, " +
            $"checks: {checks.Count - failed} passed, {failed} failed";
        response.Headers.CacheControl = "no-store";

        var baseName = Path.GetFileNameWithoutExtension(upload.FileName);
        var downloadName = (string.IsNullOrWhiteSpace(baseName) ? "statement" : baseName) + ".html";
        return Results.File(System.Text.Encoding.UTF8.GetBytes(html), "text/html; charset=utf-8", downloadName);
    }
    catch (Exception ex) when (ex is InvalidDataException or XmlException or FormatException or ArgumentException)
    {
        return Results.Text($"Error processing '{Path.GetFileName(upload.FileName)}': {ex.Message}", statusCode: 400);
    }
}).DisableAntiforgery();

app.Run();
