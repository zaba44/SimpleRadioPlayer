using System.Text.Json;
using System.Text.Json.Serialization;

namespace SimpleRadioPlayer;

public enum TrybStartu { Nic, Ostatnia, Startowa }

public sealed class Konfiguracja
{
    [JsonPropertyName("tryb_startu")]
    [JsonConverter(typeof(JsonStringEnumConverter<TrybStartu>))]
    public TrybStartu TrybStartu { get; set; } = TrybStartu.Ostatnia;

    [JsonPropertyName("ostatnia_stacja")] public Stacja? OstatniaStacja { get; set; }
    [JsonPropertyName("stacja_startowa")] public Stacja? StacjaStartowa { get; set; }
    [JsonPropertyName("glosnosc")] public int Glosnosc { get; set; } = 70;
    [JsonPropertyName("wersja_skali_glosnosci")] public int WersjaSkaliGlosnosci { get; set; }

    /// <summary>Identyfikator urządzenia Windows; pusty = domyślne urządzenie systemowe.</summary>
    [JsonPropertyName("urzadzenie_id")] public string UrzadzenieId { get; set; } = "";
    [JsonPropertyName("urzadzenie_nazwa")] public string UrzadzenieNazwa { get; set; } = "";

    [JsonPropertyName("zamykaj_do_traya")] public bool ZamykajDoTraya { get; set; } = true;

    [JsonPropertyName("pobrane_listy")] public List<PobranaLista> PobraneListy { get; set; } = [];
    /// <summary>Co ile dni pytać o aktualizację pobranych list; 0 = nie pytać.</summary>
    [JsonPropertyName("aktualizacja_list_dni")] public int AktualizacjaListDni { get; set; } = 30;
    /// <summary>Pobranie, aktualizacja albo odpowiedź „Nie” na pytanie — od tej daty liczy się okres.</summary>
    [JsonPropertyName("listy_sprawdzone")] public DateTime ListySprawdzone { get; set; }
    /// <summary>Zgłaszanie odtworzeń do rankingu Radio Browser — domyślnie wyłączone.</summary>
    [JsonPropertyName("zglaszaj_odtworzenia")] public bool ZglaszajOdtworzenia { get; set; }
    /// <summary>Lista wybrana w przełączniku nad stacjami; pusta = wszystkie listy.</summary>
    [JsonPropertyName("wybrana_lista")] public string WybranaLista { get; set; } = "";

    [JsonPropertyName("tryb_logo")]
    [JsonConverter(typeof(JsonStringEnumConverter<TrybLogo>))]
    public TrybLogo TrybLogo { get; set; } = TrybLogo.DobrejJakosci;

    /// <summary>Co ile dni sprawdzać logotypy ponownie; 0 = tylko ręcznie.</summary>
    [JsonPropertyName("odswiezanie_logo_dni")] public int OdswiezanieLogoDni { get; set; } = 30;

    [JsonPropertyName("equalizer")] public UstawieniaEq Equalizer { get; set; } = new();

    /// <summary>0 = wyłączone, 1 = łagodne, 2 = mocne (kompresor dynamiki w LibVLC).</summary>
    [JsonPropertyName("wyrownywanie")] public int Wyrownywanie { get; set; }

    [JsonPropertyName("glosnosc_per_stacja")] public bool GlosnoscPerStacja { get; set; } = true;
    [JsonPropertyName("glosnosc_stacji")] public Dictionary<string, int> GlosnoscStacji { get; set; } = [];

    [JsonIgnore] public string FolderLogo => Path.Combine(Path.GetDirectoryName(Sciezka)!, "logo");
    [JsonPropertyName("ulubione")] public List<Stacja> Ulubione { get; set; } = [];
    [JsonPropertyName("wlasne_stacje")] public List<Stacja> WlasneStacje { get; set; } = [];

    [JsonPropertyName("okno")] public int[]? Okno { get; set; }
    [JsonPropertyName("zakladka")] public int Zakladka { get; set; }

    const string NazwaPliku = "ustawienia.json";
    public static string FolderProgramu => AppContext.BaseDirectory;
    public static string FolderProfilu => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SimpleRadioPlayer");

    /// <summary>true = ustawienia w folderze programu (wersja przenośna), false = w %AppData%.</summary>
    [JsonIgnore] public bool Przenosna { get; private set; } = true;
    [JsonIgnore] public string Sciezka => Path.Combine(Przenosna ? FolderProgramu : FolderProfilu, NazwaPliku);

    /// <summary>
    /// Plik obok exe ma pierwszeństwo, potem plik w profilu. Przy pierwszym starcie
    /// program jest przenośny, chyba że folderu programu nie da się zapisać (np. Program Files).
    /// </summary>
    public static Konfiguracja Wczytaj()
    {
        string przenosny = Path.Combine(FolderProgramu, NazwaPliku);
        string profil = Path.Combine(FolderProfilu, NazwaPliku);
        bool wPrzenosnym = File.Exists(przenosny) || (!File.Exists(profil) && FolderZapisywalny(FolderProgramu));
        string plik = wPrzenosnym ? przenosny : profil;

        // Nowe ustawienia są od razu w bieżącej skali — domyślne 70% nie jest „starą” wartością do przeliczenia
        Konfiguracja k = new() { WersjaSkaliGlosnosci = SkalaGlosnosci.WersjaBiezaca };
        try
        {
            if (File.Exists(plik))
                k = JsonSerializer.Deserialize(File.ReadAllText(plik), KonfiguracjaContext.Default.Konfiguracja) ?? k;
        }
        catch { }
        k.Przenosna = wPrzenosnym;
        k.AktualizujSkaleGlosnosci();
        return k;
    }

    /// <summary>Przelicza zapisane głośności ze starszej skali suwaka, zachowując poziom dźwięku.</summary>
    internal void AktualizujSkaleGlosnosci()
    {
        int wersja = WersjaSkaliGlosnosci;
        if (wersja >= SkalaGlosnosci.WersjaBiezaca) return;
        Glosnosc = SkalaGlosnosci.ZWersji(wersja, Glosnosc);
        foreach (string klucz in GlosnoscStacji.Keys.ToArray())
            GlosnoscStacji[klucz] = SkalaGlosnosci.ZWersji(wersja, GlosnoscStacji[klucz]);
        WersjaSkaliGlosnosci = SkalaGlosnosci.WersjaBiezaca;
    }

    /// <summary>Przenosi plik ustawień do drugiego miejsca i usuwa stary.</summary>
    public bool ZmienMiejsce(bool przenosna)
    {
        if (przenosna == Przenosna) return true;
        string stara = Sciezka;
        Przenosna = przenosna;
        if (!Zapisz())
        {
            Przenosna = !przenosna;
            return false;
        }
        try { File.Delete(stara); } catch { }
        return true;
    }

    public static bool FolderZapisywalny(string folder)
    {
        try
        {
            string test = Path.Combine(folder, $".zapis_{Environment.ProcessId}.tmp");
            File.WriteAllText(test, "");
            File.Delete(test);
            return true;
        }
        catch { return false; }
    }

    public bool Zapisz()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Sciezka)!);
            string tmp = Sciezka + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, KonfiguracjaContext.Default.Konfiguracja));
            File.Move(tmp, Sciezka, true);
            return true;
        }
        catch { return false; }
    }
}

public sealed class UstawieniaEq
{
    [JsonPropertyName("wlaczony")] public bool Wlaczony { get; set; }
    /// <summary>Angielska nazwa presetu z LibVLC (stała między wersjami); pusta = własne ustawienia.</summary>
    [JsonPropertyName("preset")] public string Preset { get; set; } = "Flat";
    [JsonPropertyName("preamp")] public float Preamp { get; set; }
    [JsonPropertyName("pasma")] public float[] Pasma { get; set; } = new float[10];

    public UstawieniaEq Kopia() => new() { Wlaczony = Wlaczony, Preset = Preset, Preamp = Preamp, Pasma = (float[])Pasma.Clone() };
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Konfiguracja))]
partial class KonfiguracjaContext : JsonSerializerContext { }
