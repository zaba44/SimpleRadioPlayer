namespace SimpleRadioPlayer;

partial class GlowneOkno
{
    bool _aktualizacjaLogo;     // ręczna aktualizacja w toku — pokazujemy postęp w pasku stanu
    string? _kluczPanelu;       // stacja, której obraz jest teraz w panelu — przerysowujemy tylko przy zmianie

    Logotypy UtworzLogotypy()
    {
        var l = new Logotypy(_konf.FolderLogo)
        {
            Tryb = _konf.TrybLogo,
            DniOdswiezania = _konf.OdswiezanieLogoDni,
        };
        l.Zaktualizowano += klucz =>
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(() => { if (!IsDisposed && ReferenceEquals(l, _logo)) NaNoweLogo(klucz); }); } catch { }
        };
        return l;
    }

    Bitmap ObrazStacji(Stacja s, int px)
    {
        using var logo = _logo.Logo(s);
        return ObrazyStacji.Rysuj(s, logo, px);
    }

    void NaNoweLogo(string klucz)
    {
        if (_kluczeUlubionych.Contains(klucz)) _listaUlubione.OdswiezObraz(klucz);
        if (StacjaPanelu()?.Klucz == klucz) OdswiezLogoPanelu();
        if (_aktualizacjaLogo)
        {
            int zostalo = _logo.WToku;
            if (zostalo == 0)
            {
                _aktualizacjaLogo = false;
                UstawStatus("Logotypy zaktualizowane");
            }
            else UstawStatus($"Aktualizacja logotypów — zostało {zostalo}…");
        }
    }

    Stacja? StacjaPanelu() => _odtw?.Biezaca ?? _konf.OstatniaStacja;

    void OdswiezLogoPanelu()
    {
        var s = StacjaPanelu();
        var stary = _obrazStacji.Image;
        _obrazStacji.Image = s != null ? ObrazStacji(s, _obrazStacji.Width) : null;
        stary?.Dispose();
    }

    /// <summary>Ręczna aktualizacja: ulubione i stacje z panelu pobierane od razu, reszta przy wyświetleniu.</summary>
    void AktualizujLogotypy()
    {
        if (_logo.Tryb == TrybLogo.TylkoKafelki)
        {
            UstawStatus("Logotypy są wyłączone — włącz je w Opcjach");
            return;
        }
        _logo.OznaczDoOdswiezenia();
        var stacje = _konf.Ulubione
            .Append(_odtw?.Biezaca).Append(_konf.OstatniaStacja).Append(_konf.StacjaStartowa)
            .OfType<Stacja>().DistinctBy(s => s.Klucz).ToList();
        foreach (var s in stacje) _logo.Pobierz(s);
        _aktualizacjaLogo = stacje.Count > 0;
        UstawStatus(stacje.Count > 0 ? $"Aktualizacja logotypów ({stacje.Count})…" : "Brak stacji do aktualizacji");
    }

    /// <summary>Po zmianie trybu albo miejsca ustawień — przerysuj wszystko.</summary>
    void PrzeladujLogotypy(bool nowyFolder)
    {
        if (nowyFolder)
        {
            string stary = Path.Combine(_konf.Przenosna ? Konfiguracja.FolderProfilu : Konfiguracja.FolderProgramu, "logo");
            _logo.Dispose();
            try
            {
                if (Directory.Exists(stary) && !Directory.Exists(_konf.FolderLogo)) Directory.Move(stary, _konf.FolderLogo);
            }
            catch { }
            _logo = UtworzLogotypy();
        }
        else
        {
            _logo.Tryb = _konf.TrybLogo;
            _logo.DniOdswiezania = _konf.OdswiezanieLogoDni;
        }
        _listaUlubione.OdswiezObrazy();
        OdswiezLogoPanelu();
    }
}
