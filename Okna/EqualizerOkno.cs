namespace SimpleRadioPlayer.Okna;

/// <summary>
/// Okno niemodalne — zmiany słychać od razu, a radio można przełączać przy otwartym oknie.
/// Suwaki pracują w dziesiątych częściach dB (TrackBar ma tylko liczby całkowite).
/// </summary>
sealed class EqualizerOkno : Form
{
    static readonly Dictionary<string, string> PolskieNazwy = new()
    {
        ["Flat"] = "Płaski", ["Classical"] = "Muzyka klasyczna", ["Club"] = "Klub", ["Dance"] = "Taneczny",
        ["Full bass"] = "Mocny bas", ["Full bass and treble"] = "Bas i soprany", ["Full treble"] = "Mocne soprany",
        ["Headphones"] = "Słuchawki", ["Large Hall"] = "Duża sala", ["Live"] = "Koncert", ["Party"] = "Impreza",
        ["Pop"] = "Pop", ["Reggae"] = "Reggae", ["Rock"] = "Rock", ["Ska"] = "Ska", ["Soft"] = "Łagodny",
        ["Soft rock"] = "Soft rock", ["Techno"] = "Techno",
    };
    const string Wlasne = "Własne ustawienia";

    readonly UstawieniaEq _ust;
    readonly Action<UstawieniaEq> _zmiana;
    readonly List<(string Nazwa, float Preamp, float[] Pasma)> _presety;
    readonly CheckBox _chkWlacz = new() { Text = "Włącz equalizer", AutoSize = true, Anchor = AnchorStyles.Left };
    readonly ComboBox _cmbPreset = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190, Anchor = AnchorStyles.Left };
    readonly TrackBar _suwakPreamp;
    readonly Label _lblPreamp;
    readonly TrackBar[] _suwaki;
    readonly Label[] _wartosci;
    bool _ustawianie;   // zmiany z kodu nie przełączają presetu na „Własne”

    public EqualizerOkno(UstawieniaEq ustawienia, float[] czestotliwosci, List<(string, float, float[])> presety, Action<UstawieniaEq> zmiana)
    {
        _ust = ustawienia.Kopia();
        if (_ust.Pasma.Length != czestotliwosci.Length) _ust.Pasma = new float[czestotliwosci.Length];
        _zmiana = zmiana;
        _presety = presety;

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Equalizer";
        Font = new Font("Segoe UI", 9F);
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };

        // Górny wiersz: włącznik, preset, zerowanie
        foreach (var p in presety) _cmbPreset.Items.Add(PolskieNazwy.GetValueOrDefault(p.Item1, p.Item1));
        _cmbPreset.Items.Add(Wlasne);
        var btnZeruj = new Button { Text = "Wyzeruj", AutoSize = true, Anchor = AnchorStyles.Left };
        btnZeruj.Click += (_, _) => WybierzPreset(presety.FindIndex(p => p.Item1 == "Flat"));
        _chkWlacz.CheckedChanged += (_, _) => { if (!_ustawianie) { _ust.Wlaczony = _chkWlacz.Checked; Zglos(); } };
        _cmbPreset.SelectionChangeCommitted += (_, _) =>
        {
            if (_cmbPreset.SelectedIndex < presety.Count) WybierzPreset(_cmbPreset.SelectedIndex);
        };
        var gora = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) };
        gora.Controls.Add(_chkWlacz);
        gora.Controls.Add(new Label { Text = "Preset:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(18, 3, 3, 3) });
        gora.Controls.Add(_cmbPreset);
        gora.Controls.Add(btnZeruj);

        // Suwaki: wzmocnienie wstępne, odstęp, 10 pasm
        int n = czestotliwosci.Length;
        var siatka = new TableLayoutPanel { AutoSize = true, ColumnCount = n + 2, RowCount = 3, Dock = DockStyle.Fill };
        (_suwakPreamp, _lblPreamp) = DodajKolumne(siatka, 0, "Wstępne");
        siatka.Controls.Add(new Panel { Width = 14, Height = 1, Margin = Padding.Empty }, 1, 1);
        _suwaki = new TrackBar[n];
        _wartosci = new Label[n];
        for (int i = 0; i < n; i++)
            (_suwaki[i], _wartosci[i]) = DodajKolumne(siatka, i + 2, OpisCzestotliwosci(czestotliwosci[i]));

        _suwakPreamp.ValueChanged += (_, _) =>
        {
            _lblPreamp.Text = Opis(_suwakPreamp.Value);
            if (_ustawianie) return;
            _ust.Preamp = _suwakPreamp.Value / 10f;
            NaRecznaZmiane();
        };
        for (int i = 0; i < n; i++)
        {
            int pasmo = i;
            _suwaki[i].ValueChanged += (_, _) =>
            {
                _wartosci[pasmo].Text = Opis(_suwaki[pasmo].Value);
                if (_ustawianie) return;
                _ust.Pasma[pasmo] = _suwaki[pasmo].Value / 10f;
                NaRecznaZmiane();
            };
        }

        var dol = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
        var btnZamknij = new Button { Text = "Zamknij", AutoSize = true, MinimumSize = new Size(80, 0) };
        btnZamknij.Click += (_, _) => Close();
        dol.Controls.Add(btnZamknij);
        dol.Controls.Add(new Label
        {
            Text = "Zmiany słychać od razu i są zapamiętywane.",
            AutoSize = true, ForeColor = SystemColors.GrayText, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 20, 3),
        });

        var uklad = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(12), Dock = DockStyle.Fill };
        uklad.Controls.Add(gora);
        uklad.Controls.Add(siatka);
        uklad.Controls.Add(dol);
        Controls.Add(uklad);
        CancelButton = btnZamknij;
        ResumeLayout(false);
        PerformLayout();

        PokazUstawienia();
    }

    static (TrackBar, Label) DodajKolumne(TableLayoutPanel siatka, int kolumna, string podpis)
    {
        var wartosc = new Label { AutoSize = false, Width = 46, Height = 18, TextAlign = ContentAlignment.MiddleCenter, Anchor = AnchorStyles.None };
        var suwak = new TrackBar
        {
            Orientation = Orientation.Vertical, Minimum = -200, Maximum = 200,
            TickFrequency = 50, SmallChange = 5, LargeChange = 20, TickStyle = TickStyle.Both,
            AutoSize = false, Size = new Size(46, 170), Anchor = AnchorStyles.None,
        };
        var opis = new Label { Text = podpis, AutoSize = false, Width = 60, Height = 18, TextAlign = ContentAlignment.MiddleCenter, Anchor = AnchorStyles.None, ForeColor = SystemColors.GrayText };
        siatka.Controls.Add(wartosc, kolumna, 0);
        siatka.Controls.Add(suwak, kolumna, 1);
        siatka.Controls.Add(opis, kolumna, 2);
        return (suwak, wartosc);
    }

    static string Opis(int dziesiate) => dziesiate == 0 ? "0" : (dziesiate / 10f).ToString("+0.#;-0.#");

    static string OpisCzestotliwosci(float hz) =>
        hz >= 1000 ? $"{hz / 1000:0.#} kHz" : $"{hz:0} Hz";

    void WybierzPreset(int indeks)
    {
        if (indeks < 0 || indeks >= _presety.Count) return;
        var p = _presety[indeks];
        _ust.Preset = p.Nazwa;
        _ust.Preamp = p.Preamp;
        for (int i = 0; i < _ust.Pasma.Length && i < p.Pasma.Length; i++) _ust.Pasma[i] = p.Pasma[i];
        _ust.Wlaczony = true;       // wybór presetu przy wyłączonym equalizerze byłby niesłyszalny
        PokazUstawienia();
        Zglos();
    }

    void NaRecznaZmiane()
    {
        _ust.Preset = "";
        _ustawianie = true;
        _cmbPreset.SelectedIndex = _cmbPreset.Items.Count - 1;
        if (!_ust.Wlaczony)
        {
            _ust.Wlaczony = true;
            _chkWlacz.Checked = true;
        }
        _ustawianie = false;
        Zglos();
    }

    void PokazUstawienia()
    {
        _ustawianie = true;
        _chkWlacz.Checked = _ust.Wlaczony;
        int iPresetu = _presety.FindIndex(p => p.Nazwa == _ust.Preset);
        _cmbPreset.SelectedIndex = iPresetu >= 0 ? iPresetu : _cmbPreset.Items.Count - 1;
        _suwakPreamp.Value = Suwak(_ust.Preamp);
        _lblPreamp.Text = Opis(_suwakPreamp.Value);
        for (int i = 0; i < _suwaki.Length; i++)
        {
            _suwaki[i].Value = Suwak(_ust.Pasma[i]);
            _wartosci[i].Text = Opis(_suwaki[i].Value);
        }
        _ustawianie = false;
    }

    static int Suwak(float db) => Math.Clamp((int)Math.Round(db * 10), -200, 200);

    void Zglos() => _zmiana(_ust.Kopia());
}
