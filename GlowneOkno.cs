using System.IO.Pipes;

namespace SimpleRadioPlayer;

public partial class GlowneOkno : Form
{
    readonly Konfiguracja _konf;
    readonly CancellationTokenSource _koniec = new();
    Odtwarzacz? _odtw;
    Logotypy _logo;
    List<Stacja> _wszystkie = [];
    HashSet<string> _kluczeUlubionych = [];

    bool _moznaPokazac;     // false = start do traya, okno ma pozostać ukryte
    bool _zainicjowano;
    bool _wychodzimy;       // prawdziwe zamknięcie, nie chowanie do traya
    bool _podpowiedzTrayaPokazana;

    public GlowneOkno(Konfiguracja konf, bool startWTrayu)
    {
        _konf = konf;
        _moznaPokazac = !startWTrayu;
        _logo = UtworzLogotypy();
        BudujUi();
        BudujTray();
        PrzywrocPolozenie();
        WczytajListy();
        OdswiezUlubione();
        if (_konf.Ulubione.Count == 0) _zakladki.SelectedTab = _tabWszystkie;
        OdswiezPanel();
        OdswiezPrzyciskEq();
        Autostart.OdswiezSciezke();
        UruchomSerwerPotoku();
    }

    protected override void SetVisibleCore(bool value)
    {
        if (!_moznaPokazac)
        {
            value = false;
            if (!IsHandleCreated) CreateHandle();
        }
        base.SetVisibleCore(value);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (_zainicjowano) return;
        _zainicjowano = true;
        BeginInvoke(InicjalizujOdtwarzacz);
    }

    async void InicjalizujOdtwarzacz()
    {
        UstawStatus("Uruchamianie silnika dźwięku…");
        _postepStartu.Visible = true;
        _btnGraj.Enabled = _btnEq.Enabled = false;
        // Szybki start (typowo < 1 s) przechodzi bez komunikatu; przy dłuższym użytkownik ma wiedzieć, że program żyje
        var dlugiStart = new System.Windows.Forms.Timer { Interval = 1500 };
        dlugiStart.Tick += (_, _) =>
        {
            dlugiStart.Stop();
            if (_odtw != null) return;
            _lblTytul.Text = "Uruchamianie silnika dźwięku — pierwszy start może potrwać dłużej, bo Windows sprawdza pliki programu.";
            _tray.Text = "Simple Radio Player — uruchamianie…";
        };
        dlugiStart.Start();
        try
        {
            var odtw = await Odtwarzacz.UtworzAsync(this, _konf.Wyrownywanie);
            if (IsDisposed || _wychodzimy) { odtw.Dispose(); return; }
            _odtw = odtw;
        }
        catch (Exception ex)
        {
            dlugiStart.Dispose();
            _postepStartu.Visible = false;
            _lblTytul.Text = "";
            UstawStatus("Błąd silnika audio: " + ex.Message);
            if (Visible)
                MessageBox.Show(this, "Nie udało się uruchomić LibVLC — sprawdź, czy obok programu jest folder libvlc.\n\n" + ex.Message,
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _odtw.Glosnosc = _konf.Glosnosc;
        _odtw.Urzadzenie = _konf.UrzadzenieId;
        _odtw.Equalizer = _konf.Equalizer;
        _odtw.Wyciszony = _wyciszony;
        _odtw.Zmiana += NaZmianeOdtwarzacza;
        dlugiStart.Dispose();
        _postepStartu.Visible = false;
        _btnGraj.Enabled = _btnEq.Enabled = true;
        OdswiezUrzadzenia();
        OdswiezPanel();
        OdswiezTray();
        UstawStatus("Gotowe");

        var start = _konf.TrybStartu switch
        {
            TrybStartu.Ostatnia => _konf.OstatniaStacja,
            TrybStartu.Startowa => _konf.StacjaStartowa ?? _konf.OstatniaStacja,
            _ => null
        };
        if (start != null) Graj(start);
        SprawdzTerminAktualizacjiList();
    }

    void PrzywrocPolozenie()
    {
        if (_konf.Okno is not { Length: 4 } o) return;
        var prost = new Rectangle(o[0], o[1], o[2], o[3]);
        if (!Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(prost))) return;
        StartPosition = FormStartPosition.Manual;
        Bounds = prost;
    }

    void ZapamietajPolozenie()
    {
        if (!_moznaPokazac) return;
        var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _konf.Okno = [b.X, b.Y, b.Width, b.Height];
        _konf.Zakladka = _zakladki.SelectedIndex;
        _konf.Zapisz();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_wychodzimy && e.CloseReason == CloseReason.UserClosing && _konf.ZamykajDoTraya)
        {
            e.Cancel = true;
            SchowajDoTraya();
            return;
        }
        ZapamietajPolozenie();
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _koniec.Cancel();
        _zapisOpozniony?.Dispose();
        _konf.Zapisz();
        _tray.Visible = false;
        _odtw?.Dispose();
        _logo.Dispose();
        base.OnFormClosed(e);
    }

    void Zakoncz()
    {
        _wychodzimy = true;
        Close();
    }

    /// <summary>Druga instancja programu prosi przez named pipe o pokazanie okna.</summary>
    void UruchomSerwerPotoku()
    {
        var token = _koniec.Token;
        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var serwer = new NamedPipeServerStream(Program.NazwaPotoku, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await serwer.WaitForConnectionAsync(token);
                    using var czytnik = new StreamReader(serwer);
                    string? polecenie = await czytnik.ReadLineAsync(token);
                    if (polecenie == "pokaz" && IsHandleCreated) BeginInvoke(PrzywrocZTraya);
                }
                catch (OperationCanceledException) { return; }
                catch { await Task.Delay(500, CancellationToken.None); }
            }
        });
    }
}
