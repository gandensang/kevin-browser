namespace KevinBrowser.Linux;

/// <summary>ILayanan untuk Linux: data dan cache WebKit ada di folder profil.</summary>
sealed class LayananLinux(Riwayat riwayat, DaftarBookmark bookmark, Preferensi preferensi, string folderData, string folderCache,
    IHalaman belajar, IHalaman catur) : ILayanan
{
    const WebKit.WebsiteDataTypes JenisCache = WebKit.WebsiteDataTypes.DiskCache | WebKit.WebsiteDataTypes.MemoryCache;

    public Riwayat Riwayat => riwayat;

    public IHalaman? Belajar => belajar;

    public IHalaman? Catur => catur;

    public DaftarBookmark Bookmark => bookmark;

    public Preferensi Preferensi => preferensi;

    public TimeProvider Waktu => TimeProvider.System;

    public StatusPenyaring? Penyaring => KevinBrowser.Linux.Penyaring.Status;

    public long UkuranCache() => Penyimpanan.UkuranFolder(folderCache);

    // Riwayat, bookmark, pilihan, daftar tab, pengaturan asisten, dan daftar
    // pemblokir juga ada di folder data, tapi bukan data situs: ditampilkan
    // terpisah, dan tidak ikut terhapus.
    public long UkuranDataSitus() =>
        Penyimpanan.UkuranFolder(folderData)
        - Ukuran(riwayat.Berkas)
        - Ukuran(bookmark.Berkas)
        - Ukuran(preferensi.Berkas)
        - Ukuran(Mesin.TabTerbuka.Berkas)
        - Ukuran(Path.Combine(folderData, Mesin.BerkasAsisten))
        - Penyimpanan.UkuranFolder(Path.Combine(folderData, Mesin.FolderCatur))
        - Penyimpanan.UkuranFolder(Path.Combine(folderData, "penyaring"));

    static long Ukuran(string berkas) => File.Exists(berkas) ? new FileInfo(berkas).Length : 0;

    public Task HapusData(JenisData jenis, TimeSpan? rentang)
    {
        WebKit.WebsiteDataTypes tipe = 0;
        if (jenis.HasFlag(JenisData.Cache))
            tipe |= JenisCache;
        if (jenis.HasFlag(JenisData.Cookie))
            tipe |= WebKit.WebsiteDataTypes.Cookies;
        if (jenis.HasFlag(JenisData.DataSitus))
            tipe |= WebKit.WebsiteDataTypes.All & ~(JenisCache | WebKit.WebsiteDataTypes.Cookies);

        var pengelola = Mesin.Sesi.GetWebsiteDataManager();
        return Asli.HapusDataWebKit(pengelola.Handle.DangerousGetHandle(), (uint)tipe, rentang);
    }
}
