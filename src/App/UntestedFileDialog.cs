namespace StillLife2Patcher;

sealed class UntestedFileDialog : Form
{
    public UntestedFileDialog(string path, bool inPlace)
    {
        var p = Appearance.Colors;
        Text = I18n.T("untestedTitle"); Font = new Font("Segoe UI", 10);
        FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false; ShowInTaskbar = false; Padding = new Padding(24);
        BackColor = p.Background; ForeColor = p.Text;
        const int width = 652;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = p.Background };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        int totalHeight = 0;
        void Add(Control control, int height)
        {
            int row = root.RowStyles.Count;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            control.Dock = DockStyle.Fill; control.Margin = Padding.Empty;
            root.Controls.Add(control, 0, row); totalHeight += height;
        }
        void Paragraph(string text, Font font, Color color, int gap = 16)
        {
            int height = TextRenderer.MeasureText(text, font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + 4;
            Add(new Label { Text = text, Font = font, ForeColor = color, BackColor = p.Background, UseMnemonic = false }, height + gap);
        }
        Paragraph(I18n.T("untestedTitle"), new Font(Font.FontFamily, 15, FontStyle.Bold), p.Warning);
        Paragraph(I18n.T("untestedWarning"), Font, p.Text);
        Add(new TextBox { Text = path, ReadOnly = true, BackColor = p.Panel, ForeColor = p.Text, TabStop = false }, 36);
        Paragraph(I18n.T(inPlace ? "backupNotice" : "copyNotice"), Font, p.Text);
        var actions = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = p.Background };
        var cancel = new UiButton { Text = I18n.T("cancelRepair"), AutoSize = true, Padding = new Padding(12, 6, 12, 6), Margin = Padding.Empty, DialogResult = DialogResult.Cancel };
        var proceed = new UiButton { Text = I18n.T("continueAnyway"), AutoSize = true, Primary = true, Padding = new Padding(12, 6, 12, 6), Margin = new Padding(0, 0, 12, 0), DialogResult = DialogResult.OK };
        actions.Controls.Add(cancel); actions.Controls.Add(proceed); Add(actions, 48);
        Controls.Add(root); ClientSize = new Size(width + Padding.Horizontal, totalHeight + Padding.Vertical);
        // Enter, Esc and closing the dialog all cancel; continuing needs a
        // deliberate click or keyboard selection of the affirmative button.
        AcceptButton = CancelButton = cancel;
    }
}
