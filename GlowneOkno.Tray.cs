namespace SimpleRadioPlayer;

partial class GlowneOkno
{
    NotifyIcon _tray = null!;
    ContextMenuStrip _menuTraya = null!;

    void BudujTray()
    {
        _menuTraya = new ContextMenuStrip();
        _menuTraya.Opening += (_, _) => BudujMenuTraya();
        // Menu musi mieć choć jedną pozycję, inaczej pierwszy prawy klik go nie otworzy
        _menuTraya.Items.Add("…");

        _tray = new NotifyIcon
        {
            Icon = Ikony.Tray(false),
            Text = "Simple Radio Player",
            ContextMenuStrip = _menuTraya,
            Visible = true,
        };
        _tray.BalloonTipClicked += (_, _) => { if (_adresZDymka != null) OtworzAdres(_adresZDymka); };
        _tray.BalloonTipClosed += (_, _) => _adresZDymka = null;
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                if (Visible && WindowState != FormWindowState.Minimized) SchowajDoTraya();
                else PrzywrocZTraya();
            }
            else if (e.Button == MouseButtons.Middle) PrzelaczGranie();
        };
    }

    void BudujMenuTraya()
    {
        _menuTraya.Items.Clear();
        var s = _odtw?.Biezaca ?? _konf.OstatniaStacja;
        bool gra = _odtw?.PowinnoGrac == true;

        if (s != null)
        {
            var naglowek = new ToolStripMenuItem(Skroc(s.Nazwa, 50)) { Enabled = false };
            _menuTraya.Items.Add(naglowek);
            if (_odtw is { Tytul.Length: > 0 })
            {
                _menuTraya.Items.Add(new ToolStripMenuItem("♪ " + Skroc(_odtw.Tytul, 50)) { Enabled = false });
                _menuTraya.Items.Add("Kopiuj tytuł utworu", null, (_, _) => KopiujTytul());
            }
        }
        _menuTraya.Items.Add(gra ? "Zatrzymaj" : "Odtwórz", null, (_, _) => PrzelaczGranie()).Enabled = s != null && _odtw != null;
        _menuTraya.Items.Add(new ToolStripMenuItem(_wyciszony ? "Włącz dźwięk" : "Wycisz", null, (_, _) => PrzelaczWyciszenie()) { Checked = _wyciszony });

        var ulubione = new ToolStripMenuItem("Ulubione");
        foreach (var u in _konf.Ulubione)
        {
            var poz = new ToolStripMenuItem(Skroc(u.Nazwa, 50), null, (_, _) => Graj(u))
            {
                Checked = gra && _odtw?.Biezaca?.Klucz == u.Klucz,
            };
            ulubione.DropDownItems.Add(poz);
        }
        if (ulubione.DropDownItems.Count == 0)
            ulubione.DropDownItems.Add(new ToolStripMenuItem("(brak — dodaj gwiazdką w oknie)") { Enabled = false });
        _menuTraya.Items.Add(ulubione);

        _menuTraya.Items.Add(new ToolStripSeparator());
        _menuTraya.Items.Add("Pokaż okno", null, (_, _) => PrzywrocZTraya()).Font = new Font(_menuTraya.Font, FontStyle.Bold);
        _menuTraya.Items.Add("Zakończ", null, (_, _) => Zakoncz());
    }

    void OdswiezTray()
    {
        bool gra = _odtw?.PowinnoGrac == true;
        _tray.Icon = Ikony.Tray(gra);
        string tekst = "Simple Radio Player";
        if (_odtw?.Biezaca is { } s)
        {
            tekst = gra ? s.Nazwa : $"{s.Nazwa} (zatrzymane)";
            if (_odtw.Tytul.Length > 0) tekst += "\n" + _odtw.Tytul;
        }
        if (_wyciszony) tekst += " — wyciszone";
        _tray.Text = Skroc(tekst, 127);
    }

    void SchowajDoTraya()
    {
        ZapamietajPolozenie();
        Hide();
        if (!_podpowiedzTrayaPokazana)
        {
            _podpowiedzTrayaPokazana = true;
            _adresZDymka = null;
            _tray.ShowBalloonTip(3000, "Simple Radio Player", "Program działa dalej w obszarze powiadomień.", ToolTipIcon.Info);
        }
    }

    void PrzywrocZTraya()
    {
        _moznaPokazac = true;
        ShowInTaskbar = true;
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
        if (_pytanieOListachOdlozone) BeginInvoke(SprawdzTerminAktualizacjiList);
    }

    static string Skroc(string tekst, int max) => tekst.Length <= max ? tekst : tekst[..(max - 1)] + "…";
}
