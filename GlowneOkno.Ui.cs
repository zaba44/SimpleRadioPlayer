using SimpleRadioPlayer.Kontrolki;

namespace SimpleRadioPlayer;

partial class GlowneOkno
{
    Button _btnGraj = null!, _btnUlubiona = null!;
    PictureBox _obrazStacji = null!;
    Button _btnEq = null!, _btnWycisz = null!;
    Label _lblStacja = null!, _lblTytul = null!, _lblGlosnosc = null!;
    TrackBar _suwakGlosnosci = null!;
    ComboBox _cmbUrzadzenie = null!;
    TabControl _zakladki = null!;
    TabPage _tabWszystkie = null!, _tabUlubione = null!;
    PlaceholderTextBox _txtSzukaj = null!;
    ListaStacji _listaWszystkie = null!, _listaUlubione = null!;
    ToolStripStatusLabel _lblStatus = null!, _lblLicznik = null!;
    ToolStripProgressBar _postepStartu = null!;
    ContextMenuStrip _menuListy = null!;
    ComboBox _cmbLista = null!;
    Panel _pustyStan = null!;
    ToolStripMenuItem _miPobierzListe = null!, _miAktualizujListy = null!, _miUsunListe = null!;
    readonly ToolTip _podpowiedzi = new();

    void BudujUi()
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Simple Radio Player";
        Font = new Font("Segoe UI", 9F);
        Icon = Ikony.Okno(true);
        ClientSize = new Size(780, 580);
        MinimumSize = new Size(560, 420);
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        KeyPreview = true;

        var menu = BudujMenu();
        var panel = BudujPanelOdtwarzania();
        BudujZakladki();

        var status = new StatusStrip { SizingGrip = true };
        _lblStatus = new ToolStripStatusLabel("Gotowe") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _lblLicznik = new ToolStripStatusLabel("");
        _postepStartu = new ToolStripProgressBar { Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30, Size = new Size(120, 14), Visible = false };
        status.Items.AddRange([_lblStatus, _postepStartu, _lblLicznik]);

        // Kolejność dodawania: element Fill pierwszy, bo dokowanie idzie od ostatnio dodanego
        Controls.Add(_zakladki);
        Controls.Add(panel);
        Controls.Add(menu);
        Controls.Add(status);
        MainMenuStrip = menu;

        _menuListy = new ContextMenuStrip();
        _menuListy.Opening += (_, e) => e.Cancel = !BudujMenuListy();

        KeyDown += NaKlawisz;
        WlaczPrzeciaganie(this);
        ResumeLayout(false);
        PerformLayout();
    }

    MenuStrip BudujMenu()
    {
        var menu = new MenuStrip();

        var plik = new ToolStripMenuItem("&Plik");
        _miPobierzListe = new ToolStripMenuItem("&Pobierz listę stacji…", null, (_, _) => PobierzListe()) { ShortcutKeys = Keys.Control | Keys.L };
        _miAktualizujListy = new ToolStripMenuItem("&Aktualizuj pobrane listy", null, async (_, _) => await AktualizujPobraneListy());
        _miUsunListe = new ToolStripMenuItem("Usuń wybraną listę…", null, (_, _) => UsunWybranaListe());
        plik.DropDownItems.AddRange([_miPobierzListe, _miAktualizujListy]);
        plik.DropDownItems.Add(new ToolStripMenuItem("&Importuj listę z pliku…", null, (_, _) => ImportujListe()) { ShortcutKeys = Keys.Control | Keys.O });
        plik.DropDownItems.Add("&Eksportuj ulubione…", null, (_, _) => EksportujUlubione());
        plik.DropDownItems.Add(_miUsunListe);
        plik.DropDownItems.Add(new ToolStripSeparator());
        plik.DropDownItems.Add("&Dodaj stację…", null, (_, _) => DodajStacje());
        plik.DropDownItems.Add("&Otwórz folder z listami", null, (_, _) => OtworzFolderList());
        plik.DropDownItems.Add("Wczytaj listy &ponownie", null, (_, _) => WczytajListy());
        plik.DropDownItems.Add(new ToolStripSeparator());
        plik.DropDownItems.Add("&Zakończ", null, (_, _) => Zakoncz());

        var ustawienia = new ToolStripMenuItem("&Ustawienia");
        ustawienia.DropDownItems.Add(new ToolStripMenuItem("&Equalizer…", null, (_, _) => OtworzEqualizer()) { ShortcutKeys = Keys.Control | Keys.E });
        ustawienia.DropDownItems.Add(new ToolStripMenuItem("&Opcje…", null, (_, _) => OtworzOpcje()) { ShortcutKeys = Keys.Control | Keys.Oemcomma, ShortcutKeyDisplayString = "Ctrl+," });

        var pomoc = new ToolStripMenuItem("Po&moc");
        pomoc.DropDownItems.Add("&Licencje i źródła danych", null, (_, _) => OtworzPlikLicencji());
        pomoc.DropDownItems.Add("&Kod źródłowy (GitHub)", null, (_, _) => OtworzAdres(AdresRepozytorium));
        pomoc.DropDownItems.Add(new ToolStripSeparator());
        pomoc.DropDownItems.Add("&O programie", null, (_, _) => PokazOProgramie());

        var wsparcie = new PrzyciskWsparcia();
        wsparcie.Click += (_, _) => OtworzAdres(AdresLinku);

        menu.Items.AddRange([plik, ustawienia, pomoc, wsparcie]);
        return menu;
    }

    const string AdresLinku = "https://cuplink.to/zaba44";
    const string AdresRepozytorium = "https://github.com/zaba44/SimpleRadioPlayer";

    static void OtworzAdres(string adres)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(adres) { UseShellExecute = true }); } catch { }
    }

    Control BudujPanelOdtwarzania()
    {
        var uklad = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(10, 8, 10, 4),
            ColumnCount = 4,
            RowCount = 3,
        };
        uklad.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        uklad.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        uklad.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        uklad.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _obrazStacji = new PictureBox
        {
            Size = new Size(56, 56),
            SizeMode = PictureBoxSizeMode.CenterImage,
            Margin = new Padding(0, 0, 10, 0),
        };

        _btnGraj = new Button
        {
            Size = new Size(56, 56),
            Font = new Font("Segoe UI Symbol", 18F),
            Text = "▶",
            Margin = new Padding(0, 0, 10, 0),
        };
        _btnGraj.Click += (_, _) => PrzelaczGranie();
        _podpowiedzi.SetToolTip(_btnGraj, "Odtwórz / zatrzymaj (Spacja)");

        _lblStacja = new Label
        {
            AutoSize = false, AutoEllipsis = true, Dock = DockStyle.Fill, Height = 30,
            Font = new Font("Segoe UI", 13F, FontStyle.Bold),
            TextAlign = ContentAlignment.BottomLeft,
            UseMnemonic = false,
        };
        _lblTytul = new Label
        {
            AutoSize = false, AutoEllipsis = true, Dock = DockStyle.Fill, Height = 24,
            Font = new Font("Segoe UI", 9.5F),
            ForeColor = SystemColors.GrayText,
            TextAlign = ContentAlignment.TopLeft,
            UseMnemonic = false,
        };

        // Tytuł utworu: dwuklik albo prawy klik kopiuje go do schowka
        var menuTytulu = new ContextMenuStrip();
        var miKopiuj = new ToolStripMenuItem("Kopiuj tytuł utworu", null, (_, _) => KopiujTytul());
        menuTytulu.Items.Add(miKopiuj);
        menuTytulu.Opening += (_, e) => e.Cancel = _odtw is not { Tytul.Length: > 0 };
        _lblTytul.ContextMenuStrip = menuTytulu;
        _lblTytul.DoubleClick += (_, _) => KopiujTytul();

        _btnUlubiona = new Button
        {
            Size = new Size(44, 44),
            Font = new Font("Segoe UI Symbol", 16F),
            Text = "☆",
            Anchor = AnchorStyles.Right,
        };
        _btnUlubiona.Click += (_, _) => { if (_odtw?.Biezaca is { } s) PrzelaczUlubiona(s); };
        _podpowiedzi.SetToolTip(_btnUlubiona, "Dodaj / usuń bieżącą stację z ulubionych");

        uklad.Controls.Add(_btnGraj, 0, 0);
        uklad.SetRowSpan(_btnGraj, 2);
        uklad.Controls.Add(_obrazStacji, 1, 0);
        uklad.SetRowSpan(_obrazStacji, 2);
        uklad.Controls.Add(_lblStacja, 2, 0);
        uklad.Controls.Add(_lblTytul, 2, 1);
        uklad.Controls.Add(_btnUlubiona, 3, 0);
        uklad.SetRowSpan(_btnUlubiona, 2);

        var dolny = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 6, 0, 0) };
        _suwakGlosnosci = new TrackBar
        {
            Minimum = 0, Maximum = 100, TickStyle = TickStyle.None, AutoSize = false,
            Size = new Size(150, 28), SmallChange = 1, LargeChange = 5,
            Value = Math.Clamp(_konf.Glosnosc, 0, 100),
        };
        _suwakGlosnosci.ValueChanged += (_, _) => { if (!_ustawianieSuwaka) ZmienGlosnosc(_suwakGlosnosci.Value); };
        _podpowiedzi.SetToolTip(_suwakGlosnosci, "Głośność: każde 10% to równy krok słyszalnej głośności (3 dB)");
        _btnWycisz = new Button
        {
            Text = "🔊", Font = new Font("Segoe UI Symbol", 11F), Size = new Size(36, 28),
            Anchor = AnchorStyles.Left, Margin = new Padding(0, 1, 2, 1),
        };
        _btnWycisz.Click += (_, _) => PrzelaczWyciszenie();
        _podpowiedzi.SetToolTip(_btnWycisz, "Wycisz (Ctrl+M)");
        _lblGlosnosc = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Text = $"{_suwakGlosnosci.Value}%", MinimumSize = new Size(40, 0) };

        _cmbUrzadzenie = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250, DropDownWidth = 360, Anchor = AnchorStyles.Left };
        _cmbUrzadzenie.DropDown += (_, _) => OdswiezUrzadzenia();
        _cmbUrzadzenie.SelectionChangeCommitted += (_, _) => ZmienUrzadzenie();

        dolny.Controls.Add(_btnWycisz);
        dolny.Controls.Add(_suwakGlosnosci);
        dolny.Controls.Add(_lblGlosnosc);
        dolny.Controls.Add(new Label { Text = "Wyjście dźwięku:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(16, 3, 3, 3) });
        dolny.Controls.Add(_cmbUrzadzenie);
        _btnEq = new Button { Text = "Equalizer", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(12, 2, 3, 3) };
        _btnEq.Click += (_, _) => OtworzEqualizer();
        _podpowiedzi.SetToolTip(_btnEq, "Equalizer (Ctrl+E)");
        dolny.Controls.Add(_btnEq);

        uklad.Controls.Add(dolny, 0, 2);
        uklad.SetColumnSpan(dolny, 4);
        return uklad;
    }

    /// <summary>Ekran bez list — program niczego nie pobiera sam, tylko podsuwa trzy proste drogi.</summary>
    Panel BudujPustyStan()
    {
        _pustyStan = new Panel { Dock = DockStyle.Fill, BackColor = SystemColors.Window, Visible = false };
        var srodek = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Padding = new Padding(30, 40, 30, 20) };
        srodek.Controls.Add(new Label { Text = "Nie masz jeszcze żadnej listy stacji", AutoSize = true, Font = new Font("Segoe UI", 12F, FontStyle.Bold), Margin = new Padding(3, 0, 3, 8) });
        srodek.Controls.Add(new Label
        {
            Text = "Pobierz gotową listę z otwartej bazy Radio Browser, zaimportuj własną (M3U, M3U8, PLS, XSPF, ASX, TXT) " +
                   "albo dodaj stację ręcznie. Plik listy możesz też po prostu przeciągnąć na to okno.",
            AutoSize = true, MaximumSize = new Size(520, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 0, 3, 14),
        });
        var przyciski = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(560, 0) };
        var btnPobierz = new Button { Text = "Pobierz listę stacji…", AutoSize = true, Padding = new Padding(8, 4, 8, 4), Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
        btnPobierz.Click += (_, _) => PobierzListe();
        var btnImport = new Button { Text = "Importuj listę z pliku…", AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
        btnImport.Click += (_, _) => ImportujListe();
        var btnDodaj = new Button { Text = "Dodaj stację ręcznie…", AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
        btnDodaj.Click += (_, _) => DodajStacje();
        przyciski.Controls.AddRange([btnPobierz, btnImport, btnDodaj]);
        srodek.Controls.Add(przyciski);
        _pustyStan.Controls.Add(srodek);
        return _pustyStan;
    }

    void BudujZakladki()
    {
        _zakladki = new TabControl { Dock = DockStyle.Fill, Padding = new Point(12, 4) };

        _listaWszystkie = new ListaStacji("Lista", s => s.Zrodlo) { Dock = DockStyle.Fill };
        // ImageList nie skaluje się z DPI sam — rozmiar w pikselach urządzenia
        _listaUlubione = new ListaStacji("Adres", s => s.Url, ObrazStacji, LogicalToDeviceUnits(28)) { Dock = DockStyle.Fill };
        foreach (var lista in new[] { _listaWszystkie, _listaUlubione })
        {
            lista.CzyUlubiona = s => _kluczeUlubionych.Contains(s.Klucz);
            lista.CzyGra = s => _odtw?.Biezaca?.Klucz == s.Klucz;
            lista.ContextMenuStrip = null;
            lista.MouseUp += NaMyszNaLiscie;
            lista.ItemActivate += (_, _) => { if (lista.Zaznaczona is { } s) Graj(s); };
            lista.KeyDown += NaKlawiszListy;
        }

        _txtSzukaj = new PlaceholderTextBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(8, 4, 3, 3),
            Podpowiedz = "Szukaj stacji (Ctrl+F) — nazwa, format lub adres",
        };
        _txtSzukaj.TextChanged += (_, _) => Filtruj();
        _txtSzukaj.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Down && _listaWszystkie.Stacje.Count > 0)
            {
                _listaWszystkie.Focus();
                if (_listaWszystkie.SelectedIndices.Count == 0) _listaWszystkie.SelectedIndices.Add(0);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                var s = _listaWszystkie.Zaznaczona ?? _listaWszystkie.Stacje.FirstOrDefault();
                if (s != null) Graj(s);
                e.Handled = e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                _txtSzukaj.Clear();
                e.Handled = e.SuppressKeyPress = true;
            }
        };
        // Pasek nad stacjami: przełącznik list, wyszukiwarka, pobieranie
        _cmbLista = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250, DropDownWidth = 360, MaxDropDownItems = 20, Anchor = AnchorStyles.Left };
        _cmbLista.SelectedIndexChanged += (_, _) => NaZmianeListy();
        var btnPobierz = new Button { Text = "Pobierz listę…", AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(6, 2, 3, 3) };
        btnPobierz.Click += (_, _) => PobierzListe();
        _podpowiedzi.SetToolTip(btnPobierz, "Pobierz albo zaktualizuj listę stacji z bazy Radio Browser (Ctrl+L)");
        var pasekList = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Padding = new Padding(4, 4, 4, 2) };
        pasekList.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pasekList.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pasekList.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pasekList.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pasekList.Controls.Add(new Label { Text = "Lista:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        pasekList.Controls.Add(_cmbLista, 1, 0);
        pasekList.Controls.Add(_txtSzukaj, 2, 0);
        pasekList.Controls.Add(btnPobierz, 3, 0);

        _tabWszystkie = new TabPage("Stacje");
        _tabWszystkie.Controls.Add(_listaWszystkie);
        _tabWszystkie.Controls.Add(BudujPustyStan());
        _tabWszystkie.Controls.Add(pasekList);

        _tabUlubione = new TabPage("Ulubione");
        _tabUlubione.Controls.Add(_listaUlubione);

        _zakladki.TabPages.Add(_tabUlubione);
        _zakladki.TabPages.Add(_tabWszystkie);
        if (_konf.Zakladka is 0 or 1) _zakladki.SelectedIndex = _konf.Zakladka;
        _zakladki.SelectedIndexChanged += (_, _) => OdswiezLicznik();
    }
}
