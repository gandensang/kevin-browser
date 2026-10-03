namespace KevinBrowser;

/// <summary>Satu kunjungan di riwayat.</summary>
public sealed record Kunjungan(DateTimeOffset Waktu, string Uri, string Judul)
{
    /// <summary>Penanda untuk menghapus satu entri: milidetik Unix waktunya.</summary>
    public long Id => Waktu.ToUnixTimeMilliseconds();
}

/// <summary>
/// Riwayat kunjungan: satu baris per kunjungan di berkas teks
/// (milidetik-Unix TAB alamat TAB judul). Tidak disimpan di memori; berkasnya
/// baru dibaca saat halaman riwayat dibuka, jadi selama menjelajah tidak
/// memakan RAM.
/// </summary>
public sealed class Riwayat(string berkas)
{
    /// <summary>Kunjungan yang lebih tua dari ini dibuang saat browser dibuka.</summary>
    public static readonly TimeSpan Umur = TimeSpan.FromDays(90);
    const int JumlahMaks = 20_000;

    public string Berkas { get; } = berkas;

    /// <summary>Hanya halaman web. kevin://, about:, data:, dan sejenisnya tidak dicatat.</summary>
    public static bool LayakDicatat(string uri) =>
        uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

    public void Catat(string uri, string judul, DateTimeOffset waktu)
    {
        if (!LayakDicatat(uri))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(Berkas))!);
        File.AppendAllText(Berkas, Baris(new Kunjungan(waktu, uri, judul)));
    }

    /// <summary>Terbaru lebih dulu. <paramref name="cari"/> dicocokkan ke judul dan alamat.</summary>
    public List<Kunjungan> Baca(string? cari = null)
    {
        var hasil = BacaSemua();
        if (!string.IsNullOrWhiteSpace(cari))
            hasil = hasil.FindAll(k =>
                k.Judul.Contains(cari, StringComparison.OrdinalIgnoreCase)
                || k.Uri.Contains(cari, StringComparison.OrdinalIgnoreCase));
        hasil.Reverse();
        return hasil;
    }

    public int Jumlah() => BacaSemua().Count;

    /// <summary>Menghapus kunjungan sejak <paramref name="sejak"/>; null = semuanya.</summary>
    public void Hapus(DateTimeOffset? sejak) =>
        Tulis(sejak is { } s ? BacaSemua().FindAll(k => k.Waktu < s) : []);

    public void HapusSatu(long id) => Tulis(BacaSemua().FindAll(k => k.Id != id));

    /// <summary>Membuang kunjungan yang lebih tua dari <see cref="Umur"/> dan menjaga jumlahnya.</summary>
    public void Rapikan(DateTimeOffset sekarang)
    {
        var semua = BacaSemua();
        var sisa = semua.FindAll(k => k.Waktu >= sekarang - Umur);
        if (sisa.Count > JumlahMaks)
            sisa.RemoveRange(0, sisa.Count - JumlahMaks);
        if (sisa.Count != semua.Count)
            Tulis(sisa);
    }

    List<Kunjungan> BacaSemua()
    {
        if (!File.Exists(Berkas))
            return [];

        var hasil = new List<Kunjungan>();
        foreach (var baris in File.ReadLines(Berkas))
        {
            var bagian = baris.Split('\t', 3);
            if (bagian.Length == 3 && long.TryParse(bagian[0], out var ms))
                hasil.Add(new Kunjungan(DateTimeOffset.FromUnixTimeMilliseconds(ms), bagian[1], bagian[2]));
        }
        return hasil;
    }

    // Lewat berkas sementara, supaya berkas lama tetap utuh kalau penulisan gagal.
    void Tulis(List<Kunjungan> daftar)
    {
        if (daftar.Count == 0)
        {
            File.Delete(Berkas);
            return;
        }
        var sementara = Berkas + ".baru";
        File.WriteAllText(sementara, string.Concat(daftar.Select(Baris)));
        File.Move(sementara, Berkas, overwrite: true);
    }

    static string Baris(Kunjungan k) => $"{k.Id}\t{Bersih(k.Uri)}\t{Bersih(k.Judul)}\n";

    static string Bersih(string teks) => teks.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
