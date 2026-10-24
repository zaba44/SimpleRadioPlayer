using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace SimpleRadioPlayer;

public sealed class Stacja
{
    [JsonPropertyName("nazwa")] public string Nazwa { get; set; } = "";
    /// <summary>Zmiana adresu (np. naprawa ulubionej po aktualizacji listy) unieważnia wyliczone klucze.</summary>
    [JsonPropertyName("url")]
    public string Url
    {
        get => _url;
        set { _url = value; _klucz = null; _kluczSzukania = null; }
    }
    string _url = "";
    [JsonPropertyName("info")] public string Info { get; set; } = "";

    /// <summary>Stały identyfikator stacji w Radio Browser — przeżywa zmianę adresu strumienia.</summary>
    [JsonPropertyName("uuid")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string Uuid { get; set; } = "";

    /// <summary>Adres logo podany przez listę (tvg-logo / Radio Browser), pierwszy kandydat przy szukaniu logo.</summary>
    [JsonPropertyName("logo")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string Logo { get; set; } = "";

    /// <summary>Nazwa pliku listy, z której pochodzi stacja (albo „Własne”).</summary>
    [JsonIgnore] public string Zrodlo { get; set; } = "";
    [JsonIgnore] public bool Wlasna { get; set; }

    string? _klucz, _kluczSzukania;

    /// <summary>Znormalizowany adres — ta sama stacja z różnych list ma ten sam klucz.</summary>
    [JsonIgnore] public string Klucz => _klucz ??= KluczAdresu(Url);
    [JsonIgnore] public string KluczSzukania => _kluczSzukania ??= Normalizuj($"{Nazwa} {Info} {Url}");

    public Stacja Kopia() => new() { Nazwa = Nazwa, Url = Url, Info = Info, Uuid = Uuid, Logo = Logo };

    public override string ToString() => Nazwa;

    public static string KluczAdresu(string url)
    {
        string u = url.Trim().ToLowerInvariant();
        int p = u.IndexOf("://", StringComparison.Ordinal);
        if (p >= 0) u = u[(p + 3)..];
        if (u.StartsWith("www.")) u = u[4..];
        return u.TrimEnd('/');
    }

    /// <summary>Małe litery bez polskich znaków — „lodz” znajdzie „Łódź”.</summary>
    public static string Normalizuj(string tekst)
    {
        string d = tekst.ToLowerInvariant().Replace('ł', 'l').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (char c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString();
    }
}
