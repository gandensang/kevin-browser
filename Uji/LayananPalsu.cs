using KevinBrowser;
using KevinBrowser.Asisten;

namespace Uji;

/// <summary>
/// Tes yang membuat halaman kevin:// berjalan satu per satu. TokenSekali
/// dipakai bersama dan hanya mengingat 32 token terakhir, jadi token yang baru
/// dibuat satu tes bisa tergeser tes lain sebelum sempat dipakai.
/// </summary>
static class Koleksi
{
    public const string Halaman = "Halaman kevin://";
}

/// <summary>Jam yang berhenti di satu waktu, zona UTC.</summary>
sealed class JamTetap(DateTimeOffset sekarang) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => sekarang;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

/// <summary>ILayanan untuk tes: riwayat di folder sementara, hapus data hanya dicatat.</summary>
sealed class LayananPalsu : ILayanan, IDisposable
{
    // Kamis, 1 Oktober 2026 pukul 09.00 UTC.
    public static readonly DateTimeOffset Sekarang = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    readonly string folder = Directory.CreateTempSubdirectory("kevin-browser-uji-").FullName;

    public LayananPalsu()
    {
        Riwayat = new Riwayat(Path.Combine(folder, "riwayat.tsv"));
        Bookmark = new DaftarBookmark(Path.Combine(folder, "bookmark.tsv"));
        Preferensi = new Preferensi(Path.Combine(folder, "preferensi.tsv"));
        Catatan = new BukuCatatan(Path.Combine(folder, "catatan"));
        Belajar = new HalamanBelajar(Catatan, Waktu, FolderDibuka.Add);
    }

    public Riwayat Riwayat { get; }
    public DaftarBookmark Bookmark { get; }
    public Preferensi Preferensi { get; }
    public List<(JenisData Jenis, TimeSpan? Rentang)> Dihapus { get; } = [];
    public TimeProvider Waktu { get; } = new JamTetap(Sekarang);
    public StatusPenyaring? Penyaring { get; set; }
    public BukuCatatan Catatan { get; }
    public IHalaman? Belajar { get; }
    public List<string> FolderDibuka { get; } = [];

    public long UkuranCache() => 76L * 1024 * 1024;
    public long UkuranDataSitus() => 3L * 1024 * 1024;

    public Task HapusData(JenisData jenis, TimeSpan? rentang)
    {
        Dihapus.Add((jenis, rentang));
        return Task.CompletedTask;
    }

    public void Dispose() => Directory.Delete(folder, true);
}
