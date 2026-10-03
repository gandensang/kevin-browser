namespace KevinBrowser;

/// <summary>
/// YouTube versi mobile (m.youtube.com) di jendela desktop. Jauh lebih
/// ringan daripada www.youtube.com, dengan tampilan seperti di tablet.
/// </summary>
/// <remarks>
/// Terukur, video yang sama diputar dengan suara, build Debug, PSS+GPU
/// seluruh pohon proses: www 840–930 MB, CPU ±105% (720p/480p, H.264 VA);
/// m 631–637 MB, CPU ±81% (360p, pilihan YouTube sendiri). Versi mobile
/// tetap berjalan dengan user agent desktop kalau alamatnya membawa
/// <c>app=m&amp;persist_app=1</c>: YouTube menyimpannya sebagai <c>app=m</c>
/// di cookie <c>PREF</c> (berlaku ±2 tahun), lalu servernya sendiri
/// mengalihkan setiap alamat www.youtube.com ke m.youtube.com. Jadi skrip ini
/// biasanya hanya bekerja sekali, dan lagi setelah cookie dihapus.
/// Versi desktop tetap bisa dipilih:
/// <c>www.youtube.com/?app=desktop&amp;persist_app=1</c> menyimpan
/// <c>app=desktop</c>, dan skrip ini membiarkannya.
/// </remarks>
public static class YouTubeRingan
{
    /// <summary>Bingkai utama, awal dokumen, di <see cref="Situs"/> kecuali <see cref="Kecuali"/>.</summary>
    public const string Skrip =
        """
        (() => {
          const u = new URL(location.href);
          if (u.searchParams.get('app') === 'desktop') return;
          if (/(?:^|;\s*)PREF=[^;]*\bapp=desktop/.test(document.cookie)) return;
          u.hostname = 'm.youtube.com';
          u.searchParams.set('app', 'm');
          u.searchParams.set('persist_app', '1');
          location.replace(u.href);
        })();
        """;

    public static readonly string[] Situs = ["https://www.youtube.com/*"];

    /// <summary>
    /// Untuk m.youtube.com, di dunia JS halaman sejak awal dokumen. Versi
    /// mobile menjeda video begitu halamannya tidak terlihat (putar di latar
    /// adalah fitur berbayar di sana), jadi musik berhenti saat pindah tab.
    /// Halaman dibuat selalu mengira dirinya terlihat.
    /// </summary>
    public const string SkripPutarDiLatar =
        """
        (() => {
          for (const [nama, nilai] of [['hidden', false], ['webkitHidden', false],
                                       ['visibilityState', 'visible'], ['webkitVisibilityState', 'visible']])
            Object.defineProperty(Document.prototype, nama, { get: () => nilai, configurable: true });
          const telan = e => e.stopImmediatePropagation();
          for (const jenis of ['visibilitychange', 'webkitvisibilitychange']) {
            document.addEventListener(jenis, telan, true);
            window.addEventListener(jenis, telan, true);
          }
        })();
        """;

    public static readonly string[] SitusMobile = ["https://m.youtube.com/*"];

    // Pemutar sematan yang dibuka langsung tetap di www.
    public static readonly string[] Kecuali = ["https://www.youtube.com/embed/*"];
}
