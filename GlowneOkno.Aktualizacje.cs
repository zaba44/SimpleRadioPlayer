namespace SimpleRadioPlayer;

/// <summary>
/// Informacja o nowej wersji: dyskretny pasek pod menu, a gdy okno jest schowane — dymek w trayu.
/// Automatycznie najwyżej raz na dobę; ręcznie z menu Pomoc w dowolnej chwili.
/// </summary>
partial class GlowneOkno
{
    Panel _pasekAktualizacji = null!;
    Label _lblAktualizacja = null!;
    AktualizacjeProgramu.Wydanie? _noweWydanie;
    string? _adresZDymka;                       // dymek traya o nowej wersji — klik otwiera stronę wydania
    System.Windows.Forms.Timer? _timerAktualizacji;
    bool _sprawdzanieAktualizacji;

    Panel BudujPasekAktualizacji()
    {
        _pasekAktualizacji = new Panel
        {
            Dock = DockStyle.Top, Height = 30, Visible = false,
            BackColor = Color.FromArgb(232, 245, 236), Padding = new Padding(10, 0, 6, 0),
        };
        var uklad = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Color.Transparent };
        _lblAktualizacja = new Label { AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Color.FromArgb(31, 77, 46), Margin = new Padding(0, 7, 8, 0) };
        LinkLabel Link(string tekst, Action akcja)
        {
            var l = new LinkLabel
            {
                Text = tekst, AutoSize = true, Margin = new Padding(6, 7, 6, 0),
                LinkColor = Color.FromArgb(30, 120, 60), ActiveLinkColor = Color.FromArgb(20, 90, 45),
            };
            l.LinkClicked += (_, _) => akcja();
            return l;
        }
        uklad.Controls.Add(_lblAktualizacja);
        uklad.Controls.Add(Link("Pobierz", () => { if (_noweWydanie != null) OtworzAdres(_noweWydanie.Adres); }));
        uklad.Controls.Add(Link("Pomiń tę wersję", PominWersje));
        uklad.Controls.Add(Link("Zamknij", () => _pasekAktualizacji.Visible = false));
        _pasekAktualizacji.Controls.Add(uklad);
        return _pasekAktualizacji;
    }

    /// <summary>Start programu i potem co 6 godzin — samo sprawdzenie odbywa się najwyżej raz na dobę.</summary>
    void UruchomSprawdzanieAktualizacji()
    {
        _timerAktualizacji = new System.Windows.Forms.Timer { Interval = 6 * 60 * 60 * 1000 };
        _timerAktualizacji.Tick += async (_, _) => await SprawdzAktualizacje(false);
        _timerAktualizacji.Start();
        _ = SprawdzAktualizacje(false);
    }

    async Task SprawdzAktualizacje(bool reczne)
    {
        if (_sprawdzanieAktualizacji) return;
        if (!reczne && (!_konf.SprawdzajAktualizacje || DateTime.Now - _konf.AktualizacjeSprawdzone < TimeSpan.FromHours(20))) return;

        _sprawdzanieAktualizacji = true;
        AktualizacjeProgramu.Wydanie? wydanie;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            wydanie = await AktualizacjeProgramu.NajnowszeAsync(cts.Token, RepozytoriumTestowe());
        }
        catch (Exception ex)
        {
            if (reczne)
                MessageBox.Show(this, "Nie udało się sprawdzić aktualizacji:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;     // bez sieci — spróbujemy przy następnej okazji, termin się nie przesuwa
        }
        finally { _sprawdzanieAktualizacji = false; }

        _konf.AktualizacjeSprawdzone = DateTime.Now;
        ZapiszZOpoznieniem();

        var biezaca = AktualizacjeProgramu.BiezacaWersja;
        if (wydanie == null || wydanie.Wersja <= biezaca)
        {
            if (reczne)
                MessageBox.Show(this, $"Masz najnowszą wersję ({biezaca.ToString(3)}).", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!reczne && _konf.PominietaWersja == wydanie.Tag) return;

        _noweWydanie = wydanie;
        _lblAktualizacja.Text = $"Dostępna nowa wersja {wydanie.Wersja.ToString(3)} (masz {biezaca.ToString(3)}).";
        _pasekAktualizacji.Visible = true;
        if (!Visible)
        {
            _adresZDymka = wydanie.Adres;
            _tray.ShowBalloonTip(8000, "Simple Radio Player — nowa wersja",
                $"Dostępna wersja {wydanie.Wersja.ToString(3)}. Kliknij, aby otworzyć stronę pobierania.", ToolTipIcon.Info);
        }
    }

    void PominWersje()
    {
        if (_noweWydanie == null) return;
        _konf.PominietaWersja = _noweWydanie.Tag;
        _konf.Zapisz();
        _pasekAktualizacji.Visible = false;
    }

    /// <summary>Tylko w wersji Debug: inne repozytorium do sprawdzenia paska (zmienna SRP_REPO_AKTUALIZACJI).</summary>
    static string? RepozytoriumTestowe()
    {
#if DEBUG
        return Environment.GetEnvironmentVariable("SRP_REPO_AKTUALIZACJI");
#else
        return null;
#endif
    }
}
