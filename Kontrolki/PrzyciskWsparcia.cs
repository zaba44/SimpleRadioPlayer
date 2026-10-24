using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace SimpleRadioPlayer.Kontrolki;

/// <summary>
/// Przycisk „♥ Wsparcie” w pasku menu: zaokrąglony, z zielonym gradientem, jasną linią
/// u góry i cieniem pod spodem. Rysowany w całości sam — domyślny renderer paska
/// ignorowałby kolory i dawał płaski prostokąt.
/// Wzorzec ze skilla `wsparcie` (C:\python\skille\wsparcie).
/// </summary>
sealed class PrzyciskWsparcia : ToolStripButton
{
    public const string Adres = "https://cuplink.to/zaba44";

    static readonly Color Serce = Color.FromArgb(30, 142, 74);
    static readonly Color Napis = Color.FromArgb(31, 77, 46);

    public PrzyciskWsparcia(string tekst = "Wsparcie")
    {
        Text = tekst;
        DisplayStyle = ToolStripItemDisplayStyle.Text;
        Alignment = ToolStripItemAlignment.Right;
        Font = new Font("Segoe UI Semibold", 9F);
        Margin = new Padding(0, 1, 6, 1);
        ToolTipText = "Wesprzyj autora — " + Adres;
    }

    float Skala => (Owner?.DeviceDpi ?? 96) / 96f;

    public override Size GetPreferredSize(Size constrainingSize)
    {
        float s = Skala;
        var tekst = TextRenderer.MeasureText("♥ " + Text, Font);
        return new Size(tekst.Width + (int)(16 * s), (int)(23 * s));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        float s = Skala;
        bool wcisniety = Pressed;
        bool nad = Selected;

        var prost = new RectangleF(0.5f, 0.5f + (wcisniety ? 1 : 0), Width - 1.5f, Height - 3f);
        float r = 6 * s;

        // Cień pod przyciskiem — znika przy wciśnięciu, przycisk „osiada”
        if (!wcisniety)
        {
            using var cien = Zaokraglony(new RectangleF(prost.X, prost.Y + 1.5f, prost.Width, prost.Height), r);
            using var pedzelCienia = new SolidBrush(Color.FromArgb(nad ? 62 : 38, 40, 120, 60));
            g.FillPath(pedzelCienia, cien);
        }

        using var ksztalt = Zaokraglony(prost, r);
        var (gora, dol, ramka) = wcisniety
            ? (Color.FromArgb(189, 230, 201), Color.FromArgb(227, 245, 232), Color.FromArgb(74, 158, 100))
            : nad
                ? (Color.FromArgb(230, 247, 234), Color.FromArgb(189, 230, 201), Color.FromArgb(79, 166, 106))
                : (Color.FromArgb(242, 251, 244), Color.FromArgb(211, 239, 219), Color.FromArgb(141, 203, 158));
        using (var tlo = new LinearGradientBrush(prost, gora, dol, LinearGradientMode.Vertical))
            g.FillPath(tlo, ksztalt);

        // Jasna linia wzdłuż górnej krawędzi — efekt lekko wypukłej powierzchni
        if (!wcisniety)
        {
            using var blask = new Pen(Color.FromArgb(200, 255, 255, 255), 1f);
            g.DrawLine(blask, prost.X + r, prost.Y + 1.2f, prost.Right - r, prost.Y + 1.2f);
        }
        using (var pioro = new Pen(ramka, 1f)) g.DrawPath(pioro, ksztalt);

        // Serce i napis wyśrodkowane razem
        using var czcionkaSerca = new Font("Segoe UI Symbol", Font.Size + 0.5f);
        var rSerca = g.MeasureString("♥", czcionkaSerca, PointF.Empty, StringFormat.GenericTypographic);
        var rNapisu = g.MeasureString(Text, Font, PointF.Empty, StringFormat.GenericTypographic);
        float odstep = 4 * s;
        float x = prost.X + (prost.Width - rSerca.Width - odstep - rNapisu.Width) / 2;
        float srodek = prost.Y + prost.Height / 2;
        using (var pedzel = new SolidBrush(Serce))
            g.DrawString("♥", czcionkaSerca, pedzel, x, srodek - rSerca.Height / 2, StringFormat.GenericTypographic);
        using (var pedzel = new SolidBrush(Napis))
            g.DrawString(Text, Font, pedzel, x + rSerca.Width + odstep, srodek - rNapisu.Height / 2, StringFormat.GenericTypographic);
    }

    static GraphicsPath Zaokraglony(RectangleF r, float promien)
    {
        float d = promien * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
