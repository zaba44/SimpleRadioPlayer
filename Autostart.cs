using Microsoft.Win32;

namespace SimpleRadioPlayer;

static class Autostart
{
    const string Klucz = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Nazwa = "SimpleRadioPlayer";

    static string Polecenie => $"\"{Environment.ProcessPath}\" --tray";

    public static bool Wlaczony
    {
        get
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(Klucz);
                return k?.GetValue(Nazwa) is string;
            }
            catch { return false; }
        }
    }

    public static void Ustaw(bool wlacz)
    {
        using var k = Registry.CurrentUser.CreateSubKey(Klucz);
        if (wlacz) k.SetValue(Nazwa, Polecenie);
        else k.DeleteValue(Nazwa, false);
    }

    /// <summary>Program przeniesiony w inne miejsce — popraw ścieżkę we wpisie autostartu.</summary>
    public static void OdswiezSciezke()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(Klucz, true);
            if (k?.GetValue(Nazwa) is string obecne && obecne != Polecenie)
                k.SetValue(Nazwa, Polecenie);
        }
        catch { }
    }
}
