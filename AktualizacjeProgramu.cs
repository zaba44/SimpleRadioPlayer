using System.Net;
using System.Reflection;
using System.Text.Json;

namespace SimpleRadioPlayer;

/// <summary>
/// Sprawdzanie nowej wersji w wydaniach GitHuba. Program niczego sam nie pobiera ani nie instaluje —
/// tylko pokazuje informację i link do strony wydania. Zapytanie bez logowania (limit 60 na godzinę na adres IP).
/// </summary>
static class AktualizacjeProgramu
{
    public const string Repozytorium = "zaba44/SimpleRadioPlayer";

    public sealed record Wydanie(Version Wersja, string Tag, string Adres, string Nazwa);

    public static Version BiezacaWersja
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    /// <summary>Najnowsze opublikowane wydanie (bez wersji testowych i szkiców) albo null, gdy nie ma żadnego.</summary>
    public static async Task<Wydanie?> NajnowszeAsync(CancellationToken ct, string? repozytorium = null)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(RadioBrowser.UserAgent);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        using var odp = await http.GetAsync($"https://api.github.com/repos/{repozytorium ?? Repozytorium}/releases/latest", ct);
        if (odp.StatusCode == HttpStatusCode.NotFound) return null;
        odp.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await odp.Content.ReadAsStringAsync(ct));
        var e = json.RootElement;
        string tag = e.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.Trim().TrimStart('v', 'V'), out var wersja)) return null;
        return new Wydanie(
            new Version(wersja.Major, wersja.Minor, Math.Max(0, wersja.Build)),
            tag,
            e.GetProperty("html_url").GetString() ?? $"https://github.com/{Repozytorium}/releases/latest",
            e.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : tag);
    }
}
