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
    string?                   _token;
    bool                      _nextGen;
    string                    _lastSrc = "";
    bool                      _failed;          // last translation could not reach DeepL
    bool                      _autoSwitching;   // the app itself is changing the target language
    readonly SemaphoreSlim    _refreshLock = new(1, 1);

    // ── Auto-refresh and offline-retry timers ──
    readonly DispatcherTimer  _refresh;
    readonly DispatcherTimer  _retry;

    public MainWindow()
    {
        InitializeComponent();

        // Timers must be created before setting combo selections (which fire events)
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = TranslateAsync(); };

        _refresh = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
        _refresh.Tick += async (_, _) => await RefreshTokenAsync();
        _refresh.Start();

        // While DeepL is unreachable, try again every few seconds. A network change also
        // triggers a retry right away, see OnNetworkReset.
        _retry = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _retry.Tick += (_, _) =>
        {
            _retry.Interval = TimeSpan.FromSeconds(5);
            if (!_failed) { _retry.Stop(); return; }
            _ = TranslateAsync();
        };
        Net.Shared.Reset += () => Dispatcher.BeginInvoke(OnNetworkReset);

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
        SizeChanged     += (_, _) => RememberGeometry();
        LocationChanged += (_, _) => RememberGeometry();
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

    /// <summary>
    /// Makes sure a usable access token is loaded. Never throws: when DeepL cannot be reached,
    /// the stored login is kept and the status shows that the app is offline.
    /// </summary>
    async Task RefreshTokenAsync(bool force = false)
    {
        await _refreshLock.WaitAsync();
        try
        {
            // 1. Current token still valid → nothing to do
            if (!force && _token != null && !DeepLApi.IsTokenExpired(_token))
                return;

            SetStatus(Loc.StatusChecking);

            // 2. Standard OIDC refresh token
            if (!string.IsNullOrEmpty(_cfg.RefreshToken))
            {
                var result = await AuthManager.RefreshAsync(_cfg.RefreshToken);
                if (result.HasValue)
                {
                    var (access, refresh) = result.Value;
                    _token        = access;
                    _cfg.DlAccess = access;
                    if (refresh != null) _cfg.RefreshToken = refresh;
                    _cfg.Save();
                    SetBadge("Pro ✓"); SetStatus(Loc.StatusReady); return;
                }
            }

            // 3. Cached access token
            if (!force && !string.IsNullOrEmpty(_cfg.DlAccess) && !DeepLApi.IsTokenExpired(_cfg.DlAccess))
            {
                _token = _cfg.DlAccess;
                SetBadge("Pro ✓"); SetStatus(Loc.StatusReady); return;
            }

            _token = null;
            SetBadge("");
            SetStatus(Loc.StatusNotAuth);
        }
        catch (Exception)
        {
            // Offline or DeepL had a server error. Keep whatever login we have and try again later.
            _token ??= _cfg.DlAccess;
            SetStatus(Loc.StatusOffline);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    void OnNetworkReset()
    {
        // Windows reports several changes while a network switch settles, so wait a moment
        // and then retry what failed, using fresh connections.
        if (!_failed) return;
        _retry.Stop();
        _retry.Interval = TimeSpan.FromSeconds(1.5);
        _retry.Start();
    }

    // ──────────────────────────────── Translation ────────────────────────────────────

    async Task TranslateAsync()
    {
        var src = SrcBox.Text.Trim();
        if (src.Length < 3) return;
        if (src == _lastSrc && !_failed) return;
        _lastSrc = src;

        _cts.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        var srcLang = DeepLApi.SrcLangs[(string)SrcCombo.SelectedItem!];
        var tgtLang = DeepLApi.TgtLangs[(string)TgtCombo.SelectedItem!];

        SetStatus(Loc.StatusTranslating);
        try
        {
            (string Text, string? Detected) result;
            try
            {
                result = await DeepLApi.TranslateAsync(src, tgtLang, srcLang, _token, _nextGen, ct);
            }
            catch (AuthExpiredException)
            {
                // The token ran out between refresh checks. Renew it once and try again.
                await RefreshTokenAsync(force: true);
                ct.ThrowIfCancellationRequested();
                if (_token == null) { _failed = false; return; }   // status already says "not logged in"
                result = await DeepLApi.TranslateAsync(src, tgtLang, srcLang, _token, _nextGen, ct);
            }
            ct.ThrowIfCancellationRequested();

            // The text is already in the target language, so switch the target the way the
            // official app does and translate again. The dropdown shows the new target.
            if (srcLang == "auto" && DeepLApi.AutoTarget(result.Detected, tgtLang) is { } newTgt)
            {
                _autoSwitching = true;
                try   { TgtCombo.SelectedItem = DeepLApi.TgtLangs.First(kv => kv.Value == newTgt).Key; }
                finally { _autoSwitching = false; }
                _cfg.TgtLang = newTgt;
                _cfg.Save();
                result = await DeepLApi.TranslateAsync(src, newTgt, srcLang, _token, _nextGen, ct);
                ct.ThrowIfCancellationRequested();
            }

            TgtBox.Text = result.Text;
            _failed = false;
            _retry.Stop();
            SetStatus(Loc.StatusTranslated(result.Detected));
        }
        catch (OperationCanceledException) { }
        catch (OfflineException)
        {
            if (ct.IsCancellationRequested) return;
            _failed = true;
            if (!_retry.IsEnabled) _retry.Start();
            SetStatus(Loc.StatusOffline);
        }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested) return;
            // Not a network problem, so retrying the same text would fail the same way.
            // Clearing _lastSrc lets Ctrl+Enter or the next edit try again.
            _failed  = false;
            _lastSrc = "";
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
        if (_autoSwitching) return;
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
        bool loggedIn = _token != null;
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
        _token             = null;
        _cfg.DlAccess      = null;
        _cfg.RefreshToken  = null;
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

        // Some browsers never start the app from DeepL's "App öffnen" page. As a way around
        // that, the user can copy the page's address, and the login picks it up from the
        // clipboard. AuthManager only accepts it when its state matches this login.
        var clipWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        clipWatch.Tick += (_, _) =>
        {
            string text;
            try { text = Clipboard.GetText().Trim(); }
            catch (Exception) { return; }   // another program holds the clipboard right now
            if (!text.Contains("code=", StringComparison.Ordinal) || !AuthManager.HandleCallback(text))
                return;
            // The address holds a one-time login code, so it should not stay in the clipboard.
            try { Clipboard.Clear(); } catch (Exception) { }
        };
        clipWatch.Start();

        (string Access, string? Refresh, string? Identity)? result;
        try
        {
            result = await AuthManager.LoginAsync();
        }
        catch (Exception)
        {
            SetStatus(Loc.StatusOffline);
            return;
        }
        finally
        {
            clipWatch.Stop();
        }
        if (!result.HasValue)
        {
            SetStatus(Loc.StatusCancelled);
            return;
        }
        var (access, refresh, _) = result.Value;
        _token             = access;
        _cfg.DlAccess      = access;
        _cfg.RefreshToken  = refresh;
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
        SaveSettings();
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

    // Moving or resizing only updates the setting in memory. It is written to disk when the
    // window is hidden or the app exits, instead of on every pixel of a drag.
    void RememberGeometry()
    {
        // Left/Top are -32000 while minimized because the WPF WindowState update lags.
        if (WindowState != WindowState.Normal || Left < -10000 || Top < -10000) return;
        _cfg.Geometry = $"{Left:F0},{Top:F0},{Width:F0},{Height:F0}";
    }

    public void SaveSettings() => _cfg.Save();
}
