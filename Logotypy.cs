using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SimpleRadioPlayer;

public enum TrybLogo { Wszystkie, DobrejJakosci, TylkoKafelki }

public sealed class WpisLogo
{
    [JsonPropertyName("plik")] public string? Plik { get; set; }
    /// <summary>Krótszy bok oryginału w pikselach — miara jakości.</summary>
    [JsonPropertyName("jakosc")] public int Jakosc { get; set; }
    [JsonPropertyName("zrodlo")] public string Zrodlo { get; set; } = "";
    [JsonPropertyName("sprawdzono")] public DateTime Sprawdzono { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<string, WpisLogo>))]
partial class LogoContext : JsonSerializerContext { }

/// <summary>
/// Logotypy stacji: najpierw pole „favicon” z bazy Radio Browser (szukanie po adresie
/// strumienia), potem ikony ze strony WWW stacji (apple-touch-icon, link rel=icon, favicon.ico).
/// Wybierany jest największy obraz. Wyniki — także „nie znaleziono” — trafiają do folderu
/// logo\ z indeksem, żeby nie pytać sieci przy każdym starcie.
/// </summary>
sealed partial class Logotypy : IDisposable
{
    /// <summary>Krótszy bok od tej wartości = logo „słabej jakości” (typowa favikona 16–48 px).</summary>
    public const int MinDobrejJakosci = 64;
    const int RozmiarZapisu = 128;
    const int MaxBajtow = 2 * 1024 * 1024;

    readonly string _folder;
    readonly string _plikIndeksu;
    readonly Dictionary<string, WpisLogo> _indeks;
    readonly Dictionary<string, Bitmap> _obrazy = [];
    readonly HashSet<string> _wToku = [];
    readonly object _blokada = new();
    readonly SemaphoreSlim _limit = new(3);
    readonly HttpClient _http;
    readonly CancellationTokenSource _koniec = new();
    readonly System.Threading.Timer _zapisIndeksu;

    public TrybLogo Tryb { get; set; } = TrybLogo.DobrejJakosci;
    /// <summary>Po ilu dniach sprawdzić logo ponownie; 0 = tylko ręcznie.</summary>
    public int DniOdswiezania { get; set; } = 30;

    /// <summary>Logo stacji (klucz) się zmieniło. Wywoływane z wątku w tle.</summary>
    public event Action<string>? Zaktualizowano;

    public Logotypy(string folder)
    {
        _folder = folder;
        _plikIndeksu = Path.Combine(folder, "indeks.json");
        _indeks = WczytajIndeks();
        _zapisIndeksu = new System.Threading.Timer(_ => ZapiszIndeks());

        var obsluga = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
        };
        _http = new HttpClient(obsluga) { Timeout = TimeSpan.FromSeconds(10) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SimpleRadioPlayer/1.0");
    }

    /// <summary>
    /// Logo do wyświetlenia albo null (wtedy kafelek). W razie potrzeby zleca pobranie w tle —
    /// wynik przyjdzie zdarzeniem Zaktualizowano.
    /// </summary>
    public Bitmap? Logo(Stacja s)
    {
        if (Tryb == TrybLogo.TylkoKafelki) return null;

        WpisLogo? wpis;
        lock (_blokada) _indeks.TryGetValue(s.Klucz, out wpis);
        if (wpis == null || Przeterminowany(wpis)) Pobierz(s);
        if (wpis?.Plik == null) return null;
        if (Tryb == TrybLogo.DobrejJakosci && wpis.Jakosc < MinDobrejJakosci) return null;

        lock (_blokada)
        {
            // UI dostaje własną kopię: pobieranie w tle może zwolnić obraz z cache.
            if (_obrazy.TryGetValue(s.Klucz, out var bmp)) return new Bitmap(bmp);
            try
            {
                // Bitmap ze strumienia potrzebuje go przez całe życie obrazu, a Bitmap(plik)
                // blokuje plik — dlatego kopia niezależna od jednego i drugiego
                using var ms = new MemoryStream(File.ReadAllBytes(Path.Combine(_folder, wpis.Plik)));
                using var wczytany = Image.FromStream(ms);
                bmp = new Bitmap(wczytany);
                _obrazy[s.Klucz] = bmp;
                return new Bitmap(bmp);
            }
            catch
            {
                return null;
            }
        }
    }

    bool Przeterminowany(WpisLogo w) =>
        w.Sprawdzono == DateTime.MinValue ||
        (DniOdswiezania > 0 && DateTime.Now - w.Sprawdzono > TimeSpan.FromDays(DniOdswiezania));

    /// <summary>Wszystkie logotypy zostaną sprawdzone ponownie przy najbliższym wyświetleniu.</summary>
    public void OznaczDoOdswiezenia(IEnumerable<string>? klucze = null)
    {
        lock (_blokada)
        {
            foreach (var (klucz, w) in _indeks)
                if (klucze == null || klucze.Contains(klucz))
                    w.Sprawdzono = DateTime.MinValue;
        }
    }

    public void Pobierz(Stacja s)
    {
        if (Tryb == TrybLogo.TylkoKafelki) return;
        lock (_blokada)
            if (!_wToku.Add(s.Klucz)) return;

        var kopia = s.Kopia();
        _ = Task.Run(async () =>
        {
            try
            {
                await _limit.WaitAsync(_koniec.Token);
                try { await PobierzAsync(kopia, _koniec.Token); }
                finally { _limit.Release(); }
            }
            catch { }
            finally
            {
                lock (_blokada) _wToku.Remove(kopia.Klucz);
                if (!_koniec.IsCancellationRequested) Zaktualizowano?.Invoke(kopia.Klucz);
            }
        });
    }

    public int WToku
    {
        get { lock (_blokada) return _wToku.Count; }
    }

    async Task PobierzAsync(Stacja s, CancellationToken ct)
    {
        var (obraz, jakosc, zrodlo) = await Szukaj(s, ct);
        var wpis = new WpisLogo { Sprawdzono = DateTime.Now };
        if (obraz != null)
        {
            using (obraz)
            {
                Directory.CreateDirectory(_folder);
                string plik = NazwaPliku(s.Klucz);
                using var doZapisu = Pomniejsz(obraz, RozmiarZapisu);
                doZapisu.Save(Path.Combine(_folder, plik), ImageFormat.Png);
                wpis.Plik = plik;
                wpis.Jakosc = jakosc;
                wpis.Zrodlo = zrodlo;
            }
        }
        else
        {
            // Nie znaleziono nowego — stary plik zostaje, jeśli był
            lock (_blokada)
                if (_indeks.TryGetValue(s.Klucz, out var stary) && stary.Plik != null)
                {
                    wpis.Plik = stary.Plik;
                    wpis.Jakosc = stary.Jakosc;
                    wpis.Zrodlo = stary.Zrodlo;
                }
        }

        lock (_blokada)
        {
            _indeks[s.Klucz] = wpis;
            if (_obrazy.Remove(s.Klucz, out var b)) b.Dispose();
        }
        _zapisIndeksu.Change(1500, Timeout.Infinite);
    }

    async Task<(Bitmap? Obraz, int Jakosc, string Zrodlo)> Szukaj(Stacja s, CancellationToken ct)
    {
        var kandydaci = new List<(string Adres, int Rozmiar)>();
        string? strona = null;

        // Logo podane przez listę (tvg-logo) — zwykle to samo co w Radio Browser, ale bez dodatkowego zapytania
        if (s.Logo.Length > 0 && Uri.IsWellFormedUriString(s.Logo, UriKind.Absolute)) kandydaci.Add((s.Logo, 1000));
        foreach (var st in await StacjeZRadioBrowser(s, ct))
        {
            if (st.Logo.Length > 0 && !kandydaci.Any(k => k.Adres == st.Logo)) kandydaci.Add((st.Logo, 1000));
            if (strona == null && st.Strona.Length > 0) strona = st.Strona;
        }
        if (strona != null) kandydaci.AddRange(await IkonyZeStrony(strona, ct));

        Bitmap? najlepszy = null;
        int jakosc = 0;
        string zrodlo = "";
        foreach (var (adres, _) in kandydaci.OrderByDescending(k => k.Rozmiar).DistinctBy(k => k.Adres).Take(6))
        {
            var bmp = await PobierzObraz(adres, ct);
            if (bmp == null) continue;
            int j = Math.Min(bmp.Width, bmp.Height);
            if (j > jakosc)
            {
                najlepszy?.Dispose();
                najlepszy = bmp;
                jakosc = j;
                zrodlo = adres;
            }
            else bmp.Dispose();
            if (jakosc >= 180) break;
        }
        return (najlepszy, jakosc, zrodlo);
    }

    /// <summary>
    /// Stacja w Radio Browser: po identyfikatorze (stacje z pobranych list), po adresie, a gdy adres
    /// się zmienił — po dokładnej nazwie, ale tylko na tym samym serwerze strumienia.
    /// </summary>
    static async Task<List<RadioBrowser.StacjaRb>> StacjeZRadioBrowser(Stacja s, CancellationToken ct)
    {
        try
        {
            if (s.Uuid.Length > 0)
            {
                var poUuid = await RadioBrowser.StacjeAsync("json/stations/byuuid/" + Uri.EscapeDataString(s.Uuid), ct);
                if (poUuid.Count > 0) return poUuid;
            }
            var wynik = await RadioBrowser.StacjeAsync("json/stations/byurl?url=" + Uri.EscapeDataString(s.Url), ct);
            if (wynik.Count > 0) return wynik;

            string? host = Uri.TryCreate(s.Url, UriKind.Absolute, out var u) ? u.Host : null;
            if (host == null) return wynik;
            var poNazwie = await RadioBrowser.StacjeAsync("json/stations/bynameexact/" + Uri.EscapeDataString(s.Nazwa), ct);
            return poNazwie.Where(r => Uri.TryCreate(r.Url, UriKind.Absolute, out var ru) && ru.Host.Equals(host, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return []; }
    }

    [GeneratedRegex(@"<link\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ZnacznikLink();

    [GeneratedRegex(@"\b(rel|href|sizes)\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.IgnoreCase)]
    private static partial Regex Atrybut();

    /// <summary>Ikony zadeklarowane na stronie głównej stacji, z deklarowanym rozmiarem do sortowania.</summary>
    async Task<List<(string, int)>> IkonyZeStrony(string strona, CancellationToken ct)
    {
        var wynik = new List<(string, int)>();
        if (!Uri.TryCreate(strona, UriKind.Absolute, out var baza)) return wynik;
        try
        {
            using var odp = await _http.GetAsync(baza, HttpCompletionOption.ResponseHeadersRead, ct);
            if (odp.IsSuccessStatusCode)
            {
                baza = odp.RequestMessage?.RequestUri ?? baza;      // po przekierowaniach
                string html = await CzytajTekst(odp, 512 * 1024, ct);
                foreach (Match m in ZnacznikLink().Matches(html))
                {
                    string rel = "", href = "", sizes = "";
                    foreach (Match a in Atrybut().Matches(m.Value))
                    {
                        string wartosc = a.Groups[2].Success ? a.Groups[2].Value : a.Groups[3].Success ? a.Groups[3].Value : a.Groups[4].Value;
                        switch (a.Groups[1].Value.ToLowerInvariant())
                        {
                            case "rel": rel = wartosc.ToLowerInvariant(); break;
                            case "href": href = WebUtility.HtmlDecode(wartosc); break;
                            case "sizes": sizes = wartosc; break;
                        }
                    }
                    if (href.Length == 0 || !rel.Contains("icon") || rel.Contains("mask")) continue;
                    if (href.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!Uri.TryCreate(baza, href, out var adres)) continue;
                    int rozmiar = RozmiarZDeklaracji(sizes);
                    if (rozmiar == 0) rozmiar = rel.Contains("apple-touch") ? 180 : 32;
                    wynik.Add((adres.AbsoluteUri, rozmiar));
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { }

        wynik.Add((new Uri(baza, "/apple-touch-icon.png").AbsoluteUri, 150));
        wynik.Add((new Uri(baza, "/favicon.ico").AbsoluteUri, 16));
        return wynik;
    }

    static int RozmiarZDeklaracji(string sizes)
    {
        int max = 0;
        foreach (var r in sizes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int x = r.IndexOf('x', StringComparison.OrdinalIgnoreCase);
            if (x > 0 && int.TryParse(r[..x], out int w)) max = Math.Max(max, w);
        }
        return max;
    }

    async Task<Bitmap?> PobierzObraz(string adres, CancellationToken ct)
    {
        try
        {
            using var odp = await _http.GetAsync(adres, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!odp.IsSuccessStatusCode) return null;
            byte[] dane = await CzytajBajty(odp, MaxBajtow, ct);
            if (dane.Length < 16) return null;

            using var ms = new MemoryStream(dane);
            if (dane[0] == 0 && dane[1] == 0 && dane[2] == 1 && dane[3] == 0)
            {
                // .ico — bierzemy największą klatkę
                using var ico = new Icon(ms, 256, 256);
                return ico.ToBitmap();
            }
            using var obraz = Image.FromStream(ms);
            return new Bitmap(obraz);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch
        {
            return null;   // SVG, WebP i uszkodzone pliki — GDI+ ich nie wczyta, próbujemy następny
        }
    }

    static async Task<byte[]> CzytajBajty(HttpResponseMessage odp, int limit, CancellationToken ct)
    {
        await using var strumien = await odp.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var bufor = new byte[16384];
        int n;
        while ((n = await strumien.ReadAsync(bufor, ct)) > 0)
        {
            ms.Write(bufor, 0, n);
            if (ms.Length > limit) break;
        }
        return ms.ToArray();
    }

    static async Task<string> CzytajTekst(HttpResponseMessage odp, int limit, CancellationToken ct) =>
        Encoding.UTF8.GetString(await CzytajBajty(odp, limit, ct));

    /// <summary>Zmniejsza krokami przez połowienie — jednokrokowe skalowanie gubi detale.</summary>
    static Bitmap Pomniejsz(Bitmap zrodlo, int max)
    {
        Bitmap biezacy = new(zrodlo);
        while (Math.Max(biezacy.Width, biezacy.Height) > max)
        {
            double skala = Math.Max(0.5, (double)max / Math.Max(biezacy.Width, biezacy.Height));
            int w = Math.Max(1, (int)Math.Round(biezacy.Width * skala));
            int h = Math.Max(1, (int)Math.Round(biezacy.Height * skala));
            var nowy = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(nowy))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(biezacy, new Rectangle(0, 0, w, h));
            }
            biezacy.Dispose();
            biezacy = nowy;
        }
        return biezacy;
    }

    static string NazwaPliku(string klucz) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(klucz)))[..16].ToLowerInvariant() + ".png";

    Dictionary<string, WpisLogo> WczytajIndeks()
    {
        try
        {
            if (File.Exists(_plikIndeksu))
                return JsonSerializer.Deserialize(File.ReadAllText(_plikIndeksu), LogoContext.Default.DictionaryStringWpisLogo) ?? [];
        }
        catch { }
        return [];
    }

    void ZapiszIndeks()
    {
        try
        {
            string json;
            lock (_blokada) json = JsonSerializer.Serialize(_indeks, LogoContext.Default.DictionaryStringWpisLogo);
            Directory.CreateDirectory(_folder);
            string tmp = _plikIndeksu + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _plikIndeksu, true);
        }
        catch { }
    }

    public void Dispose()
    {
        _koniec.Cancel();
        _zapisIndeksu.Dispose();
        ZapiszIndeks();
        _http.Dispose();
        lock (_blokada)
        {
            foreach (var b in _obrazy.Values) b.Dispose();
            _obrazy.Clear();
        }
    }
}
