using System.ComponentModel;

namespace SimpleRadioPlayer.Kontrolki;

/// <summary>
/// Wirtualna lista stacji — przy prawie dwóch tysiącach pozycji filtrowanie jest natychmiastowe.
/// Opcjonalnie z kolumną logotypów (tylko na krótkich listach — każdy obraz to osobny bitmap).
/// </summary>
sealed class ListaStacji : ListView
{
    public List<Stacja> Stacje { get; private set; } = [];
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<Stacja, bool> CzyUlubiona { get; set; } = _ => false;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<Stacja, bool> CzyGra { get; set; } = _ => false;

    readonly Func<Stacja, string> _ostatniaKolumna;
    readonly Func<Stacja, int, Bitmap>? _obraz;
    readonly ImageList? _obrazki;
    readonly int _kolumnaNazwy;
    Font? _pogrubiona;

    public ListaStacji(string naglowekOstatniej, Func<Stacja, string> ostatniaKolumna,
        Func<Stacja, int, Bitmap>? obraz = null, int rozmiarObrazu = 0)
    {
        _ostatniaKolumna = ostatniaKolumna;
        _obraz = obraz;
        View = View.Details;
        VirtualMode = true;
        FullRowSelect = true;
        MultiSelect = false;
        HideSelection = false;
        DoubleBuffered = true;
        BorderStyle = BorderStyle.None;

        if (obraz != null)
        {
            _obrazki = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(rozmiarObrazu, rozmiarObrazu) };
            SmallImageList = _obrazki;
            Columns.Add("", rozmiarObrazu + 8);
        }
        Columns.Add("★", 28, HorizontalAlignment.Center);
        _kolumnaNazwy = Columns.Count;
        Columns.Add("Nazwa", 330);
        Columns.Add("Format", 130);
        Columns.Add(naglowekOstatniej, 200);
    }

    public Stacja? Zaznaczona =>
        SelectedIndices.Count > 0 && SelectedIndices[0] < Stacje.Count ? Stacje[SelectedIndices[0]] : null;

    public void Ustaw(List<Stacja> stacje)
    {
        var zaznaczona = Zaznaczona;
        BeginUpdate();
        SelectedIndices.Clear();
        Stacje = stacje;
        OdswiezObrazy();
        VirtualListSize = stacje.Count;
        if (zaznaczona != null) Zaznacz(zaznaczona, false);
        EndUpdate();
        Invalidate();
    }

    /// <summary>Przerysowuje obrazki stacji (np. po pobraniu logo albo zmianie trybu).</summary>
    public void OdswiezObrazy()
    {
        if (_obrazki == null || _obraz == null) return;
        int px = _obrazki.ImageSize.Width;
        // ImageList kopiuje obraz dopiero przy tworzeniu uchwytu listy — do tego czasu
        // oryginał musi żyć, więc zwalniamy poprzednie bitmapy dopiero po Clear()
        _obrazki.Images.Clear();
        foreach (var b in _bitmapy) b.Dispose();
        _bitmapy.Clear();
        foreach (var s in Stacje)
        {
            var bmp = _obraz(s, px);
            _bitmapy.Add(bmp);
            _obrazki.Images.Add(bmp);
        }
        Invalidate();
    }

    readonly List<Bitmap> _bitmapy = [];

    /// <summary>Po pobraniu jednego logo wymieniamy tylko jego obraz, bez przebudowy całej listy.</summary>
    public void OdswiezObraz(string klucz)
    {
        if (_obrazki == null || _obraz == null) return;
        for (int i = 0; i < Stacje.Count; i++)
        {
            if (Stacje[i].Klucz != klucz || i >= _bitmapy.Count) continue;
            var bmp = _obraz(Stacje[i], _obrazki.ImageSize.Width);
            var stary = _bitmapy[i];
            _obrazki.Images[i] = bmp;
            _bitmapy[i] = bmp;
            stary.Dispose();
            if (IsHandleCreated) RedrawItems(i, i, false);
        }
    }

    public void Zaznacz(Stacja s, bool przewin = true)
    {
        int i = Stacje.FindIndex(x => x.Klucz == s.Klucz);
        if (i < 0) return;
        SelectedIndices.Clear();
        SelectedIndices.Add(i);
        FocusedItem = Items[i];
        if (przewin) EnsureVisible(i);
    }

    protected override void OnRetrieveVirtualItem(RetrieveVirtualItemEventArgs e)
    {
        var s = Stacje[e.ItemIndex];
        string gwiazdka = CzyUlubiona(s) ? "★" : "";
        ListViewItem it;
        if (_obrazki != null)
        {
            it = new ListViewItem("", e.ItemIndex < _obrazki.Images.Count ? e.ItemIndex : -1);
            it.SubItems.Add(gwiazdka);
        }
        else it = new ListViewItem(gwiazdka);
        it.SubItems.Add(s.Nazwa);
        it.SubItems.Add(s.Info);
        it.SubItems.Add(_ostatniaKolumna(s));
        if (CzyGra(s))
        {
            it.Font = _pogrubiona ??= new Font(Font, FontStyle.Bold);
            it.ForeColor = Color.FromArgb(0, 102, 204);
        }
        e.Item = it;
    }

    protected override void OnFontChanged(EventArgs e)
    {
        _pogrubiona?.Dispose();
        _pogrubiona = null;
        base.OnFontChanged(e);
    }

    /// <summary>Kolumna „Nazwa” zajmuje całą wolną szerokość.</summary>
    public void RozciagnijNazwe()
    {
        if (Columns.Count <= _kolumnaNazwy) return;
        int inne = 0;
        for (int i = 0; i < Columns.Count; i++)
            if (i != _kolumnaNazwy) inne += Columns[i].Width;
        int w = ClientSize.Width - inne - 2;
        if (w > 100) Columns[_kolumnaNazwy].Width = w;
    }

    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        RozciagnijNazwe();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _obrazki?.Dispose();
            foreach (var b in _bitmapy) b.Dispose();
        }
        base.Dispose(disposing);
    }
}
