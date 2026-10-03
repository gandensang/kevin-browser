using System.Globalization;
using System.Text;

namespace KevinBrowser.Asisten;

/// <summary>Satu catatan: berkas .md di folder catatan atau di folder mata pelajarannya.</summary>
/// <param name="Mapel">Nama folder mata pelajaran; null kalau langsung di folder catatan.</param>
/// <param name="Nama">Nama berkas tanpa .md, mis. "2026-09-hukum-newton".</param>
/// <param name="Judul">Judul "# …" pertama di berkas, atau dari nama berkasnya.</param>
public sealed record Catatan(string? Mapel, string Nama, string Judul, DateTime Diubah);

/// <summary>Satu hasil pencarian: catatannya, dan potongan teks di sekitar kata yang dicari.</summary>
public sealed record HasilCari(Catatan Catatan, string Cuplikan);

public enum HasilSimpan
{
    Tersimpan,
    /// <summary>Berkasnya berubah sejak dibuka (disunting aplikasi lain), jadi tidak ditimpa.</summary>
    BerubahDiDisk,
    /// <summary>Nama atau isinya tidak boleh dipakai.</summary>
    Ditolak,
}

/// <summary>
/// Buku catatan pelajaran: berkas Markdown biasa di satu folder, satu
/// subfolder per mata pelajaran (fisika/2026-09-hukum-newton.md), ditambah
/// sumber.md, daftar dokumen yang sudah diolah jadi catatan. Berkasnya tetap
/// bisa dibaca dan disunting tanpa browser ini.
/// </summary>
/// <remarks>
/// Yang disentuh hanya berkas .md di folder catatan dan satu tingkat
/// subfolder di bawahnya. Nama berawalan titik (.git, berkas sementara),
/// pemisah folder, dan tautan simbolik ditolak atau dilewati, jadi alamat
/// kevin://belajar tidak bisa dipakai untuk membuka berkas lain.
/// </remarks>
public sealed class BukuCatatan(string folder)
{
    public const string BerkasSumber = "sumber.md";

    /// <summary>Berkas yang lebih besar dari ini bukan catatan: tidak dibaca dan tidak ditulis.</summary>
    public const int UkuranMaks = 1024 * 1024;

    static readonly EnumerationOptions Opsi = new() { MatchCasing = MatchCasing.CaseInsensitive, IgnoreInaccessible = true };

    // Judul per berkas, supaya daftar catatan tidak membaca ulang semua berkas
    // setiap kali dibuka: laptop lama sering masih memakai harddisk.
    readonly Dictionary<string, (DateTime Diubah, long Ukuran, string Judul)> judulTersimpan = [];

    // Penyerap menulis dari thread latar sementara halaman dibaca di thread
    // utama: daftar judul dan penulisan berkas dijaga satu kunci.
    readonly object kunci = new();

    public string Folder => folder;

    public bool AdaSumber => File.Exists(Path.Combine(folder, BerkasSumber));

    /// <summary>Folder mata pelajaran, urut nama.</summary>
    public List<string> Mapel() =>
        Directory.Exists(folder)
            ? [.. new DirectoryInfo(folder).EnumerateDirectories("*", Opsi).Where(Aman).Select(d => d.Name).Order(StringComparer.OrdinalIgnoreCase)]
            : [];

    /// <summary>Semua catatan: yang langsung di folder catatan, lalu per mata pelajaran.</summary>
    public List<Catatan> Semua()
    {
        var hasil = new List<Catatan>();
        if (!Directory.Exists(folder))
            return hasil;
        foreach (var berkas in BerkasCatatan(folder))
            if (!berkas.Name.Equals(BerkasSumber, StringComparison.OrdinalIgnoreCase))
                hasil.Add(Dari(null, berkas));
        foreach (var mapel in Mapel())
            foreach (var berkas in BerkasCatatan(Path.Combine(folder, mapel)))
                hasil.Add(Dari(mapel, berkas));
        return hasil;
    }

    /// <summary>Satu catatan, atau null kalau tidak ada atau namanya tidak boleh dipakai.</summary>
    public Catatan? Ambil(string? mapel, string nama) =>
        Jalur(mapel, nama) is { } jalur && new FileInfo(jalur) is { Exists: true } info
            ? Dari(mapel, info)
            : null;

    /// <summary>Isi catatan, atau null kalau tidak ada, terlalu besar, atau namanya tidak boleh dipakai.</summary>
    public string? Baca(string? mapel, string nama) => Jalur(mapel, nama) is { } jalur ? BacaBerkas(jalur) : null;

    /// <summary>Isi sumber.md, atau null kalau belum ada.</summary>
    public string? BacaSumber() => BacaBerkas(Path.Combine(folder, BerkasSumber));

    /// <summary>
    /// Sidik isi berkas, diambil saat catatan dibuka untuk disunting; "" kalau
    /// berkasnya belum ada. <see cref="Simpan"/> tidak menimpa berkas yang
    /// sidiknya sudah lain.
    /// </summary>
    public string Sidik(string? mapel, string nama)
    {
        if (Jalur(mapel, nama) is not { } jalur || !File.Exists(jalur))
            return "";
        // FNV-1a 64 bit: cukup untuk tahu isinya berubah. Bukan SHA-256,
        // karena itu menarik lapisan kriptografi .NET ke binary.
        var hash = 14695981039346656037UL;
        foreach (var b in File.ReadAllBytes(jalur))
            hash = (hash ^ b) * 1099511628211UL;
        return hash.ToString("X16");
    }

    /// <summary>Menimpa isi catatan, kalau berkasnya belum berubah sejak dibuka.</summary>
    public HasilSimpan Simpan(string? mapel, string nama, string isi, string sidikAwal)
    {
        var teks = Rapikan(isi);
        if (Jalur(mapel, nama) is not { } jalur || Encoding.UTF8.GetByteCount(teks) > UkuranMaks)
            return HasilSimpan.Ditolak;
        lock (kunci)
        {
            if (Sidik(mapel, nama) != sidikAwal)
                return HasilSimpan.BerubahDiDisk;
            Directory.CreateDirectory(Path.GetDirectoryName(jalur)!);
            TulisAtomik(jalur, teks);
            return HasilSimpan.Tersimpan;
        }
    }

    /// <summary>
    /// Catatan baru: "# judul" lalu isinya, di folder mata pelajarannya, dengan
    /// nama berkas berawalan bulan (2026-10-hukum-newton.md). Folder mata
    /// pelajaran yang belum ada dibuat. Null kalau mata pelajaran atau judulnya
    /// tidak bisa dipakai.
    /// </summary>
    public Catatan? Tulis(string mapel, string judul, string isi, DateTime sekarang)
    {
        judul = SatuBaris(judul);
        if (NamaFolderMapel(mapel) is not { } folderMapel || judul.Length == 0)
            return null;

        var teks = Rapikan(isi);
        var lengkap = $"# {judul}\n" + (teks.Length > 0 ? "\n" + teks : "");
        if (Encoding.UTF8.GetByteCount(lengkap) > UkuranMaks)
            return null;

        lock (kunci)
        {
            var nama = NamaBaru(folderMapel, Slug(judul), sekarang, []);
            Directory.CreateDirectory(Path.Combine(folder, folderMapel));
            TulisAtomik(Path.Combine(folder, folderMapel, nama + ".md"), lengkap);
            return Ambil(folderMapel, nama);
        }
    }

    /// <summary>
    /// Catatan hasil serapan, di folder mata pelajarannya. Baris pertama tiap
    /// catatan menyebut sumbernya; nama berkas berawalan bulan, dan tautan
    /// [[nama]] antarcatatan di kelompok ini ikut diganti ke nama berkasnya.
    /// </summary>
    public List<Catatan> TulisSerapan(string folderMapel, IReadOnlyList<CatatanSerapan> semua, string sumber, DateTime sekarang)
    {
        if (!NamaAman(folderMapel))
            throw new IOException("nama mata pelajaran tidak boleh dipakai");
        lock (kunci)
        {
            var nama = new List<string>();
            var peta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in semua)
            {
                var slug = Slug(c.Nama) is { Length: > 0 } dariNama ? dariNama : Slug(JudulDari(c.Isi) ?? "");
                var akhir = NamaBaru(folderMapel, slug, sekarang, nama);
                nama.Add(akhir);
                peta.TryAdd(c.Nama.Trim(), akhir);
                if (slug.Length > 0)
                    peta.TryAdd(slug, akhir);
            }
            Directory.CreateDirectory(Path.Combine(folder, folderMapel));
            var hasil = new List<Catatan>();
            for (var i = 0; i < semua.Count; i++)
            {
                TulisAtomik(Path.Combine(folder, folderMapel, nama[i] + ".md"),
                    Rapikan($"Sumber: {SatuBaris(sumber)}\n\n{GantiTautan(semua[i].Isi, peta)}"));
                hasil.Add(Ambil(folderMapel, nama[i])!);
            }
            return hasil;
        }
    }

    /// <summary>
    /// Satu baris baru di sumber.md (dibuat kalau belum ada), dengan kolom yang
    /// sama seperti yang sudah dipakai: berkas, ukuran, waktu-ubah, diserap,
    /// jadi catatan.
    /// </summary>
    public void TambahSumber(string berkas, long ukuran, long waktuUbah, DateTime diserap, IEnumerable<Catatan> catatan)
    {
        var jalur = Path.Combine(folder, BerkasSumber);
        var baris = $"| {SatuBaris(berkas).Replace('|', '/')} | {ukuran} | {waktuUbah} | {diserap:yyyy-MM-dd} | "
            + string.Join(", ", catatan.Select(c => c.Mapel is null ? c.Nama + ".md" : $"{c.Mapel}/{c.Nama}.md")) + " |\n";
        lock (kunci)
        {
            Directory.CreateDirectory(folder);
            var info = new FileInfo(jalur);
            if (!info.Exists)
                File.WriteAllText(jalur, KepalaSumber + baris, new UTF8Encoding(false));
            else if (Aman(info))
            {
                var lama = File.ReadAllText(jalur);
                File.AppendAllText(jalur, (lama.Length == 0 || lama.EndsWith('\n') ? "" : "\n") + baris, new UTF8Encoding(false));
            }
        }
    }

    /// <summary>
    /// Kolom "jadi catatan" sumber.md kalau berkas dengan nama, ukuran, dan
    /// waktu ubah yang sama persis sudah pernah diserap; null kalau belum.
    /// Memeriksa daftar ini murah, membaca ulang PDF-nya mahal.
    /// </summary>
    public string? SudahDiserap(string berkas, long ukuran, long waktuUbah)
    {
        foreach (var baris in (BacaSumber() ?? "").Split('\n'))
        {
            var sel = baris.Trim().Trim('|').Split('|');
            if (sel.Length >= 5 && sel[0].Trim() == berkas && sel[1].Trim() == ukuran.ToString(CultureInfo.InvariantCulture)
                && sel[2].Trim() == waktuUbah.ToString(CultureInfo.InvariantCulture))
                return sel[4].Trim();
        }
        return null;
    }

    const string KepalaSumber = """
        # Dokumen yang sudah diserap jadi catatan

        Waktu-ubah = detik sejak 1 Januari 1970 (UTC), dipakai untuk mendeteksi
        berkas yang diperbarui.

        | berkas | ukuran | waktu-ubah | diserap | jadi catatan |
        |--------|--------|------------|---------|--------------|

        """;

    // "2026-10-slug", "2026-10-slug-2", …: belum ada di disk dan belum
    // dipakai di kelompok yang sama.
    string NamaBaru(string folderMapel, string slug, DateTime sekarang, List<string> terpakai)
    {
        var dasar = $"{sekarang:yyyy-MM}-{(slug.Length > 0 ? slug : "catatan")}";
        var nama = dasar;
        for (var i = 2; File.Exists(Path.Combine(folder, folderMapel, nama + ".md")) || terpakai.Contains(nama); i++)
            nama = $"{dasar}-{i}";
        return nama;
    }

    static string? JudulDari(string isi) =>
        isi.Split('\n').Take(40).Select(JudulBaris).FirstOrDefault(j => j is not null);

    // [[nama]] dan [[nama|label]] yang ada di peta diganti ke nama berkasnya.
    static string GantiTautan(string isi, Dictionary<string, string> peta)
    {
        var hasil = new StringBuilder(isi.Length);
        var i = 0;
        while (i < isi.Length)
        {
            var buka = isi.IndexOf("[[", i, StringComparison.Ordinal);
            var tutup = buka < 0 ? -1 : isi.IndexOf("]]", buka + 2, StringComparison.Ordinal);
            if (tutup < 0)
                break;
            var dalam = isi[(buka + 2)..tutup];
            var garis = dalam.IndexOf('|');
            var sasaran = garis < 0 ? dalam : dalam[..garis];
            hasil.Append(isi, i, buka - i).Append("[[");
            hasil.Append(peta.TryGetValue(sasaran.Trim(), out var baru) ? baru + (garis < 0 ? "" : dalam[garis..]) : dalam);
            hasil.Append("]]");
            i = tutup + 2;
        }
        return hasil.Append(isi, i, isi.Length - i).ToString();
    }

    /// <summary>
    /// Catatan yang memuat semua kata di <paramref name="kata"/> (di judul,
    /// isi, atau nama mata pelajarannya), tanpa membedakan huruf besar dan
    /// kecil. Yang cocok di judul lebih dulu, lalu yang terbaru.
    /// </summary>
    public List<HasilCari> Cari(string kata, int maks = 100)
    {
        var bagian = kata.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (bagian.Length == 0)
            return [];
        var hasil = new List<(HasilCari Hasil, bool DiJudul)>();
        foreach (var c in Semua())
        {
            var isi = Baca(c.Mapel, c.Nama) ?? "";
            var semua = $"{c.Mapel}\n{c.Judul}\n{isi}";
            if (bagian.All(b => semua.Contains(b, StringComparison.OrdinalIgnoreCase)))
                hasil.Add((new HasilCari(c, Cuplikan(isi, bagian)),
                    bagian.All(b => c.Judul.Contains(b, StringComparison.OrdinalIgnoreCase))));
        }
        return [.. hasil.OrderByDescending(h => h.DiJudul).ThenByDescending(h => h.Hasil.Catatan.Diubah).Take(maks).Select(h => h.Hasil)];
    }

    /// <summary>
    /// Pencari catatan untuk tautan [[nama]]: nama berkas tanpa .md, boleh
    /// dengan "mapel/" di depan. Yang ada di mata pelajaran yang sama didahulukan.
    /// </summary>
    public Func<string, Catatan?> Penaut(string? mapelSekarang)
    {
        var semua = Semua();
        return sasaran =>
        {
            var nama = sasaran.Trim();
            if (nama.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                nama = nama[..^3];
            string? mapel = null;
            if (nama.LastIndexOf('/') is var garis and >= 0)
                (mapel, nama) = (nama[..garis], nama[(garis + 1)..]);
            var cocok = semua.Where(c => c.Nama.Equals(nama, StringComparison.OrdinalIgnoreCase)
                && (mapel is null || string.Equals(c.Mapel, mapel, StringComparison.OrdinalIgnoreCase))).ToList();
            return cocok.FirstOrDefault(c => c.Mapel == mapelSekarang) ?? cocok.FirstOrDefault();
        };
    }

    /// <summary>Nama berkas atau folder yang boleh dipakai: satu bagian jalur, tidak diawali titik.</summary>
    public static bool NamaAman(string? nama) =>
        !string.IsNullOrWhiteSpace(nama) && nama.Length <= 150 && nama[0] != '.' && nama.Trim() == nama
        && !nama.Any(c => c is '/' or '\\' || char.IsControl(c));

    /// <summary>"# Hukum Newton" → "Hukum Newton"; null kalau baris itu bukan judul tingkat satu.</summary>
    public static string? JudulBaris(string baris)
    {
        var teks = baris.TrimStart(' ');
        if (baris.Length - teks.Length > 3 || !teks.StartsWith("# ", StringComparison.Ordinal))
            return null;
        var judul = teks[2..].Trim().TrimEnd('#').Trim();
        return judul.Length > 0 ? judul : null;
    }

    /// <summary>"2026-09-hukum-newton" → "Hukum newton".</summary>
    public static string JudulDariNama(string nama)
    {
        var tanpaTanggal = nama[PanjangTanggal(nama)..];
        var teks = (tanpaTanggal.Length > 0 ? tanpaTanggal : nama).Replace('-', ' ').Replace('_', ' ').Trim();
        return teks.Length == 0 ? nama : char.ToUpperInvariant(teks[0]) + teks[1..];
    }

    /// <summary>Awalan tanggal nama berkas ("2026-09" atau "2026-09-23"), untuk urutan; "" kalau tidak ada.</summary>
    public static string AwalanTanggal(string nama) => PanjangTanggal(nama) is var n and > 0 ? nama[..(n - 1)] : "";

    /// <summary>"Hukum Newton (bag. 2)" → "hukum-newton-bag-2": hanya a–z, 0–9, dan tanda hubung.</summary>
    public static string Slug(string teks)
    {
        var hasil = new StringBuilder();
        foreach (var c in teks.ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
                hasil.Append(c);
            else if (hasil.Length > 0 && hasil[^1] != '-')
                hasil.Append('-');
        }
        var slug = hasil.ToString().Trim('-');
        return slug.Length > 60 ? slug[..60].TrimEnd('-') : slug;
    }

    // Panjang "2026-09-" atau "2026-09-23-" di depan nama berkas; 0 kalau tidak ada.
    static int PanjangTanggal(string nama)
    {
        static bool Angka(string s, int dari, int n) => s.Length >= dari + n && s.AsSpan(dari, n).IndexOfAnyExceptInRange('0', '9') < 0;
        if (!Angka(nama, 0, 4) || nama.Length < 8 || nama[4] != '-' || !Angka(nama, 5, 2) || nama[7] != '-')
            return 0;
        return Angka(nama, 8, 2) && nama.Length > 10 && nama[10] == '-' ? 11 : 8;
    }

    /// <summary>
    /// Folder untuk mata pelajaran <paramref name="mapel"/>: folder yang sudah
    /// ada dipakai apa adanya, juga yang dibuat di luar browser ini (mis.
    /// "Bahasa Inggris"); nama baru dijadikan slug. Null kalau tidak bisa.
    /// </summary>
    public string? NamaFolderMapel(string mapel)
    {
        mapel = SatuBaris(mapel);
        var slug = Slug(mapel);
        var ada = Mapel();
        var folderMapel = ada.FirstOrDefault(m => m.Equals(mapel, StringComparison.OrdinalIgnoreCase))
            ?? ada.FirstOrDefault(m => m.Equals(slug, StringComparison.OrdinalIgnoreCase))
            ?? slug;
        return NamaAman(folderMapel) ? folderMapel : null;
    }

    // Jalur berkas catatan, atau null kalau namanya tidak boleh dipakai atau
    // jalurnya lewat tautan simbolik.
    string? Jalur(string? mapel, string nama)
    {
        if (!NamaAman(nama) || (mapel is not null && !NamaAman(mapel)))
            return null;
        if (mapel is not null && new DirectoryInfo(Path.Combine(folder, mapel)) is { Exists: true } d && !Aman(d))
            return null;
        var jalur = mapel is null ? Path.Combine(folder, nama + ".md") : Path.Combine(folder, mapel, nama + ".md");
        return File.Exists(jalur) && !Aman(new FileInfo(jalur)) ? null : jalur;
    }

    static bool Aman(FileSystemInfo info) => !info.Name.StartsWith('.') && info.LinkTarget is null;

    static IEnumerable<FileInfo> BerkasCatatan(string folder) =>
        new DirectoryInfo(folder).EnumerateFiles("*.md", Opsi).Where(f => Aman(f) && f.Length <= UkuranMaks);

    Catatan Dari(string? mapel, FileInfo berkas)
    {
        var nama = Path.GetFileNameWithoutExtension(berkas.Name);
        return new Catatan(mapel, nama, Judul(berkas, nama), berkas.LastWriteTime);
    }

    string Judul(FileInfo berkas, string nama)
    {
        lock (kunci)
        {
            if (judulTersimpan.TryGetValue(berkas.FullName, out var simpanan)
                && simpanan.Diubah == berkas.LastWriteTimeUtc && simpanan.Ukuran == berkas.Length)
                return simpanan.Judul;
            var judul = JudulDariIsi(berkas) ?? JudulDariNama(nama);
            judulTersimpan[berkas.FullName] = (berkas.LastWriteTimeUtc, berkas.Length, judul);
            return judul;
        }
    }

    // Judul "# …" di 40 baris pertama; baris pertama catatan sering
    // menyebut sumbernya dulu.
    static string? JudulDariIsi(FileInfo berkas)
    {
        if (berkas.Length > UkuranMaks)
            return null;
        try
        {
            using var baca = new StreamReader(berkas.FullName, Encoding.UTF8);
            for (var i = 0; i < 40 && baca.ReadLine() is { } baris; i++)
                if (JudulBaris(baris) is { } judul)
                    return judul;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        return null;
    }

    static string? BacaBerkas(string jalur)
    {
        var info = new FileInfo(jalur);
        if (!info.Exists || !Aman(info) || info.Length > UkuranMaks)
            return null;
        try
        {
            return File.ReadAllText(jalur, Encoding.UTF8);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Lewat berkas sementara lalu diganti namanya, supaya catatan tidak
    // tertinggal setengah tertulis kalau laptop mati saat menyimpan.
    static void TulisAtomik(string jalur, string isi)
    {
        var sementara = Path.Combine(Path.GetDirectoryName(jalur)!, $".{Path.GetFileName(jalur)}.{Environment.ProcessId}.tmp");
        File.WriteAllText(sementara, isi, new UTF8Encoding(false));
        File.Move(sementara, jalur, true);
    }

    // Baris baru gaya Unix (formulir mengirim \r\n), satu baris baru di akhir.
    static string Rapikan(string isi)
    {
        var teks = isi.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd();
        return teks.Length == 0 ? "" : teks + "\n";
    }

    static string SatuBaris(string teks) =>
        new([.. string.Join(' ', teks.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Where(c => !char.IsControl(c))]);

    // ±160 huruf di sekitar kata pertama yang ketemu, dalam satu baris, tanpa
    // memotong kata dan tanpa tanda Markdown yang paling sering (**, `, [[ ]]).
    static string Cuplikan(string isi, string[] kata)
    {
        var polos = isi.Replace("**", "").Replace("__", "").Replace("`", "").Replace("[[", "").Replace("]]", "");
        var teks = string.Join(' ', polos.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Where(k => k.Trim('#').Length > 0));
        var posisi = kata.Select(k => teks.IndexOf(k, StringComparison.OrdinalIgnoreCase)).Where(p => p >= 0).DefaultIfEmpty(0).Min();
        var awal = Math.Max(0, posisi - 60);
        var akhir = Math.Min(teks.Length, posisi + 100);
        if (awal > 0 && teks.IndexOf(' ', awal) is var spasiAwal and >= 0 && spasiAwal < posisi)
            awal = spasiAwal + 1;
        if (akhir < teks.Length && teks.LastIndexOf(' ', akhir) is var spasiAkhir && spasiAkhir > posisi)
            akhir = spasiAkhir;
        return (awal > 0 ? "…" : "") + teks[awal..akhir] + (akhir < teks.Length ? "…" : "");
    }
}
