using System.Globalization;

namespace SimpleRadioPlayer.Okna;

/// <summary>
/// Wybór listy do pobrania z Radio Browser. Dwie najczęstsze listy są pod jednym przyciskiem,
/// reszta to kraj × kategoria × zakres. Samo pobieranie robi okno główne (postęp w pasku stanu).
/// </summary>
sealed class PobierzListeOkno : Form
{
    sealed record PozycjaKraju(string Kod, string Nazwa, int Liczba)
    {
        public override string ToString() => Liczba > 0 ? $"{Nazwa} ({Liczba})" : Nazwa;
    }

    readonly ComboBox _cmbKraj = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280, MaxDropDownItems = 20 };
    readonly ComboBox _cmbKategoria = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    readonly RadioButton _rbPopularne = new() { Text = $"Najpopularniejsze ({ListyRadioBrowser.LimitPopularnych} stacji)", AutoSize = true, Checked = true };
    readonly RadioButton _rbWszystkie = new() { Text = "Wszystkie działające", AutoSize = true };
    readonly Label _lblUwaga = new() { AutoSize = true, ForeColor = Color.FromArgb(170, 90, 0), MaximumSize = new Size(470, 0) };
    readonly Label _lblNazwa = new() { AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(470, 0) };
    readonly CancellationTokenSource _zamkniecie = new();

    public PobranaLista? Wynik { get; private set; }

    public PobierzListeOkno()
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Pobierz listę stacji";
        Font = new Font("Segoe UI", 9F);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var uklad = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Padding = new Padding(14), Dock = DockStyle.Fill };
        uklad.Controls.Add(new Label
        {
            Text = "Stacje z otwartej bazy Radio Browser (radio-browser.info) — tylko te, które przeszły ostatni test działania. " +
                   "Pobrana lista pojawi się w przełączniku nad stacjami; ponowne pobranie tej samej listy ją aktualizuje.",
            AutoSize = true, MaximumSize = new Size(480, 0), Margin = new Padding(3, 0, 3, 10),
        });

        // Najczęstszy wybór — jeden klik
        var szybkie = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 10) };
        szybkie.Controls.Add(PrzyciskSzybki("Polska — popularne", $"{ListyRadioBrowser.LimitPopularnych} najczęściej słuchanych", new PobranaLista { Kraj = "PL", Popularne = true }));
        szybkie.Controls.Add(PrzyciskSzybki("Polska — wszystkie", "wszystkie działające (ok. 1000)", new PobranaLista { Kraj = "PL", Popularne = false }));
        uklad.Controls.Add(szybkie);

        // Dowolna lista
        _cmbKraj.Items.Add(new PozycjaKraju("", ListyRadioBrowser.NazwaKraju(""), 0));
        _cmbKraj.Items.Add(new PozycjaKraju("PL", ListyRadioBrowser.NazwaKraju("PL"), 0));
        _cmbKraj.SelectedIndex = 1;
        _cmbKategoria.Items.AddRange(ListyRadioBrowser.Kategorie);
        _cmbKategoria.SelectedIndex = 0;
        foreach (Control c in new Control[] { _cmbKraj, _cmbKategoria, _rbPopularne, _rbWszystkie })
        {
            if (c is ComboBox cmb) cmb.SelectedIndexChanged += (_, _) => OdswiezOpis();
            if (c is RadioButton rb) rb.CheckedChanged += (_, _) => OdswiezOpis();
        }

        var siatka = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
        siatka.Controls.Add(new Label { Text = "Kraj:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        siatka.Controls.Add(_cmbKraj, 1, 0);
        siatka.Controls.Add(new Label { Text = "Kategoria:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        siatka.Controls.Add(_cmbKategoria, 1, 1);
        var zakres = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        zakres.Controls.AddRange([_rbPopularne, _rbWszystkie]);
        siatka.Controls.Add(new Label { Text = "Zakres:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        siatka.Controls.Add(zakres, 1, 2);

        var btnPobierz = new Button { Text = "Pobierz tę listę", AutoSize = true, Margin = new Padding(3, 8, 3, 3) };
        btnPobierz.Click += (_, _) => Zatwierdz(new PobranaLista
        {
            Kraj = (_cmbKraj.SelectedItem as PozycjaKraju)?.Kod ?? "",
            Kategoria = (_cmbKategoria.SelectedItem as Kategoria)?.Id ?? "",
            Popularne = _rbPopularne.Checked,
        });

        var wnetrze = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(4) };
        wnetrze.Controls.AddRange([siatka, _lblNazwa, _lblUwaga, btnPobierz]);
        var grupa = new GroupBox { Text = "Inna lista", AutoSize = true, MinimumSize = new Size(490, 0), Padding = new Padding(8, 6, 8, 6) };
        grupa.Controls.Add(wnetrze);
        uklad.Controls.Add(grupa);

        var dol = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, MinimumSize = new Size(490, 0), Margin = new Padding(0, 10, 0, 0) };
        var btnAnuluj = new Button { Text = "Anuluj", DialogResult = DialogResult.Cancel, AutoSize = true };
        dol.Controls.Add(btnAnuluj);
        uklad.Controls.Add(dol);

        Controls.Add(uklad);
        CancelButton = btnAnuluj;
        ResumeLayout(false);
        PerformLayout();
        OdswiezOpis();
        Shown += async (_, _) => await WczytajKraje();
        FormClosed += (_, _) => _zamkniecie.Cancel();
    }

    Button PrzyciskSzybki(string tytul, string opis, PobranaLista definicja)
    {
        var b = new Button
        {
            Text = tytul + "\n" + opis,
            Size = new Size(236, 56),
            Margin = new Padding(3, 3, 8, 3),
            TextAlign = ContentAlignment.MiddleCenter,
        };
        b.Click += (_, _) => Zatwierdz(definicja);
        return b;
    }

    void Zatwierdz(PobranaLista definicja)
    {
        Wynik = definicja;
        DialogResult = DialogResult.OK;
    }

    void OdswiezOpis()
    {
        var d = new PobranaLista
        {
            Kraj = (_cmbKraj.SelectedItem as PozycjaKraju)?.Kod ?? "",
            Kategoria = (_cmbKategoria.SelectedItem as Kategoria)?.Id ?? "",
            Popularne = _rbPopularne.Checked,
        };
        _lblNazwa.Text = "Lista zostanie zapisana jako: " + ListyRadioBrowser.NazwaListy(d);
        _lblUwaga.Text = d.Kraj.Length == 0 && d.Kategoria.Length == 0 && !d.Popularne
            ? "Cała baza to ok. 55 000 stacji i ok. 25 MB do pobrania — potrwa to dłuższą chwilę, a lista będzie bardzo długa."
            : "";
        _lblUwaga.Visible = _lblUwaga.Text.Length > 0;
    }

    /// <summary>Pełna lista krajów z bazy; do czasu odpowiedzi (albo bez sieci) zostają „Cały świat” i „Polska”.</summary>
    async Task WczytajKraje()
    {
        try
        {
            var kraje = await RadioBrowser.KrajeAsync(_zamkniecie.Token);
            if (IsDisposed) return;
            var pl = StringComparer.Create(new CultureInfo("pl-PL"), true);
            var pozycje = kraje.Where(k => k.Liczba > 0)
                .Select(k => new PozycjaKraju(k.Kod, ListyRadioBrowser.NazwaKraju(k.Kod, k.Nazwa), k.Liczba))
                .OrderBy(k => k.Kod == "PL" ? 0 : 1).ThenBy(k => k.Nazwa, pl).ToList();
            string wybrany = (_cmbKraj.SelectedItem as PozycjaKraju)?.Kod ?? "PL";
            _cmbKraj.BeginUpdate();
            _cmbKraj.Items.Clear();
            _cmbKraj.Items.Add(new PozycjaKraju("", ListyRadioBrowser.NazwaKraju(""), kraje.Sum(k => k.Liczba)));
            foreach (var p in pozycje) _cmbKraj.Items.Add(p);
            _cmbKraj.SelectedItem = _cmbKraj.Items.Cast<PozycjaKraju>().FirstOrDefault(p => p.Kod == wybrany) ?? _cmbKraj.Items[0];
            _cmbKraj.EndUpdate();
        }
        catch { }
    }
}
