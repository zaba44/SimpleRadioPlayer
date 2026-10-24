namespace SimpleRadioPlayer;

/// <summary>
/// Skala suwaka głośności. Ucho odbiera głośność logarytmicznie, więc równe kroki suwaka
/// to równe kroki w decybelach: od 10% do 100% każde 10% to ZakresDb/10 dB.
/// Poniżej progu poziom maleje liniowo do zera — sama skala dB nigdy nie dochodzi do ciszy.
/// Na końcu wynik trafia do VLC przez pierwiastek sześcienny, bo moduł mmdevice
/// VLC 3 podnosi głośność do trzeciej potęgi.
/// </summary>
static class SkalaGlosnosci
{
    public const int WersjaBiezaca = 2;
    const double ZakresDb = 30;
    const double Prog = 10;

    /// <summary>Poziom sygnału (amplituda 0–1) dla pozycji suwaka.</summary>
    public static double Poziom(int procent)
    {
        double p = Math.Clamp(procent, 0, 100);
        if (p <= 0) return 0;
        if (p >= Prog) return DbNaPoziom(-ZakresDb * (1 - p / 100));
        return DbNaPoziom(-ZakresDb * (1 - Prog / 100)) * p / Prog;
    }

    /// <summary>Pozycja suwaka dająca podany poziom sygnału — do przeliczania starych ustawień.</summary>
    public static int ZPoziomu(double poziom)
    {
        if (poziom <= 0) return 0;
        double prog = DbNaPoziom(-ZakresDb * (1 - Prog / 100));
        double p = poziom >= prog
            ? 100 * (1 + 20 * Math.Log10(Math.Min(poziom, 1)) / ZakresDb)
            : Prog * poziom / prog;
        // Ciche, ale niezerowe granie nie może zamienić się w wyciszenie
        return Math.Clamp((int)Math.Round(p), 1, 100);
    }

    public static int DoVlc(int procent) => (int)Math.Round(100 * Math.Cbrt(Poziom(procent)));

    /// <summary>Przelicza zapisany procent ze starszej wersji skali, zachowując poziom dźwięku.</summary>
    public static int ZWersji(int wersja, int procent)
    {
        if (procent <= 0) return 0;
        double p = Math.Clamp(procent, 0, 100) / 100.0;
        double poziom = wersja switch
        {
            0 => p * p * p,     // suwak przekazywany wprost do VLC (krzywa sześcienna mmdevice)
            1 => p,             // suwak = liniowy poziom sygnału
            _ => Poziom(procent),
        };
        return ZPoziomu(poziom);
    }

    static double DbNaPoziom(double db) => Math.Pow(10, db / 20);
}
