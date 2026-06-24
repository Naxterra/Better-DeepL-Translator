using System.Windows;

namespace DeepLTranslator;

public partial class TokenDialog : Window
{
    public string Token => TokenBox.Text.Trim().Replace("\r", "").Replace("\n", "").Replace(" ", "");

    public TokenDialog() => InitializeComponent();

    void Ok_Click(object s, RoutedEventArgs e)
    {
        var t = TokenBox.Text.Trim().Replace("\r", "").Replace("\n", "").Replace(" ", "");
        if (!t.StartsWith("eyJ") || t.Split('.').Length != 3)
        {
            ErrText.Text       = "Kein gültiges JWT — muss mit eyJ beginnen und drei Punkte enthalten.";
            ErrText.Visibility = Visibility.Visible;
            return;
        }
        DialogResult = true;
    }

    void Cancel_Click(object s, RoutedEventArgs e) => DialogResult = false;
}
