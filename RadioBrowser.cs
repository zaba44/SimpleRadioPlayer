using System.Net;
using System.Reflection;
using System.Text.Json;

namespace SimpleRadioPlayer;

/// <summary>
/// Klient otwartej bazy stacji Radio Browser (radio-browser.info). API jest darmowe, bez klucza,
/// dozwolone w programach darmowych i płatnych. Zalecenia autorów: listę serwerów brać z DNS
/// (all.api.radio-browser.info) i przedstawiać się czytelnym User-Agentem.
/// </summary>
static class RadioBrowser
{
    // Tylko gdy DNS zawiedzie; aktualny skład serwerów zawsze bierzemy z all.api.radio-browser.info
    static readonly string[] SerweryZapasowe = ["de1.api.radio-browser.info", "de2.api.radio-browser.info"];

    static readonly HttpClient Http = UtworzKlienta();
    static readonly SemaphoreSlim BlokadaSerwerow = new(1, 1);
    static List<string>? _serwery;

    public static string UserAgent =>
        "SimpleRadioPlayer/" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0");

    static HttpClient UtworzKlienta()
    {
        var obsluga = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        // Lista „cały świat” to ok. 25 MB — limit czasu z zapasem
        var http = new HttpClient(obsluga) { Timeout = TimeSpan.FromMinutes(3) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return http;
    }

    /// <summary>Serwery z DNS (nazwy z odwrotnego DNS, bo HTTPS wymaga nazwy), w losowej kolejności; zapasowo stała lista.</summary>
    static async Task<List<string>> SerweryAsync(CancellationToken ct)
    {
        if (_serwery != null) return _serwery;
        await BlokadaSerwerow.WaitAsync(ct);
        try
        {
            if (_serwery != null) return _serwery;
            var nazwy = new List<string>();
            try
            {
                foreach (var ip in await Dns.GetHostAddressesAsync("all.api.radio-browser.info", ct))
                {
                    try
                    {
                        string nazwa = (await Dns.GetHostEntryAsync(ip.ToString(), ct)).HostName.ToLowerInvariant();
                        if (nazwa.EndsWith(".api.radio-browser.info") && !nazwy.Contains(nazwa)) nazwy.Add(nazwa);
                    }
                    catch { }
                }
            }
            catch { }
            nazwy = [.. nazwy.OrderBy(_ => Random.Shared.Next())];
            foreach (var z in SerweryZapasowe)
                if (!nazwy.Contains(z)) nazwy.Add(z);
            return _serwery = nazwy;
        }
        finally { BlokadaSerwerow.Release(); }
    }

    /// <summary>
    /// Zapytanie do pierwszego działającego serwera. Serwerów jest zwykle tylko dwa, a zdarzają się
    /// chwilowe zerwania połączenia TLS — stąd druga runda po krótkiej przerwie.
    /// </summary>
    public static async Task<JsonDocument> ZapytajAsync(string sciezka, CancellationToken ct)
    {
        Exception? ostatni = null;
        for (int runda = 0; runda < 2; runda++)
        {
            if (runda > 0) await Task.Delay(1500, ct);
            foreach (var serwer in await SerweryAsync(ct))
            {
                try
                {
                    using var odp = await Http.GetAsync($"https://{serwer}/{sciezka}", HttpCompletionOption.ResponseHeadersRead, ct);
                    odp.EnsureSuccessStatusCode();
                    await using var strumien = await odp.Content.ReadAsStreamAsync(ct);
                    return await JsonDocument.ParseAsync(strumien, cancellationToken: ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) { ostatni = ex; }
            }
        }
        throw new InvalidOperationException("Żaden serwer Radio Browser nie odpowiada" + (ostatni != null ? ": " + ostatni.Message : "."), ostatni);
    }

    public sealed record StacjaRb(string Uuid, string Nazwa, string Url, string Logo, string Strona,
        string Kodek, int Bitrate, int Klikniecia, int Glosy)
    {
        /// <summary>Popularność: odtworzenia z ostatniej doby ważą 10× bardziej niż głosy zbierane latami.</summary>
        public long Popularnosc => 10L * Klikniecia + Glosy;
    }

    public static async Task<List<StacjaRb>> StacjeAsync(string sciezka, CancellationToken ct)
    {
        using var json = await ZapytajAsync(sciezka, ct);
        var wynik = new List<StacjaRb>();
        foreach (var e in json.RootElement.EnumerateArray())
            wynik.Add(new StacjaRb(Tekst(e, "stationuuid"), Tekst(e, "name"), Tekst(e, "url"), Tekst(e, "favicon"),
                Tekst(e, "homepage"), Tekst(e, "codec"), Liczba(e, "bitrate"), Liczba(e, "clickcount"), Liczba(e, "votes")));
        return wynik;
    }

    public static async Task<List<(string Kod, string Nazwa, int Liczba)>> KrajeAsync(CancellationToken ct)
    {
        using var json = await ZapytajAsync("json/countries?hidebroken=true", ct);
        var wynik = new List<(string, string, int)>();
        foreach (var e in json.RootElement.EnumerateArray())
        {
            string kod = Tekst(e, "iso_3166_1");
            if (kod.Length == 2) wynik.Add((kod.ToUpperInvariant(), Tekst(e, "name"), Liczba(e, "stationcount")));
        }
        return wynik;
    }

    /// <summary>
    /// Zgłasza odtworzenie stacji (ranking popularności Radio Browser). Wysyłane tylko, gdy użytkownik
    /// włączył to w Opcjach. Błędy ignorujemy — to grzeczność wobec bazy, nie funkcja programu.
    /// </summary>
    public static void ZglosOdtworzenie(string uuid)
    {
        if (string.IsNullOrWhiteSpace(uuid)) return;
        _ = Task.Run(async () =>
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                using var _ = await ZapytajAsync("json/url/" + Uri.EscapeDataString(uuid), cts.Token);
            }
            catch { }
        });
    }

    static string Tekst(JsonElement e, string pole) =>
        e.TryGetProperty(pole, out var w) && w.ValueKind == JsonValueKind.String ? w.GetString()!.Trim() : "";

    static int Liczba(JsonElement e, string pole) =>
        e.TryGetProperty(pole, out var w) && w.ValueKind == JsonValueKind.Number && w.TryGetInt32(out int n) ? n : 0;
}
