namespace DeezerRpc.Windows;

internal sealed class SettingsForm : Form
{
    private readonly CheckBox _browser = new()
    {
        AutoSize = true,
        Text = "Détection expérimentale de Deezer Web (session média du navigateur)"
    };
    private readonly CheckBox _startup = new() { AutoSize = true, Text = "Démarrer automatiquement avec Windows" };

    public AppSettings Result { get; private set; }

    public SettingsForm(AppSettings current)
    {
        Result = current;
        Text = "Paramètres DeezerRPC";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(560, 165);
        Font = new Font("Segoe UI", 9F);
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;

        _browser.Checked = current.EnableBrowserDetection;
        _startup.Checked = current.StartWithWindows;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true
        };
        var save = new Button { Text = "Enregistrer", AutoSize = true };
        var cancel = new Button { Text = "Annuler", AutoSize = true, DialogResult = DialogResult.Cancel };
        save.Click += SaveClicked;
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 4
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(_browser, 0, 0);
        layout.Controls.Add(_startup, 0, 1);
        layout.Controls.Add(buttons, 0, 3);
        Controls.Add(layout);

        AcceptButton = save;
        CancelButton = cancel;
    }

    private void SaveClicked(object? sender, EventArgs e)
    {
        Result = new AppSettings
        {
            EnableBrowserDetection = _browser.Checked,
            StartWithWindows = _startup.Checked,
            PollIntervalMilliseconds = Result.PollIntervalMilliseconds
        };
        DialogResult = DialogResult.OK;
        Close();
    }
}
