using System.Text.Json.Serialization;

namespace SimpleRadioPlayer;

public sealed record Kategoria(string Id, string Nazwa, string[] Tagi)
{
    public override string ToString() => Nazwa;
}

/// <summary>Lista pobrana z Radio Browser — zapamiętana w ustawieniach, żeby dało się ją zaktualizować.</summary>
public sealed class PobranaLista
{
    [JsonPropertyName("plik")] public string Plik { get; set; } = "";
    /// <summary>Kod ISO kraju; pusty = cały świat.</summary>
    [JsonPropertyName("kraj")] public string Kraj { get; set; } = "";
    [JsonPropertyName("kategoria")] public string Kategoria { get; set; } = "";
    [JsonPropertyName("popularne")] public bool Popularne { get; set; }
    [JsonPropertyName("pobrano")] public DateTime Pobrano { get; set; }
}

/// <summary>
/// Gotowe listy z Radio Browser: kraj × kategoria × (najpopularniejsze / wszystkie działające).
/// Tagi w bazie są wolnym tekstem w wielu językach, więc kategoria zbiera kilka tagów;
/// wyszukiwanie tagu w API jest częściowe („rock” łapie też „classic rock”).
/// </summary>
static class ListyRadioBrowser
{
    public const int LimitPopularnych = 200;

    public static readonly Kategoria[] Kategorie =
    [
        new("", "Wszystkie gatunki", []),
        new("pop", "Pop i przeboje", ["pop", "hits", "top 40"]),
        new("rock", "Rock", ["rock", "alternative"]),
        new("metal", "Metal", ["metal"]),
        new("dance", "Dance i elektronika", ["dance", "electronic", "house", "techno", "trance", "edm"]),
        new("chill", "Chillout i relaks", ["chill", "lounge", "ambient", "relax"]),
        new("jazz", "Jazz, blues i soul", ["jazz", "blues", "soul"]),
        new("klasyczna", "Muzyka klasyczna", ["classical", "opera"]),
        new("oldies", "Przeboje lat 60–90", ["oldies", "60s", "70s", "80s", "90s", "classic hits"]),
        new("hiphop", "Hip-hop i R&B", ["hip hop", "hiphop", "rap", "rnb"]),
        new("news", "Informacje i rozmowy", ["news", "talk"]),
        new("religijne", "Katolickie i religijne", ["catholic", "christian", "religious"]),
        new("dzieci", "Dla dzieci", ["kids", "children"]),
    ];

    /// <summary>Polskie nazwy krajów z największą liczbą stacji; pozostałe zostają po angielsku z bazy.</summary>
    static readonly Dictionary<string, string> PolskieNazwy = new()
    {
        ["PL"] = "Polska", ["US"] = "Stany Zjednoczone", ["DE"] = "Niemcy", ["FR"] = "Francja", ["GB"] = "Wielka Brytania",
        ["RU"] = "Rosja", ["MX"] = "Meksyk", ["GR"] = "Grecja", ["CN"] = "Chiny", ["AU"] = "Australia", ["IT"] = "Włochy",
        ["BR"] = "Brazylia", ["CA"] = "Kanada", ["ES"] = "Hiszpania", ["NL"] = "Holandia", ["AR"] = "Argentyna",
        ["AT"] = "Austria", ["CH"] = "Szwajcaria", ["BE"] = "Belgia", ["CZ"] = "Czechy", ["SK"] = "Słowacja",
        ["UA"] = "Ukraina", ["BY"] = "Białoruś", ["LT"] = "Litwa", ["LV"] = "Łotwa", ["EE"] = "Estonia",
        ["HU"] = "Węgry", ["RO"] = "Rumunia", ["BG"] = "Bułgaria", ["RS"] = "Serbia", ["HR"] = "Chorwacja",
        ["SI"] = "Słowenia", ["SE"] = "Szwecja", ["NO"] = "Norwegia", ["DK"] = "Dania", ["FI"] = "Finlandia",
        ["IE"] = "Irlandia", ["PT"] = "Portugalia", ["TR"] = "Turcja", ["IN"] = "Indie", ["JP"] = "Japonia",
        ["KR"] = "Korea Południowa", ["ID"] = "Indonezja", ["PH"] = "Filipiny", ["CO"] = "Kolumbia", ["CL"] = "Chile",
        ["PE"] = "Peru", ["VE"] = "Wenezuela", ["ZA"] = "RPA", ["EG"] = "Egipt", ["IL"] = "Izrael", ["NZ"] = "Nowa Zelandia",
        ["IS"] = "Islandia", ["LU"] = "Luksemburg", ["CY"] = "Cypr", ["MT"] = "Malta", ["MD"] = "Mołdawia",
        ["BA"] = "Bośnia i Hercegowina", ["MK"] = "Macedonia Północna", ["AL"] = "Albania", ["ME"] = "Czarnogóra",
    };

    public static string NazwaKraju(string kod, string angielska = "") =>
        kod.Length == 0 ? "Cały świat" : PolskieNazwy.GetValueOrDefault(kod, angielska.Length > 0 ? angielska : kod);

    public static Kategoria KategoriaWgId(string id) => Kategorie.FirstOrDefault(k => k.Id == id) ?? Kategorie[0];

    public static string NazwaListy(PobranaLista d)
    {
        var czesci = new List<string> { NazwaKraju(d.Kraj) };
        if (d.Kategoria.Length > 0) czesci.Add(KategoriaWgId(d.Kategoria).Nazwa);
        czesci.Add(d.Popularne ? "popularne" : "wszystkie");
        string nazwa = string.Join(" – ", czesci);
        foreach (char c in Path.GetInvalidFileNameChars()) nazwa = nazwa.Replace(c, '-');
        return nazwa;
    }

    /// <summary>Pobiera stacje według definicji: tylko działające, bez powtórzeń adresu, od najpopularniejszych.</summary>
    public static async Task<List<Stacja>> PobierzAsync(PobranaLista d, IProgress<string>? postep, CancellationToken ct)
    {
        string kraj = d.Kraj.Length > 0 ? "&countrycode=" + d.Kraj : "";
        // Dla „najpopularniejszych” wystarczy czoło rankingu każdego zapytania
        string limit = d.Popularne ? "&order=clickcount&reverse=true&limit=1000" : "&limit=100000";
        var kategoria = KategoriaWgId(d.Kategoria);

        var surowe = new Dictionary<string, RadioBrowser.StacjaRb>();
        string[] zapytania = kategoria.Tagi.Length == 0
            ? [$"json/stations/search?hidebroken=true{kraj}{limit}"]
            : kategoria.Tagi.Select(t => $"json/stations/search?hidebroken=true&tag={Uri.EscapeDataString(t)}{kraj}{limit}").ToArray();
        for (int i = 0; i < zapytania.Length; i++)
        {
            postep?.Report(zapytania.Length > 1 ? $"Pobieranie z Radio Browser ({i + 1}/{zapytania.Length})…" : "Pobieranie z Radio Browser…");
            foreach (var s in await RadioBrowser.StacjeAsync(zapytania[i], ct))
                if (s.Uuid.Length > 0 && Playlisty.CzyAdres(s.Url)) surowe.TryAdd(s.Uuid, s);
        }

        postep?.Report("Przygotowanie listy…");
        var znane = new HashSet<string>();
        var wynik = new List<Stacja>();
        foreach (var s in surowe.Values.OrderByDescending(s => s.Popularnosc))
        {
            var st = new Stacja
            {
                Nazwa = string.IsNullOrWhiteSpace(s.Nazwa) ? s.Url : s.Nazwa.Replace('\n', ' ').Replace('\r', ' ').Trim(),
                Url = s.Url,
                Info = Opis(s.Kodek, s.Bitrate),
                Uuid = s.Uuid,
                Logo = s.Logo,
            };
            if (!znane.Add(st.Klucz)) continue;
            wynik.Add(st);
            if (d.Popularne && wynik.Count >= LimitPopularnych) break;
        }
        return wynik;
    }

    static string Opis(string kodek, int bitrate)
    {
        kodek = kodek.Trim().ToUpperInvariant();
        if (kodek.Length == 0 || kodek == "UNKNOWN") kodek = "";
        string kb = bitrate > 0 ? $"{bitrate} kb/s" : "";
        return kodek.Length > 0 && kb.Length > 0 ? $"{kodek} / {kb}" : kodek + kb;
    }

    /// <summary>Zapisuje listę do folderu listy\; poprzednia wersja zostaje jako jedna kopia .bak.</summary>
    public static string Zapisz(PobranaLista d, List<Stacja> stacje)
    {
        string nazwa = NazwaListy(d);
        string plik = Path.Combine(Playlisty.Folder, nazwa + ".m3u8");
        Directory.CreateDirectory(Playlisty.Folder);
        if (File.Exists(plik)) File.Copy(plik, plik + ".bak", true);
        Playlisty.ZapiszM3u8(plik, nazwa, stacje,
            $"Źródło: Radio Browser (radio-browser.info); pobrano {DateTime.Now:yyyy-MM-dd}; tylko działające stacje.");
        d.Plik = Path.GetFileName(plik);
        d.Pobrano = DateTime.Now;
        return plik;
    }
}
