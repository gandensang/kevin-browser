using System.Globalization;

namespace KevinBrowser.Asisten;

/// <summary>
/// Skor tebak langkah yang sudah selesai, satu baris per latihan di folder
/// data catur (<c>skor.tsv</c>: waktu, partai, sisi, poin, maks, poin
/// partai, maks partai), supaya skor baru bisa dibandingkan dengan latihan
/// siswa sendiri sebelumnya. Tidak bergantung pada catatan, yang bisa
/// diubah atau dihapus pemakai.
/// </summary>
public sealed class RiwayatSkor(string berkas)
{
    readonly object kunci = new();

    public void Tambah(DateTimeOffset waktu, string partai, bool putih, SkorLatihan skor)
    {
        var baris = string.Join('\t', waktu.ToString("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture), partai.Replace('\t', ' '),
            putih ? "putih" : "hitam", skor.Poin, skor.Maks, skor.PoinPartai, skor.SoalPartai * 3);
        lock (kunci)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(berkas)!);
            File.AppendAllText(berkas, baris + "\n");
        }
    }

    /// <summary>
    /// Rata-rata persen paling banyak <paramref name="jumlah"/> latihan
    /// terakhir (persen tiap latihan, lalu dirata-rata), dan banyaknya; null
    /// kalau belum ada.
    /// </summary>
    public (int Jumlah, int Persen)? RataRata(int jumlah)
    {
        List<double> persen = [];
        lock (kunci)
        {
            if (!File.Exists(berkas))
                return null;
            foreach (var baris in File.ReadAllLines(berkas))
            {
                var kolom = baris.Split('\t');
                if (kolom.Length >= 5 && int.TryParse(kolom[3], CultureInfo.InvariantCulture, out var poin)
                    && int.TryParse(kolom[4], CultureInfo.InvariantCulture, out var maks) && maks > 0 && poin >= 0 && poin <= maks)
                    persen.Add(100.0 * poin / maks);
            }
        }
        var terakhir = persen.TakeLast(jumlah).ToList();
        return terakhir.Count == 0 ? null : (terakhir.Count, (int)Math.Round(terakhir.Average()));
    }
}
