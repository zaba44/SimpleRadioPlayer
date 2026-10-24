using System.Globalization;
using SimpleRadioPlayer.Okna;

namespace SimpleRadioPlayer;

/// <summary>
/// Listy stacji: przełącznik list nad stacjami, pobieranie i aktualizacja z Radio Browser,
/// import plików (także przeciągnij i upuść), eksport ulubionych.
/// </summary>
partial class GlowneOkno
{
    const string NazwaWlasnych = "Własne stacje";

    sealed record PozycjaListy(string Klucz, string Opis)
    {
        public override string ToString() => Opis;
    }

    List<ListaPlikowa> _listy = [];
    CancellationTokenSource? _pobieranie;
    bool _pytanieOListachOdlozone;      // termin minął, gdy okno było w trayu — zapytamy po jego pokazaniu
    bool _ustawianiePrzelacznika;

    static readonly StringComparer PorownaniePl = StringComparer.Create(new CultureInfo("pl-PL"), true);

    // ---------- Wczytywanie i przełącznik ----------

    void WczytajListy()
    {
        var bledy = new List<string>();
        _listy = Playlisty.WczytajWszystkie(bledy);
        if (_konf.WlasneStacje.Count > 0)
        {
            var wlasne = _konf.WlasneStacje.Select(w =>
            {
                var k = w.Kopia();
                k.Zrodlo = NazwaWlasnych;
                k.Wlasna = true;
                return k;
            }).ToList();
            _listy.Insert(0, new ListaPlikowa(NazwaWlasnych, "", wlasne));
        }
        foreach (var l in _listy) l.Stacje.Sort((a, b) => PorownaniePl.Compare(a.Nazwa, b.Nazwa));

        // Widok „wszystkie listy”: ta sama stacja z kilku list tylko raz (wygrywa pierwsza lista)
        var znane = new HashSet<string>();
        var wszystkie = new List<Stacja>();
        foreach (var l in _listy)
            foreach (var s in l.Stacje)
                if (znane.Add(s.Klucz)) wszystkie.Add(s);
        wszystkie.Sort((a, b) => PorownaniePl.Compare(a.Nazwa, b.Nazwa));
        _wszystkie = wszystkie;

        // Pliku pobranej listy już nie ma (usunięty ręcznie) — nie aktualizujemy go dalej
        int przed = _konf.PobraneListy.Count;
        _konf.PobraneListy.RemoveAll(p => !File.Exists(Path.Combine(Playlisty.Folder, p.Plik)));
        if (_konf.PobraneListy.Count != przed) _konf.Zapisz();

        OdswiezPrzelacznikList();
        Filtruj();
        // Visible czytane z kontrolki zwraca false, dopóki zakładka nie jest pokazana — liczymy stan sami
        bool pusto = _listy.Count == 0;
        _pustyStan.Visible = pusto;
        if (pusto) _pustyStan.BringToFront();
        _miAktualizujListy.Enabled = _konf.PobraneListy.Count > 0;

        if (bledy.Count > 0) UstawStatus("Nie wczytano: " + string.Join("; ", bledy));
    }

    void OdswiezPrzelacznikList()
    {
        _ustawianiePrzelacznika = true;
        try
        {
            _cmbLista.BeginUpdate();
            _cmbLista.Items.Clear();
            _cmbLista.Items.Add(new PozycjaListy("", $"Wszystkie listy ({_wszystkie.Count})"));
            foreach (var l in _listy) _cmbLista.Items.Add(new PozycjaListy(l.Nazwa, $"{l.Nazwa} ({l.Stacje.Count})"));
            var wybrana = _cmbLista.Items.Cast<PozycjaListy>().FirstOrDefault(p => p.Klucz == _konf.WybranaLista);
            _cmbLista.SelectedItem = wybrana ?? _cmbLista.Items[0];
            if (wybrana == null) _konf.WybranaLista = "";
            _cmbLista.EndUpdate();
        }
        finally { _ustawianiePrzelacznika = false; }
        OdswiezMenuList();
    }

    void NaZmianeListy()
    {
        if (_ustawianiePrzelacznika || _cmbLista.SelectedItem is not PozycjaListy p) return;
        _konf.WybranaLista = p.Klucz;
        ZapiszZOpoznieniem();
        OdswiezMenuList();
        Filtruj();
    }

    void WybierzListe(string nazwa)
    {
        _konf.WybranaLista = _listy.Any(l => l.Nazwa == nazwa) ? nazwa : "";
        ZapiszZOpoznieniem();
        OdswiezPrzelacznikList();
        Filtruj();
        _zakladki.SelectedTab = _tabWszystkie;
    }

    List<Stacja> StacjeWybranejListy() =>
        _konf.WybranaLista.Length == 0
            ? _wszystkie
            : _listy.FirstOrDefault(l => l.Nazwa == _konf.WybranaLista)?.Stacje ?? _wszystkie;

    void Filtruj()
    {
        var zrodlo = StacjeWybranejListy();
        var slowa = Stacja.Normalizuj(_txtSzukaj.Text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var wynik = slowa.Length == 0
            ? zrodlo
            : zrodlo.Where(s => slowa.All(sl => s.KluczSzukania.Contains(sl, StringComparison.Ordinal))).ToList();
        _listaWszystkie.Ustaw(wynik);
        OdswiezLicznik();
    }

    void OdswiezLicznik()
    {
        _tabUlubione.Text = $"Ulubione ({_konf.Ulubione.Count})";
        _tabWszystkie.Text = $"Stacje ({StacjeWybranejListy().Count})";
        _lblLicznik.Text = _zakladki.SelectedTab == _tabWszystkie && _listaWszystkie.Stacje.Count != StacjeWybranejListy().Count
            ? $"Znaleziono: {_listaWszystkie.Stacje.Count}"
            : "";
    }

    void OdswiezMenuList()
    {
        var lista = _listy.FirstOrDefault(l => l.Nazwa == _konf.WybranaLista);
        _miUsunListe.Enabled = lista is { Plik.Length: > 0 };
        _miUsunListe.Text = lista is { Plik.Length: > 0 } ? $"Usuń listę „{lista.Nazwa}”…" : "Usuń wybraną listę…";
    }

    // ---------- Pobieranie i aktualizacja z Radio Browser ----------

    async void PobierzListe()
    {
        if (_pobieranie != null)
        {
            UstawStatus("Pobieranie listy już trwa…");
            return;
        }
        using var okno = new PobierzListeOkno();
        if (okno.ShowDialog(this) != DialogResult.OK || okno.Wynik is not { } definicja) return;
        if (await PobierzIZapisz([definicja])) WybierzListe(Path.GetFileNameWithoutExtension(definicja.Plik));
    }

    async Task AktualizujPobraneListy()
    {
        if (_pobieranie != null || _konf.PobraneListy.Count == 0) return;
        await PobierzIZapisz([.. _konf.PobraneListy]);
    }

    async Task<bool> PobierzIZapisz(List<PobranaLista> definicje)
    {
        _pobieranie = new CancellationTokenSource();
        _postepStartu.Visible = true;
        _miPobierzListe.Enabled = _miAktualizujListy.Enabled = false;
        int pobrane = 0, naprawione = 0;
        var bledy = new List<string>();
        var postep = new Progress<string>(UstawStatus);
        try
        {
            foreach (var d in definicje)
            {
                string nazwa = ListyRadioBrowser.NazwaListy(d);
                try
                {
                    UstawStatus($"Pobieranie listy „{nazwa}”…");
                    var stacje = await ListyRadioBrowser.PobierzAsync(d, postep, _pobieranie.Token);
                    if (stacje.Count == 0)
                    {
                        bledy.Add($"{nazwa}: baza nie zwróciła żadnej stacji");
                        continue;
                    }
                    await Task.Run(() => ListyRadioBrowser.Zapisz(d, stacje));
                    _konf.PobraneListy.RemoveAll(p => p.Plik.Equals(d.Plik, StringComparison.OrdinalIgnoreCase));
                    _konf.PobraneListy.Add(d);
                    naprawione += NaprawZapisaneStacje(stacje);
                    pobrane++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { bledy.Add($"{nazwa}: {ex.Message}"); }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            _pobieranie.Dispose();
            _pobieranie = null;
            _postepStartu.Visible = false;
            _miPobierzListe.Enabled = true;
        }

        if (pobrane > 0) _konf.ListySprawdzone = DateTime.Now;
        _konf.Zapisz();
        WczytajListy();
        OdswiezUlubione();

        string wynik = pobrane switch
        {
            0 => "Nie pobrano listy",
            1 => "Lista pobrana",
            _ => $"Pobrano list: {pobrane}",
        };
        if (naprawione > 0) wynik += $"; zaktualizowano adresy ulubionych: {naprawione}";
        if (bledy.Count > 0) wynik += " — błędy: " + string.Join("; ", bledy);
        UstawStatus(wynik);
        if (pobrane == 0 && bledy.Count > 0 && Visible)
            MessageBox.Show(this, "Nie udało się pobrać listy stacji.\n\n" + string.Join("\n", bledy) +
                "\n\nSprawdź połączenie z internetem i spróbuj ponownie.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return pobrane > 0;
    }

    /// <summary>
    /// Ulubione, ostatnia i startowa stacja po aktualizacji listy: dopisuje identyfikator Radio Browser,
    /// a gdy stacja o tym samym identyfikatorze ma nowy adres — przenosi ją na niego (z głośnością stacji).
    /// </summary>
    int NaprawZapisaneStacje(List<Stacja> nowe)
    {
        var poUuid = nowe.Where(s => s.Uuid.Length > 0).GroupBy(s => s.Uuid).ToDictionary(g => g.Key, g => g.First());
        var poKluczu = nowe.GroupBy(s => s.Klucz).ToDictionary(g => g.Key, g => g.First());

        bool Napraw(Stacja z)
        {
            if (z.Uuid.Length == 0)
            {
                if (poKluczu.TryGetValue(z.Klucz, out var ta) && ta.Uuid.Length > 0)
                {
                    z.Uuid = ta.Uuid;
                    if (z.Logo.Length == 0) z.Logo = ta.Logo;
                }
                return false;
            }
            if (!poUuid.TryGetValue(z.Uuid, out var nowa) || nowa.Klucz == z.Klucz) return false;
            string stary = z.Klucz;
            z.Url = nowa.Url;
            if (_konf.GlosnoscStacji.Remove(stary, out int g)) _konf.GlosnoscStacji[z.Klucz] = g;
            return true;
        }

        int zmienione = _konf.Ulubione.Count(Napraw);
        foreach (var s in new[] { _konf.OstatniaStacja, _konf.StacjaStartowa })
            if (s != null) Napraw(s);
        return zmienione;
    }

    /// <summary>Po upływie ustawionego okresu pyta o aktualizację; „Nie” też odlicza okres od nowa.</summary>
    void SprawdzTerminAktualizacjiList()
    {
        if (_konf.PobraneListy.Count == 0 || _konf.AktualizacjaListDni <= 0 || _pobieranie != null) return;
        var od = _konf.ListySprawdzone != default ? _konf.ListySprawdzone : _konf.PobraneListy.Min(p => p.Pobrano);
        if (DateTime.Now - od < TimeSpan.FromDays(_konf.AktualizacjaListDni)) return;
        if (!Visible)
        {
            _pytanieOListachOdlozone = true;
            return;
        }
        _pytanieOListachOdlozone = false;

        int wiek = (int)(DateTime.Now - _konf.PobraneListy.Max(p => p.Pobrano)).TotalDays;
        string listy = string.Join("\n", _konf.PobraneListy.Select(p => "• " + Path.GetFileNameWithoutExtension(p.Plik)));
        var odp = MessageBox.Show(this,
            $"Pobrane listy stacji mają już {wiek} dni:\n\n{listy}\n\nZaktualizować je teraz z Radio Browser?\n" +
            "Poprzednia wersja każdej listy zostanie zachowana jako kopia .bak.",
            "Aktualizacja list stacji", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (odp == DialogResult.Yes)
        {
            _ = AktualizujPobraneListy();
            return;
        }
        _konf.ListySprawdzone = DateTime.Now;
        _konf.Zapisz();
    }

    // ---------- Import, eksport, usuwanie ----------

    void ImportujListe()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Wybierz listę stacji",
            Filter = Playlisty.FiltrPlikow,
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) ImportujPliki(dlg.FileNames);
    }

    /// <summary>Kopiuje listy do folderu listy\ (z plików wybranych w oknie albo upuszczonych na okno).</summary>
    void ImportujPliki(IEnumerable<string> pliki)
    {
        var zaimportowane = new List<(string Nazwa, int Liczba)>();
        var pominiete = new List<string>();
        try
        {
            Directory.CreateDirectory(Playlisty.Folder);
            foreach (var plik in pliki.Where(Playlisty.CzyLista))
            {
                int liczba;
                try { liczba = Playlisty.Wczytaj(plik).Count; }
                catch { liczba = 0; }
                if (liczba == 0)
                {
                    pominiete.Add(Path.GetFileName(plik));
                    continue;
                }
                string cel = Path.Combine(Playlisty.Folder, Path.GetFileName(plik));
                if (!string.Equals(Path.GetFullPath(plik), Path.GetFullPath(cel), StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(cel) && MessageBox.Show(this, $"Lista „{Path.GetFileName(cel)}” już jest. Zastąpić?\n(Poprzednia wersja zostanie zachowana jako .bak.)",
                            Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) continue;
                    if (File.Exists(cel)) File.Copy(cel, cel + ".bak", true);
                    File.Copy(plik, cel, true);
                }
                zaimportowane.Add((Path.GetFileNameWithoutExtension(cel), liczba));
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Nie udało się skopiować listy:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        WczytajListy();
        if (zaimportowane.Count == 1) WybierzListe(zaimportowane[0].Nazwa);
        else if (zaimportowane.Count > 1) WybierzListe("");
        string opis = zaimportowane.Count > 0
            ? "Zaimportowano: " + string.Join(", ", zaimportowane.Select(z => $"{z.Nazwa} ({z.Liczba})"))
            : "Nic nie zaimportowano";
        if (pominiete.Count > 0) opis += " — bez stacji: " + string.Join(", ", pominiete);
        UstawStatus(opis);
    }

    void EksportujUlubione()
    {
        if (_konf.Ulubione.Count == 0)
        {
            MessageBox.Show(this, "Lista ulubionych jest pusta.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dlg = new SaveFileDialog
        {
            Title = "Eksportuj ulubione",
            Filter = "Lista M3U8 (UTF-8)|*.m3u8|Lista M3U|*.m3u",
            FileName = "Ulubione stacje.m3u8",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            Playlisty.ZapiszM3u8(dlg.FileName, "Ulubione stacje", _konf.Ulubione,
                $"Wyeksportowano z Simple Radio Player {DateTime.Now:yyyy-MM-dd}.");
            UstawStatus($"Zapisano {_konf.Ulubione.Count} ulubionych do {Path.GetFileName(dlg.FileName)}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Nie udało się zapisać listy:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Usunięcie listy = zmiana nazwy na .bak, więc przypadkowe usunięcie da się cofnąć.</summary>
    void UsunWybranaListe()
    {
        var lista = _listy.FirstOrDefault(l => l.Nazwa == _konf.WybranaLista);
        if (lista is not { Plik.Length: > 0 }) return;
        if (MessageBox.Show(this, $"Usunąć listę „{lista.Nazwa}” ({lista.Stacje.Count} stacji)?\n\n" +
                "Ulubione z tej listy zostaną. Plik zostanie zachowany jako kopia .bak w folderze list.",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            File.Move(lista.Plik, lista.Plik + ".bak", true);
            _konf.PobraneListy.RemoveAll(p => p.Plik.Equals(Path.GetFileName(lista.Plik), StringComparison.OrdinalIgnoreCase));
            _konf.WybranaLista = "";
            _konf.Zapisz();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Nie udało się usunąć listy:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        WczytajListy();
    }

    // ---------- Przeciągnij i upuść ----------

    void WlaczPrzeciaganie(Control c)
    {
        c.AllowDrop = true;
        c.DragEnter += NaPrzeciaganie;
        c.DragOver += NaPrzeciaganie;
        c.DragDrop += NaUpuszczenie;
        foreach (Control dziecko in c.Controls) WlaczPrzeciaganie(dziecko);
    }

    static List<string> PlikiList(DragEventArgs e) =>
        e.Data?.GetData(DataFormats.FileDrop) is string[] pliki ? [.. pliki.Where(Playlisty.CzyLista)] : [];

    void NaPrzeciaganie(object? sender, DragEventArgs e) =>
        e.Effect = PlikiList(e).Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;

    void NaUpuszczenie(object? sender, DragEventArgs e)
    {
        var pliki = PlikiList(e);
        // Okna dialogowe po upuszczeniu dopiero po zakończeniu operacji przeciągania w Eksploratorze
        if (pliki.Count > 0) BeginInvoke(() => ImportujPliki(pliki));
    }
}
