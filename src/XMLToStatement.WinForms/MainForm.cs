using System.Drawing;
using XMLToStatement;

namespace XMLToStatement.WinForms;

internal sealed class MainForm : Form
{
    private readonly TextBox inputPath = new() { Dock = DockStyle.Fill };
    private readonly TextBox outputFolder = new() { Dock = DockStyle.Fill };
    private readonly TextBox log = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        WordWrap = false,
    };
    private readonly Button convertButton = new() { Text = "Convert", AutoSize = true };

    public MainForm()
    {
        Text = "XML to Statement Converter";
        MinimumSize = new Size(640, 360);
        StartPosition = FormStartPosition.CenterScreen;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 3,
            RowCount = 5,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var instructions = new Label
        {
            Text = "Choose a camt XML statement and the folder where its HTML report should be saved.",
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 10),
        };
        layout.Controls.Add(instructions, 0, 0);
        layout.SetColumnSpan(instructions, 3);

        layout.Controls.Add(new Label { Text = "Input XML:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        layout.Controls.Add(inputPath, 1, 1);
        var browseInput = new Button { Text = "Browse...", AutoSize = true };
        browseInput.Click += BrowseInput;
        layout.Controls.Add(browseInput, 2, 1);

        layout.Controls.Add(new Label { Text = "Output folder:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        layout.Controls.Add(outputFolder, 1, 2);
        var browseOutput = new Button { Text = "Browse...", AutoSize = true };
        browseOutput.Click += BrowseOutput;
        layout.Controls.Add(browseOutput, 2, 2);

        layout.Controls.Add(log, 0, 3);
        layout.SetColumnSpan(log, 3);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
        };
        convertButton.Click += async (_, _) => await ConvertFileAsync();
        buttonPanel.Controls.Add(convertButton);
        layout.Controls.Add(buttonPanel, 0, 4);
        layout.SetColumnSpan(buttonPanel, 3);
        Controls.Add(layout);

        AcceptButton = convertButton;
    }

    private void BrowseInput(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select an input XML file",
            Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            inputPath.Text = dialog.FileName;
            if (string.IsNullOrWhiteSpace(outputFolder.Text))
            {
                outputFolder.Text = Path.GetDirectoryName(dialog.FileName) ?? "";
            }
        }
    }

    private void BrowseOutput(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the folder for the generated HTML statement",
            UseDescriptionForTitle = true,
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            outputFolder.Text = dialog.SelectedPath;
        }
    }

    private async Task ConvertFileAsync()
    {
        var input = inputPath.Text.Trim();
        var destination = outputFolder.Text.Trim();
        if (string.IsNullOrWhiteSpace(input) || !File.Exists(input))
        {
            MessageBox.Show(this, "Select an existing input XML file.", "Input required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(destination))
        {
            MessageBox.Show(this, "Select an output folder.", "Output folder required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        convertButton.Enabled = false;
        log.Clear();
        try
        {
            var result = await Task.Run(() =>
            {
                using var stdout = new StringWriter();
                using var stderr = new StringWriter();
                Directory.CreateDirectory(destination);
                var exitCode = Cli.Run([input, "--output", destination], stdout, stderr);
                return (ExitCode: exitCode, Output: stdout.ToString(), Error: stderr.ToString());
            });

            log.Text = result.Output + result.Error;
            if (result.ExitCode == Cli.Success)
            {
                MessageBox.Show(this, "Conversion completed.", "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(this, "Conversion failed. See the details in the window.", "Conversion error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Xml.XmlException or ArgumentException or NotSupportedException)
        {
            log.Text = ex.Message;
            MessageBox.Show(this, "Conversion failed. See the details in the window.", "Conversion error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            convertButton.Enabled = true;
        }
    }
}
