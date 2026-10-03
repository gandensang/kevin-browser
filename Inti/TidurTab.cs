namespace KevinBrowser;

/// <summary>Satu tab, dilihat dari aturan tidur-tab.</summary>
public interface ITab
{
    /// <summary>Punya WebView — berarti punya proses web yang memakan RAM.</summary>
    bool Hidup { get; }

    /// <summary>
    /// Tidak boleh ditidurkan: ada ketikan yang belum terkirim, sedang
    /// bersuara, memakai mikrofon/kamera, atau <see cref="TidurTab.SelaluHidup"/>.
    /// </summary>
    bool Dilindungi { get; }

    /// <summary><see cref="Environment.TickCount64"/> saat tab ini terakhir dilihat.</summary>
    long TerakhirAktif { get; }
}

/// <summary>
/// Menidurkan tab: WebView tab latar dihancurkan (prosesnya ikut berhenti),
/// yang disimpan hanya alamat dan riwayat mundur/maju. Tab dimuat lagi
/// saat dilihat. Ini penghemat RAM terbesar — tiap tab hidup berarti satu
/// proses web sendiri.
/// </summary>
public static class TidurTab
{
    // Pesan WhatsApp tidak masuk selama tabnya tidur, dan memuat ulang
    // WhatsApp Web lama (terukur ±16 detik di profil kosong).
    static readonly string[] SitusSelaluHidup = ["web.whatsapp.com"];

    /// <summary>Tab di situs ini tidak pernah ditidurkan.</summary>
    public static bool SelaluHidup(string? uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var u)
        && u.Scheme == Uri.UriSchemeHttps
        && SitusSelaluHidup.Contains(u.Host);

    /// <summary>
    /// Tab latar yang harus ditidurkan: semua yang sudah tidak dilihat selama
    /// <paramref name="tidurSetelah"/>, lalu, kalau tab hidup (termasuk yang
    /// aktif) masih lebih dari <paramref name="hidupMaks"/>, yang paling lama
    /// tidak dilihat. Tab aktif dan tab yang dilindungi tidak pernah dipilih,
    /// tetapi tetap dihitung, jadi tab hidup bisa tetap melebihi batas.
    /// </summary>
    /// <param name="sekarang"><see cref="Environment.TickCount64"/> saat ini.</param>
    /// <param name="ramMenipis">
    /// RAM laptop hampir habis (<see cref="Setelan.RamMenipisMB"/>): semua tab
    /// latar yang tidak dilindungi ditidurkan sekarang juga, tanpa menunggu
    /// batas jumlah atau waktu.
    /// </param>
    public static List<T> PilihYangDitidurkan<T>(IEnumerable<T> semua, T aktif, int hidupMaks, long sekarang, TimeSpan tidurSetelah,
        bool ramMenipis = false)
        where T : class, ITab
    {
        var latar = semua.Where(t => t.Hidup && t != aktif).ToList();
        var calon = latar.Where(t => !t.Dilindungi).OrderBy(t => t.TerakhirAktif).ToList();
        if (ramMenipis)
            return calon;
        // Yang terlalu lama diam pasti ada di depan calon (paling lama tidak dilihat).
        var diam = calon.Count(t => sekarang - t.TerakhirAktif >= tidurSetelah.TotalMilliseconds);
        var kelebihan = latar.Count - diam + (aktif.Hidup ? 1 : 0) - Math.Max(hidupMaks, 1);
        return calon.Take(diam + Math.Max(kelebihan, 0)).ToList();
    }
}
