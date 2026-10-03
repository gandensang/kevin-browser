namespace KevinBrowser;

/// <summary>Jenis data yang bisa dihapus dari halaman Pengaturan.</summary>
[Flags]
public enum JenisData
{
    Kosong = 0,
    Riwayat = 1,
    Cache = 2,
    /// <summary>Cookie: login di semua situs ikut hilang.</summary>
    Cookie = 4,
    /// <summary>Penyimpanan situs lainnya: localStorage, IndexedDB, service worker, dsb.</summary>
    DataSitus = 8,
}

/// <summary>
/// Yang disediakan platform (Linux sekarang, Windows nanti) untuk halaman
/// kevin:// yang dinamis: Pengaturan dan Riwayat.
/// </summary>
public interface ILayanan
{
    Riwayat Riwayat { get; }
    DaftarBookmark Bookmark { get; }

    /// <summary>Pilihan pemakai, mis. bahasa tampilan.</summary>
    Preferensi Preferensi { get; }

    /// <summary>Byte di disk.</summary>
    long UkuranCache();

    /// <summary>Byte di disk: cookie dan penyimpanan situs, tanpa riwayat.</summary>
    long UkuranDataSitus();

    /// <summary>
    /// Menghapus data milik mesin web (bukan riwayat) yang berubah dalam
    /// <paramref name="rentang"/> terakhir; null = semua waktu.
    /// </summary>
    Task HapusData(JenisData jenis, TimeSpan? rentang);

    TimeProvider Waktu { get; }

    /// <summary>Pemblokir iklan dan pelacak; null kalau dimatikan.</summary>
    StatusPenyaring? Penyaring { get; }
}

/// <summary>Keadaan pemblokir iklan dan pelacak untuk halaman Pengaturan.</summary>
public sealed record StatusPenyaring(bool SedangMemperbarui, InfoFilter? Info, int FilterAktif, long Ukuran);

public static class Penyimpanan
{
    public static long UkuranFolder(string folder)
    {
        if (!Directory.Exists(folder))
            return 0;
        var opsi = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        return new DirectoryInfo(folder).EnumerateFiles("*", opsi).Sum(f => f.Length);
    }
}
