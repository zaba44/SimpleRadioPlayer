namespace SimpleRadioPlayer.Okna;

sealed class OpcjeOkno : Form
{
    public sealed record Urzadzenie(string Id, string Opis)
    {
        public override string ToString() => Opis;
    }

    sealed record PozycjaStacji(Stacja Stacja, string Opis)
    {
        public override string ToString() => Opis;
    }

    readonly ComboBox _cmbUrzadzenie = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    readonly CheckBox _chkAutostart = new() { Text = "Uruchamiaj razem z systemem Windows (program startuje w trayu)", AutoSize = true };
    readonly RadioButton _rbNic = new() { Text = "Nic nie odtwarzaj", AutoSize = true };
    readonly RadioButton _rbOstatnia = new() { Text = "Odtwarzaj ostatnio słuchane radio", AutoSize = true };
    readonly RadioButton _rbStartowa = new() { Text = "Odtwarzaj stację startową:", AutoSize = true };
    readonly ComboBox _cmbStartowa = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, DropDownWidth = 360 };
    readonly CheckBox _chkTray = new() { Text = "Zamknięcie okna chowa program do traya (wyjście: Plik → Zakończ)", AutoSize = true };
    readonly RadioButton _rbPrzenosna = new() { AutoSize = true };
    readonly RadioButton _rbProfil = new() { AutoSize = true };

    readonly CheckBox _chkGlosnoscStacji = new() { Text = "Zapamiętuj głośność osobno dla każdej stacji", AutoSize = true, Margin = new Padding(3, 8, 3, 3) };
    readonly ComboBox _cmbWyrownywanie = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, Anchor = AnchorStyles.Left };

    readonly ComboBox _cmbAktualizacjaList = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120, Anchor = AnchorStyles.Left };
    readonly CheckBox _chkZglaszaj = new() { Text = "Pomagaj w rankingu popularności Radio Browser", AutoSize = true, Margin = new Padding(3, 8, 3, 0) };
    static readonly (int Dni, string Opis)[] OkresyList = [(0, "nigdy"), (7, "co 7 dni"), (14, "co 14 dni"), (30, "co 30 dni"), (90, "co 90 dni")];

    public int AktualizacjaListDni => OkresyList[Math.Max(0, _cmbAktualizacjaList.SelectedIndex)].Dni;
    public bool ZglaszajOdtworzenia => _chkZglaszaj.Checked;

    public bool GlosnoscPerStacja => _chkGlosnoscStacji.Checked;
    public int Wyrownywanie => Math.Max(0, _cmbWyrownywanie.SelectedIndex);

    public string UrzadzenieId => (_cmbUrzadzenie.SelectedItem as Urzadzenie)?.Id ?? "";
    public string UrzadzenieOpis => (_cmbUrzadzenie.SelectedItem as Urzadzenie)?.Opis ?? "";
    public bool Autostart => _chkAutostart.Checked;
    public TrybStartu TrybStartu => _rbNic.Checked ? TrybStartu.Nic : _rbStartowa.Checked ? TrybStartu.Startowa : TrybStartu.Ostatnia;
    public Stacja? StacjaStartowa => (_cmbStartowa.SelectedItem as PozycjaStacji)?.Stacja;
    public bool ZamykajDoTraya => _chkTray.Checked;
    public bool Przenosna => _rbPrzenosna.Checked;

    readonly RadioButton _rbLogoWszystkie = new() { Text = "Pokazuj wszystkie znalezione logotypy, brakujące zastąp kafelkiem", AutoSize = true };
    readonly RadioButton _rbLogoDobre = new() { Text = $"Pokazuj tylko logotypy dobrej jakości (od {Logotypy.MinDobrejJakosci} px), resztę zastąp kafelkiem", AutoSize = true };
    readonly RadioButton _rbLogoKafelki = new() { Text = "Tylko kafelki z literą — bez pobierania z internetu", AutoSize = true };
    readonly ComboBox _cmbOdswiezanie = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    static readonly (int Dni, string Opis)[] Okresy = [(0, "ręcznie"), (7, "co 7 dni"), (30, "co 30 dni"), (90, "co 90 dni")];

    public TrybLogo TrybLogo => _rbLogoWszystkie.Checked ? TrybLogo.Wszystkie : _rbLogoKafelki.Checked ? TrybLogo.TylkoKafelki : TrybLogo.DobrejJakosci;
    public int OdswiezanieLogoDni => Okresy[Math.Max(0, _cmbOdswiezanie.SelectedIndex)].Dni;

    public OpcjeOkno(Konfiguracja konf, List<Urzadzenie> urzadzenia, Stacja? biezaca, bool autostart, Action<TrybLogo> aktualizujLogo)
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Opcje";
        Font = new Font("Segoe UI", 9F);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        // Dwie kolumny — w jednej okno byłoby wyższe niż ekran typowego laptopa
        var uklad = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Padding = new Padding(12), Dock = DockStyle.Fill };
        var lewa = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0, 0, 12, 0) };
        var prawa = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = Padding.Empty };

        // Dźwięk
        foreach (var u in urzadzenia) _cmbUrzadzenie.Items.Add(u);
        _cmbUrzadzenie.SelectedItem = urzadzenia.FirstOrDefault(u => u.Id == konf.UrzadzenieId) ?? urzadzenia.FirstOrDefault();
        _chkGlosnoscStacji.Checked = konf.GlosnoscPerStacja;
        _cmbWyrownywanie.Items.AddRange(["wyłączone", "łagodne (zalecane)", "mocne"]);
        _cmbWyrownywanie.SelectedIndex = Math.Clamp(konf.Wyrownywanie, 0, 2);
        var wierszWyrownywania = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        wierszWyrownywania.Controls.Add(new Label { Text = "Wyrównywanie głośności między stacjami:", AutoSize = true, Anchor = AnchorStyles.Left });
        wierszWyrownywania.Controls.Add(_cmbWyrownywanie);
        var grDzwiek = Grupa("Dźwięk",
            new Label { Text = "Wyjście dźwięku dla radia — zapamiętywane i używane przy każdym starcie:", AutoSize = true },
            _cmbUrzadzenie,
            _chkGlosnoscStacji,
            wierszWyrownywania,
            new Label
            {
                Text = "Wyrównywanie podgłaśnia ciche stacje i lekko ścisza najgłośniejsze. Zmiana przerywa odtwarzanie na około sekundę.",
                AutoSize = true, MaximumSize = new Size(470, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 2, 3, 0),
            });
        _cmbUrzadzenie.Width = 460;
        _cmbUrzadzenie.Dock = DockStyle.None;

        // Uruchamianie
        _chkAutostart.Checked = autostart;
        var stacje = new List<PozycjaStacji>();
        void Dodaj(Stacja? s, string dopisek)
        {
            if (s != null && !stacje.Any(p => p.Stacja.Klucz == s.Klucz)) stacje.Add(new PozycjaStacji(s, s.Nazwa + dopisek));
        }
        Dodaj(konf.StacjaStartowa, "");
        Dodaj(biezaca, "  (bieżąca)");
        foreach (var u in konf.Ulubione) Dodaj(u, "");
        Dodaj(konf.OstatniaStacja, "  (ostatnia)");
        foreach (var p in stacje) _cmbStartowa.Items.Add(p);
        if (_cmbStartowa.Items.Count > 0) _cmbStartowa.SelectedIndex = 0;
        _cmbStartowa.SelectedIndexChanged += (_, _) => _rbStartowa.Checked = true;
        _rbStartowa.Enabled = _cmbStartowa.Enabled = stacje.Count > 0;

        (konf.TrybStartu switch
        {
            TrybStartu.Nic => _rbNic,
            TrybStartu.Startowa when stacje.Count > 0 => _rbStartowa,
            _ => _rbOstatnia,
        }).Checked = true;

        var wierszStartowej = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        wierszStartowej.Controls.Add(_rbStartowa);
        wierszStartowej.Controls.Add(_cmbStartowa);
        var panelStartu = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(16, 0, 0, 0) };
        panelStartu.Controls.AddRange([_rbNic, _rbOstatnia, wierszStartowej]);
        var grStart = Grupa("Uruchamianie",
            _chkAutostart,
            new Label { Text = "Po uruchomieniu programu:", AutoSize = true, Margin = new Padding(3, 8, 3, 0) },
            panelStartu,
            _chkTray);
        _chkTray.Checked = konf.ZamykajDoTraya;

        // Logotypy
        (konf.TrybLogo switch
        {
            TrybLogo.Wszystkie => _rbLogoWszystkie,
            TrybLogo.TylkoKafelki => _rbLogoKafelki,
            _ => _rbLogoDobre,
        }).Checked = true;
        foreach (var o in Okresy) _cmbOdswiezanie.Items.Add(o.Opis);
        int iOkresu = Array.FindIndex(Okresy, o => o.Dni == konf.OdswiezanieLogoDni);
        _cmbOdswiezanie.SelectedIndex = iOkresu >= 0 ? iOkresu : 2;
        var btnAktualizuj = new Button { Text = "Aktualizuj logotypy teraz", AutoSize = true, Margin = new Padding(16, 2, 3, 3) };
        btnAktualizuj.Click += (_, _) =>
        {
            aktualizujLogo(TrybLogo);
            btnAktualizuj.Enabled = false;
            btnAktualizuj.Text = "Aktualizacja uruchomiona";
        };
        void UstawDostepnosc()
        {
            bool zSieci = !_rbLogoKafelki.Checked;
            _cmbOdswiezanie.Enabled = zSieci;
            if (btnAktualizuj.Text != "Aktualizacja uruchomiona") btnAktualizuj.Enabled = zSieci;
        }
        _rbLogoKafelki.CheckedChanged += (_, _) => UstawDostepnosc();
        UstawDostepnosc();

        var wierszOdswiezania = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
        wierszOdswiezania.Controls.Add(new Label { Text = "Sprawdzaj logotypy ponownie:", AutoSize = true, Anchor = AnchorStyles.Left });
        wierszOdswiezania.Controls.Add(_cmbOdswiezanie);
        wierszOdswiezania.Controls.Add(btnAktualizuj);
        var grLogo = Grupa("Logotypy stacji", _rbLogoWszystkie, _rbLogoDobre, _rbLogoKafelki, wierszOdswiezania,
            new Label
            {
                Text = "Źródło: baza Radio Browser i strony WWW stacji. Pobrane logotypy leżą w folderze logo obok pliku ustawień.",
                AutoSize = true, MaximumSize = new Size(470, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 6, 3, 0),
            });

        // Miejsce ustawień
        bool zapisywalny = Konfiguracja.FolderZapisywalny(Konfiguracja.FolderProgramu);
        _rbPrzenosna.Text = "W folderze programu — wersja przenośna" + (zapisywalny ? "" : " (folder tylko do odczytu)");
        _rbProfil.Text = "W profilu użytkownika Windows (%AppData%)";
        _rbPrzenosna.Enabled = zapisywalny || konf.Przenosna;
        (konf.Przenosna ? _rbPrzenosna : _rbProfil).Checked = true;
        var sciezka = new LinkLabel { Text = konf.Sciezka, AutoSize = true, MaximumSize = new Size(470, 0), Margin = new Padding(3, 6, 3, 0) };
        sciezka.LinkClicked += (_, _) => OtworzFolder(Path.GetDirectoryName(konf.Sciezka)!);
        var grMiejsce = Grupa("Gdzie zapisywać ustawienia", _rbPrzenosna, _rbProfil,
            new Label { Text = "Obecny plik:", AutoSize = true, Margin = new Padding(3, 6, 3, 0), ForeColor = SystemColors.GrayText }, sciezka);

        var przyciski = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 0) };
        var btnAnuluj = new Button { Text = "Anuluj", DialogResult = DialogResult.Cancel, AutoSize = true };
        var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(80, 0) };
        przyciski.Controls.Add(btnAnuluj);
        przyciski.Controls.Add(btnOk);

        // Listy stacji
        foreach (var o in OkresyList) _cmbAktualizacjaList.Items.Add(o.Opis);
        int iList = Array.FindIndex(OkresyList, o => o.Dni == konf.AktualizacjaListDni);
        _cmbAktualizacjaList.SelectedIndex = iList >= 0 ? iList : 3;
        _chkZglaszaj.Checked = konf.ZglaszajOdtworzenia;
        var wierszList = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        wierszList.Controls.Add(new Label { Text = "Pytaj o aktualizację pobranych list:", AutoSize = true, Anchor = AnchorStyles.Left });
        wierszList.Controls.Add(_cmbAktualizacjaList);
        var grListy = Grupa("Listy stacji", wierszList,
            new Label
            {
                Text = "Program nigdy nie aktualizuje list sam — po tym czasie tylko zapyta. Odpowiedź „Nie” odkłada pytanie o kolejny okres.",
                AutoSize = true, MaximumSize = new Size(470, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 2, 3, 0),
            },
            _chkZglaszaj,
            new Label
            {
                Text = "Przy starcie stacji z pobranej listy wysyła do Radio Browser informację o jej odtworzeniu (zasila ranking „popularne”). " +
                       "Serwery bazy widzą wtedy Twój adres IP i słuchaną stację. Na działanie programu nie ma to wpływu.",
                AutoSize = true, MaximumSize = new Size(470, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(21, 0, 3, 0),
            });

        lewa.Controls.AddRange([grDzwiek, grStart]);
        prawa.Controls.AddRange([grLogo, grListy, grMiejsce]);
        uklad.Controls.Add(lewa, 0, 0);
        uklad.Controls.Add(prawa, 1, 0);
        uklad.Controls.Add(przyciski, 0, 1);
        uklad.SetColumnSpan(przyciski, 2);
        Controls.Add(uklad);
        AcceptButton = btnOk;
        CancelButton = btnAnuluj;
        ResumeLayout(false);
        PerformLayout();
    }

    static GroupBox Grupa(string tytul, params Control[] kontrolki)
    {
        var wnetrze = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
            Dock = DockStyle.Fill, Padding = new Padding(4, 2, 4, 4),
        };
        wnetrze.Controls.AddRange(kontrolki);
        var gr = new GroupBox { Text = tytul, AutoSize = true, MinimumSize = new Size(500, 0), Margin = new Padding(0, 0, 0, 10), Padding = new Padding(8, 6, 8, 6) };
        gr.Controls.Add(wnetrze);
        return gr;
    }

    static void OtworzFolder(string folder)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true }); } catch { }
    }
}
