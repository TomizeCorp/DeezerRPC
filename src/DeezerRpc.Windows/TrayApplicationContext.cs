namespace DeezerRpc.Windows;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly SettingsStore _settingsStore = new();
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly System.Windows.Forms.Timer _statusTimer;
    private readonly SemaphoreSlim _restartGate = new(1, 1);
    private AppSettings _settings;
    private PresenceWorker? _worker;
    private PresenceSnapshot _snapshot = new();
    private DashboardForm? _dashboard;
    private bool _closing;

    public TrayApplicationContext()
    {
        _settings = _settingsStore.Load();
        _statusItem = new ToolStripMenuItem(_snapshot.StatusText) { Enabled = false };

        var openItem = new ToolStripMenuItem("Ouvrir Deezer Presence");
        openItem.Click += OpenDashboardClicked;
        var quitItem = new ToolStripMenuItem("Quitter");
        quitItem.Click += QuitClicked;

        var menu = new ContextMenuStrip();
        menu.Items.Add(openItem);
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(quitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application,
            Text = "Deezer Presence",
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += OpenDashboardClicked;

        _statusTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _statusTimer.Tick += (_, _) => RefreshStatus();
        _statusTimer.Start();

        ApplySettingsAndStart();
        if (!Environment.GetCommandLineArgs().Any(argument => argument.Equals("--background", StringComparison.OrdinalIgnoreCase)))
        {
            OpenDashboard();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _statusTimer.Dispose();
            _restartGate.Dispose();
            _dashboard?.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }

        base.Dispose(disposing);
    }

    private void RefreshStatus()
    {
        var status = _snapshot.StatusText;
        _statusItem.Text = status;
        _notifyIcon.Text = status.Length <= 63 ? status : string.Concat(status.AsSpan(0, 62), "…");
    }

    private void ReportSnapshot(PresenceSnapshot snapshot) => _snapshot = snapshot;

    private void OpenDashboardClicked(object? sender, EventArgs e) => OpenDashboard();

    private void OpenDashboard()
    {
        if (_closing)
        {
            return;
        }

        if (_dashboard is null || _dashboard.IsDisposed)
        {
            _dashboard = new DashboardForm(_settings, () => _snapshot, SaveSettings);
            _dashboard.ExitRequested += QuitClicked;
        }

        _dashboard.Reveal();
    }

    private void SaveSettings(AppSettings settings)
    {
        _settings = settings;
        _settingsStore.Save(_settings);
        StartupManager.SetEnabled(_settings.StartWithWindows);
        _ = RestartWorkerAsync();
    }

    private void ApplySettingsAndStart()
    {
        StartupManager.SetEnabled(_settings.StartWithWindows);
        _worker = new PresenceWorker(_settings, ReportSnapshot);
        _worker.Start();
    }

    private async Task RestartWorkerAsync()
    {
        await _restartGate.WaitAsync();
        try
        {
            if (_closing)
            {
                return;
            }

            if (_worker is not null)
            {
                await _worker.DisposeAsync();
                _worker = null;
            }

            ApplySettingsAndStart();
        }
        finally
        {
            _restartGate.Release();
        }
    }

    private async void QuitClicked(object? sender, EventArgs e)
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        _snapshot = _snapshot with { StatusText = "Arrêt…" };
        _statusTimer.Stop();
        await _restartGate.WaitAsync();
        try
        {
            if (_worker is not null)
            {
                await _worker.DisposeAsync();
                _worker = null;
            }
        }
        finally
        {
            _restartGate.Release();
        }

        _dashboard?.CloseForExit();
        _notifyIcon.Visible = false;
        ExitThread();
    }
}
