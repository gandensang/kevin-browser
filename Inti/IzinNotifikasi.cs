namespace KevinBrowser;

/// <summary>
/// Situs yang boleh menampilkan notifikasi desktop, tanpa ditanya. Hanya
/// WhatsApp Web: tabnya tidak pernah ditidurkan supaya pesan tetap masuk,
/// dan notifikasi memberi tahu pesan itu walau tabnya sedang tidak dilihat.
/// Situs lain selalu ditolak.
/// </summary>
public static class IzinNotifikasi
{
    static readonly string[] Situs = ["web.whatsapp.com"];

    /// <summary>
    /// Asal (origin) yang sudah diizinkan sejak proses web dimulai. Halaman
    /// bisa memeriksa <c>Notification.permission</c> tanpa pernah meminta
    /// izin; WhatsApp lalu hanya menampilkan ajakan untuk menyalakan
    /// notifikasi.
    /// </summary>
    public static IEnumerable<string> Asal => Situs.Select(s => "https://" + s);

    /// <param name="uri">Alamat bingkai utama tab yang meminta izin.</param>
    public static bool Boleh(string? uri) => IzinMedia.SitusHttps(uri, Situs);
}
