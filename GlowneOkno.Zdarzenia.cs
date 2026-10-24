using System.Diagnostics;
using System.Reflection;
using SimpleRadioPlayer.Kontrolki;
using SimpleRadioPlayer.Okna;

namespace SimpleRadioPlayer;

partial class GlowneOkno
{
    // ---------- Odtwarzanie ----------

    void Graj(Stacja s)
    {
        if (_odtw == null)
        {
            UstawStatus("Silnik audio jeszcze się uruchamia…");
            return;
        }
        _konf.OstatniaStacja = s.Kopia();
        if (_konf.ZglaszajOdtworzenia) RadioBrowser.ZglosOdtworzenie(s.Uuid);
        ZapiszZOpoznieniem();
        // Głośność stacji ustawiana przed startem, żeby nie zagrała na chwilę poziomem poprzedniej
        if (_konf.GlosnoscPerStacja && _konf.GlosnoscStacji.TryGetValue(s.Klucz, out int g))
            UstawSuwakGlosnosci(g);
        _odtw.Graj(s);
        OdswiezListy();
    }

    void UstawSuwakGlosnosci(int wartosc)
    {
        _ustawianieSuwaka = true;
        try
        {
            _suwakGlosnosci.Value = Math.Clamp(wartosc, _suwakGlosnosci.Minimum, _suwakGlosnosci.Maximum);
            ZmienGlosnosc(_suwakGlosnosci.Value);
        }
        finally { _ustawianieSuwaka = false; }
    }

    bool _ustawianieSuwaka;     // zmiana suwaka z kodu (wybór stacji) nie jest zapisywana jako głośność stacji

    void PrzelaczGranie()
    {
        if (_odtw == null) return;
        if (_odtw.PowinnoGrac)
        {
            _odtw.Zatrzymaj();
            return;
        }
        var s = _odtw.Biezaca ?? AktywnaLista().Zaznaczona ?? _konf.OstatniaStacja;
        if (s != null) Graj(s);
    }

    void NaZmianeOdtwarzacza()
    {
        OdswiezPanel();
        OdswiezTray();
    }

    void OdswiezPanel()
    {
        var s = _odtw?.Biezaca ?? _konf.OstatniaStacja;
        bool gra = _odtw?.PowinnoGrac == true;

        _lblStacja.Text = s?.Nazwa ?? "Wybierz stację z listy";
        if (s?.Klucz != _kluczPanelu)
        {
            _kluczPanelu = s?.Klucz;
            OdswiezLogoPanelu();
        }
        _lblTytul.Text = _odtw is { Tytul.Length: > 0 } ? "♪ " + _odtw.Tytul : s?.Info ?? "";
        _podpowiedzi.SetToolTip(_lblTytul, _odtw is { Tytul.Length: > 0 } ? "Dwuklik albo prawy przycisk: kopiuj tytuł utworu" : "");
        _btnGraj.Text = gra ? "■" : "▶";
        _btnUlubiona.Enabled = _odtw?.Biezaca != null;
        _btnUlubiona.Text = s != null && _kluczeUlubionych.Contains(s.Klucz) ? "★" : "☆";
        _btnUlubiona.ForeColor = _btnUlubiona.Text == "★" ? Color.FromArgb(230, 160, 0) : SystemColors.ControlText;
        Text = gra && s != null ? $"{s.Nazwa} — Simple Radio Player" : "Simple Radio Player";
        if (_odtw != null) UstawStatus(_odtw.Opis);
    }

    void ZmienGlosnosc(int wartosc)
    {
        // Ruch suwaka przez użytkownika oznacza „chcę słyszeć” — zdejmujemy wyciszenie
        if (!_ustawianieSuwaka && _wyciszony) UstawWyciszenie(false);
        _lblGlosnosc.Text = $"{wartosc}%";
        if (_odtw != null) _odtw.Glosnosc = wartosc;
        _konf.Glosnosc = wartosc;
        if (!_ustawianieSuwaka && _konf.GlosnoscPerStacja && _odtw?.Biezaca is { } s)
            _konf.GlosnoscStacji[s.Klucz] = wartosc;
        ZapiszZOpoznieniem();
    }

    /// <summary>Wyciszenie nie jest zapisywane — po ponownym uruchomieniu radio gra normalnie.</summary>
    bool _wyciszony;

    void PrzelaczWyciszenie() => UstawWyciszenie(!_wyciszony);

    void UstawWyciszenie(bool wyciszony)
    {
        _wyciszony = wyciszony;
        if (_odtw != null) _odtw.Wyciszony = wyciszony;
        _btnWycisz.Text = wyciszony ? "🔇" : "🔊";
        _btnWycisz.ForeColor = wyciszony ? Color.FromArgb(200, 40, 40) : SystemColors.ControlText;
        _podpowiedzi.SetToolTip(_btnWycisz, wyciszony ? "Włącz dźwięk (Ctrl+M)" : "Wycisz (Ctrl+M)");
        _lblGlosnosc.Text = wyciszony ? "wyciszone" : $"{_suwakGlosnosci.Value}%";
        OdswiezTray();
    }

    /// <summary>Zapis z opóźnieniem — suwaki przy przeciąganiu generują dziesiątki zmian.</summary>
    void ZapiszZOpoznieniem()
    {
        if (_zapisOpozniony == null)
        {
            _zapisOpozniony = new System.Windows.Forms.Timer { Interval = 800 };
            _zapisOpozniony.Tick += (_, _) => { _zapisOpozniony.Stop(); _konf.Zapisz(); };
        }
        _zapisOpozniony.Stop();
        _zapisOpozniony.Start();
    }

    System.Windows.Forms.Timer? _zapisOpozniony;

    EqualizerOkno? _oknoEq;

    void OtworzEqualizer()
    {
        if (_odtw == null)
        {
            UstawStatus("Silnik audio jeszcze się uruchamia…");
            return;
        }
        if (_oknoEq is { IsDisposed: false })
        {
            _oknoEq.Activate();
            return;
        }
        _oknoEq = new EqualizerOkno(_konf.Equalizer, Odtwarzacz.CzestotliwosciPasm(), Odtwarzacz.Presety(), eq =>
        {
            _konf.Equalizer = eq;
            if (_odtw != null) _odtw.Equalizer = eq;
            OdswiezPrzyciskEq();
            ZapiszZOpoznieniem();
        });
        _oknoEq.Show(this);
    }

    void OdswiezPrzyciskEq()
    {
        bool wl = _konf.Equalizer.Wlaczony;
        _btnEq.Text = wl ? "Equalizer: wł." : "Equalizer";
        _btnEq.Font = wl ? (_pogrubionaEq ??= new Font(Font, FontStyle.Bold)) : Font;
    }

    Font? _pogrubionaEq;

    const string DopisekOdlaczone = " (niepodłączone)";

    List<OpcjeOkno.Urzadzenie> ListaUrzadzen()
    {
        var pozycje = new List<OpcjeOkno.Urzadzenie> { new("", "Domyślne urządzenie systemowe") };
        if (_odtw != null)
            pozycje.AddRange(_odtw.PobierzUrzadzenia().Select(u => new OpcjeOkno.Urzadzenie(u.Id, u.Opis)));

        // Zapisane urządzenie jest odłączone — zostaje na liście, żeby nie zgubić wyboru
        string id = _konf.UrzadzenieId;
        if (id.Length > 0 && !pozycje.Any(p => p.Id == id))
            pozycje.Add(new(id, (_konf.UrzadzenieNazwa.Length > 0 ? _konf.UrzadzenieNazwa : "Zapisane urządzenie") + DopisekOdlaczone));
        return pozycje;
    }

    void OdswiezUrzadzenia()
    {
        var pozycje = ListaUrzadzen();
        string id = _konf.UrzadzenieId;
        _cmbUrzadzenie.BeginUpdate();
        _cmbUrzadzenie.Items.Clear();
        foreach (var p in pozycje) _cmbUrzadzenie.Items.Add(p);
        _cmbUrzadzenie.SelectedItem = pozycje.First(p => p.Id == id);
        _cmbUrzadzenie.EndUpdate();
    }

    void ZmienUrzadzenie()
    {
        if (_cmbUrzadzenie.SelectedItem is OpcjeOkno.Urzadzenie p) UstawUrzadzenie(p.Id, p.Opis);
    }

    void UstawUrzadzenie(string id, string opis)
    {
        _konf.UrzadzenieId = id;
        _konf.UrzadzenieNazwa = id.Length > 0 ? opis.Replace(DopisekOdlaczone, "") : "";
        _konf.Zapisz();
        if (_odtw != null) _odtw.Urzadzenie = id;
    }

    async void OtworzOpcje()
    {
        using var okno = new OpcjeOkno(_konf, ListaUrzadzen(), _odtw?.Biezaca, Autostart.Wlaczony, tryb =>
        {
            // „Aktualizuj teraz” przy świeżo zmienionym trybie — tryb stosujemy od razu
            if (tryb != _konf.TrybLogo)
            {
                _konf.TrybLogo = tryb;
                _konf.Zapisz();
                PrzeladujLogotypy(false);
            }
            AktualizujLogotypy();
        });
        if (okno.ShowDialog(this) != DialogResult.OK) return;

        bool zmianaLogo = okno.TrybLogo != _konf.TrybLogo || okno.OdswiezanieLogoDni != _konf.OdswiezanieLogoDni;
        _konf.TrybLogo = okno.TrybLogo;
        _konf.OdswiezanieLogoDni = okno.OdswiezanieLogoDni;

        if (okno.UrzadzenieId != _konf.UrzadzenieId)
        {
            UstawUrzadzenie(okno.UrzadzenieId, okno.UrzadzenieOpis);
            OdswiezUrzadzenia();
        }
        _konf.TrybStartu = okno.TrybStartu;
        if (okno.StacjaStartowa is { } st) _konf.StacjaStartowa = st.Kopia();
        _konf.ZamykajDoTraya = okno.ZamykajDoTraya;
        _konf.AktualizacjaListDni = okno.AktualizacjaListDni;
        _konf.ZglaszajOdtworzenia = okno.ZglaszajOdtworzenia;
        _konf.SprawdzajAktualizacje = okno.SprawdzajAktualizacje;
        _konf.Zapisz();

        if (okno.Autostart != Autostart.Wlaczony)
        {
            try { Autostart.Ustaw(okno.Autostart); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Nie udało się zmienić autostartu:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        bool nowyFolder = false;
        if (okno.Przenosna != _konf.Przenosna)
        {
            if (_konf.ZmienMiejsce(okno.Przenosna)) nowyFolder = true;
            else MessageBox.Show(this, "Nie udało się zapisać ustawień w nowym miejscu — zostają tam, gdzie były.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        if (zmianaLogo || nowyFolder) PrzeladujLogotypy(nowyFolder);

        _konf.GlosnoscPerStacja = okno.GlosnoscPerStacja;
        if (okno.Wyrownywanie != _konf.Wyrownywanie)
        {
            try
            {
                if (_odtw != null) await _odtw.UstawWyrownywanieAsync(okno.Wyrownywanie);
                _konf.Wyrownywanie = okno.Wyrownywanie;
            }
            catch (Exception ex) { UstawStatus("Nie udało się zmienić wyrównywania: " + ex.Message); }
        }
        _konf.Zapisz();
    }

    // ---------- Listy stacji ----------

    void OdswiezUlubione()
    {
        _kluczeUlubionych = _konf.Ulubione.Select(s => s.Klucz).ToHashSet();
        _listaUlubione.Ustaw([.. _konf.Ulubione]);
        OdswiezLicznik();
    }

    void OdswiezListy()
    {
        _listaWszystkie.Invalidate();
        _listaUlubione.Invalidate();
    }

    ListaStacji AktywnaLista() => _zakladki.SelectedTab == _tabUlubione ? _listaUlubione : _listaWszystkie;

    void PrzelaczUlubiona(Stacja s)
    {
        int i = _konf.Ulubione.FindIndex(u => u.Klucz == s.Klucz);
        if (i >= 0) _konf.Ulubione.RemoveAt(i);
        else _konf.Ulubione.Add(s.Kopia());
        _konf.Zapisz();
        OdswiezUlubione();
        OdswiezListy();
        OdswiezPanel();
    }

    void PrzesunUlubiona(Stacja s, int kierunek)
    {
        int i = _konf.Ulubione.FindIndex(u => u.Klucz == s.Klucz);
        int j = i + kierunek;
        if (i < 0 || j < 0 || j >= _konf.Ulubione.Count) return;
        (_konf.Ulubione[i], _konf.Ulubione[j]) = (_konf.Ulubione[j], _konf.Ulubione[i]);
        _konf.Zapisz();
        OdswiezUlubione();
        _listaUlubione.Zaznacz(s);
    }

    void UstawStartowa(Stacja s)
    {
        _konf.StacjaStartowa = s.Kopia();
        _konf.TrybStartu = TrybStartu.Startowa;
        _konf.Zapisz();
        UstawStatus($"Stacja startowa: {s.Nazwa}");
    }

    void DodajStacje()
    {
        using var okno = new DodajStacjeOkno();
        if (okno.ShowDialog(this) != DialogResult.OK || okno.Wynik is not { } s) return;
        _konf.WlasneStacje.RemoveAll(w => w.Klucz == s.Klucz);
        _konf.WlasneStacje.Add(s);
        if (okno.DoUlubionych && !_kluczeUlubionych.Contains(s.Klucz)) _konf.Ulubione.Add(s.Kopia());
        _konf.Zapisz();
        WczytajListy();
        OdswiezUlubione();
    }

    void UsunWlasna(Stacja s)
    {
        if (MessageBox.Show(this, $"Usunąć stację „{s.Nazwa}” z własnych stacji?", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _konf.WlasneStacje.RemoveAll(w => w.Klucz == s.Klucz);
        _konf.Zapisz();
        WczytajListy();
    }

    void OtworzFolderList()
    {
        try
        {
            Directory.CreateDirectory(Playlisty.Folder);
            Process.Start(new ProcessStartInfo(Playlisty.Folder) { UseShellExecute = true });
        }
        catch { }
    }

    // ---------- Menu kontekstowe listy ----------

    void NaMyszNaLiscie(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right || sender is not ListaStacji lista) return;
        var trafienie = lista.HitTest(e.Location);
        if (trafienie.Item == null) return;
        lista.SelectedIndices.Clear();
        lista.SelectedIndices.Add(trafienie.Item.Index);
        _menuListy.Show(lista, e.Location);
    }

    bool BudujMenuListy()
    {
        var lista = AktywnaLista();
        if (lista.Zaznaczona is not { } s) return false;
        bool ulubiona = _kluczeUlubionych.Contains(s.Klucz);

        _menuListy.Items.Clear();
        var graj = new ToolStripMenuItem("Odtwórz", null, (_, _) => Graj(s)) { Font = new Font(_menuListy.Font, FontStyle.Bold) };
        _menuListy.Items.Add(graj);
        _menuListy.Items.Add(ulubiona ? "Usuń z ulubionych" : "Dodaj do ulubionych", null, (_, _) => PrzelaczUlubiona(s));
        _menuListy.Items.Add("Ustaw jako stację startową", null, (_, _) => UstawStartowa(s));
        if (lista == _listaUlubione)
        {
            _menuListy.Items.Add(new ToolStripSeparator());
            _menuListy.Items.Add("Przesuń w górę", null, (_, _) => PrzesunUlubiona(s, -1));
            _menuListy.Items.Add("Przesuń w dół", null, (_, _) => PrzesunUlubiona(s, 1));
        }
        _menuListy.Items.Add(new ToolStripSeparator());
        _menuListy.Items.Add("Kopiuj adres strumienia", null, (_, _) => { try { Clipboard.SetText(s.Url); } catch { } });
        if (s.Wlasna) _menuListy.Items.Add("Usuń stację…", null, (_, _) => UsunWlasna(s));
        return true;
    }

    void NaKlawiszListy(object? sender, KeyEventArgs e)
    {
        if (sender is not ListaStacji lista || lista.Zaznaczona is not { } s) return;
        if (e.KeyCode == Keys.Delete && lista == _listaUlubione)
        {
            PrzelaczUlubiona(s);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Insert || (e.Control && e.KeyCode == Keys.D))
        {
            PrzelaczUlubiona(s);
            e.Handled = true;
        }
        else if (e.Control && e.KeyCode == Keys.Up && lista == _listaUlubione)
        {
            PrzesunUlubiona(s, -1);
            e.Handled = true;
        }
        else if (e.Control && e.KeyCode == Keys.Down && lista == _listaUlubione)
        {
            PrzesunUlubiona(s, 1);
            e.Handled = true;
        }
    }

    void NaKlawisz(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.M)
        {
            PrzelaczWyciszenie();
            e.Handled = e.SuppressKeyPress = true;
        }
        else if (e.Control && e.KeyCode == Keys.F)
        {
            _zakladki.SelectedTab = _tabWszystkie;
            _txtSzukaj.Focus();
            _txtSzukaj.SelectAll();
            e.Handled = e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Space && ActiveControl is ListaStacji)
        {
            PrzelaczGranie();
            e.Handled = e.SuppressKeyPress = true;
        }
    }

    void UstawStatus(string tekst) => _lblStatus.Text = tekst;

    void KopiujTytul()
    {
        if (_odtw is not { Tytul.Length: > 0 } o) return;
        try
        {
            Clipboard.SetText(o.Tytul);
            UstawStatus("Skopiowano tytuł: " + o.Tytul);
        }
        catch { UstawStatus("Nie udało się skopiować tytułu — schowek jest zajęty przez inny program"); }
    }

    void PokazOProgramie()
    {
        var a = Assembly.GetExecutingAssembly();
        string produkt = a.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "";
        string wersja = a.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "";
        string autor = a.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? "";
        string prawa = a.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";
        string opis = a.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description ?? "";
        string silnik = _odtw != null ? $"LibVLC {_odtw.WersjaSilnika}" : "LibVLC";
        MessageBox.Show(this,
            $"{produkt}\nWersja {wersja}\n\n{opis}\n\nAutor: {autor}\n{prawa}\n" +
            "Wolne oprogramowanie na licencji GNU GPL v3 — darmowe, także do użytku w firmie.\n" +
            "Program jest udostępniany bez jakiejkolwiek gwarancji.\n" +
            $"Kod źródłowy: {AdresRepozytorium}\n\n" +
            $"Jeśli program Ci się przydaje, możesz dobrowolnie wesprzeć autora:\n{AdresLinku}\n\n" +
            "Dane stacji: Radio Browser (radio-browser.info)\n" +
            $"Silnik dźwięku: {silnik} — VideoLAN, licencja LGPL\n" +
            "Szczegóły: Pomoc → Licencje i źródła danych\n\n" +
            $"Ustawienia: {_konf.Sciezka}",
            "O programie", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    void OtworzPlikLicencji()
    {
        string plik = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.txt");
        OtworzAdres(File.Exists(plik) ? plik : AdresRepozytorium + "/blob/main/THIRD-PARTY-NOTICES.txt");
    }
}
