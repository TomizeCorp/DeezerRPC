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
    private TextView? _runtimeState;
    private TextView? _permissionState;
    private global::Android.Widget.Switch? _presenceSwitch;
    private System.Threading.Timer? _refreshTimer;
    private string _page = "home";
    private string? _coverUrl;
    private string? _navigationSignature;
    private bool _synchronizing;
    private bool _connectingDiscord;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Window?.SetStatusBarColor(Background);
        Window?.SetNavigationBarColor(Background);
        SetContentView(BuildShell());
        ShowPage("home");

        if (AndroidSettings.IsDiscordConnectionEnabled(this) &&
            !DiscordSocialSdkInitializer.IsInitialized &&
            !DiscordSocialSdkInitializer.TryInitialize(this, out var discordError))
        {
            AndroidSettings.SetLastStatus(this, discordError);
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(33) &&
            CheckSelfPermission("android.permission.POST_NOTIFICATIONS") != Permission.Granted)
        {
            RequestPermissions(["android.permission.POST_NOTIFICATIONS"], 1540);
        }
    }

    protected override void OnResume()
    {
        base.OnResume();
        if (DiscordOAuthBrowserFlow.HasPendingAuthorization)
        {
            DiscordOAuthBrowserFlow.Cancel("Connexion Discord annulée");
        }
        if (AndroidSettings.IsDiscordConnectionEnabled(this) &&
            DiscordSocialSdkInitializer.IsInitialized &&
            AndroidSettings.GetDiscordOAuthTokens(this) is not null)
        {
            _ = ConnectDiscordAccountAsync();
        }
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

    private View DiscordNavItem(AndroidPlaybackSnapshot snapshot)
    {
        var item = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            Clickable = true,
            Focusable = true
        };
        item.SetGravity(GravityFlags.Center);
        item.SetPadding(0, Dp(7), 0, Dp(5));

        if (snapshot.DiscordAccountConnected &&
            System.Uri.TryCreate(snapshot.DiscordAccount?.AvatarUrl, UriKind.Absolute, out var avatarUri))
        {
            var avatar = new ImageView(this)
            {
                ClipToOutline = true,
                Background = Circle(Parse("#252932"))
            };
            avatar.SetScaleType(ImageView.ScaleType.CenterCrop);
            avatar.SetImageResource(Resource.Mipmap.appicon);
            item.AddView(avatar, new LinearLayout.LayoutParams(Dp(26), Dp(26)));
            _ = LoadAvatarAsync(avatarUri, avatar);
        }
        else
        {
            item.AddView(
                new MonochromeLogoView(this, MonochromeLogo.Discord, Muted),
                new LinearLayout.LayoutParams(Dp(27), Dp(27)));
        }

        item.AddView(Text("Discord", 10F, Muted, gravity: GravityFlags.Center));
        item.Click += (_, _) =>
        {
            if (AndroidSettings.GetPlayback(this).DiscordAccountConnected)
            {
                ShowDiscordProfile();
            }
            else
            {
                InitializeAndOpenDiscord();
            }
        };
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
        var snapshot = AndroidSettings.GetPlayback(this);
        _bottomNavigation.AddView(DiscordNavItem(snapshot), new LinearLayout.LayoutParams(0, Dp(68), 1F));
        _navigationSignature = NavigationSignature(snapshot);
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

        var deezer = MonochromeButton(MonochromeLogo.Deezer, "Écouter sur Deezer");
        deezer.Click += (_, _) => OpenTrack();
        content.AddView(deezer, Margin(top: 12, height: 54));

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
        button.Background = Rounded(Card, Purple, 16);
        button.Clickable = true;
        button.Focusable = true;
        return button;
    }

    private View MonochromeButton(MonochromeLogo logo, string text)
    {
        var button = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal,
            Clickable = true,
            Focusable = true,
            Background = Rounded(Card, Purple, 16)
        };
        button.SetGravity(GravityFlags.Center);
        button.AddView(
            new MonochromeLogoView(this, logo, Purple),
            new LinearLayout.LayoutParams(Dp(22), Dp(22)));
        var label = Text(text, 14F, Purple, bold: true, gravity: GravityFlags.Center);
        label.SetPadding(Dp(10), 0, 0, 0);
        button.AddView(label, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.MatchParent));
        return button;
    }

    private View CardView(View content, int bottomMargin = 0)
    {
        var frame = new FrameLayout(this) { Background = Rounded(Card, Border, 18) };
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

    private GradientDrawable Circle(global::Android.Graphics.Color fill)
    {
        var background = new GradientDrawable();
        background.SetShape(ShapeType.Oval);
        background.SetColor(fill);
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

        var snapshot = AndroidSettings.GetPlayback(this);
        var navigationSignature = NavigationSignature(snapshot);
        if (!string.Equals(_navigationSignature, navigationSignature, StringComparison.Ordinal))
        {
            RefreshNavigation();
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

        _runtimeState!.Text = snapshot.StatusText;
    }

    private static string NavigationSignature(AndroidPlaybackSnapshot snapshot) => string.Join('|',
        snapshot.DiscordAccountConnected,
        snapshot.DiscordAccount?.UserId,
        snapshot.DiscordAccount?.DisplayName,
        snapshot.DiscordAccount?.Username,
        snapshot.DiscordAccount?.AvatarUrl);

    private void InitializeAndOpenDiscord()
    {
        AndroidSettings.SetDiscordConnectionEnabled(this, true);
        if (!DiscordSocialSdkInitializer.TryInitialize(this, out var error))
        {
            AndroidSettings.SetLastStatus(this, error);
            RefreshState();
            return;
        }

        AndroidSettings.SetLastStatus(this, "Ouverture de la connexion sécurisée Discord…");
        Toast.MakeText(this, "Ouverture de Discord…", ToastLength.Short)?.Show();
        RefreshState();
        if (AndroidSettings.GetDiscordOAuthTokens(this) is not null)
        {
            _ = ConnectDiscordAccountAsync();
        }
        else
        {
            _ = AuthorizeDiscordAccountAsync();
        }
    }

    private async Task ConnectDiscordAccountAsync()
    {
        if (_connectingDiscord)
        {
            return;
        }

        _connectingDiscord = true;
        try
        {
            var result = await DiscordMobileConnection.EnsureConnectedAsync(this);
            if (!AndroidSettings.IsDiscordConnectionEnabled(this))
            {
                return;
            }

            if (result.Connected)
            {
                if (AndroidSettings.GetPlayback(this).Track is null)
                {
                    var name = string.IsNullOrWhiteSpace(result.Profile?.DisplayName)
                        ? "compte lié"
                        : result.Profile.DisplayName;
                    AndroidSettings.SetLastStatus(this, $"Discord connecté — {name}");
                }
            }
            else if (AndroidSettings.GetPlayback(this).Track is null)
            {
                AndroidSettings.SetLastStatus(this, result.Error);
            }
        }
        finally
        {
            _connectingDiscord = false;
            RunOnUiThread(RefreshState);
        }
    }

    private async Task AuthorizeDiscordAccountAsync()
    {
        if (_connectingDiscord)
        {
            return;
        }

        _connectingDiscord = true;
        try
        {
            var result = await DiscordMobileConnection.AuthorizeAsync(this);
            if (!AndroidSettings.IsDiscordConnectionEnabled(this))
            {
                return;
            }

            if (result.Connected)
            {
                var name = string.IsNullOrWhiteSpace(result.Profile?.DisplayName)
                    ? "compte lié"
                    : result.Profile.DisplayName;
                AndroidSettings.SetLastStatus(this, $"Discord connecté — {name}");
            }
            else if (AndroidSettings.GetDiscordOAuthTokens(this) is not null)
            {
                AndroidSettings.SetLastStatus(this, "Compte Discord lié — reconnexion automatique en cours");
            }
            else
            {
                AndroidSettings.SetLastStatus(this, result.Error);
                RunOnUiThread(() =>
                    Toast.MakeText(this, result.Error, ToastLength.Long)?.Show());
            }
        }
        finally
        {
            _connectingDiscord = false;
            RunOnUiThread(RefreshState);
        }
    }

    private void ShowDiscordProfile()
    {
        var snapshot = AndroidSettings.GetPlayback(this);
        if (!snapshot.DiscordAccountConnected)
        {
            InitializeAndOpenDiscord();
            return;
        }

        var profile = snapshot.DiscordAccount;
        var dialog = new Dialog(this);
        var content = Vertical(Dp(24), Dp(22));
        content.Background = Rounded(Card, Border, 24);

        if (System.Uri.TryCreate(profile?.AvatarUrl, UriKind.Absolute, out var avatarUri))
        {
            var avatar = new ImageView(this)
            {
                ClipToOutline = true,
                Background = Circle(Parse("#252932"))
            };
            avatar.SetScaleType(ImageView.ScaleType.CenterCrop);
            avatar.SetImageResource(Resource.Mipmap.appicon);
            var avatarParams = new LinearLayout.LayoutParams(Dp(76), Dp(76))
            {
                Gravity = GravityFlags.CenterHorizontal
            };
            content.AddView(avatar, avatarParams);
            _ = LoadAvatarAsync(avatarUri, avatar);
        }
        else
        {
            var logoParams = new LinearLayout.LayoutParams(Dp(70), Dp(70))
            {
                Gravity = GravityFlags.CenterHorizontal
            };
            content.AddView(new MonochromeLogoView(this, MonochromeLogo.Discord, Purple), logoParams);
        }

        var displayName = string.IsNullOrWhiteSpace(profile?.DisplayName)
            ? "Discord connecté"
            : profile.DisplayName;
        var title = Text(displayName, 20F, White, bold: true, gravity: GravityFlags.Center);
        title.SetPadding(0, Dp(14), 0, 0);
        content.AddView(title);
        if (!string.IsNullOrWhiteSpace(profile?.Username))
        {
            var username = Text($"@{profile.Username}", 13F, Muted, gravity: GravityFlags.Center);
            username.SetPadding(0, Dp(4), 0, 0);
            content.AddView(username);
        }

        var state = Text("Compte utilisé pour la Rich Presence", 12F, Muted, gravity: GravityFlags.Center);
        state.SetPadding(0, Dp(10), 0, Dp(18));
        content.AddView(state);

        var disconnect = OutlineButton("Se déconnecter");
        disconnect.Click += (_, _) =>
        {
            AndroidDiscordPresenceClient.DisconnectExisting();
            DiscordSocialSdkInitializer.Reset();
            AndroidSettings.DisconnectDiscordAccount(this);
            AndroidSettings.SetLastStatus(this, "Discord déconnecté");
            dialog.Dismiss();
            RefreshState();
        };
        content.AddView(disconnect, Margin(height: 52));

        var close = Text("Fermer", 13F, Muted, bold: true, gravity: GravityFlags.Center);
        close.SetPadding(0, Dp(16), 0, Dp(4));
        close.Clickable = true;
        close.Click += (_, _) => dialog.Dismiss();
        content.AddView(close, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(48)));

        dialog.SetContentView(content);
        dialog.Window?.SetBackgroundDrawable(new ColorDrawable(global::Android.Graphics.Color.Transparent));
        dialog.Show();
        dialog.Window?.SetLayout(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        dialog.Window?.DecorView.SetPadding(Dp(22), 0, Dp(22), 0);
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
            Bitmap? bitmap;
            if (uri.Scheme == System.Uri.UriSchemeHttps)
            {
                var bytes = await _images.GetByteArrayAsync(uri);
                bitmap = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length);
            }
            else if (uri.Scheme == System.Uri.UriSchemeFile && !string.IsNullOrWhiteSpace(uri.LocalPath))
            {
                bitmap = await Task.Run(() => BitmapFactory.DecodeFile(uri.LocalPath));
            }
            else
            {
                bitmap = null;
            }
            if (bitmap is not null && _coverUrl == requested)
            {
                RunOnUiThread(() => _cover?.SetImageBitmap(bitmap));
            }
            else if (bitmap is null)
            {
                throw new InvalidOperationException("Pochette Deezer illisible");
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

    private async Task LoadAvatarAsync(System.Uri uri, ImageView target)
    {
        try
        {
            var bytes = await _images.GetByteArrayAsync(uri);
            var bitmap = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length);
            if (bitmap is not null)
            {
                RunOnUiThread(() => target.SetImageBitmap(bitmap));
            }
        }
        catch
        {
            // The monochrome application icon remains as a local fallback.
        }
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
