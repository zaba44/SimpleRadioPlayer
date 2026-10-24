using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace SimpleRadioPlayer;

/// <summary>
/// Ikona rysowana w kodzie: kropka nadajnika i fale. Każdy rozmiar rysowany osobno
/// (nie zmniejszany z dużego), żeby 16 px było ostre. Ta sama grafika trafia do app.ico
/// (narzędzie generujące: Ikony.ZbudujIco), okna i traya; kolor rozróżnia granie od ciszy.
/// </summary>
static class Ikony
{
    public static readonly Color Akcent = Color.FromArgb(0, 102, 204);
    static readonly Color Wyciszona = Color.FromArgb(120, 128, 140);

    /// <summary>Rozmiary wymagane przez Eksploratora i pasek zadań przy skalowaniu 100–400%.</summary>
    public static readonly int[] Rozmiary = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];

    static Icon? _oknoGra, _oknoCisza, _trayGra, _trayCisza;

    /// <summary>Ikona wielorozmiarowa dla okna — Windows sam wybiera klatkę pod DPI.</summary>
    public static Icon Okno(bool gra) => gra
        ? _oknoGra ??= new Icon(new MemoryStream(ZbudujIco(Akcent)))
        : _oknoCisza ??= new Icon(new MemoryStream(ZbudujIco(Wyciszona)));

    /// <summary>Ikona traya w rozmiarze małej ikony systemu (16 px przy 100%, 24 px przy 150% itd.).</summary>
    public static Icon Tray(bool gra)
    {
        var r = SystemInformation.SmallIconSize;
        return gra
            ? _trayGra ??= new Icon(Okno(true), r)
            : _trayCisza ??= new Icon(Okno(false), r);
    }

    public static Bitmap Rysuj(Color tlo, int px)
    {
        var bmp = new Bitmap(px, px, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);

        float s = px / 32f;
        using (var sciezka = ZaokraglonyProstokat(new RectangleF(0, 0, px, px), 7 * s))
        using (var pedzel = new SolidBrush(tlo))
            g.FillPath(pedzel, sciezka);

        float c = px / 2f;
        float kropka = Math.Max(3f, 6.5f * s);
        g.FillEllipse(Brushes.White, c - kropka / 2, c - kropka / 2, kropka, kropka);

        // Przy 16–20 px dwie fale zlewają się w plamę — zostaje jedna, grubsza
        float[] promienie = px <= 20 ? [9.5f] : [7f, 12f];
        float grubosc = px <= 20 ? 3.2f : 2.4f;
        using var pioro = new Pen(Color.White, Math.Max(1.4f, grubosc * s)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        foreach (float r0 in promienie)
        {
            float r = r0 * s;
            var prost = new RectangleF(c - r, c - r, 2 * r, 2 * r);
            g.DrawArc(pioro, prost, -40, 80);
            g.DrawArc(pioro, prost, 140, 80);
        }
        return bmp;
    }

    /// <summary>Plik .ico: 256 px jako PNG, mniejsze jako 32-bitowe DIB (najszersza zgodność).</summary>
    public static byte[] ZbudujIco(Color tlo)
    {
        var obrazy = new List<byte[]>();
        foreach (int px in Rozmiary)
        {
            using var bmp = Rysuj(tlo, px);
            obrazy.Add(px >= 256 ? Png(bmp) : Dib(bmp));
        }

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((short)0);
        w.Write((short)1);
        w.Write((short)Rozmiary.Length);
        int przesuniecie = 6 + 16 * Rozmiary.Length;
        for (int i = 0; i < Rozmiary.Length; i++)
        {
            int px = Rozmiary[i];
            w.Write((byte)(px >= 256 ? 0 : px));
            w.Write((byte)(px >= 256 ? 0 : px));
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((short)1);
            w.Write((short)32);
            w.Write(obrazy[i].Length);
            w.Write(przesuniecie);
            przesuniecie += obrazy[i].Length;
        }
        foreach (var o in obrazy) w.Write(o);
        return ms.ToArray();
    }

    static byte[] Png(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    static byte[] Dib(Bitmap bmp)
    {
        int px = bmp.Width;
        int maskaWiersz = ((px + 31) / 32) * 4;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(40);
        w.Write(px);
        w.Write(px * 2);            // w .ico wysokość obejmuje kolor i maskę AND
        w.Write((short)1);
        w.Write((short)32);
        w.Write(0);
        w.Write(px * px * 4 + maskaWiersz * px);
        w.Write(0); w.Write(0); w.Write(0); w.Write(0);

        var dane = bmp.LockBits(new Rectangle(0, 0, px, px), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var wiersz = new byte[px * 4];
            for (int y = px - 1; y >= 0; y--)      // DIB zapisuje wiersze od dołu
            {
                System.Runtime.InteropServices.Marshal.Copy(dane.Scan0 + y * dane.Stride, wiersz, 0, wiersz.Length);
                w.Write(wiersz);
            }
        }
        finally { bmp.UnlockBits(dane); }

        w.Write(new byte[maskaWiersz * px]);   // maska pusta — przezroczystość z kanału alfa
        return ms.ToArray();
    }

    static GraphicsPath ZaokraglonyProstokat(RectangleF r, float promien)
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
