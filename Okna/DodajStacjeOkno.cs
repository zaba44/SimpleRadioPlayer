namespace SimpleRadioPlayer.Okna;

sealed class DodajStacjeOkno : Form
{
    readonly TextBox _txtNazwa = new() { Dock = DockStyle.Fill };
    readonly TextBox _txtAdres = new() { Dock = DockStyle.Fill };
    readonly CheckBox _chkUlubione = new() { Text = "Dodaj też do ulubionych", Checked = true, AutoSize = true };

    public Stacja? Wynik { get; private set; }
    public bool DoUlubionych => _chkUlubione.Checked;

    public DodajStacjeOkno()
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Dodaj stację";
        Font = new Font("Segoe UI", 9F);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(480, 160);

        var uklad = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 4 };
        uklad.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        uklad.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        uklad.Controls.Add(new Label { Text = "Nazwa:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        uklad.Controls.Add(_txtNazwa, 1, 0);
        uklad.Controls.Add(new Label { Text = "Adres strumienia:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        uklad.Controls.Add(_txtAdres, 1, 1);
        uklad.Controls.Add(_chkUlubione, 1, 2);

        var przyciski = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
        var btnAnuluj = new Button { Text = "Anuluj", DialogResult = DialogResult.Cancel, AutoSize = true };
        var btnOk = new Button { Text = "Dodaj", AutoSize = true };
        btnOk.Click += (_, _) => Zatwierdz();
        przyciski.Controls.Add(btnAnuluj);
        przyciski.Controls.Add(btnOk);
        uklad.Controls.Add(przyciski, 0, 3);
        uklad.SetColumnSpan(przyciski, 2);

        Controls.Add(uklad);
        AcceptButton = btnOk;
        CancelButton = btnAnuluj;
        ResumeLayout(false);
        PerformLayout();
    }

    void Zatwierdz()
    {
        string adres = _txtAdres.Text.Trim();
        if (!Playlisty.CzyAdres(adres))
        {
            MessageBox.Show(this, "Podaj pełny adres strumienia, np. http://stream.example.pl:8000/radio.mp3",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtAdres.Focus();
            return;
        }
        string nazwa = _txtNazwa.Text.Trim();
        Wynik = new Stacja { Nazwa = nazwa.Length > 0 ? nazwa : adres, Url = adres };
        DialogResult = DialogResult.OK;
    }
}
