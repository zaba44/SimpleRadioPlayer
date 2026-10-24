using System.IO.Pipes;
using System.Text;

namespace SimpleRadioPlayer;

static class Program
{
    const string NazwaMutexu = @"Local\SimpleRadioPlayer_7c1e4a52";
    public const string NazwaPotoku = "SimpleRadioPlayer_7c1e4a52";

    [STAThread]
    static void Main(string[] args)
    {
        // Wywoływane przez build.bat po publikacji: tworzy plugins.dat, żeby pierwszy start był szybki
        if (args.Any(a => a.Equals("--przygotuj-wtyczki", StringComparison.OrdinalIgnoreCase)))
        {
            Odtwarzacz.PrzygotujCacheWtyczek();
            return;
        }

        bool doTraya = args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));

        using var mutex = new Mutex(true, NazwaMutexu, out bool pierwsza);
        if (!pierwsza)
        {
            // Program już działa — ręczne uruchomienie ma tylko pokazać jego okno
            if (!doTraya) WyslijDoPierwszejInstancji("pokaz");
            return;
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        ApplicationConfiguration.Initialize();
        Application.Run(new GlowneOkno(Konfiguracja.Wczytaj(), doTraya));
    }

    static void WyslijDoPierwszejInstancji(string polecenie)
    {
        try
        {
            using var klient = new NamedPipeClientStream(".", NazwaPotoku, PipeDirection.Out);
            klient.Connect(2000);
            using var pisarz = new StreamWriter(klient);
            pisarz.WriteLine(polecenie);
        }
        catch { }
    }
}
