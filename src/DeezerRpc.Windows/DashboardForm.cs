using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using DeezerRpc.Core;

namespace DeezerRpc.Windows;

internal sealed class DashboardForm : Form
{
    private static readonly Color WindowColor = Color.FromArgb(11, 13, 16);
    private static readonly Color SidebarColor = Color.FromArgb(14, 17, 20);
    private static readonly Color CardColor = Color.FromArgb(19, 22, 26);
    private static readonly Color BorderColor = Color.FromArgb(45, 49, 56);
    private static readonly Color MutedColor = Color.FromArgb(169, 171, 181);
    private static readonly Color Purple = Color.FromArgb(163, 77, 255);
    private static readonly Color Green = Color.FromArgb(91, 201, 105);

    private readonly Func<PresenceSnapshot> _getSnapshot;
    private readonly Action<AppSettings> _saveSettings;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly Panel _pageHost = new() { Dock = DockStyle.Fill };
    private readonly Dictionary<string, Button> _navigation = new(StringComparer.Ordinal);
    private readonly HttpClient _images = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly Image _appImage;
    private AppSettings _settings;
    private Label _sidebarState = null!;
    private Label _sidebarSubstate = null!;
    private ToggleSwitch _presenceToggle = null!;
    private PictureBox _cover = null!;
    private Label _title = null!;
    private Label _artist = null!;
    private Label _album = null!;
    private Label _currentTime = null!;
    private Label _duration = null!;
    private ProgressLine _progress = null!;
    private Button _deezerButton = null!;
    private Label _discordState = null!;
    private Label _discordSubstate = null!;
    private Label _deezerState = null!;
    private Label _deezerSubstate = null!;
    private string? _loadedCoverUrl;
    private Image? _downloadedCover;
    private bool _forceClose;
    private bool _updatingToggle;

    public event EventHandler? ExitRequested;

    public DashboardForm(
        AppSettings settings,
        Func<PresenceSnapshot> getSnapshot,
        Action<AppSettings> saveSettings)
    {
        _settings = settings;
        _getSnapshot = getSnapshot;
        _saveSettings = saveSettings;
        _appImage = LoadAppImage();

        Text = "Deezer Presence";
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(920, 660);
        ClientSize = new Size(1040, 720);
        BackColor = WindowColor;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10F);
        DoubleBuffered = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = WindowColor
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 226));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(BuildSidebar(), 0, 0);
        root.Controls.Add(_pageHost, 1, 0);
        Controls.Add(root);

        ShowPage("Accueil");
        _refreshTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _refreshTimer.Tick += (_, _) => RefreshSnapshot();
        _refreshTimer.Start();
        RefreshSnapshot();

        Shown += (_, _) => EnableDarkTitleBar();
        FormClosing += HandleFormClosing;
    }

    public void Reveal()
    {
        if (!Visible)
        {
            Show();
        }

        WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    public void CloseForExit()
    {
        _forceClose = true;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer?.Dispose();
            _images.Dispose();
            _downloadedCover?.Dispose();
            _appImage.Dispose();
        }

        base.Dispose(disposing);
    }

    private Control BuildSidebar()
    {
        var sidebar = new Panel { Dock = DockStyle.Fill, BackColor = SidebarColor, Padding = new Padding(14, 20, 14, 24) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = SidebarColor };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 154));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));

        var brand = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = SidebarColor };
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        brand.Controls.Add(new PictureBox
        {
            Image = _appImage,
            SizeMode = PictureBoxSizeMode.Zoom,
            Dock = DockStyle.Fill,
            Margin = new Padding(3, 5, 7, 12)
        }, 0, 0);
        brand.Controls.Add(new Label
        {
            Text = "Deezer Presence",
            ForeColor = Color.White,
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI Semibold", 10F)
        }, 1, 0);

        var nav = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = SidebarColor
        };
        nav.Controls.Add(NavButton("Accueil", "⌂"));
        nav.Controls.Add(NavButton("Paramètres", "⚙"));

        var statusCard = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            FillColor = Color.FromArgb(18, 21, 24),
            BorderColor = BorderColor,
            CornerRadius = 20,
            Padding = new Padding(16, 14, 12, 10),
            Margin = new Padding(2, 0, 2, 2)
        };
        var statusLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Color.Transparent };
        statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 18));
        statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        statusLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        statusLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        statusLayout.Controls.Add(new StatusDot { DotColor = Green, Dock = DockStyle.Fill }, 0, 0);
        _sidebarState = Label("Actif", 10F, Color.White, bold: true);
        _sidebarSubstate = Label("Rich Presence activée", 8.5F, MutedColor);
        statusLayout.Controls.Add(_sidebarState, 1, 0);
        statusLayout.Controls.Add(_sidebarSubstate, 1, 1);
        statusCard.Controls.Add(statusLayout);

        layout.Controls.Add(brand, 0, 0);
        layout.Controls.Add(nav, 0, 1);
        layout.Controls.Add(statusCard, 0, 3);
        sidebar.Controls.Add(layout);
        return sidebar;
    }

    private Button NavButton(string text, string glyph)
    {
        var button = new Button
        {
            Text = $"  {glyph}    {text}",
            TextAlign = ContentAlignment.MiddleLeft,
            Width = 196,
            Height = 48,
            Margin = new Padding(0, 4, 0, 2),
            FlatStyle = FlatStyle.Flat,
            BackColor = SidebarColor,
            ForeColor = Color.FromArgb(220, 221, 226),
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI", 10F)
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += (_, _) => ShowPage(text);
        _navigation[text] = button;
        return button;
    }

    private void ShowPage(string page)
    {
        _pageHost.SuspendLayout();
        _pageHost.Controls.Clear();
        _pageHost.Controls.Add(page switch
        {
            "Paramètres" => BuildSettingsPage(),
            _ => BuildHomePage()
        });
        foreach (var item in _navigation)
        {
            item.Value.BackColor = item.Key == page ? Color.FromArgb(52, 36, 78) : SidebarColor;
            item.Value.ForeColor = item.Key == page ? Color.White : Color.FromArgb(220, 221, 226);
        }
        _pageHost.ResumeLayout();
        RefreshSnapshot();
    }

    private Control BuildHomePage()
    {
        var scroll = PageScroll();
        var content = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(30, 26, 30, 24), BackColor = WindowColor };
        content.RowStyles.Clear();

        var header = new TableLayoutPanel { Dock = DockStyle.Top, Height = 78, ColumnCount = 2, BackColor = WindowColor };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        var titleArea = new Panel { Dock = DockStyle.Fill, BackColor = WindowColor };
        titleArea.Controls.Add(Label("Deezer Presence", 24F, Color.White, bold: true, dockTop: true, height: 38));
        var subtitle = Label("Affiche ce que tu écoutes sur Deezer sur ton profil Discord.", 10F, MutedColor, dockTop: true, height: 30);
        subtitle.Padding = new Padding(0, 5, 0, 0);
        titleArea.Controls.Add(subtitle);
        titleArea.Controls.SetChildIndex(subtitle, 0);
        var toggleCard = new RoundedPanel { Dock = DockStyle.Fill, FillColor = CardColor, BorderColor = BorderColor, CornerRadius = 20, Padding = new Padding(14), Margin = new Padding(14, 8, 0, 10) };
        var toggleLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent };
        toggleLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toggleLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54));
        toggleLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        toggleLayout.Controls.Add(Label("Rich Presence activée", 9.5F, Color.White), 0, 0);
        _presenceToggle = new ToggleSwitch { Checked = _settings.RichPresenceEnabled, Dock = DockStyle.Fill };
        _presenceToggle.CheckedChanged += (_, _) =>
        {
            if (!_updatingToggle)
            {
                Save(_settings with { RichPresenceEnabled = _presenceToggle.Checked });
            }
        };
        toggleLayout.Controls.Add(_presenceToggle, 1, 0);
        toggleCard.Controls.Add(toggleLayout);
        header.Controls.Add(titleArea, 0, 0);
        header.Controls.Add(toggleCard, 1, 0);
        content.Controls.Add(header);
        content.Controls.Add(SectionTitle("Lecture en cours"));

        var playbackCard = new RoundedPanel
        {
            Dock = DockStyle.Top,
            Height = 248,
            FillColor = CardColor,
            BorderColor = BorderColor,
            CornerRadius = 22,
            Padding = new Padding(18),
            Margin = new Padding(0, 10, 0, 12)
        };
        var playback = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Color.Transparent };
        playback.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 214));
        playback.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _cover = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = _appImage, Margin = new Padding(0, 0, 20, 0), BackColor = Color.FromArgb(10, 11, 14) };
        playback.Controls.Add(_cover, 0, 0);
        var metadata = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, BackColor = Color.Transparent, Padding = new Padding(4, 12, 0, 4) };
        metadata.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        metadata.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        metadata.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
        metadata.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        metadata.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        _title = Label("En attente de Deezer", 19F, Color.White, bold: true);
        _artist = Label("Lance une musique", 13F, Purple);
        _album = Label("La pochette et les informations apparaîtront ici", 11F, MutedColor);
        metadata.Controls.Add(_title, 0, 0);
        metadata.Controls.Add(_artist, 0, 1);
        metadata.Controls.Add(_album, 0, 2);
        var progressLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, BackColor = Color.Transparent };
        progressLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));
        progressLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        progressLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55));
        _currentTime = Label("00:00", 9F, Color.FromArgb(220, 221, 225));
        _duration = Label("00:00", 9F, Color.FromArgb(220, 221, 225));
        _duration.TextAlign = ContentAlignment.MiddleRight;
        _progress = new ProgressLine { Dock = DockStyle.Fill, Margin = new Padding(5, 7, 5, 7) };
        progressLayout.Controls.Add(_currentTime, 0, 0);
        progressLayout.Controls.Add(_progress, 1, 0);
        progressLayout.Controls.Add(_duration, 2, 0);
        metadata.Controls.Add(progressLayout, 0, 4);
        playback.Controls.Add(metadata, 1, 0);
        playbackCard.Controls.Add(playback);
        content.Controls.Add(playbackCard);

        _deezerButton = AccentOutlineButton("🔗   Écouter sur Deezer");
        _deezerButton.Dock = DockStyle.Top;
        _deezerButton.Height = 54;
        _deezerButton.Click += (_, _) => OpenCurrentTrack();
        content.Controls.Add(_deezerButton);

        var statuses = new TableLayoutPanel { Dock = DockStyle.Top, Height = 96, ColumnCount = 2, BackColor = WindowColor, Margin = new Padding(0, 18, 0, 0) };
        statuses.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        statuses.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var discordCard = StatusCard("◉", out _discordState, out _discordSubstate);
        discordCard.Cursor = Cursors.Hand;
        WireClick(discordCard, (_, _) => OpenDiscordDesktop());
        statuses.Controls.Add(discordCard, 0, 0);
        var deezerCard = StatusCard("≋", out _deezerState, out _deezerSubstate);
        deezerCard.Margin = new Padding(8, 0, 0, 2);
        statuses.Controls.Add(deezerCard, 1, 0);
        content.Controls.Add(statuses);

        scroll.Controls.Add(content);
        return scroll;
    }

    private Control BuildSettingsPage()
    {
        var scroll = PageScroll();
        var content = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(36, 28, 36, 30), BackColor = WindowColor };
        content.Controls.Add(PageTitle("Paramètres", "Personnalise l’affichage et le fonctionnement en arrière-plan."));
        content.Controls.Add(SectionLabel("AFFICHAGE"));
        content.Controls.Add(SettingRow("Afficher l’album", "Affiche l’album sur une ligne séparée sous l’artiste", _settings.ShowAlbum, value => Save(_settings with { ShowAlbum = value })));
        content.Controls.Add(SettingRow("Afficher la progression", "Utilise les horodatages natifs de Discord", _settings.ShowProgress, value => Save(_settings with { ShowProgress = value })));
        content.Controls.Add(SettingRow("Afficher le bouton Deezer", "Ajoute « Écouter sur Deezer » quand le lien est disponible", _settings.ShowDeezerButton, value => Save(_settings with { ShowDeezerButton = value })));
        content.Controls.Add(SectionLabel("GÉNÉRAL"));
        content.Controls.Add(SettingRow("Lancer au démarrage", "Démarre Deezer Presence avec Windows", _settings.StartWithWindows, value => Save(_settings with { StartWithWindows = value })));
        content.Controls.Add(SettingRow("Fonctionner en arrière-plan", "Fermer la fenêtre laisse la Rich Presence active", _settings.KeepRunningInBackground, value => Save(_settings with { KeepRunningInBackground = value })));
        content.Controls.Add(SettingRow("Détecter Deezer Web", "Active la détection expérimentale des navigateurs", _settings.EnableBrowserDetection, value => Save(_settings with { EnableBrowserDetection = value })));
        content.Controls.Add(SettingRow("Notifications", "Affiche les changements importants dans Windows", _settings.ShowNotifications, value => Save(_settings with { ShowNotifications = value })));
        scroll.Controls.Add(content);
        return scroll;
    }

    private Control PageTitle(string title, string subtitle)
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = 95, BackColor = WindowColor };
        panel.Controls.Add(Label(title, 24F, Color.White, bold: true, dockTop: true, height: 45));
        var sub = Label(subtitle, 10.5F, MutedColor, dockTop: true, height: 38);
        sub.Padding = new Padding(0, 8, 0, 0);
        panel.Controls.Add(sub);
        panel.Controls.SetChildIndex(sub, 0);
        return panel;
    }

    private Control SettingRow(string title, string subtitle, bool value, Action<bool> changed)
    {
        var row = new RoundedPanel { Dock = DockStyle.Top, Height = 70, FillColor = CardColor, BorderColor = BorderColor, CornerRadius = 18, Padding = new Padding(17, 9, 14, 8), Margin = new Padding(0, 0, 0, 8) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var text = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, BackColor = Color.Transparent };
        text.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        text.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        text.Controls.Add(Label(title, 10F, Color.White, bold: true), 0, 0);
        text.Controls.Add(Label(subtitle, 8.6F, MutedColor), 0, 1);
        var toggle = new ToggleSwitch { Checked = value, Dock = DockStyle.Fill };
        toggle.CheckedChanged += (_, _) => changed(toggle.Checked);
        layout.Controls.Add(text, 0, 0);
        layout.Controls.Add(toggle, 1, 0);
        row.Controls.Add(layout);
        return row;
    }

    private RoundedPanel StatusCard(string glyph, out Label state, out Label substate)
    {
        var card = new RoundedPanel { Dock = DockStyle.Fill, FillColor = CardColor, BorderColor = BorderColor, CornerRadius = 20, Padding = new Padding(16), Margin = new Padding(0, 0, 8, 2) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Color.Transparent };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        var icon = Label(glyph, 24F, Purple);
        icon.TextAlign = ContentAlignment.MiddleCenter;
        layout.Controls.Add(icon, 0, 0);
        layout.SetRowSpan(icon, 2);
        state = Label("En attente", 10F, Color.White, bold: true);
        substate = Label("Initialisation…", 8.5F, MutedColor);
        layout.Controls.Add(state, 1, 0);
        layout.Controls.Add(substate, 1, 1);
        card.Controls.Add(layout);
        return card;
    }

    private static ScrollableControl PageScroll() => new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = WindowColor };

    private static Label Label(string text, float size, Color color, bool bold = false, bool dockTop = false, int height = 0) => new()
    {
        Text = text,
        ForeColor = color,
        BackColor = Color.Transparent,
        Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
        Dock = dockTop ? DockStyle.Top : DockStyle.Fill,
        AutoEllipsis = true,
        Height = height,
        TextAlign = ContentAlignment.MiddleLeft
    };

    private static Label SectionTitle(string text)
    {
        var label = Label(text, 14F, Color.White, bold: true, dockTop: true, height: 42);
        label.Padding = new Padding(0, 8, 0, 0);
        return label;
    }

    private static Label SectionLabel(string text)
    {
        var label = Label(text, 9F, Purple, bold: true, dockTop: true, height: 46);
        label.Padding = new Padding(0, 18, 0, 5);
        return label;
    }

    private static Button AccentOutlineButton(string text)
    {
        var button = new Button { Text = text, FlatStyle = FlatStyle.Flat, BackColor = CardColor, ForeColor = Color.FromArgb(192, 122, 255), Cursor = Cursors.Hand, Font = new Font("Segoe UI Semibold", 10F), Margin = new Padding(0) };
        button.FlatAppearance.BorderColor = Color.FromArgb(80, 60, 100);
        button.FlatAppearance.BorderSize = 1;
        return button;
    }

    private void Save(AppSettings settings)
    {
        _settings = settings;
        _saveSettings(settings);
        RefreshSnapshot();
    }

    private void RefreshSnapshot()
    {
        if (IsDisposed)
        {
            return;
        }

        var snapshot = _getSnapshot();
        _sidebarState.Text = _settings.RichPresenceEnabled ? "Actif" : "Désactivé";
        _sidebarSubstate.Text = _settings.RichPresenceEnabled ? "Rich Presence activée" : "Publication suspendue";
        if (_presenceToggle is not null)
        {
            _updatingToggle = true;
            _presenceToggle.Checked = _settings.RichPresenceEnabled;
            _updatingToggle = false;
        }

        if (_title is null)
        {
            return;
        }

        var track = snapshot.Track;
        if (track is null)
        {
            _title.Text = "En attente de Deezer";
            _artist.Text = "Lance une musique";
            _album.Text = "La pochette et les informations apparaîtront ici";
            _album.Visible = true;
            _currentTime.Text = "00:00";
            _duration.Text = "00:00";
            _progress.Value = 0;
            _deezerButton.Enabled = true;
            SetFallbackCover();
        }
        else
        {
            var position = track.ProjectPosition(DateTimeOffset.UtcNow);
            _title.Text = track.Title;
            _artist.Text = track.Artist;
            _album.Visible = _settings.ShowAlbum && !string.IsNullOrWhiteSpace(track.Album);
            _album.Text = track.Album.Trim();
            _currentTime.Text = FormatTime(position);
            _duration.Text = FormatTime(track.Duration);
            _progress.Value = track.Duration > TimeSpan.Zero ? Math.Clamp(position.TotalSeconds / track.Duration.TotalSeconds, 0, 1) : 0;
            _deezerButton.Enabled = true;
            _ = LoadCoverAsync(track.CoverUrl);
        }

        _discordState.Text = snapshot.DiscordConnected ? "Discord connecté" : "Connexion Discord";
        _discordSubstate.Text = snapshot.DiscordConnected ? "Compte Discord Desktop détecté" : "Clique ici pour ouvrir Discord Desktop";
        _deezerState.Text = snapshot.DeezerDetected ? "Deezer détecté" : "Deezer en attente";
        _deezerSubstate.Text = snapshot.StatusText;
    }

    private async Task LoadCoverAsync(Uri? uri)
    {
        if (uri is null || uri.Scheme != Uri.UriSchemeHttps)
        {
            SetFallbackCover();
            return;
        }

        var url = uri.AbsoluteUri;
        if (url == _loadedCoverUrl)
        {
            return;
        }

        _loadedCoverUrl = url;
        try
        {
            var bytes = await _images.GetByteArrayAsync(uri);
            await using var stream = new MemoryStream(bytes);
            using var source = Image.FromStream(stream);
            var copy = new Bitmap(source);
            if (IsDisposed || _loadedCoverUrl != url)
            {
                copy.Dispose();
                return;
            }

            BeginInvoke(() =>
            {
                var previous = _downloadedCover;
                _downloadedCover = copy;
                _cover.Image = copy;
                previous?.Dispose();
            });
        }
        catch
        {
            if (_loadedCoverUrl == url)
            {
                SetFallbackCover();
            }
        }
    }

    private void SetFallbackCover()
    {
        _loadedCoverUrl = null;
        if (_cover is not null && _cover.Image != _appImage)
        {
            _cover.Image = _appImage;
            _downloadedCover?.Dispose();
            _downloadedCover = null;
        }
    }

    private void OpenCurrentTrack()
    {
        var track = _getSnapshot().Track;
        if (track is not null)
        {
            var url = DeezerLinks.GetListenUri(track);
            Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
        }
    }

    private static void OpenDiscordDesktop()
    {
        try
        {
            Process.Start(new ProcessStartInfo("discord://-/channels/@me") { UseShellExecute = true });
        }
        catch
        {
            Process.Start(new ProcessStartInfo("https://discord.com/app") { UseShellExecute = true });
        }
    }

    private static void WireClick(Control control, EventHandler handler)
    {
        control.Click += handler;
        foreach (Control child in control.Controls)
        {
            child.Cursor = Cursors.Hand;
            WireClick(child, handler);
        }
    }

    private void HandleFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_forceClose)
        {
            return;
        }

        e.Cancel = true;
        if (_settings.KeepRunningInBackground)
        {
            Hide();
        }
        else
        {
            ExitRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private Image LoadAppImage()
    {
        var assembly = typeof(DashboardForm).Assembly;
        using var stream = assembly.GetManifestResourceStream("DeezerRpc.Windows.AppIcon");
        if (stream is not null)
        {
            using var source = Image.FromStream(stream);
            return new Bitmap(source);
        }

        return (Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application).ToBitmap();
    }

    private void EnableDarkTitleBar()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            return;
        }

        var enabled = 1;
        _ = DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int));
    }

    private static string FormatTime(TimeSpan value)
    {
        var total = Math.Max(0, (int)value.TotalSeconds);
        return total >= 3600 ? $"{total / 3600}:{total / 60 % 60:00}:{total % 60:00}" : $"{total / 60:00}:{total % 60:00}";
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}

internal sealed class RoundedPanel : Panel
{
    public Color FillColor { get; set; } = Color.FromArgb(19, 22, 26);
    public Color BorderColor { get; set; } = Color.FromArgb(45, 49, 56);
    public int CornerRadius { get; set; } = 12;

    public RoundedPanel()
    {
        DoubleBuffered = true;
        BackColor = Color.Transparent;
        Resize += (_, _) => Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var borderPath = RoundedRectangle(ClientPixelBounds(), CornerRadius);
        using var borderBrush = new SolidBrush(BorderColor);
        e.Graphics.FillPath(borderBrush, borderPath);

        var innerBounds = new Rectangle(
            2,
            2,
            Math.Max(0, ClientSize.Width - 5),
            Math.Max(0, ClientSize.Height - 5));
        using var fillPath = RoundedRectangle(innerBounds, Math.Max(1, CornerRadius - 2));
        using var fillBrush = new SolidBrush(FillColor);
        e.Graphics.FillPath(fillBrush, fillPath);

        var borderY = Math.Max(1, ClientSize.Height - 3);
        var horizontalInset = Math.Min(CornerRadius, Math.Max(1, ClientSize.Width / 2));
        using var bottomPen = new Pen(BorderColor, 2F)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        e.Graphics.DrawLine(
            bottomPen,
            horizontalInset,
            borderY,
            Math.Max(horizontalInset, ClientSize.Width - horizontalInset),
            borderY);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
    }

    private Rectangle ClientPixelBounds() => new(
        0,
        0,
        Math.Max(0, ClientSize.Width - 1),
        Math.Max(0, ClientSize.Height - 1));

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return path;
        }

        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class ToggleSwitch : Control
{
    private bool _checked;
    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
            {
                return;
            }

            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Size = new Size(48, 26);
        MinimumSize = new Size(48, 26);
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        AccessibleRole = AccessibleRole.CheckButton;
        Click += (_, _) => Checked = !Checked;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var track = new Rectangle(2, (Height - 24) / 2, Math.Min(48, Width - 4), 24);
        using var path = Capsule(track);
        using var brush = new SolidBrush(Checked ? Color.FromArgb(145, 65, 235) : Color.FromArgb(65, 68, 74));
        e.Graphics.FillPath(brush, path);
        var knob = new Rectangle(Checked ? track.Right - 21 : track.Left + 3, track.Top + 3, 18, 18);
        using var knobBrush = new SolidBrush(Color.White);
        e.Graphics.FillEllipse(knobBrush, knob);
    }

    private static GraphicsPath Capsule(Rectangle bounds)
    {
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, bounds.Height, bounds.Height, 90, 180);
        path.AddArc(bounds.Right - bounds.Height, bounds.Top, bounds.Height, bounds.Height, 270, 180);
        path.CloseFigure();
        return path;
    }
}

internal sealed class ProgressLine : Control
{
    private double _value;
    public double Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0, 1);
            Invalidate();
        }
    }

    public ProgressLine()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        DoubleBuffered = true;
        MinimumSize = new Size(80, 18);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var y = Height / 2;
        using var background = new Pen(Color.FromArgb(62, 65, 70), 3F) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var foreground = new Pen(Color.FromArgb(164, 77, 255), 3F) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        e.Graphics.DrawLine(background, 4, y, Math.Max(4, Width - 4), y);
        var x = 4 + (int)((Width - 8) * Value);
        e.Graphics.DrawLine(foreground, 4, y, x, y);
        using var knob = new SolidBrush(Color.FromArgb(176, 91, 255));
        e.Graphics.FillEllipse(knob, x - 5, y - 5, 10, 10);
    }
}

internal sealed class StatusDot : Control
{
    public Color DotColor { get; set; } = Color.LimeGreen;

    public StatusDot()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(DotColor);
        e.Graphics.FillEllipse(brush, Math.Max(0, Width / 2 - 5), Math.Max(0, Height / 2 - 5), 10, 10);
    }
}
