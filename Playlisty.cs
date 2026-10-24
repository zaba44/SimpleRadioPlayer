using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SimpleRadioPlayer;

/// <summary>Jedna lista stacji z folderu listy\ — nazwa to nazwa pliku bez rozszerzenia.</summary>
sealed record ListaPlikowa(string Nazwa, string Plik, List<Stacja> Stacje);

static partial class Playlisty
{
    /// <summary>Formaty list: M3U/M3U8, PLS, XSPF (VLC), ASX/WAX/WVX (Windows Media) i TXT z adresami.</summary>
    public static readonly string[] Rozszerzenia = [".m3u", ".m3u8", ".pls", ".xspf", ".asx", ".wax", ".wvx", ".txt"];

    public const string FiltrPlikow =
        "Listy stacji|*.m3u;*.m3u8;*.pls;*.xspf;*.asx;*.wax;*.wvx;*.txt|M3U / M3U8|*.m3u;*.m3u8|PLS|*.pls|XSPF (VLC)|*.xspf|ASX (Windows Media)|*.asx;*.wax;*.wvx|Tekst z adresami|*.txt|Wszystkie pliki|*.*";

    public static string Folder => Path.Combine(AppContext.BaseDirectory, "listy");

    public static bool CzyLista(string plik) => Rozszerzenia.Contains(Path.GetExtension(plik).ToLowerInvariant());

    static readonly HashSet<string> Schematy = new(StringComparer.OrdinalIgnoreCase)
        { "http", "https", "mms", "mmsh", "rtsp", "rtmp", "icyx" };

    [GeneratedRegex(@"^(.*\S)\s*\[([^\[\]]*)\]$")]
    private static partial Regex NazwaZInfo();

    [GeneratedRegex(@"^(File|Title)(\d+)\s*=(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex WpisPls();

    // #EXTINF:-1 radiobrowser-uuid="…" tvg-logo="…",Nazwa — atrybuty przed przecinkiem mogą zawierać przecinki
    [GeneratedRegex(@"^#EXTINF:\s*-?[\d.]+((?:\s+[\w-]+\s*=\s*""[^""]*"")*)\s*,(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex WpisExtinf();

    [GeneratedRegex(@"([\w-]+)\s*=\s*""([^""]*)""")]
    private static partial Regex AtrybutExtinf();

    [GeneratedRegex(@"<entry\b.*?</entry>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex WpisAsx();

    [GeneratedRegex(@"<title>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TytulAsx();

    [GeneratedRegex(@"<ref\s+href\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex OdnosnikAsx();

    [GeneratedRegex(@"(?:https?|mmsh?|rtsp|rtmp|icyx)://\S+", RegexOptions.IgnoreCase)]
    private static partial Regex AdresWTekscie();

    /// <summary>Wszystkie listy z folderu listy\ — każda osobno, bez powtórzeń adresu w obrębie listy.</summary>
    public static List<ListaPlikowa> WczytajWszystkie(List<string> bledy)
    {
        var wynik = new List<ListaPlikowa>();
        if (!Directory.Exists(Folder)) return wynik;

        var pliki = Directory.EnumerateFiles(Folder).Where(CzyLista)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
        foreach (var plik in pliki)
        {
            try
            {
                var znane = new HashSet<string>();
                var stacje = Wczytaj(plik).Where(s => znane.Add(s.Klucz)).ToList();
                wynik.Add(new ListaPlikowa(Path.GetFileNameWithoutExtension(plik), plik, stacje));
            }
            catch (Exception ex)
            {
                bledy.Add($"{Path.GetFileName(plik)}: {ex.Message}");
            }
        }
        return wynik;
    }

    public static List<Stacja> Wczytaj(string plik)
    {
        string tekst = CzytajTekst(plik);
        string zrodlo = Path.GetFileNameWithoutExtension(plik);
        var linie = tekst.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0);
        var stacje = Path.GetExtension(plik).ToLowerInvariant() switch
        {
            ".pls" => CzytajPls(linie),
            ".xspf" => CzytajXspf(tekst),
            ".asx" or ".wax" or ".wvx" => CzytajAsx(tekst),
            ".txt" => CzytajTxt(linie),
            _ => CzytajM3u(linie),
        };
        foreach (var s in stacje) s.Zrodlo = zrodlo;
        return stacje;
    }

    static List<Stacja> CzytajM3u(IEnumerable<string> linie)
    {
        var wynik = new List<Stacja>();
        string? nazwa = null;
        Dictionary<string, string>? atrybuty = null;
        foreach (var l in linie)
        {
            if (l.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
            {
                var m = WpisExtinf().Match(l);
                if (m.Success)
                {
                    nazwa = m.Groups[2].Value.Trim();
                    atrybuty = AtrybutExtinf().Matches(m.Groups[1].Value)
                        .ToDictionary(a => a.Groups[1].Value.ToLowerInvariant(), a => a.Groups[2].Value, StringComparer.Ordinal);
                }
                else
                {
                    int p = l.IndexOf(',');
                    nazwa = p >= 0 ? l[(p + 1)..].Trim() : null;
                    atrybuty = null;
                }
                continue;
            }
            if (l.StartsWith('#')) continue;
            if (CzyAdres(l))
            {
                var s = Utworz(nazwa, l);
                if (atrybuty != null)
                {
                    s.Uuid = atrybuty.GetValueOrDefault("radiobrowser-uuid", "");
                    s.Logo = atrybuty.GetValueOrDefault("tvg-logo", "");
                }
                wynik.Add(s);
            }
            nazwa = null;
            atrybuty = null;
        }
        return wynik;
    }

    static List<Stacja> CzytajPls(IEnumerable<string> linie)
    {
        var tytuly = new Dictionary<int, string>();
        var adresy = new SortedDictionary<int, string>();
        foreach (var l in linie)
        {
            var m = WpisPls().Match(l);
            if (!m.Success) continue;
            int nr = int.Parse(m.Groups[2].Value);
            string wartosc = m.Groups[3].Value.Trim();
            if (m.Groups[1].Value.Equals("File", StringComparison.OrdinalIgnoreCase)) adresy[nr] = wartosc;
            else tytuly[nr] = wartosc;
        }
        return adresy.Where(a => CzyAdres(a.Value))
            .Select(a => Utworz(tytuly.GetValueOrDefault(a.Key), a.Value))
            .ToList();
    }

    /// <summary>XSPF — playlista XML VLC: &lt;track&gt;&lt;location&gt; + &lt;title&gt;.</summary>
    static List<Stacja> CzytajXspf(string tekst)
    {
        var doc = XDocument.Parse(tekst);
        return doc.Descendants().Where(e => e.Name.LocalName == "track")
            .Select(t => (
                Adres: t.Elements().FirstOrDefault(e => e.Name.LocalName == "location")?.Value.Trim() ?? "",
                Tytul: t.Elements().FirstOrDefault(e => e.Name.LocalName == "title")?.Value.Trim()))
            .Where(t => CzyAdres(t.Adres))
            .Select(t => Utworz(t.Tytul, t.Adres))
            .ToList();
    }

    /// <summary>ASX — format Windows Media; często nie jest poprawnym XML-em, dlatego wyrażenia regularne.</summary>
    static List<Stacja> CzytajAsx(string tekst)
    {
        var wynik = new List<Stacja>();
        var wpisy = WpisAsx().Matches(tekst).Select(m => m.Value).ToList();
        if (wpisy.Count == 0) wpisy.Add(tekst);   // plik z samymi <ref> bez <entry>
        foreach (var wpis in wpisy)
        {
            var tytul = TytulAsx().Match(wpis);
            string? nazwa = tytul.Success ? WebUtility.HtmlDecode(tytul.Groups[1].Value.Trim()) : null;
            // Wpis bywa kilkoma lustrami tej samej stacji — bierzemy pierwszy działający adres
            var adres = OdnosnikAsx().Matches(wpis).Select(m => WebUtility.HtmlDecode(m.Groups[1].Value.Trim())).FirstOrDefault(CzyAdres);
            if (adres != null) wynik.Add(Utworz(nazwa, adres));
        }
        return wynik;
    }

    /// <summary>TXT — w każdej linii adres strumienia, opcjonalnie z nazwą przed albo po nim („Moje radio | http://…”).</summary>
    static List<Stacja> CzytajTxt(IEnumerable<string> linie)
    {
        var wynik = new List<Stacja>();
        foreach (var l in linie)
        {
            if (l.StartsWith('#') || l.StartsWith("//")) continue;
            var m = AdresWTekscie().Match(l);
            if (!m.Success || !CzyAdres(m.Value)) continue;
            string nazwa = (l[..m.Index] + " " + l[(m.Index + m.Length)..]).Trim(' ', '\t', '|', ';', ',', '-', '–', ':', '=');
            wynik.Add(Utworz(nazwa.Length > 0 ? nazwa : null, m.Value));
        }
        return wynik;
    }

    static Stacja Utworz(string? nazwa, string url)
    {
        string info = "";
        if (!string.IsNullOrWhiteSpace(nazwa))
        {
            var m = NazwaZInfo().Match(nazwa);
            if (m.Success) { nazwa = m.Groups[1].Value; info = m.Groups[2].Value.Trim(); }
        }
        if (string.IsNullOrWhiteSpace(nazwa))
            nazwa = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : url;
        return new Stacja { Nazwa = nazwa.Trim(), Url = url.Trim(), Info = info };
    }

    public static bool CzyAdres(string tekst) =>
        Uri.TryCreate(tekst.Trim(), UriKind.Absolute, out var u) && Schematy.Contains(u.Scheme);

    /// <summary>Zapis listy w formacie M3U8 z identyfikatorem Radio Browser i logo w atrybutach #EXTINF.</summary>
    public static void ZapiszM3u8(string plik, string nazwaListy, IEnumerable<Stacja> stacje, string? komentarz = null)
    {
        var sb = new StringBuilder();
        sb.Append("#EXTM3U\n#EXTENC:UTF-8\n");
        sb.Append("#PLAYLIST:").Append(BezNowychLinii(nazwaListy)).Append('\n');
        if (komentarz != null) sb.Append("# ").Append(BezNowychLinii(komentarz)).Append('\n');
        foreach (var s in stacje)
        {
            sb.Append("#EXTINF:-1");
            if (s.Uuid.Length > 0) sb.Append(" radiobrowser-uuid=\"").Append(s.Uuid.Replace("\"", "")).Append('"');
            if (s.Logo.Length > 0) sb.Append(" tvg-logo=\"").Append(s.Logo.Replace("\"", "%22")).Append('"');
            sb.Append(',').Append(BezNowychLinii(s.Nazwa));
            if (s.Info.Length > 0) sb.Append(" [").Append(BezNowychLinii(s.Info)).Append(']');
            sb.Append('\n').Append(s.Url.Trim()).Append('\n');
        }
        string tmp = plik + ".tmp";
        File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
        File.Move(tmp, plik, true);
    }

    static string BezNowychLinii(string t) => t.Replace('\r', ' ').Replace('\n', ' ').Trim();

    /// <summary>UTF-8, a gdy plik nim nie jest — Windows-1250 (stare listy z Winampa).</summary>
    static string CzytajTekst(string plik)
    {
        byte[] bajty = File.ReadAllBytes(plik);
        try
        {
            return new UTF8Encoding(false, true).GetString(bajty).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1250).GetString(bajty);
        }
    }
}
