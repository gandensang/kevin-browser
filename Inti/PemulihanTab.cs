namespace KevinBrowser;

/// <summary>
/// Satu tab yang disimpan untuk dibuka lagi: alamat, judul, dan riwayat
/// mundur/majunya (data sesi WebKit yang sudah diserialisasi; null kalau tab
/// itu belum pernah dimuat).
/// </summary>
public sealed record TabTersimpan(string Uri, string Judul, byte[]? Riwayat);

/// <summary>
/// Tab yang sedang terbuka, disimpan ke berkas supaya bisa dibuka lagi saat
/// browser dibuka berikutnya, termasuk setelah mati mendadak (baterai
/// habis, RAM habis). Satu baris per tab, urut seperti di jendela:
/// alamat TAB judul TAB riwayat (base64).
/// </summary>
public sealed class TabTerbuka(string berkas)
{
    string? isiTerakhir;

    public string Berkas { get; } = berkas;

    public List<TabTersimpan> Baca()
    {
        if (!File.Exists(Berkas))
            return [];

        var hasil = new List<TabTersimpan>();
        foreach (var baris in File.ReadLines(Berkas))
        {
            var bagian = baris.Split('\t');
            if (bagian.Length != 3 || bagian[0].Length == 0)
                continue;
            byte[]? riwayat = null;
            try
            {
                if (bagian[2].Length > 0)
                    riwayat = Convert.FromBase64String(bagian[2]);
            }
            catch (FormatException)
            {
                // Riwayatnya rusak: tab tetap dibuka, di alamatnya saja.
            }
            hasil.Add(new TabTersimpan(bagian[0], bagian[1], riwayat));
        }
        return hasil;
    }

    /// <summary>
    /// Menulis daftar tab, tetapi hanya kalau isinya berubah sejak terakhir
    /// ditulis. Daftar kosong menghapus berkasnya.
    /// </summary>
    /// <returns>true kalau berkasnya ditulis.</returns>
    public bool Simpan(IEnumerable<TabTersimpan> tab)
    {
        var isi = string.Concat(tab.Select(Baris));
        if (isi == isiTerakhir)
            return false;

        if (isi.Length == 0)
            File.Delete(Berkas);
        else
        {
            // Lewat berkas sementara, supaya berkas lama tetap utuh kalau
            // listrik padam di tengah penulisan.
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(Berkas))!);
            var sementara = Berkas + ".baru";
            File.WriteAllText(sementara, isi);
            File.Move(sementara, Berkas, overwrite: true);
        }
        isiTerakhir = isi;
        return true;
    }

    static string Baris(TabTersimpan t) =>
        $"{Bersih(t.Uri)}\t{Bersih(t.Judul)}\t{(t.Riwayat is { } r ? Convert.ToBase64String(r) : "")}\n";

    static string Bersih(string teks) => teks.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}

/// <summary>
/// Tab yang baru ditutup, untuk dibuka lagi dengan Ctrl+Shift+T. Hanya di
/// memori: yang terakhir ditutup dibuka lebih dulu.
/// </summary>
public sealed class TabTertutup
{
    public const int JumlahMaks = 10;

    readonly List<(TabTersimpan Tab, int Posisi)> daftar = [];

    public int Jumlah => daftar.Count;

    /// <param name="posisi">Urutan tab itu di jendela, untuk dikembalikan ke tempatnya.</param>
    public void Tambah(TabTersimpan tab, int posisi)
    {
        daftar.Add((tab, posisi));
        if (daftar.Count > JumlahMaks)
            daftar.RemoveAt(0);
    }

    public (TabTersimpan Tab, int Posisi)? Ambil()
    {
        if (daftar.Count == 0)
            return null;
        var terakhir = daftar[^1];
        daftar.RemoveAt(daftar.Count - 1);
        return terakhir;
    }
}
