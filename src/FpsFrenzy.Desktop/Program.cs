using FpsFrenzy.Kni;

namespace FpsFrenzy.Desktop;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        using FpsFrenzyGame game = new(mouseCapture: new SdlMouseCapture());
        game.Run();
    }
}
