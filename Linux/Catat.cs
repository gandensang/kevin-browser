namespace KevinBrowser.Linux;

/// <summary>
/// Log siklus tab ke stderr (bangun, tidur, proses mati) kalau env
/// <c>KEVIN_BROWSER_CATAT=1</c>. Untuk memeriksa aturan tidur-tab tanpa debugger.
/// </summary>
static class Catat
{
    static readonly bool Nyala = Environment.GetEnvironmentVariable("KEVIN_BROWSER_CATAT") == "1";

    public static void Tulis(string pesan)
    {
        if (Nyala)
            Console.Error.WriteLine($"[kevin-browser {DateTime.Now:HH:mm:ss.fff}] {pesan}");
    }
}
