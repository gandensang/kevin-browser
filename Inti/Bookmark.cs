namespace KevinBrowser;

/// <summary>Satu halaman yang disimpan pemakai (Ctrl+D atau tombol bintang).</summary>
public sealed record Bookmark(DateTimeOffset Waktu, string Uri, string Judul)
{
    /// <summary>Penanda untuk menghapus atau mengganti nama: milidetik Unix waktu disimpan.</summary>
    public long Id => Waktu.ToUnixTimeMilliseconds();

    public string Host => System.Uri.TryCreate(Uri, UriKind.Absolute, out var u) ? u.Host : Uri;

    /// <summary>Judul, atau host kalau judulnya kosong.</summary>
    public string Nama => Judul.Length > 0 ? Judul : Host;
}

/// <summary>
/// Daftar bookmark di berkas teks, urut sesuai waktu disimpan
/// (milidetik-Unix TAB alamat TAB judul, sama dengan riwayat). Isinya kecil
/// tetapi sering dicek: bintang di toolbar diperbarui setiap halaman
/// berganti. Karena itu setelah dibaca sekali, daftarnya disimpan di memori.
/// Hanya proses ini yang menulis berkasnya (instans tunggal).
/// </summary>
public sealed class DaftarBookmark(string berkas)
{
    List<Bookmark>? isi;

    public string Berkas { get; } = berkas;

    public IReadOnlyList<Bookmark> Semua() => Muat();

    public bool Ada(string uri) => Muat().Any(b => b.Uri == uri);

    /// <summary>false kalau alamatnya bukan halaman web atau sudah tersimpan.</summary>
    public bool Tambah(string uri, string judul, DateTimeOffset waktu)
    {
        if (!Riwayat.LayakDicatat(uri) || Ada(uri))
            return false;
        var daftar = Muat();
        // Id = milidetik; jangan sampai dua bookmark berbagi id.
        while (daftar.Any(b => b.Id == waktu.ToUnixTimeMilliseconds()))
            waktu = waktu.AddMilliseconds(1);
        Tulis([.. daftar, new Bookmark(waktu, uri, Bersih(judul))]);
        return true;
    }

    public void Hapus(string uri) => Tulis(Muat().FindAll(b => b.Uri != uri));

    public void HapusSatu(long id) => Tulis(Muat().FindAll(b => b.Id != id));

    /// <summary>Judul kosong berarti kembali memakai nama host.</summary>
    public void GantiNama(long id, string judul) =>
        Tulis(Muat().ConvertAll(b => b.Id == id ? b with { Judul = Bersih(judul) } : b));

    List<Bookmark> Muat()
    {
        if (isi is not null)
            return isi;

        isi = [];
        if (File.Exists(Berkas))
            foreach (var baris in File.ReadLines(Berkas))
            {
                var bagian = baris.Split('\t', 3);
                if (bagian.Length == 3 && long.TryParse(bagian[0], out var ms))
                    isi.Add(new Bookmark(DateTimeOffset.FromUnixTimeMilliseconds(ms), bagian[1], bagian[2]));
            }
        return isi;
    }

    // Lewat berkas sementara, supaya berkas lama tetap utuh kalau penulisan gagal.
    void Tulis(List<Bookmark> daftar)
    {
        isi = daftar;
        if (daftar.Count == 0)
        {
            File.Delete(Berkas);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(Berkas))!);
        var sementara = Berkas + ".baru";
        File.WriteAllText(sementara, string.Concat(daftar.Select(b => $"{b.Id}\t{b.Uri}\t{b.Judul}\n")));
        File.Move(sementara, Berkas, overwrite: true);
    }

    static string Bersih(string teks) => teks.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
}
