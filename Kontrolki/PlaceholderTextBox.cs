using System.ComponentModel;

namespace SimpleRadioPlayer.Kontrolki;

/// <summary>Pole z podpowiedzią rysowaną na WM_PAINT — natywny PlaceholderText bywa ucięty.</summary>
sealed class PlaceholderTextBox : TextBox
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Podpowiedz { get; set; } = "";
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color KolorPodpowiedzi { get; set; } = Color.Gray;

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == 0x000F && !Focused && Text.Length == 0 && Podpowiedz.Length > 0)
        {
            using var g = Graphics.FromHwnd(Handle);
            TextRenderer.DrawText(g, Podpowiedz, Font, new Point(1, 1), KolorPodpowiedzi);
        }
    }
}
