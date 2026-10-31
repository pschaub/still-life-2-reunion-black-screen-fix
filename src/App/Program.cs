using StillLife2Patcher.Core;
using System.Drawing.Drawing2D;

namespace StillLife2Patcher;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--validate-translations")
        { File.WriteAllText(args[1], I18n.Validate() + Environment.NewLine); return; }
        ApplicationConfiguration.Initialize();
        Application.SetColorMode(Appearance.IsDark(ThemeChoice.System) ? SystemColorMode.Dark : SystemColorMode.Classic);
        if (args.Length >= 4 && I18n.Languages.Any(x => x.Code == args[3])) I18n.Language = args[3];
        using var form = new MainForm();
        if (args.Length >= 5 && Enum.TryParse<ThemeChoice>(args[4], true, out var selectedTheme)) form.SetTheme(selectedTheme);
        if (args.Length >= 2 && args[0] == "--preview-warning")
        {
            bool save = args.Contains("--save");
            using var warning = new UntestedFileDialog(save ? @"C:\Games\GOG\Still Life 2\EXTDataSave\men008_94.sav" : @"D:\SteamLibrary\steamapps\common\Still life 2\SL2.exe", !args.Contains("--copy"));
            RenderPreview(warning, args[1]); return;
        }
        if (args.Length >= 2 && args[0] is "--render-preview" or "--preview-folder" or "--preview-demo" or "--preview-exe-demo" or "--preview-empty")
        {
            if (args.Length >= 3)
            {
                if (args[0] == "--preview-demo") form.PreviewDemo(args[2]);
                else if (args[0] == "--preview-exe-demo") form.PreviewExeDemo(args[2]);
                else if (args[0] == "--preview-folder") form.PreviewFolder(args[2]);
                else if (args[0] != "--preview-empty") form.InspectFile(args[2]);
            }
            if (args.Contains("--details")) form.ShowDetails();
            RenderPreview(form, args[1]); return;
        }
        Application.Run(form);
    }
    static void RenderPreview(Form form, string path)
    {
        // Parentless controls only: never show or activate a desktop window.
        var surface = form.Controls[0]; form.Controls.Remove(surface);
        using (surface)
        {
            surface.Font = form.Font; surface.BackColor = Appearance.Colors.Background; surface.Dock = DockStyle.None;
            surface.Size = new Size(form.ClientSize.Width - form.Padding.Horizontal, form.ClientSize.Height - form.Padding.Vertical);
            void Prepare(Control control)
            { control.CreateControl(); foreach (Control child in control.Controls) Prepare(child); control.PerformLayout(); }
            Prepare(surface); Prepare(surface);
            using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
            using (var canvas = Graphics.FromImage(bitmap)) canvas.Clear(Appearance.Colors.Background);
            surface.DrawToBitmap(bitmap, new Rectangle(new Point(form.Padding.Left, form.Padding.Top), surface.Size));
            using (var canvas = Graphics.FromImage(bitmap))
            using (var outline = new GraphicsPath())
            using (var pen = new Pen(Appearance.Colors.Border))
            {
                // Keep the real client padding and add a subtle documentation frame.
                const float radius = 12;
                float right = bitmap.Width - 1, bottom = bitmap.Height - 1;
                outline.AddArc(.5f, .5f, radius, radius, 180, 90);
                outline.AddArc(right - radius, .5f, radius, radius, 270, 90);
                outline.AddArc(right - radius, bottom - radius, radius, radius, 0, 90);
                outline.AddArc(.5f, bottom - radius, radius, radius, 90, 90);
                outline.CloseFigure(); canvas.SmoothingMode = SmoothingMode.AntiAlias;
                canvas.DrawPath(pen, outline);
            }
            bitmap.Save(path);
        }
    }
}

sealed partial class MainForm : Form
{
    readonly Label heading = new() { AutoSize = true };
    readonly Label intro = new() { Dock = DockStyle.Fill };
    readonly Label help = new() { Dock = DockStyle.Fill, ForeColor = Color.DimGray };
    readonly Label status = new() { Dock = DockStyle.Fill };
    readonly Label scanStatus = new() { Dock = DockStyle.Fill, ForeColor = Color.DimGray };
    readonly UiButton language = new() { Width = 165, Height = 32, Chevron = true };
    readonly ContextMenuStrip languageMenu = new();
    readonly RadioButton saveMode = new() { AutoSize = true, Checked = true };
    readonly RadioButton exeMode = new() { AutoSize = true };
    readonly TextBox file = new() { ReadOnly = true, BorderStyle = BorderStyle.None };
    readonly Panel fileBox = new() { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0) };
    readonly TextBox details = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.FromArgb(245, 247, 249), BorderStyle = BorderStyle.FixedSingle };
    readonly UiButton detailsToggle = new() { Width = 110, Height = 28, Borderless = true, Chevron = true, Anchor = AnchorStyles.Right, Margin = Padding.Empty };
    readonly Button browse = MakeButton(), auto = MakeButton(), folder = MakeButton(), cancel = MakeButton(), copy = MakeButton(), replace = MakeButton();
    readonly FlowLayoutPanel scanBar = new() { Dock = DockStyle.Fill, WrapContents = false };
    readonly DataGridView grid = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, BackgroundColor = Color.White, BorderStyle = BorderStyle.FixedSingle, AutoGenerateColumns = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
    readonly TableLayoutPanel root = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 11 };
    Inspection? inspection;
    string? fileError, selectedFolder;
    DiscoveryResult? discovery;
    CancellationTokenSource? scan;
    bool populating, detailsOpen, lastSaveMode = true;
    int layoutExtraHeight;
    static Button MakeButton() => new UiButton { AutoSize = true, Padding = new Padding(9, 4, 9, 4), Margin = new Padding(0, 0, 10, 0) };
    static string T(string key, params object[] args) => I18n.T(key, args);
    bool IsSave => saveMode.Checked;

    public MainForm()
    {
        Font = new Font("Segoe UI", 10); ClientSize = new Size(1020, 600); MinimumSize = new Size(980, 450);
        StartPosition = FormStartPosition.CenterScreen; BackColor = Color.White; Padding = new Padding(24); AllowDrop = true;
        heading.Font = new Font(Font.FontFamily, 20, FontStyle.Bold);
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (float height in new[] { 48f, 36f, 38f, 34f, 38f, 48f, 28f }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        foreach (float height in new[] { 36f, 0f, 44f }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 172)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
        header.Controls.Add(heading, 0, 0); header.Controls.Add(language, 1, 0); header.Controls.Add(theme, 2, 0); root.Controls.Add(header, 0, 0);
        root.Controls.Add(intro, 0, 1);
        var modes = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        saveMode.Margin = new Padding(0, 3, 24, 3); modes.Controls.Add(saveMode); modes.Controls.Add(exeMode); root.Controls.Add(modes, 0, 2);
        root.Controls.Add(help, 0, 3);
        var picker = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        picker.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); picker.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        picker.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        fileBox.Controls.Add(file);
        fileBox.Layout += (_, _) =>
        {
            int inset = (int)(9 * fileBox.DeviceDpi / 96f);
            file.SetBounds(inset, Math.Max(0, (fileBox.ClientSize.Height - file.PreferredHeight) / 2), Math.Max(0, fileBox.ClientSize.Width - inset * 2), file.PreferredHeight);
        };
        file.FontChanged += (_, _) => fileBox.PerformLayout();
        fileBox.Paint += (_, e) => { using var edge = new Pen(Appearance.Colors.Border); e.Graphics.DrawRectangle(edge, 0, 0, fileBox.Width - 1, fileBox.Height - 1); };
        browse.AutoSize = false; browse.Dock = DockStyle.Fill; browse.Margin = Padding.Empty;
        picker.Controls.Add(fileBox, 0, 0); picker.Controls.Add(browse, 1, 0); root.Controls.Add(picker, 0, 4);
        scanBar.Controls.Add(auto); scanBar.Controls.Add(folder); scanBar.Controls.Add(cancel); root.Controls.Add(scanBar, 0, 5);
        root.Controls.Add(scanStatus, 0, 6); root.Controls.Add(grid, 0, 7);
        var summary = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); summary.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        status.TextAlign = ContentAlignment.MiddleLeft;
        summary.Controls.Add(status, 0, 0); summary.Controls.Add(detailsToggle, 1, 0); root.Controls.Add(summary, 0, 8);
        root.Controls.Add(details, 0, 9);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        ((UiButton)replace).Primary = true;
        actions.Controls.Add(replace); actions.Controls.Add(copy); root.Controls.Add(actions, 0, 10);
        Controls.Add(root);
        foreach (var (name, weight) in new[] { ("file", 18f), ("modified", 20f), ("status", 29f), ("location", 33f) })
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.DefaultCellStyle.Padding = new Padding(3); grid.RowTemplate.Height = 28;
        grid.ColumnHeadersHeight = 34;
        foreach (var item in I18n.Languages)
        {
            var option = new ToolStripMenuItem(item.Name) { Tag = item.Code };
            option.Click += (_, _) => { I18n.Language = item.Code; Translate(); };
            languageMenu.Items.Add(option);
        }
        language.Click += (_, _) => languageMenu.Show(language, new Point(0, language.Height));
        detailsToggle.Click += (_, _) => { detailsOpen = !detailsOpen; LayoutDetails(); };
        Disposed += (_, _) => languageMenu.Dispose();
        saveMode.CheckedChanged += (_, _) => { inspection = null; fileError = null; file.Clear(); grid.ClearSelection(); Translate(); };
        browse.Click += (_, _) =>
        {
            using var d = new OpenFileDialog { Filter = T(IsSave ? "filterSave" : "filterExe") + (IsSave ? "|*.sav" : "|*.exe"), Title = T("browse") };
            if (d.ShowDialog(this) == DialogResult.OK) InspectFile(d.FileName);
        };
        auto.Click += async (_, _) => await ScanAsync(true);
        folder.Click += async (_, _) =>
        {
            using var d = new FolderBrowserDialog { Description = T("folder"), UseDescriptionForTitle = true };
            if (d.ShowDialog(this) == DialogResult.OK) { selectedFolder = d.SelectedPath; await ScanAsync(false); }
        };
        cancel.Click += (_, _) => scan?.Cancel();
        grid.SelectionChanged += (_, _) =>
        {
            if (!populating && grid.SelectedRows.Count == 1 && grid.SelectedRows[0].Tag is SaveEntry entry) InspectFile(entry.Path);
        };
        copy.Click += (_, _) => Repair(false); replace.Click += (_, _) => Repair(true);
        DragEnter += (_, e) => e.Effect = scan is null && e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (_, e) => { if (scan is null && e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: 1 } paths) InspectFile(paths[0]); };
        FormClosing += (_, _) => scan?.Cancel();
        InitializeThemes();
        Translate();
    }
    static string StatusKey(Inspection? info, bool save = true) => info is null ? "unavailable" : info.AlreadyPatched ? save ? "saveAlreadyStatus" : "exeAlreadyStatus" : !info.Compatible ? "notApplicable" : info.RequiresConsent ? "untested" : "ready";
    void Translate()
    {
        Text = heading.Text = T("title"); intro.Text = T("intro") + "\n" + T("closeGame"); help.Text = T(IsSave ? "saveHelp" : "exeHelp");
        language.Text = I18n.Languages.Single(x => x.Code == I18n.Language).Name;
        foreach (ToolStripMenuItem option in languageMenu.Items) option.Checked = (string?)option.Tag == I18n.Language;
        saveMode.Text = T("modeSave"); exeMode.Text = T("modeExe"); browse.Text = T("browse"); file.PlaceholderText = T("none");
        auto.Text = T("auto"); folder.Text = T("folder"); cancel.Text = T("cancel");
        copy.Text = T("copy"); replace.Text = T("replace");
        foreach (DataGridViewColumn column in grid.Columns) column.HeaderText = T(column.Name);
        scanBar.Visible = scanStatus.Visible = grid.Visible = IsSave;
        root.RowStyles[5].Height = IsSave ? 48 : 0; root.RowStyles[6].Height = IsSave ? 28 : 0;
        root.RowStyles[7].SizeType = IsSave ? SizeType.Percent : SizeType.Absolute; root.RowStyles[7].Height = IsSave ? 100 : 0;
        foreach (DataGridViewRow row in grid.Rows) if (row.Tag is SaveEntry entry) row.Cells["status"].Value = T(StatusKey(entry.Inspection));
        if (scan is null) scanStatus.Text = discovery is null ? "" : T(discovery.Saves.Count == 0 ? "noSaves" : "scanned", discovery.Saves.Count);
        RefreshDetails(); UpdateButtons(); ApplyTheme();
    }
    void RefreshDetails()
    {
        status.Text = string.IsNullOrEmpty(file.Text) ? T("none") : T(StatusKey(inspection, IsSave));
        var parts = new List<string>();
        if (inspection is not null)
        {
            string profile = IsSave ? inspection.Sha256 == SaveRepair.TestedHash ? "testedSave" : "otherSave" : inspection.Sha256 == Patcher.GogHash ? "gogTested" : inspection.Sha256 == Patcher.SteamHash ? "steamUntested" : "unknownExe";
            string explanation = inspection.AlreadyPatched ? IsSave ? "saveAlreadyDetails" : "exeAlreadyDetails" : inspection.Compatible ? IsSave ? "saveReadyDetails" : "exeReadyDetails" : "notApplicableDetails";
            parts.Add(T(profile)); parts.Add(T(explanation)); parts.Add("SHA-256: " + inspection.Sha256);
        }
        if (fileError is not null) parts.Add(T("readFailure") + "\r\n" + fileError);
        if (IsSave && discovery?.Warnings.Count > 0) parts.Add(T("scanWarning"));
        details.Text = string.Join("\r\n\r\n", parts);
        if (fileError is not null || (IsSave && discovery?.Warnings.Count > 0)) detailsOpen = true;
        LayoutDetails();
        ApplyTheme();
    }
    internal void ShowDetails() { detailsOpen=true; LayoutDetails(); }
    void LayoutDetails()
    {
        root.SuspendLayout();
        details.Visible = detailsOpen; detailsToggle.Text = T("details"); detailsToggle.Expanded = detailsOpen; detailsToggle.Invalidate();
        root.RowStyles[9].Height = detailsOpen ? 128 : 0;
        int extra = detailsOpen ? 128 : 0;
        int delta = extra - layoutExtraHeight + (lastSaveMode == IsSave ? 0 : IsSave ? 240 : -240);
        int targetHeight = ClientSize.Height + delta;
        layoutExtraHeight = extra; lastSaveMode = IsSave;
        MinimumSize = new Size(980, IsSave ? 450 : 380);
        if (delta != 0 && WindowState == FormWindowState.Normal) ClientSize = new Size(ClientSize.Width, targetHeight);
        root.ResumeLayout();
    }
    internal void InspectFile(string path)
    {
        bool isSave = Path.GetExtension(path).Equals(".sav", StringComparison.OrdinalIgnoreCase);
        if (isSave) saveMode.Checked = true; else exeMode.Checked = true;
        inspection = null; fileError = null;
        try
        {
            file.Text = Path.GetFullPath(path); selectedFolder = Path.GetDirectoryName(file.Text);
            if (new FileInfo(path).Length > 64L * 1024 * 1024) throw new InvalidDataException("File exceeds the 64 MiB inspection limit.");
            var data = File.ReadAllBytes(path); inspection = IsSave ? SaveRepair.Inspect(data) : Patcher.Inspect(data);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { fileError = $"0x{e.HResult:X8}"; }
        RefreshDetails(); UpdateButtons();
    }
    void UpdateButtons()
    {
        bool idle = scan is null;
        copy.Enabled = replace.Enabled = idle && inspection is { Compatible: true };
        browse.Enabled = auto.Enabled = folder.Enabled = saveMode.Enabled = exeMode.Enabled = grid.Enabled = idle;
        cancel.Visible = !idle;
        replace.Invalidate();
    }
    internal void PreviewFolder(string path) => Populate(Discovery.Scan(path, false, CancellationToken.None));
    internal void PreviewExeDemo(string source)
    {
        exeMode.Checked = true;
        inspection = Patcher.Inspect(File.ReadAllBytes(source));
        file.Text = @"C:\Games\Still Life 2\SL2.exe"; fileError = null;
        RefreshDetails(); UpdateButtons();
    }
    internal void PreviewDemo(string fixture)
    {
        // Documentation-only presentation data. No game paths are read or written.
        var affected = SaveRepair.Inspect(File.ReadAllBytes(fixture));
        string gog = @"C:\Games\GOG\Still Life 2\EXTDataSave", steam = @"D:\SteamLibrary\steamapps\common\Still life 2\EXTDataSave";
        var ordinary = affected with { Compatible = false, RequiresConsent = false, AlreadyPatched = false };
        var repaired = ordinary with { AlreadyPatched = true };
        var samples = new SaveEntry[] {
            new(Path.Combine(gog, "men008_94.sav"), new DateTime(2026, 6, 14, 20, 42, 0), 7514274, affected, null),
            new(Path.Combine(gog, "men008_95.sav"), new DateTime(2026, 6, 14, 20, 55, 0), 7514338, repaired, null),
            new(Path.Combine(gog, "men008_93.sav"), new DateTime(2026, 6, 14, 20, 30, 0), 7510000, ordinary, null),
            new(Path.Combine(steam, "men009_12.sav"), new DateTime(2026, 6, 13, 19, 15, 0), 7500000, ordinary, null)
        };
        Populate(new(samples, [gog, steam], []), false);
        file.Text = samples[0].Path; inspection = affected; fileError = null; RefreshDetails(); UpdateButtons();
    }
    void Populate(DiscoveryResult result, bool inspectSelection = true)
    {
        discovery = result; populating = true; grid.Rows.Clear();
        foreach (var entry in result.Saves)
        {
            int index = grid.Rows.Add(Path.GetFileName(entry.Path), entry.Modified == DateTime.MinValue ? "" : entry.Modified.ToString("yyyy-MM-dd HH:mm"), T(StatusKey(entry.Inspection)), Path.GetDirectoryName(entry.Path) ?? "");
            var row = grid.Rows[index]; row.Tag = entry; row.Cells["location"].ToolTipText = entry.Path;
            if (entry.Inspection?.Compatible == true) row.DefaultCellStyle.ForeColor = entry.Inspection.RequiresConsent ? Color.DarkGoldenrod : Color.DarkGreen;
        }
        grid.ClearSelection();
        if (grid.Rows.Count > 0) { grid.Rows[0].Selected = true; if (inspectSelection) InspectFile(result.Saves[0].Path); }
        populating = false;
        Translate();
    }
    async Task ScanAsync(bool autoDetect)
    {
        if (scan is not null) return;
        using var cancellation = new CancellationTokenSource(); scan = cancellation; UpdateButtons();
        scanStatus.Text = T("scan", 0, 0);
        try
        {
            var progress = new Progress<(int Done, int Total)>(p => { if (!IsDisposed && scan == cancellation) scanStatus.Text = T("scan", p.Done, p.Total); });
            var result = await Task.Run(() => Discovery.Scan(selectedFolder, autoDetect, cancellation.Token, progress));
            if (!IsDisposed) Populate(result);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        { if (!IsDisposed) { fileError = $"0x{e.HResult:X8}"; RefreshDetails(); } }
        finally { scan = null; if (!IsDisposed) Translate(); }
    }
    void Repair(bool inPlace)
    {
        if (inspection is not { Compatible: true }) return;
        string? output = null;
        if (inPlace)
        {
            if (!inspection.RequiresConsent && MessageBox.Show(this, T("confirm", file.Text), T("replace"), MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        }
        else
        {
            using var d = new SaveFileDialog { Filter = T(IsSave ? "filterSave" : "filterExe") + (IsSave ? "|*.sav" : "|*.exe"), FileName = IsSave ? Path.GetFileNameWithoutExtension(file.Text) + "-fixed.sav" : "SL2-timerfix.exe", InitialDirectory = Path.GetDirectoryName(file.Text), OverwritePrompt = true };
            if (d.ShowDialog(this) != DialogResult.OK) return; output = d.FileName;
            if (Path.GetFullPath(output).Equals(Path.GetFullPath(file.Text), StringComparison.OrdinalIgnoreCase))
            { MessageBox.Show(this, T("sourceSame"), T("error")); return; }
        }
        // Approval applies only to this file and repair attempt; never remember it
        // across selections or retries. The core still rejects incompatible data.
        bool allowUntested = false;
        if (inspection.RequiresConsent)
        {
            using var warning = new UntestedFileDialog(file.Text, inPlace);
            if (warning.ShowDialog(this) != DialogResult.OK) return;
            allowUntested = true;
        }
        try
        {
            if (Patcher.Hash(File.ReadAllBytes(file.Text)) != inspection.Sha256) throw new IOException("The selected file changed since inspection. Select it again before repairing.");
            var result = IsSave ? SaveRepair.Apply(file.Text, output, allowUntested) : Patcher.Apply(file.Text, output, allowUntested);
            MessageBox.Show(this, T("success", result.Output) + "\n\n" + (result.Backup is null ? T("originalKept") : T("backup", result.Backup)) + "\n\n" + T(IsSave ? "saveHint" : "exeHint"), T("done"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            if (inPlace)
            {
                InspectFile(file.Text);
                foreach (DataGridViewRow row in grid.Rows)
                    if (row.Tag is SaveEntry entry && entry.Path.Equals(file.Text, StringComparison.OrdinalIgnoreCase))
                    { row.Tag = entry with { Inspection = inspection }; row.Cells["status"].Value = T(StatusKey(inspection)); }
                ApplyTheme();
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        { MessageBox.Show(this, T(FailureKey(e)) + $"\n\n0x{e.HResult:X8}", T("error"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
    static string FailureKey(Exception e) => e is UnauthorizedAccessException ? "accessDenied" : e is InvalidDataException ? "notApplicableDetails" :
        e.Message.Contains("backup", StringComparison.OrdinalIgnoreCase) ? "backupConflict" :
        e.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase) ? "destinationExists" :
        e.Message.Contains("changed", StringComparison.OrdinalIgnoreCase) ? "fileChanged" : "operationFailed";
}
