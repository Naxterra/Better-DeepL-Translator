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
    public static string StatusBrowser   => En ? "Log in in the browser, then click \"Open app\". If nothing happens, copy the page address from the address bar."
                                               : "Im Browser anmelden, dann „App öffnen“ klicken. Passiert nichts, die Adresse aus der Adressleiste kopieren.";
    public static string StatusLoggedIn  => En ? "Logged in."                              : "Angemeldet.";
    public static string StatusLoggedOut => En ? "Logged out."                             : "Abgemeldet.";
    public static string StatusCancelled => En ? "Login cancelled or failed."              : "Anmeldung abgebrochen oder fehlgeschlagen.";
    public static string StatusNotAuth   => En ? "Not logged in. Click ⚙ Connect."         : "Nicht angemeldet. Bitte ⚙ Verbindung klicken.";
    public static string StatusTranslating => En ? "Translating…"                          : "Übersetze…";
    public static string StatusError     => En ? "Error: "                                 : "Fehler: ";
    public static string StatusOffline   => En ? "No connection to DeepL. Retrying automatically…"
                                               : "Keine Verbindung zu DeepL. Neuer Versuch läuft automatisch…";

    public static string MenuStartup      => En ? "Start with Windows"                      : "Mit Windows starten";
    public static string MenuAbout        => En ? "About"                                   : "Über";

    public static string StatusTranslated(string? lang) =>
        lang != null
            ? (En ? $"Translated (detected: {lang})" : $"Übersetzt (erkannt: {lang})")
            : (En ? "Translated" : "Übersetzt");
}
