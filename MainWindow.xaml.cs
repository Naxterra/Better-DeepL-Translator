using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Clipboard    = System.Windows.Clipboard;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace DeepLTranslator;

public partial class MainWindow : Window
{
    // ── DWM dark title bar ──
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int sz);
    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    // ── State ──
    readonly Config           _cfg  = Config.Load();
    readonly DispatcherTimer  _debounce;
    CancellationTokenSource   _cts  = new();
    Dictionary<string, string>? _cookies;
    bool                      _nextGen;
    string                    _lastSrc = "";

    // ── Auto-refresh timer ──
    readonly DispatcherTimer  _refresh;

    public MainWindow()
    {
        InitializeComponent();

        // Timers must be created before setting combo selections (which fire events)
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = TranslateAsync(); };

        _refresh = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
        _refresh.Tick += async (_, _) => await RefreshTokenAsync();
        _refresh.Start();

        // Restore UI language before populating combos (language names depend on it)
        Loc.SetLanguage(_cfg.UiLang);
        if (_cfg.UiLang == "en") LangEnRb.IsChecked = true;
        else                     LangDeRb.IsChecked  = true;
        Loc.Changed += ApplyLocalization;

        // Populate dropdowns
        SrcCombo.ItemsSource = DeepLApi.SrcLangs.Keys;
        TgtCombo.ItemsSource = DeepLApi.TgtLangs.Keys;

        // Restore config
        SrcCombo.SelectedItem = DeepLApi.SrcLangs.Keys.FirstOrDefault(
            k => DeepLApi.SrcLangs[k] == _cfg.SrcLang, "Automatisch");
        TgtCombo.SelectedItem = DeepLApi.TgtLangs.Keys.FirstOrDefault(
            k => DeepLApi.TgtLangs[k] == _cfg.TgtLang, "Deutsch");

        if (_cfg.Engine == "nextgen") NextgenRb.IsChecked = true;
        else                          ClassicRb.IsChecked = true;

        // Restore geometry
        if (!string.IsNullOrEmpty(_cfg.Geometry))
        {
            var p = _cfg.Geometry.Split(',');
            if (p.Length == 4 &&
                double.TryParse(p[0], out var l) && double.TryParse(p[1], out var t) &&
                double.TryParse(p[2], out var w) && double.TryParse(p[3], out var h))
            { Left = l; Top = t; Width = w; Height = h; }
        }

        // Reflect current startup registry state
        MenuItemStartup.IsChecked = StartupHelper.IsEnabled();

        Loaded          += OnLoaded;
        Closing         += OnClosing;
        SizeChanged     += (_, _) => SaveGeometry();
        LocationChanged += (_, _) => SaveGeometry();
    }

    // ──────────────────────────────────── Startup ────────────────────────────────────

    async void OnLoaded(object s, RoutedEventArgs e)
    {
        // Dark title bar (Windows 11)
        var hwnd  = new WindowInteropHelper(this).Handle;
        var value = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));

        await RefreshTokenAsync();
    }

    async Task RefreshTokenAsync()
    {
        // 1. Current token still valid → nothing to do
        if (_cookies != null &&
            _cookies.TryGetValue("dl_access", out var cur) &&
            !DeepLApi.IsTokenExpired(cur))
            return;

        SetStatus(Loc.StatusChecking);

        // 2. Standard OIDC refresh token
        if (!string.IsNullOrEmpty(_cfg.RefreshToken))
        {
            var result = await AuthManager.RefreshAsync(_cfg.RefreshToken);
            if (result.HasValue)
            {
                var (access, refresh) = result.Value;
                _cookies          = new Dictionary<string, string> { ["dl_access"] = access };
                _cfg.DlAccess     = access;
                if (refresh != null) _cfg.RefreshToken = refresh;
                _cfg.Save();
                SetBadge("Pro ✓"); SetStatus(Loc.StatusReady); return;
            }
        }

        // 3. dl_access cookie from browser (works without OIDC tokens)
        var all = await Task.Run(BraveExtractor.ExtractAll);
        if (all != null && all.TryGetValue("dl_access", out var bTok) && !DeepLApi.IsTokenExpired(bTok))
        {
            _cookies      = all;
            _cfg.DlAccess = bTok;
            _cfg.Save();
            SetBadge("Pro ✓"); SetStatus(Loc.StatusReady); return;
        }

        // 4. Cached access token
        if (!string.IsNullOrEmpty(_cfg.DlAccess) && !DeepLApi.IsTokenExpired(_cfg.DlAccess))
        {
            _cookies = new Dictionary<string, string> { ["dl_access"] = _cfg.DlAccess };
            SetBadge("Pro ✓"); SetStatus(Loc.StatusReady); return;
        }

        _cookies = null;
        SetBadge("");
        SetStatus(Loc.StatusNotAuth);
    }

    // ──────────────────────────────── Translation ────────────────────────────────────

    async Task TranslateAsync()
    {
        var src = SrcBox.Text.Trim();
        if (src.Length < 3 || src == _lastSrc) return;
        _lastSrc = src;

        _cts.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        var srcLang = DeepLApi.SrcLangs[(string)SrcCombo.SelectedItem!];
        var tgtLang = DeepLApi.TgtLangs[(string)TgtCombo.SelectedItem!];

        SetStatus(Loc.StatusTranslating);
        try
        {
            var (text, detected) = await DeepLApi.TranslateAsync(
                src, tgtLang, srcLang, _cookies, _nextGen, ct);
            ct.ThrowIfCancellationRequested();
            TgtBox.Text = text;
            SetStatus(Loc.StatusTranslated(detected));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            SetStatus(Loc.StatusError + ex.Message);
        }
    }

    // ──────────────────────────────── Event handlers ─────────────────────────────────

    void SrcBox_Changed(object s, TextChangedEventArgs e)
    {
        CharCount.Text = $"{SrcBox.Text.Length} / 5000";
        if (SrcBox.Text.Length > 5000) return;
        _debounce.Stop();
        _debounce.Start();
    }

    void SrcBox_KeyDown(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            _debounce.Stop();
            _ = TranslateAsync();
            e.Handled = true;
        }
    }

    void Lang_Changed(object s, SelectionChangedEventArgs e)
    {
        if (SrcCombo.SelectedItem is null || TgtCombo.SelectedItem is null) return;
        _cfg.SrcLang = DeepLApi.SrcLangs[(string)SrcCombo.SelectedItem];
        _cfg.TgtLang = DeepLApi.TgtLangs[(string)TgtCombo.SelectedItem];
        _cfg.Save();
        _lastSrc = "";   // force re-translate
        _debounce.Stop();
        _debounce.Start();
    }

    void Engine_Changed(object s, RoutedEventArgs e)
    {
        _nextGen       = NextgenRb.IsChecked == true;
        _cfg.Engine    = _nextGen ? "nextgen" : "classic";
        _cfg.Save();
        _lastSrc       = "";
        _debounce.Stop();
        _debounce.Start();
    }

    void Swap_Click(object s, RoutedEventArgs e)
    {
        if (SrcCombo.SelectedItem is not string srcName) return;
        var srcCode = DeepLApi.SrcLangs[srcName];
        if (srcCode == "auto") return;   // can't swap auto

        // Find target's matching source entry
        var tgtCode = DeepLApi.TgtLangs[(string)TgtCombo.SelectedItem!];
        var baseTgt = tgtCode.Split('-')[0];

        var newSrc = DeepLApi.SrcLangs.Keys.FirstOrDefault(k => DeepLApi.SrcLangs[k] == baseTgt);
        var newTgt = DeepLApi.TgtLangs.Keys.FirstOrDefault(k => DeepLApi.TgtLangs[k].StartsWith(srcCode));

        if (newSrc != null) SrcCombo.SelectedItem = newSrc;
        if (newTgt != null) TgtCombo.SelectedItem = newTgt;

        (SrcBox.Text, TgtBox.Text) = (TgtBox.Text, SrcBox.Text);
        _lastSrc = "";
    }

    void Clear_Click(object s, RoutedEventArgs e)
    {
        SrcBox.Clear();
        TgtBox.Clear();
        _lastSrc = "";
        SetStatus(Loc.StatusReady);
    }

    void Copy_Click(object s, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(TgtBox.Text))
            Clipboard.SetText(TgtBox.Text);
    }

    void Verbindung_Click(object s, RoutedEventArgs e)
    {
        // Update which auth items are visible before opening
        bool loggedIn = _cookies != null;
        MenuItemLogin.Visibility  = loggedIn ? Visibility.Collapsed : Visibility.Visible;
        MenuItemSwitch.Visibility = loggedIn ? Visibility.Visible   : Visibility.Collapsed;
        MenuItemLogout.Visibility = loggedIn ? Visibility.Visible   : Visibility.Collapsed;

        var cm = VerbindungBtn.ContextMenu;
        cm.PlacementTarget = VerbindungBtn;
        cm.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        cm.IsOpen = true;
    }

    async void Login_Click(object s, RoutedEventArgs e)         => await DoLoginAsync();
    async void SwitchAccount_Click(object s, RoutedEventArgs e) => await DoLoginAsync();

    void Logout_Click(object s, RoutedEventArgs e)
    {
        _cookies           = null;
        _cfg.DlAccess      = null;
        _cfg.RefreshToken  = null;
        _cfg.IdentityToken = null;
        _cfg.Save();
        SetBadge("");
        SetStatus(Loc.StatusLoggedOut);
    }

    void Startup_Click(object s, RoutedEventArgs e)
    {
        StartupHelper.SetEnabled(MenuItemStartup.IsChecked);
    }

    void About_Click(object s, RoutedEventArgs e)
    {
        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        System.Windows.MessageBox.Show(
            $"Better DeepL Translator\nVersion {ver}\n\nAuthor: Naxterra\nhttps://github.com/Naxterra/Better-DeepL-Translator",
            Loc.MenuAbout,
            MessageBoxButton.OK,
            MessageBoxImage.None);
    }

    async Task DoLoginAsync()
    {
        SetStatus(Loc.StatusBrowser);
        var result = await AuthManager.LoginAsync();
        if (!result.HasValue)
        {
            SetStatus(Loc.StatusCancelled);
            return;
        }
        var (access, refresh, identity) = result.Value;
        _cookies           = new Dictionary<string, string> { ["dl_access"] = access };
        _cfg.DlAccess      = access;
        _cfg.RefreshToken  = refresh;
        _cfg.IdentityToken = identity;
        _cfg.Save();
        SetBadge("Pro ✓");
        SetStatus(Loc.StatusLoggedIn);
    }

    void UiLang_Changed(object s, RoutedEventArgs e)
    {
        var lang = LangEnRb.IsChecked == true ? "en" : "de";
        _cfg.UiLang = lang;
        _cfg.Save();
        Loc.SetLanguage(lang);
    }

    // Called by App when Ctrl+C+C fires
    public void OnCtrlCC()
    {
        var text = Clipboard.GetText().Trim();
        if (text.Length < 2) return;

        Show();
        WindowState = WindowState.Normal;
        Activate();
        SrcBox.Text = text;
        SrcBox.CaretIndex = text.Length;
        _lastSrc = "";
        _debounce.Stop();
        _ = TranslateAsync();
    }

    // ──────────────────────────── Minimize to tray ───────────────────────────────────

    void OnClosing(object? s, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    // ──────────────────────────── Localization ────────────────────────────────────────

    void ApplyLocalization()
    {
        TitleText.Text          = Loc.AppTitle;
        VerbindungBtn.Content   = Loc.BtnConnect;
        ClearBtn.Content        = Loc.BtnClear;
        CopyBtn.Content         = Loc.BtnCopy;
        MenuItemLogin.Header    = Loc.MenuLogin;
        MenuItemSwitch.Header   = Loc.MenuSwitch;
        MenuItemLogout.Header   = Loc.MenuLogout;
        MenuItemStartup.Header  = Loc.MenuStartup;
        MenuItemAbout.Header    = Loc.MenuAbout;
        Title                   = Loc.AppTitle;
    }

    // ──────────────────────────── Helpers ────────────────────────────────────────────

    void SetBadge(string text) => Badge.Text = text;

    void SetStatus(string text) => StatusText.Text = text;

    void SaveGeometry()
    {
        // Left/Top are -32000 while minimized — WPF WindowState update lags
        if (WindowState != WindowState.Normal || Left < -10000 || Top < -10000) return;
        _cfg.Geometry = $"{Left:F0},{Top:F0},{Width:F0},{Height:F0}";
        _cfg.Save();
    }
}
