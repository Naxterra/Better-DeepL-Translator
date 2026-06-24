namespace DeepLTranslator;

public static class Loc
{
    public static string Current { get; private set; } = "de";
    public static event Action? Changed;

    public static void SetLanguage(string lang)
    {
        if (Current == lang) return;
        Current = lang;
        Changed?.Invoke();
    }

    static bool En => Current == "en";

    public static string AppTitle        => En ? "Better DeepL Translator"                 : "Better DeepL Übersetzer";
    public static string BtnConnect      => En ? "⚙ Connect"                               : "⚙ Verbindung";
    public static string MenuLogin        => En ? "Login"                                   : "Anmelden";
    public static string MenuSwitch      => En ? "Switch Account"                          : "Konto wechseln";
    public static string MenuLogout      => En ? "Logout"                                  : "Abmelden";
    public static string BtnClear        => En ? "✕ Clear"                                 : "✕ Löschen";
    public static string BtnCopy         => En ? "📋 Copy"                                 : "📋 Kopieren";
    public static string StatusReady     => En ? "Ready"                                   : "Bereit";
    public static string StatusChecking  => En ? "Checking login…"                         : "Anmeldung wird geprüft…";
    public static string StatusBrowser   => En ? "Opening browser…"                        : "Browser wird geöffnet…";
    public static string StatusLoggedIn  => En ? "Logged in."                              : "Angemeldet.";
    public static string StatusLoggedOut => En ? "Logged out."                             : "Abgemeldet.";
    public static string StatusCancelled => En ? "Login cancelled or failed."              : "Anmeldung abgebrochen oder fehlgeschlagen.";
    public static string StatusNotAuth   => En ? "Not logged in — click ⚙ Connect"        : "Nicht angemeldet — bitte ⚙ Connect klicken";
    public static string StatusTranslating => En ? "Translating…"                          : "Übersetze…";
    public static string StatusError     => En ? "Error: "                                 : "Fehler: ";

    public static string MenuStartup      => En ? "Start with Windows"                      : "Mit Windows starten";
    public static string MenuAbout        => En ? "About"                                   : "Über";

    public static string StatusTranslated(string? lang) =>
        lang != null
            ? (En ? $"Translated (detected: {lang})" : $"Übersetzt (erkannt: {lang})")
            : (En ? "Translated" : "Übersetzt");
}
