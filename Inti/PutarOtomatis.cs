namespace KevinBrowser;

/// <summary>
/// Video tidak berputar sendiri. Situs berita dan sejenisnya memutar video
/// tanpa suara begitu halaman dibuka, walau tidak ditonton: CPU, RAM, dan
/// kuota terpakai. Di sini video baru diputar setelah diklik, kecuali di
/// <see cref="SitusBoleh"/>. Video bersuara memang sudah butuh klik di WebKit.
/// </summary>
/// <remarks>
/// Terukur 3 Okt 2026, binary rilis, 25 detik setelah dibuka, 2× masing-masing
/// (kevin-browser lain sedang terbuka, jadi PSS mutlaknya lebih kecil):
/// artikel video 20.detik.com, berputar sendiri 1389–1395 MB PSS+GPU dan CPU
/// 151–156% satu inti; menunggu klik 567–575 MB dan 90–99%. Hampir semuanya
/// di proses halaman (PSS 1131 lawan 315 MB). Klik di pemutarnya tetap
/// memutar video. Di beranda detik, kompas, tribunnews, cnnindonesia,
/// kompas.tv, liputan6, dan kumparan tidak ada bedanya (±10 MB): di sana
/// tidak ada video yang berputar sendiri, atau pemutarnya sudah diblokir.
/// </remarks>
public static class PutarOtomatis
{
    // WhatsApp mengirim GIF sebagai video tanpa suara. YouTube sengaja tidak
    // dikecualikan (keputusan pemakai, 3 Okt 2026): video menunggu diketuk.
    static readonly string[] SitusBoleh = ["web.whatsapp.com"];

    /// <summary>
    /// Halaman di <paramref name="uri"/> boleh memutar video tanpa diklik.
    /// Yang menentukan adalah bingkai utama: video di dalam iframe mengikuti
    /// halaman yang memuatnya.
    /// </summary>
    public static bool Boleh(string? uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var u)
        && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp)
        && SitusBoleh.Any(s => u.Host == s || u.Host.EndsWith("." + s, StringComparison.Ordinal));
}
