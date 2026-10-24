using LibVLCSharp.Shared;

namespace SimpleRadioPlayer;

public enum StanOdtwarzania { Zatrzymane, Laczenie, Gra, Ponawianie, Blad }

/// <summary>
/// Opakowanie LibVLC. Metody publiczne wołane są z wątku UI, zdarzenia LibVLC
/// przychodzą z jego wątków i są przerzucane na UI. Polecenia Stop/Play idą przez
/// kolejkę w tle — Stop na zawieszonym połączeniu potrafi blokować kilka sekund.
///
/// Każde uruchomienie dostaje nowy MediaPlayer z urządzeniem ustawionym PRZED Play.
/// Stop w LibVLC 3 niszczy wyjście audio, a nowe powstaje dopiero chwilę po zdarzeniu
/// Playing — ustawienie urządzenia w tym oknie czasowym przepada i dźwięk idzie na
/// domyślne urządzenie Windows (sprawdzone na sesjach audio systemu).
/// </summary>
sealed class Odtwarzacz : IDisposable
{
    // Odstępy kolejnych prób w sekundach; stacja, która jeszcze nie zagrała, dostaje tylko 3 próby
    static readonly int[] OpoznieniaPonowien = [2, 4, 8, 15, 30, 30, 30, 30];
    const int ProbyNowejStacji = 3;

    LibVLC _vlc;
    int _wyrownywanie;
    UstawieniaEq? _eq;
    readonly Control _ui;
    readonly System.Windows.Forms.Timer _timerPonowienia = new();
    readonly System.Windows.Forms.Timer _timerUstawien = new() { Interval = 25 };
    readonly SemaphoreSlim _zmianaSilnika = new(1, 1);
    bool _przelaczanieSilnika;
    bool _disposed;
    int _generacja;
    int _ustawieniaWKolejce;
    int _zmianyUstawien;
    const int ZmianaGlosnosci = 1, ZmianaEq = 2, ZmianaUrzadzenia = 4;
    sealed record ParametryAudio(int Glosnosc, bool Wyciszony, UstawieniaEq? Eq, string Urzadzenie)
    {
        public int GlosnoscVlc => Wyciszony ? 0 : SkalaGlosnosci.DoVlc(Glosnosc);
    }
    ParametryAudio _parametry = new(70, false, null, "");
    readonly object _blokadaKolejki = new();
    Task _kolejka = Task.CompletedTask;

    MediaPlayer? _mp;
    Media? _media;      // medium stacji (jej adres)
    Media? _aktywne;    // medium faktycznie grane — przy adresie .pls/.m3u to pozycja z tej playlisty
    int _glebokosc;
    int _proby;
    bool _zagrala;
    DateTime _graOd;
    int _glosnosc = 70;
    string _urzadzenie = "";

    public Stacja? Biezaca { get; private set; }
    public bool PowinnoGrac { get; private set; }
    public StanOdtwarzania Stan { get; private set; }
    public string Opis { get; private set; } = "Zatrzymane";
    public string Tytul { get; private set; } = "";
    public string WersjaSilnika => _vlc.Version;

    /// <summary>Zmienił się stan, opis albo tytuł utworu.</summary>
    public event Action? Zmiana;

    Odtwarzacz(LibVLC vlc, int wyrownywanie, Control ui)
    {
        _vlc = vlc;
        _wyrownywanie = wyrownywanie;
        _ui = ui;
        _timerUstawien.Tick += (_, _) => WyslijUstawienia();
        _timerPonowienia.Tick += (_, _) =>
        {
            _timerPonowienia.Stop();
            if (PowinnoGrac && Biezaca != null) Uruchom(Biezaca);
        };
    }

    public static async Task<Odtwarzacz> UtworzAsync(Control ui, int wyrownywanie)
    {
        var (vlc, zapisanoCache) = await Task.Run(() =>
        {
            Core.Initialize(FolderLibVlc);
            // Bez plugins.dat LibVLC ładuje przy starcie wszystkie ~300 bibliotek wtyczek, a Defender
            // skanuje każdą — pierwszy start trwał przez to ~30 s. Z plikiem ładuje 2, resztę w razie potrzeby.
            bool bezCache = !File.Exists(PlikCache) && Konfiguracja.FolderZapisywalny(FolderWtyczek);
            var opcje = OpcjeSilnika(wyrownywanie);
            return (new LibVLC(bezCache ? [.. opcje, "--reset-plugins-cache"] : opcje), bezCache);
        });
        if (!zapisanoCache && CacheNieaktualny()) _ = Task.Run(PrzygotujCacheWtyczek);
        return new Odtwarzacz(vlc, wyrownywanie, ui);
    }

    static string FolderLibVlc => Path.Combine(AppContext.BaseDirectory, "libvlc",
        System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.Arm64 => "win-arm64",
            System.Runtime.InteropServices.Architecture.X86 => "win-x86",
            _ => "win-x64"
        });
    static string FolderWtyczek => Path.Combine(FolderLibVlc, "plugins");
    static string PlikCache => Path.Combine(FolderWtyczek, "plugins.dat");

    /// <summary>
    /// Cache unieważniają zmienione daty plików (np. ZIP zaokrągla je do 2 s) — wtedy LibVLC
    /// po cichu ładuje wtyczki jak bez cache. Poznajemy to po liczbie wczytanych bibliotek.
    /// </summary>
    static bool CacheNieaktualny()
    {
        try
        {
            int wczytane = System.Diagnostics.Process.GetCurrentProcess().Modules
                .Cast<System.Diagnostics.ProcessModule>()
                .Count(m => m.FileName.StartsWith(FolderWtyczek, StringComparison.OrdinalIgnoreCase));
            return wczytane > 30 && Konfiguracja.FolderZapisywalny(FolderWtyczek);
        }
        catch { return false; }
    }

    /// <summary>Zapisuje plugins.dat. Wołane w tle po wykryciu nieaktualnego cache i przez build.bat (--przygotuj-wtyczki).</summary>
    public static void PrzygotujCacheWtyczek()
    {
        try
        {
            Core.Initialize(FolderLibVlc);
            using var vlc = new LibVLC("--reset-plugins-cache", "--no-video", "--quiet");
        }
        catch { }
    }

    /// <summary>
    /// Wyrównywanie głośności to kompresor dynamiki z niskim progiem i wzmocnieniem:
    /// głośne, już skompresowane stacje komercyjne prawie się nie zmieniają, ciche dostają kilka dB.
    /// Wbudowany normvol tylko przycina szczyty i nie podnosi cichych stacji (sprawdzone pomiarem RMS).
    /// </summary>
    static string[] OpcjeSilnika(int wyrownywanie)
    {
        // --no-video: część stacji to strumienie HLS z obrazem, nie chcemy okna wideo
        var opcje = new List<string> { "--no-video", "--aout=mmdevice", "--network-caching=2000", "--http-reconnect", "--quiet" };
        if (wyrownywanie > 0)
        {
            bool mocne = wyrownywanie >= 2;
            opcje.AddRange([
                "--audio-filter=compressor",
                mocne ? "--compressor-threshold=-30" : "--compressor-threshold=-24",
                mocne ? "--compressor-ratio=8" : "--compressor-ratio=4",
                mocne ? "--compressor-makeup-gain=16" : "--compressor-makeup-gain=9",
                "--compressor-attack=25", "--compressor-release=400", "--compressor-rms-peak=0",
            ]);
        }
        return [.. opcje];
    }

    /// <summary>
    /// Filtry audio w LibVLC 3 są ustawieniem całego silnika, więc zmiana wymaga nowego LibVLC —
    /// odtwarzanie przerywa się na moment i wznawia.
    /// </summary>
    public async Task UstawWyrownywanieAsync(int poziom)
    {
        await _zmianaSilnika.WaitAsync();
        try
        {
            if (_disposed || poziom == _wyrownywanie) return;
            _przelaczanieSilnika = true;
            Interlocked.Increment(ref _generacja);
            _timerPonowienia.Stop();
            if (PowinnoGrac) UstawStan(StanOdtwarzania.Laczenie, "Przełączanie wyrównywania głośności…");
            var nowy = await Task.Run(() => new LibVLC(OpcjeSilnika(poziom)));
            if (_disposed) { await Task.Run(nowy.Dispose); return; }

            var staryMp = _mp;
            var stareMedia = _media;
            var staryVlc = _vlc;
            _mp = null;
            _media = null;
            _aktywne = null;
            _vlc = nowy;
            _wyrownywanie = poziom;
            WKolejce(() =>
            {
                staryMp?.Stop();
                staryMp?.Dispose();
                stareMedia?.Dispose();
                staryVlc.Dispose();
            });
        }
        finally
        {
            bool wznow = _przelaczanieSilnika;
            _przelaczanieSilnika = false;
            _zmianaSilnika.Release();
            // Podczas await użytkownik mógł nacisnąć Stop albo wybrać inną stację.
            if (wznow && !_disposed && PowinnoGrac && Biezaca is { } stacja) Uruchom(stacja);
        }
    }

    /// <summary>Ustawienia equalizera; zmiana działa od razu, także w trakcie grania.</summary>
    public UstawieniaEq? Equalizer
    {
        get => _eq;
        set
        {
            _eq = value?.Kopia();
            ZlecUstawienia(ZmianaEq);
        }
    }

    static void ZastosujEq(MediaPlayer mp, UstawieniaEq? eq)
    {
        if (eq is not { Wlaczony: true })
        {
            mp.UnsetEqualizer();
            return;
        }
        // LibVLC kopiuje ustawienia, więc obiekt Equalizer można od razu zwolnić
        using var e = new Equalizer();
        e.SetPreamp(eq.Preamp);
        for (uint i = 0; i < e.BandCount && i < eq.Pasma.Length; i++) e.SetAmp(eq.Pasma[i], i);
        mp.SetEqualizer(e);
    }

    /// <summary>Częstotliwości pasm i presety wbudowane w LibVLC (wymaga zainicjowanego silnika).</summary>
    public static float[] CzestotliwosciPasm()
    {
        using var e = new Equalizer();
        return Enumerable.Range(0, (int)e.BandCount).Select(i => e.BandFrequency((uint)i)).ToArray();
    }

    public static List<(string Nazwa, float Preamp, float[] Pasma)> Presety()
    {
        var wynik = new List<(string, float, float[])>();
        using var pusty = new Equalizer();
        for (uint p = 0; p < pusty.PresetCount; p++)
        {
            using var e = new Equalizer(p);
            wynik.Add((pusty.PresetName(p) ?? $"Preset {p}", e.Preamp, Enumerable.Range(0, (int)e.BandCount).Select(i => e.Amp((uint)i)).ToArray()));
        }
        return wynik;
    }

    public int Glosnosc
    {
        get => _glosnosc;
        set
        {
            _glosnosc = Math.Clamp(value, 0, 100);
            ZlecUstawienia(ZmianaGlosnosci);
        }
    }

    /// <summary>Wyciszenie niezależne od suwaka — po odciszeniu wraca dokładnie poprzednia głośność.</summary>
    public bool Wyciszony
    {
        get => _wyciszony;
        set
        {
            if (_wyciszony == value) return;
            _wyciszony = value;
            ZlecUstawienia(ZmianaGlosnosci);
        }
    }
    bool _wyciszony;

    /// <summary>Identyfikator urządzenia wyjściowego; pusty = domyślne urządzenie Windows.</summary>
    public string Urzadzenie
    {
        get => _urzadzenie;
        set
        {
            _urzadzenie = value ?? "";
            ZlecUstawienia(ZmianaUrzadzenia);
        }
    }

    void ZlecUstawienia(int zmiany)
    {
        if (_disposed) return;
        Volatile.Write(ref _parametry, new(_glosnosc, _wyciszony, _eq, _urzadzenie));
        Interlocked.Or(ref _zmianyUstawien, zmiany);
        _timerUstawien.Start();
    }

    void WyslijUstawienia()
    {
        if (_disposed || _mp == null || Volatile.Read(ref _zmianyUstawien) == 0)
        {
            _timerUstawien.Stop();
            return;
        }
        if (Interlocked.CompareExchange(ref _ustawieniaWKolejce, 1, 0) != 0) return;
        var mp = _mp;
        WKolejce(() =>
        {
            try
            {
                if (mp == null || mp != _mp) return;
                int zmiany = Interlocked.Exchange(ref _zmianyUstawien, 0);
                var p = Volatile.Read(ref _parametry);
                if ((zmiany & ZmianaUrzadzenia) != 0) mp.SetOutputDevice(p.Urzadzenie);
                if ((zmiany & ZmianaGlosnosci) != 0) mp.Volume = p.GlosnoscVlc;
                if ((zmiany & ZmianaEq) != 0) ZastosujEq(mp, p.Eq);
            }
            finally { Volatile.Write(ref _ustawieniaWKolejce, 0); }
        });
        // Najwyżej jedno oczekujące polecenie; kolejne ruchy zastępują dane, nie tworzą kolejki.
    }

    public List<(string Id, string Opis)> PobierzUrzadzenia()
    {
        var wynik = new List<(string, string)>();
        try
        {
            foreach (var d in _vlc.AudioOutputDevices("mmdevice"))
                if (!string.IsNullOrEmpty(d.DeviceIdentifier))
                    wynik.Add((d.DeviceIdentifier, d.Description));
        }
        catch { }
        return wynik;
    }

    public void Graj(Stacja s)
    {
        if (_disposed) return;
        Biezaca = s;
        PowinnoGrac = true;
        _proby = 0;
        _zagrala = false;
        Uruchom(s);
    }

    public void Zatrzymaj()
    {
        Interlocked.Increment(ref _generacja);
        PowinnoGrac = false;
        _timerPonowienia.Stop();
        Tytul = "";
        UstawStan(StanOdtwarzania.Zatrzymane, "Zatrzymane");
        var mp = _mp;
        if (mp != null) WKolejce(() => mp.Stop());
    }

    void Uruchom(Stacja s)
    {
        if (_disposed || _przelaczanieSilnika) return;
        _timerPonowienia.Stop();
        _glebokosc = 0;
        Tytul = "";
        UstawStan(StanOdtwarzania.Laczenie, _proby == 0 ? "Łączenie…" : $"Ponowne łączenie (próba {_proby})…");

        var media = new Media(_vlc, s.Url, FromType.FromLocation);
        var stare = _media;
        _media = media;
        GrajMedium(media, stare);
    }

    /// <summary>Nowy MediaPlayer z urządzeniem i głośnością ustawionymi przed startem.</summary>
    void GrajMedium(Media media, Media? doZwolnienia = null)
    {
        int generacja = Interlocked.Increment(ref _generacja);
        _aktywne = media;
        PodepnijMeta(media);

        // Numer uruchomienia unieważnia także spóźnione zdarzenia po Stop
        // i podczas wymiany silnika (sender LibVLCSharp nie jest MediaPlayerem).
        var mp = new MediaPlayer(_vlc);
        bool Aktualny() => generacja == Volatile.Read(ref _generacja);
        mp.Playing += (_, _) => NaUi(() => { if (Aktualny()) NaGranie(); });
        mp.EncounteredError += (_, _) => NaUi(() => { if (Aktualny()) NaPrzerwanie(); });
        mp.EndReached += (_, _) => NaUi(() => { if (Aktualny()) NaKoniec(); });
        mp.Buffering += (_, e) => { float c = e.Cache; NaUi(() => { if (Aktualny()) NaBuforowanie(c); }); };

        var stary = _mp;
        _mp = mp;
        WKolejce(() =>
        {
            if (stary != null)
            {
                stary.Stop();
                stary.Dispose();
            }
            doZwolnienia?.Dispose();
            if (!Aktualny()) return;
            var p = Volatile.Read(ref _parametry);
            if (p.Urzadzenie.Length > 0) mp.SetOutputDevice(p.Urzadzenie);
            mp.Volume = p.GlosnoscVlc;
            ZastosujEq(mp, p.Eq);
            if (!mp.Play(media)) NaUi(() => { if (Aktualny()) NaPrzerwanie(); });
        });
    }

    void NaGranie()
    {
        if (!PowinnoGrac) return;
        _zagrala = true;
        _graOd = DateTime.Now;
        UstawStan(StanOdtwarzania.Gra, "Odtwarzanie");
        ZlecUstawienia(ZmianaGlosnosci | ZmianaEq);
    }

    void NaBuforowanie(float procent)
    {
        if (PowinnoGrac && Stan == StanOdtwarzania.Laczenie && procent < 100)
            UstawStan(Stan, $"Buforowanie {procent:0}%…");
    }

    void NaKoniec()
    {
        if (!PowinnoGrac) return;
        // Adres stacji bywa playlistą (.pls, .m3u) — LibVLC tylko ją rozwija, trzeba zagrać jej pozycję
        var akt = _aktywne;
        if (akt != null && _glebokosc < 3)
        {
            Media? pozycja = null;
            try { if (akt.SubItems.Count > 0) pozycja = akt.SubItems[0]; } catch { }
            if (pozycja != null)
            {
                _glebokosc++;
                GrajMedium(pozycja);
                return;
            }
        }
        NaPrzerwanie();
    }

    void NaPrzerwanie()
    {
        if (!PowinnoGrac || Biezaca == null || _timerPonowienia.Enabled) return;

        // Po dłuższym poprawnym graniu zerwanie to nowa awaria, licznik prób od nowa
        if (Stan == StanOdtwarzania.Gra && DateTime.Now - _graOd > TimeSpan.FromSeconds(30)) _proby = 0;

        int limit = _zagrala ? OpoznieniaPonowien.Length : ProbyNowejStacji;
        if (_proby >= limit)
        {
            PowinnoGrac = false;
            Tytul = "";
            UstawStan(StanOdtwarzania.Blad, "Nie udało się połączyć ze stacją");
            var mp = _mp;
            if (mp != null) WKolejce(() => mp.Stop());
            return;
        }

        int sekundy = OpoznieniaPonowien[_proby++];
        UstawStan(StanOdtwarzania.Ponawianie, $"Brak połączenia — ponowna próba za {sekundy} s ({_proby}/{limit})");
        _timerPonowienia.Interval = sekundy * 1000;
        _timerPonowienia.Start();
    }

    void PodepnijMeta(Media m)
    {
        m.MetaChanged += (_, e) =>
        {
            if (e.MetadataType == MetadataType.NowPlaying) NaUi(() => OdczytajTytul(m));
        };
    }

    void OdczytajTytul(Media m)
    {
        if (!PowinnoGrac || _przelaczanieSilnika || !ReferenceEquals(m, _aktywne)) return;
        string? t = null;
        try { t = m.Meta(MetadataType.NowPlaying); } catch { }
        if (string.IsNullOrWhiteSpace(t) || t.Trim() == Tytul) return;
        Tytul = t.Trim();
        Zmiana?.Invoke();
    }

    void UstawStan(StanOdtwarzania stan, string opis)
    {
        if (Stan == stan && Opis == opis) return;
        Stan = stan;
        Opis = opis;
        Zmiana?.Invoke();
    }

    void NaUi(Action a)
    {
        try
        {
            if (!_ui.IsDisposed && _ui.IsHandleCreated) _ui.BeginInvoke(() => { if (!_disposed) a(); });
        }
        catch { }
    }

    void WKolejce(Action a)
    {
        lock (_blokadaKolejki)
            _kolejka = _kolejka.ContinueWith(_ => { try { a(); } catch { } }, TaskScheduler.Default);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Interlocked.Increment(ref _generacja);
        PowinnoGrac = false;
        _timerPonowienia.Dispose();
        _timerUstawien.Dispose();
        var mp = _mp;
        _mp = null;
        if (mp != null) WKolejce(() => mp.Stop());
        Task t;
        lock (_blokadaKolejki) t = _kolejka;
        // Gdy Stop wisi na martwym połączeniu, nie zwalniamy LibVLC — proces i tak się kończy
        if (!t.Wait(3000)) return;
        try
        {
            mp?.Dispose();
            _media?.Dispose();
            _vlc.Dispose();
        }
        catch { }
    }
}
