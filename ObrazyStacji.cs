using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace SimpleRadioPlayer;

/// <summary>
/// Obraz stacji w zadanym rozmiarze: logo na jasnej karcie albo kolorowy kafelek z literą.
/// Kolor kafelka wynika z nazwy, więc ta sama stacja ma zawsze ten sam kolor.
/// </summary>
static class ObrazyStacji
{
    static readonly Color[] Paleta =
    [
        Color.FromArgb(192, 57, 43), Color.FromArgb(211, 84, 0), Color.FromArgb(22, 160, 133),
        Color.FromArgb(41, 128, 185), Color.FromArgb(142, 68, 173), Color.FromArgb(44, 62, 80),
        Color.FromArgb(39, 174, 96), Color.FromArgb(194, 24, 91), Color.FromArgb(0, 131, 143),
        Color.FromArgb(93, 64, 55), Color.FromArgb(57, 73, 171), Color.FromArgb(127, 140, 141),
    ];

    // Słowa pomijane przy wyborze litery — inaczej połowa kafelków miałaby „R” od „Radio”
    static readonly HashSet<string> Pomijane = new(StringComparer.OrdinalIgnoreCase)
        { "radio", "polskie", "the", "fm", "internetowe", "web", "online" };

    public static Bitmap Rysuj(Stacja s, Bitmap? logo, int px)
    {
        var bmp = new Bitmap(px, px);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);

        var prost = new RectangleF(0.5f, 0.5f, px - 1.5f, px - 1.5f);
        using var ksztalt = Zaokraglony(prost, px * 0.18f);

        if (logo != null)
        {
            using (var tlo = new SolidBrush(Color.White)) g.FillPath(tlo, ksztalt);
            float margines = px * 0.08f;
            var obszar = RectangleF.Inflate(prost, -margines, -margines);
            float skala = Math.Min(obszar.Width / logo.Width, obszar.Height / logo.Height);
            float w = logo.Width * skala, h = logo.Height * skala;
            g.SetClip(ksztalt);
            g.DrawImage(logo, obszar.X + (obszar.Width - w) / 2, obszar.Y + (obszar.Height - h) / 2, w, h);
            g.ResetClip();
            using var ramka = new Pen(Color.FromArgb(220, 224, 230));
            g.DrawPath(ramka, ksztalt);
        }
        else
        {
            var kolor = Paleta[Skrot(s.Nazwa) % Paleta.Length];
            using (var tlo = new LinearGradientBrush(prost, Rozjasnij(kolor, 0.12f), kolor, LinearGradientMode.Vertical))
                g.FillPath(tlo, ksztalt);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var czcionka = new Font("Segoe UI Semibold", px * 0.46f, GraphicsUnit.Pixel);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            var tekst = new RectangleF(0, px * 0.02f, px, px);
            g.DrawString(Litera(s.Nazwa), czcionka, Brushes.White, tekst, format);
        }
        return bmp;
    }

    static string Litera(string nazwa)
    {
        var slowa = nazwa.Split([' ', '-', '.', '|', '(', ')', '/', '_', ':', ','], StringSplitOptions.RemoveEmptyEntries);
        foreach (var slowo in slowa.Where(sl => !Pomijane.Contains(sl)).Append(nazwa))
            foreach (char c in slowo)
                if (char.IsLetterOrDigit(c)) return char.ToUpper(c).ToString();
        return "♪";
    }

    /// <summary>FNV-1a — string.GetHashCode zmienia się między uruchomieniami.</summary>
    static int Skrot(string tekst)
    {
        uint h = 2166136261;
        foreach (char c in tekst.ToLowerInvariant()) h = (h ^ c) * 16777619;
        return (int)(h & 0x7FFFFFFF);
    }

    static Color Rozjasnij(Color c, float ile) => Color.FromArgb(
        (int)(c.R + (255 - c.R) * ile), (int)(c.G + (255 - c.G) * ile), (int)(c.B + (255 - c.B) * ile));

    public static GraphicsPath Zaokraglony(RectangleF r, float promien)
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
