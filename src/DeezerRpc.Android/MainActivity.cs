using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Widget;
using DeezerRpc.Core;
using Uri = Android.Net.Uri;

namespace DeezerRpc.Android;

[Activity(
    Label = "Deezer Presence",
    MainLauncher = true,
    Exported = true,
    Icon = "@mipmap/appicon",
    Theme = "@android:style/Theme.Material.Light.NoActionBar")]
public sealed class MainActivity : Activity
{
    private static readonly global::Android.Graphics.Color Background = Parse("#0B0D10");
    private static readonly global::Android.Graphics.Color Card = Parse("#13161A");
    private static readonly global::Android.Graphics.Color Border = Parse("#30343B");
    private static readonly global::Android.Graphics.Color White = Parse("#F5F5F7");
    private static readonly global::Android.Graphics.Color Muted = Parse("#A7A9B2");
    private static readonly global::Android.Graphics.Color Purple = Parse("#A34DFF");
    private static readonly global::Android.Graphics.Color Green = Parse("#5BC969");

    private readonly HttpClient _images = new() { Timeout = TimeSpan.FromSeconds(8) };
    private FrameLayout? _pageHost;
    private LinearLayout? _bottomNavigation;
    private ImageView? _cover;
    private TextView? _trackTitle;
    private TextView? _artist;
    private TextView? _album;
    private TextView? _currentTime;
    private TextView? _duration;
    private ProgressBar? _progress;
    private TextView? _discordState;
    private TextView? _discordSubstate;
    private TextView? _runtimeState;
    private TextView? _permissionState;
    private global::Android.Widget.Switch? _presenceSwitch;
    private System.Threading.Timer? _refreshTimer;
    private string _page = "home";
    private string? _coverUrl;
    private bool _synchronizing;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Window?.SetStatusBarColor(Background);
        Window?.SetNavigationBarColor(Background);
        SetContentView(BuildShell());
        ShowPage("home");

        if (OperatingSystem.IsAndroidVersionAtLeast(33) &&
            CheckSelfPermission("android.permission.POST_NOTIFICATIONS") != Permission.Granted)
        {
            RequestPermissions(["android.permission.POST_NOTIFICATIONS"], 1540);
        }
    }

    protected override void OnResume()
    {
        base.OnResume();
        RefreshState();
        _refreshTimer?.Dispose();
        _refreshTimer = new System.Threading.Timer(
            _ => RunOnUiThread(RefreshState),
            null,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));
    }

    protected override void OnPause()
    {
        _refreshTimer?.Dispose();
        _refreshTimer = null;
        base.OnPause();
    }

    protected override void OnDestroy()
    {
        _images.Dispose();
        base.OnDestroy();
    }

    private View BuildShell()
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetBackgroundColor(Background);
        _pageHost = new FrameLayout(this);
        root.AddView(_pageHost, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1F));

        _bottomNavigation = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal
        };
        _bottomNavigation.SetGravity(GravityFlags.Center);
        _bottomNavigation.SetBackgroundColor(Parse("#101317"));
        RefreshNavigation();
        root.AddView(_bottomNavigation);
        return root;
    }

    private View NavItem(string icon, string label, string page)
    {
        var item = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            Clickable = true,
            Focusable = true
        };
        item.SetGravity(GravityFlags.Center);
        item.SetPadding(0, Dp(7), 0, Dp(5));
        item.AddView(Text(icon, 20F, page == _page ? Purple : Muted, gravity: GravityFlags.Center));
        item.AddView(Text(label, 10F, page == _page ? Purple : Muted, gravity: GravityFlags.Center));
        item.Click += (_, _) => ShowPage(page);
        return item;
    }

    private void ShowPage(string page)
    {
        _page = page;
        RefreshNavigation();
        _pageHost?.RemoveAllViews();
        _pageHost?.AddView(page switch
        {
            "settings" => BuildSettingsPage(),
            _ => BuildHomePage()
        });
        RefreshState();
    }

    private void RefreshNavigation()
    {
        if (_bottomNavigation is null)
        {
            return;
        }

        _bottomNavigation.RemoveAllViews();
        _bottomNavigation.AddView(NavItem("⌂", "Accueil", "home"), new LinearLayout.LayoutParams(0, Dp(68), 1F));
        _bottomNavigation.AddView(NavItem("⚙", "Paramètres", "settings"), new LinearLayout.LayoutParams(0, Dp(68), 1F));
    }

    private View BuildHomePage()
    {
        var scroll = new ScrollView(this);
        scroll.SetBackgroundColor(Background);
        var content = Vertical(Dp(22), Dp(22));

        var header = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        header.SetGravity(GravityFlags.CenterVertical);
        header.AddView(Text("Deezer Presence", 24F, White, bold: true), new LinearLayout.LayoutParams(0, Dp(54), 1F));
        _presenceSwitch = NewSwitch(AndroidSettings.GetAppSettings(this).RichPresenceEnabled);
        _presenceSwitch.CheckedChange += (_, args) =>
        {
            if (!_synchronizing)
            {
                Save(settings => settings with { RichPresenceEnabled = args.IsChecked });
            }
        };
        header.AddView(_presenceSwitch, new LinearLayout.LayoutParams(Dp(60), Dp(48)));
        content.AddView(header);

        var section = Text("Lecture en cours  •", 17F, White, bold: true);
        section.SetPadding(0, Dp(10), 0, Dp(12));
        content.AddView(section);

        var playback = Vertical(Dp(16), Dp(16));
        _cover = new ImageView(this);
        _cover.SetImageResource(Resource.Mipmap.appicon);
        _cover.SetScaleType(ImageView.ScaleType.CenterCrop);
        playback.AddView(_cover, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(290)));

        _trackTitle = Text("En attente de Deezer", 20F, White, bold: true);
        _trackTitle.SetPadding(0, Dp(16), 0, 0);
        playback.AddView(_trackTitle);
        _artist = Text("Lance une musique", 15F, Purple);
        _artist.SetPadding(0, Dp(6), 0, 0);
        playback.AddView(_artist);
        _album = Text("La pochette apparaîtra ici", 13F, Muted);
        _album.SetPadding(0, Dp(4), 0, Dp(9));
        playback.AddView(_album);

        var progressRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        progressRow.SetGravity(GravityFlags.CenterVertical);
        _currentTime = Text("00:00", 11F, Muted);
        _duration = Text("00:00", 11F, Muted, gravity: GravityFlags.Right | GravityFlags.CenterVertical);
        _progress = new ProgressBar(this, null, global::Android.Resource.Attribute.ProgressBarStyleHorizontal)
        {
            Max = 1000,
            Progress = 0,
            ProgressTintList = global::Android.Content.Res.ColorStateList.ValueOf(Purple),
            ProgressBackgroundTintList = global::Android.Content.Res.ColorStateList.ValueOf(Parse("#41444A"))
        };
        progressRow.AddView(_currentTime, new LinearLayout.LayoutParams(Dp(46), Dp(28)));
        progressRow.AddView(_progress, new LinearLayout.LayoutParams(0, Dp(28), 1F));
        progressRow.AddView(_duration, new LinearLayout.LayoutParams(Dp(50), Dp(28)));
        playback.AddView(progressRow);
        content.AddView(CardView(playback));

        var deezer = OutlineButton("🔗   Écouter sur Deezer   ↗");
        deezer.Click += (_, _) => OpenTrack();
        content.AddView(deezer, Margin(top: 12, height: 54));

        var discordContent = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        discordContent.SetGravity(GravityFlags.CenterVertical);
        var discordIcon = Text("●", 28F, Purple, gravity: GravityFlags.Center);
        discordContent.AddView(discordIcon, new LinearLayout.LayoutParams(Dp(54), Dp(70)));
        var discordText = Vertical(0, 0);
        _discordState = Text("Connexion Discord", 14F, White, bold: true);
        _discordSubstate = Text("Ouvre Discord et connecte-toi", 11F, Muted);
        discordText.AddView(_discordState);
        discordText.AddView(_discordSubstate);
        discordContent.AddView(discordText, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1F));
        content.AddView(CardView(discordContent), Margin(top: 12));

        var connect = OutlineButton("Se connecter à Discord");
        connect.Click += (_, _) => OpenDiscord();
        content.AddView(connect, Margin(top: 10, height: 52));

        _runtimeState = Text("Initialisation…", 11F, Muted);
        _runtimeState.SetPadding(Dp(4), Dp(14), Dp(4), Dp(8));
        content.AddView(_runtimeState);
        scroll.AddView(content);
        return scroll;
    }

    private View BuildSettingsPage()
    {
        var scroll = new ScrollView(this);
        var content = Vertical(Dp(22), Dp(24));
        content.AddView(PageHeading("Paramètres", "Affichage et fonctionnement en arrière-plan"));
        var settings = AndroidSettings.GetAppSettings(this);
        content.AddView(Section("AFFICHAGE"));
        content.AddView(SettingRow("Afficher l’album", "Ligne séparée sous le nom de l’artiste", settings.ShowAlbum, value => Save(s => s with { ShowAlbum = value })));
        content.AddView(SettingRow("Afficher la progression", "Durée et progression natives Discord", settings.ShowProgress, value => Save(s => s with { ShowProgress = value })));
        content.AddView(SettingRow("Afficher le bouton Deezer", "Lien direct vers le morceau", settings.ShowDeezerButton, value => Save(s => s with { ShowDeezerButton = value })));
        content.AddView(Section("GÉNÉRAL"));
        content.AddView(SettingRow("Fonctionner en arrière-plan", "Maintient la détection active", settings.KeepRunningInBackground, value => Save(s => s with { KeepRunningInBackground = value })));
        content.AddView(SettingRow("Notifications", "Notification permanente de fonctionnement", settings.ShowNotifications, value => Save(s => s with { ShowNotifications = value })));

        _permissionState = Text(string.Empty, 12F, Muted);
        _permissionState.SetPadding(Dp(4), Dp(20), Dp(4), Dp(8));
        content.AddView(_permissionState);
        var permission = OutlineButton("Autoriser l’accès média Deezer");
        permission.Click += (_, _) => StartActivity(new Intent(Settings.ActionNotificationListenerSettings));
        content.AddView(permission, Margin(height: 52));
        scroll.AddView(content);
        return scroll;
    }

    private View PageHeading(string title, string subtitle)
    {
        var heading = Vertical(0, 0);
        heading.AddView(Text(title, 25F, White, bold: true));
        var sub = Text(subtitle, 13F, Muted);
        sub.SetPadding(0, Dp(7), 0, Dp(12));
        heading.AddView(sub);
        return heading;
    }

    private View Section(string title)
    {
        var label = Text(title, 12F, Purple, bold: true);
        label.SetPadding(Dp(2), Dp(18), 0, Dp(10));
        return label;
    }

    private View SettingRow(string title, string subtitle, bool value, Action<bool> changed)
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetPadding(Dp(15), Dp(10), Dp(10), Dp(10));
        var text = Vertical(0, 0);
        text.AddView(Text(title, 14F, White, bold: true));
        text.AddView(Text(subtitle, 11F, Muted));
        row.AddView(text, new LinearLayout.LayoutParams(0, Dp(62), 1F));
        var toggle = NewSwitch(value);
        toggle.CheckedChange += (_, args) => changed(args.IsChecked);
        row.AddView(toggle, new LinearLayout.LayoutParams(Dp(60), Dp(48)));
        return CardView(row, bottomMargin: 8);
    }

    private global::Android.Widget.Switch NewSwitch(bool value)
    {
        var toggle = new global::Android.Widget.Switch(this) { Checked = value, ShowText = false };
        toggle.ThumbTintList = global::Android.Content.Res.ColorStateList.ValueOf(White);
        toggle.TrackTintList = global::Android.Content.Res.ColorStateList.ValueOf(value ? Purple : Parse("#555960"));
        toggle.CheckedChange += (_, args) => toggle.TrackTintList = global::Android.Content.Res.ColorStateList.ValueOf(args.IsChecked ? Purple : Parse("#555960"));
        return toggle;
    }

    private TextView OutlineButton(string text)
    {
        var button = Text(text, 14F, Purple, bold: true, gravity: GravityFlags.Center);
        button.Background = Rounded(Card, Purple, 10);
        button.Clickable = true;
        button.Focusable = true;
        return button;
    }

    private View CardView(View content, int bottomMargin = 0)
    {
        var frame = new FrameLayout(this) { Background = Rounded(Card, Border, 12) };
        frame.AddView(content);
        frame.LayoutParameters = Margin(bottom: bottomMargin);
        return frame;
    }

    private LinearLayout Vertical(int horizontalPadding, int verticalPadding)
    {
        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        layout.SetPadding(horizontalPadding, verticalPadding, horizontalPadding, verticalPadding);
        return layout;
    }

    private TextView Text(string value, float size, global::Android.Graphics.Color color, bool bold = false, GravityFlags gravity = GravityFlags.CenterVertical)
    {
        var view = new TextView(this)
        {
            Text = value,
            TextSize = size,
            Gravity = gravity
        };
        view.SetTextColor(color);
        if (bold)
        {
            view.SetTypeface(null, TypefaceStyle.Bold);
        }
        return view;
    }

    private LinearLayout.LayoutParams Margin(int left = 0, int top = 0, int right = 0, int bottom = 0, int height = -2)
    {
        var parameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, height == -2 ? ViewGroup.LayoutParams.WrapContent : Dp(height));
        parameters.SetMargins(Dp(left), Dp(top), Dp(right), Dp(bottom));
        return parameters;
    }

    private GradientDrawable Rounded(global::Android.Graphics.Color fill, global::Android.Graphics.Color stroke, int radius)
    {
        var background = new GradientDrawable();
        background.SetColor(fill);
        background.SetCornerRadius(Dp(radius));
        background.SetStroke(Dp(1), stroke);
        return background;
    }

    private int Dp(int value) => (int)(value * (Resources?.DisplayMetrics?.Density ?? 1F));

    private void Save(Func<AndroidAppSettings, AndroidAppSettings> update)
    {
        var settings = update(AndroidSettings.GetAppSettings(this));
        AndroidSettings.SaveAppSettings(this, settings);
        RefreshState();
    }

    private void RefreshState()
    {
        var permission = Settings.Secure.GetString(ContentResolver, "enabled_notification_listeners")
            ?.Contains(PackageName ?? string.Empty, StringComparison.OrdinalIgnoreCase) == true;
        if (_permissionState is not null)
        {
            _permissionState.Text = permission ? "●  Accès média Deezer autorisé" : "Accès requis pour détecter automatiquement Deezer";
            _permissionState.SetTextColor(permission ? Green : Muted);
        }

        if (_page != "home" || _trackTitle is null)
        {
            return;
        }

        var settings = AndroidSettings.GetAppSettings(this);
        if (_presenceSwitch is not null)
        {
            _synchronizing = true;
            _presenceSwitch.Checked = settings.RichPresenceEnabled;
            _synchronizing = false;
        }

        var snapshot = AndroidSettings.GetPlayback(this);
        var track = snapshot.Track;
        if (track is null)
        {
            _trackTitle.Text = "En attente de Deezer";
            _artist!.Text = permission ? "Lance une musique" : "Autorise l’accès média";
            _album!.Text = "La pochette apparaîtra ici";
            _album.Visibility = ViewStates.Visible;
            _currentTime!.Text = "00:00";
            _duration!.Text = "00:00";
            _progress!.Progress = 0;
            if (_coverUrl is not null)
            {
                _coverUrl = null;
                _cover!.SetImageResource(Resource.Mipmap.appicon);
            }
        }
        else
        {
            var position = track.ProjectPosition(DateTimeOffset.UtcNow);
            _trackTitle.Text = track.Title;
            _artist!.Text = track.Artist;
            _album!.Visibility = settings.ShowAlbum && !string.IsNullOrWhiteSpace(track.Album)
                ? ViewStates.Visible
                : ViewStates.Gone;
            _album.Text = track.Album.Trim();
            _currentTime!.Text = FormatTime(position);
            _duration!.Text = FormatTime(track.Duration);
            _progress!.Progress = track.Duration > TimeSpan.Zero
                ? (int)(Math.Clamp(position.TotalSeconds / track.Duration.TotalSeconds, 0, 1) * 1000)
                : 0;
            _ = LoadCoverAsync(track.CoverUrl);
        }

        _discordState!.Text = snapshot.DiscordConnected ? "Discord connecté" : "Connexion Discord";
        _discordSubstate!.Text = snapshot.DiscordConnected ? "Rich Presence active" : "Ouvre Discord et connecte-toi";
        _runtimeState!.Text = snapshot.StatusText;
    }

    private async Task LoadCoverAsync(System.Uri? uri)
    {
        if (uri is null || _coverUrl == uri.AbsoluteUri)
        {
            return;
        }

        var requested = uri.AbsoluteUri;
        _coverUrl = requested;
        try
        {
            var bytes = await _images.GetByteArrayAsync(uri);
            var bitmap = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length);
            if (bitmap is not null && _coverUrl == requested)
            {
                RunOnUiThread(() => _cover?.SetImageBitmap(bitmap));
            }
        }
        catch
        {
            if (_coverUrl == requested)
            {
                _coverUrl = null;
                RunOnUiThread(() => _cover?.SetImageResource(Resource.Mipmap.appicon));
            }
        }
    }

    private void OpenDiscord()
    {
        var launchIntent = PackageManager?.GetLaunchIntentForPackage("com.discord");
        if (launchIntent is null)
        {
            Toast.MakeText(this, "Discord Android n’est pas installé", ToastLength.Long)?.Show();
            return;
        }

        StartActivity(launchIntent);
    }

    private void OpenTrack()
    {
        var track = AndroidSettings.GetPlayback(this).Track;
        if (track is not null)
        {
            var url = DeezerLinks.GetListenUri(track);
            StartActivity(new Intent(Intent.ActionView, Uri.Parse(url.AbsoluteUri)));
        }
    }

    private static string FormatTime(TimeSpan value)
    {
        var seconds = Math.Max(0, (int)value.TotalSeconds);
        return seconds >= 3600 ? $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}" : $"{seconds / 60:00}:{seconds % 60:00}";
    }

    private static global::Android.Graphics.Color Parse(string value) => global::Android.Graphics.Color.ParseColor(value);
}
