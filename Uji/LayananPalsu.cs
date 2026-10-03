using KevinBrowser;

namespace Uji;

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
    }

    public Riwayat Riwayat { get; }
    public DaftarBookmark Bookmark { get; }
    public Preferensi Preferensi { get; }
    public List<(JenisData Jenis, TimeSpan? Rentang)> Dihapus { get; } = [];
    public TimeProvider Waktu { get; } = new JamTetap(Sekarang);
    public StatusPenyaring? Penyaring { get; set; }

    public long UkuranCache() => 76L * 1024 * 1024;
    public long UkuranDataSitus() => 3L * 1024 * 1024;

    public Task HapusData(JenisData jenis, TimeSpan? rentang)
    {
        Dihapus.Add((jenis, rentang));
        return Task.CompletedTask;
    }

    public void Dispose() => Directory.Delete(folder, true);
}
