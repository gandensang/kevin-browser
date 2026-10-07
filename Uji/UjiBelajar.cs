using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using KevinBrowser;

namespace Uji;

[Collection(Koleksi.Halaman)]
public sealed partial class UjiBelajar : IDisposable
{
    readonly LayananPalsu layanan = new();

    public void Dispose() => layanan.Dispose();

    string Folder => layanan.Catatan.Folder;

    async Task<string> Html(string uri, string? post = null)
    {
        var (isi, jenis) = await HalamanBawaan.Ambil(uri, layanan, post);
        Assert.Equal("text/html", jenis);
        return Encoding.UTF8.GetString(isi);
    }

    void Tulis(string jalur, string isi)
    {
        var lengkap = Path.Combine(Folder, jalur);
        Directory.CreateDirectory(Path.GetDirectoryName(lengkap)!);
        File.WriteAllText(lengkap, isi);
    }

    static string Post(params (string Kunci, string Nilai)[] isi) =>
        string.Join('&', isi.Select(p => $"{Uri.EscapeDataString(p.Kunci)}={Uri.EscapeDataString(p.Nilai).Replace("%20", "+")}"));

    [GeneratedRegex("name=\"token\" value=\"([0-9A-F]+)\"")]
    private static partial Regex Token();

    [GeneratedRegex("name=\"sidik\" value=\"([0-9A-F]*)\"")]
    private static partial Regex Sidik();

    [GeneratedRegex("http-equiv=\"refresh\" content=\"0; url=([^\"]+)\"")]
    private static partial Regex Pindah();

    [GeneratedRegex("href=\"(kevin://belajar\\?buka[^\"]+)\"")]
    private static partial Regex TautanBuka();

    static string Ambil(Regex pola, string html) => WebUtility.HtmlDecode(pola.Match(html).Groups[1].Value);

    [Fact]
    public async Task DaftarKosong()
    {
        var html = await Html("kevin://belajar");
        Assert.Contains("<a href=\"kevin://belajar\" aria-current=\"page\">Belajar</a>", html);
        Assert.Contains("<title>Belajar · Kevin Browser</title>", html);
        Assert.Contains("Belum ada catatan.", html);
        Assert.Contains("href=\"kevin://belajar?baru\"", html);
        Assert.DoesNotContain("?sumber", html);   // tombol Dokumen sumber hanya kalau sumber.md ada
        Assert.DoesNotContain("{{", html);
    }

    [Fact]
    public async Task DaftarPerMapel()
    {
        Tulis("fisika/2026-09-parabola.md", "# Gerak Parabola\n");
        Tulis("fisika/2026-08-newton.md", "# Hukum Newton\n");
        Tulis("ipa/2026-09-sel.md", "# Sel <hewan>\n");
        Tulis("lepas.md", "# Catatan lepas\n");
        Tulis("sumber.md", "# Dokumen\n");

        var html = await Html("kevin://belajar");

        Assert.Contains("<h3><a href=\"kevin://belajar?m=fisika\">Fisika</a></h3><span class=\"jumlah\">2 catatan</span>", html);
        Assert.Contains("<span class=\"lencana-mapel\" aria-hidden=\"true\">IPA</span>", html);
        Assert.Contains("<h3>Tanpa mata pelajaran</h3>", html);
        Assert.Contains("<a href=\"kevin://belajar?m=fisika&amp;c=2026-09-parabola\"><span>Gerak Parabola</span><time>", html);
        Assert.Contains("Sel &lt;hewan&gt;", html);
        Assert.Contains("href=\"kevin://belajar?baru&amp;m=fisika\"", html);
        Assert.Contains("href=\"kevin://belajar?sumber\"", html);
        // Yang terbaru dulu.
        Assert.True(html.IndexOf("Gerak Parabola", StringComparison.Ordinal) < html.IndexOf("Hukum Newton", StringComparison.Ordinal));
        // Mata pelajaran urut nama, tanpa mata pelajaran paling akhir.
        Assert.True(html.IndexOf(">Fisika<", StringComparison.Ordinal) < html.IndexOf(">IPA<", StringComparison.Ordinal));
        Assert.True(html.IndexOf(">IPA<", StringComparison.Ordinal) < html.IndexOf(">Tanpa mata pelajaran<", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LihatCatatan()
    {
        Tulis("fisika/2026-09-parabola.md", "Sumber: handout\n\n# Gerak Parabola\n\n## Rumus\n\nLihat [[2026-09-kesalahan]] dan [[tidak-ada]].\n");
        Tulis("fisika/2026-09-kesalahan.md", "# Kesalahan umum\n");

        var html = await Html("kevin://belajar?m=fisika&c=2026-09-parabola");

        Assert.Contains("<title>Gerak Parabola · Kevin Browser</title>", html);
        Assert.Contains("<h1>Gerak Parabola</h1>", html);
        Assert.Single(Regex.Matches(html, "Gerak Parabola</h"));   // judul tidak diulang di isi
        Assert.Contains("<span>Sumber: handout</span>", html);   // keterangan di bawah judul, bukan paragraf pertama
        Assert.DoesNotContain("<p>Sumber:", html);
        Assert.Contains("<h3>Rumus</h3>", html);
        Assert.Contains("<a href=\"kevin://belajar?m=fisika&amp;c=2026-09-kesalahan\">Kesalahan umum</a>", html);
        Assert.Contains("<span class=\"putus\">tidak-ada</span>", html);
        Assert.Contains("href=\"kevin://belajar?m=fisika&amp;c=2026-09-parabola&amp;sunting\"", html);
        Assert.Contains("fisika/2026-09-parabola.md</code>", html);
        Assert.Contains("<a href=\"kevin://belajar\">Belajar</a> › <a href=\"kevin://belajar?m=fisika\">Fisika</a>", html);
        Assert.Contains("<a href=\"kevin://belajar?m=fisika&amp;c=2026-09-kesalahan\">Kesalahan umum</a></li>", html);   // catatan lain di samping
        Assert.Contains("<a href=\"kevin://belajar?m=fisika&amp;c=2026-09-parabola\" aria-current=\"page\">Gerak Parabola</a>", html);
    }

    [Fact]
    public async Task SatuMapel()
    {
        Tulis("fisika/2026-09-parabola.md", "# Gerak Parabola\n");
        Tulis("fisika/2026-08-newton.md", "# Hukum Newton\n");
        Tulis("ipa/2026-09-sel.md", "# Sel\n");

        var html = await Html("kevin://belajar?m=fisika");

        Assert.Contains("<title>Fisika · Kevin Browser</title>", html);
        Assert.Contains("<h1><span class=\"lencana-mapel\" aria-hidden=\"true\">F</span>Fisika</h1>", html);
        Assert.Contains("<p class=\"keterangan\">2 catatan</p>", html);
        Assert.Contains("href=\"kevin://belajar?baru&amp;m=fisika\"", html);
        Assert.True(html.IndexOf("Gerak Parabola", StringComparison.Ordinal) < html.IndexOf("Hukum Newton", StringComparison.Ordinal));
        Assert.DoesNotContain("Sel", html);
        Assert.Contains("Catatan tidak ditemukan", await Html("kevin://belajar?m=kimia"));
    }

    [Fact]
    public async Task KartuMapelMeringkasCatatanBanyak()
    {
        for (var i = 1; i <= 7; i++)
            Tulis($"fisika/2026-09-{i:00}-bab.md", $"# Bab {i}\n");

        var html = await Html("kevin://belajar");

        Assert.Equal(5, Regex.Count(html, "<span>Bab \\d</span>"));
        Assert.Contains("<a href=\"kevin://belajar?m=fisika\">Semua 7 catatan</a>", html);
        Assert.Equal(7, Regex.Count(await Html("kevin://belajar?m=fisika"), "<span>Bab \\d</span>"));
    }

    [Fact]
    public async Task SuntingDanSimpan()
    {
        Tulis("fisika/newton.md", "# Newton\n\nversi 1 </textarea>\n");

        var form = await Html("kevin://belajar?m=fisika&c=newton&sunting");
        Assert.Contains("versi 1 &lt;/textarea&gt;\n</textarea>", form);
        Assert.Contains("method=\"post\"", form);
        var token = Ambil(Token(), form);
        var sidik = Ambil(Sidik(), form);

        var isiBaru = "# Newton\r\n\r\nversi 2: F = m * a & lainnya";
        var hasil = await Html("kevin://belajar?m=fisika&c=newton", Post(("aksi", "simpan"), ("token", token), ("sidik", sidik), ("isi", isiBaru)));
        Assert.Equal("kevin://belajar?m=fisika&c=newton&disimpan", Ambil(Pindah(), hasil));
        Assert.Equal("# Newton\n\nversi 2: F = m * a & lainnya\n", File.ReadAllText(Path.Combine(Folder, "fisika", "newton.md")));

        var lihat = await Html("kevin://belajar?m=fisika&c=newton&disimpan");
        Assert.Contains("Catatan disimpan.", lihat);
        Assert.Contains("versi 2: F = m * a &amp; lainnya", lihat);

        // Formulir yang sama dikirim lagi (muat ulang, tab dipulihkan): tidak
        // disimpan, dan teksnya tetap ada di formulir.
        var ulang = await Html("kevin://belajar?m=fisika&c=newton", Post(("aksi", "simpan"), ("token", token), ("sidik", sidik), ("isi", "versi lama")));
        Assert.Contains("sudah dipakai atau kedaluwarsa", ulang);
        Assert.Contains(">\nversi lama</textarea>", ulang);
        Assert.Contains("versi 2", File.ReadAllText(Path.Combine(Folder, "fisika", "newton.md")));
    }

    [Fact]
    public async Task TidakMenimpaBerkasYangBerubahDiDisk()
    {
        Tulis("fisika/newton.md", "# Newton\n\nversi 1\n");
        var form = await Html("kevin://belajar?m=fisika&c=newton&sunting");
        File.WriteAllText(Path.Combine(Folder, "fisika", "newton.md"), "# Newton\n\nversi dari aplikasi lain\n");

        var hasil = await Html("kevin://belajar?m=fisika&c=newton",
            Post(("aksi", "simpan"), ("token", Ambil(Token(), form)), ("sidik", Ambil(Sidik(), form)), ("isi", "versi saya")));

        Assert.Contains("Berkas ini berubah sejak dibuka", hasil);
        Assert.Contains(">\nversi saya</textarea>", hasil);
        Assert.NotEqual(Ambil(Sidik(), form), Ambil(Sidik(), hasil));
        Assert.Contains("aplikasi lain", File.ReadAllText(Path.Combine(Folder, "fisika", "newton.md")));

        // Simpan sekali lagi dari formulir itu: sekarang memang menimpa.
        await Html("kevin://belajar?m=fisika&c=newton",
            Post(("aksi", "simpan"), ("token", Ambil(Token(), hasil)), ("sidik", Ambil(Sidik(), hasil)), ("isi", "versi saya")));
        Assert.Equal("versi saya\n", File.ReadAllText(Path.Combine(Folder, "fisika", "newton.md")));
    }

    [Fact]
    public async Task TulisCatatanBaru()
    {
        var form = await Html("kevin://belajar?baru");
        Assert.DoesNotContain("<select", form);   // belum ada mata pelajaran untuk dipilih
        Assert.Contains("name=\"mapel-baru\"", form);

        var hasil = await Html("kevin://belajar?baru",
            Post(("aksi", "simpan"), ("token", Ambil(Token(), form)), ("mapel-baru", "Bahasa Inggris"), ("judul", "Simple Past"), ("isi", "S + V2")));

        // Jam palsu: 1 Oktober 2026.
        Assert.Equal("kevin://belajar?m=bahasa-inggris&c=2026-10-simple-past&dibuat", Ambil(Pindah(), hasil));
        Assert.Equal("# Simple Past\n\nS + V2\n", File.ReadAllText(Path.Combine(Folder, "bahasa-inggris", "2026-10-simple-past.md")));
        Assert.Contains("Catatan baru disimpan.", await Html("kevin://belajar?m=bahasa-inggris&c=2026-10-simple-past&dibuat"));

        // Sekarang mata pelajarannya bisa dipilih, dan terpilih kalau datang dari daftarnya.
        var lagi = await Html("kevin://belajar?baru&m=bahasa-inggris");
        Assert.Contains("<option value=\"bahasa-inggris\" selected>Bahasa Inggris</option>", lagi);
    }

    [Fact]
    public async Task IsianKurangTidakMenghilangkanKetikan()
    {
        var form = await Html("kevin://belajar?baru");
        var hasil = await Html("kevin://belajar?baru",
            Post(("aksi", "simpan"), ("token", Ambil(Token(), form)), ("mapel-baru", "Fisika"), ("judul", " "), ("isi", "isi penting")));

        Assert.Contains("Judulnya belum diisi.", hasil);
        Assert.Contains("value=\"Fisika\"", hasil);
        Assert.Contains(">\nisi penting</textarea>", hasil);
        Assert.False(Directory.Exists(Folder));
    }

    [Fact]
    public async Task CariMenyorotKata()
    {
        Tulis("fisika/2026-09-parabola.md", "# Gerak Parabola\n\nRumus jangkauan maksimum.\n");

        var html = await Html("kevin://belajar?cari=JANGKAUAN");
        Assert.Contains("value=\"JANGKAUAN\"", html);
        Assert.Contains("<mark>jangkauan</mark>", html);
        Assert.Contains("href=\"kevin://belajar?m=fisika&amp;c=2026-09-parabola\"", html);

        var kosong = await Html("kevin://belajar?cari=%3Cb%3Ekimia");
        Assert.Contains("Tidak ada catatan yang memuat “&lt;b&gt;kimia”.", kosong);
        Assert.DoesNotContain("<b>kimia", kosong);
    }

    [Theory]
    [InlineData("kevin://belajar?m=..&c=passwd")]
    [InlineData("kevin://belajar?c=..%2F..%2Fetc%2Fpasswd")]
    [InlineData("kevin://belajar?m=fisika&c=.git")]
    [InlineData("kevin://belajar?m=fisika&c=tidak-ada")]
    public async Task AlamatAnehTidakDitemukan(string uri)
    {
        Tulis("fisika/ada.md", "# Ada\n");
        Assert.Contains("Catatan tidak ditemukan", await Html(uri));
    }

    [Fact]
    public async Task SimpanKeNamaAnehDitolak()
    {
        var hasil = await Html("kevin://belajar?m=..&c=jahat", Post(("aksi", "simpan"), ("token", "X"), ("sidik", ""), ("isi", "jahat")));
        Assert.Contains("Catatan tidak ditemukan", hasil);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(Folder)!, "jahat.md")));
    }

    [Fact]
    public async Task BukaFolderSekaliPerToken()
    {
        var daftar = await Html("kevin://belajar");
        var buka = Ambil(TautanBuka(), daftar);
        Assert.StartsWith("kevin://belajar?buka&token=", buka);

        Assert.Contains("Folder catatan dibuka di pengelola berkas.", await Html(buka));
        await Html(buka);   // muat ulang: tidak membuka lagi
        Assert.Equal([Folder], layanan.FolderDibuka);
        Assert.True(Directory.Exists(Folder));
    }

    [Fact]
    public async Task DokumenSumber()
    {
        Assert.Contains("Belum ada.", await Html("kevin://belajar?sumber"));

        Tulis("sumber.md", "# Dokumen yang sudah diserap\n\n| berkas | ukuran |\n|---|---|\n| handout.pdf | 2269 |\n");
        var html = await Html("kevin://belajar?sumber");
        Assert.Contains("<h1>Dokumen sumber</h1>", html);
        Assert.Contains("<td>handout.pdf</td><td>2269</td>", html);
        Assert.DoesNotContain("Dokumen yang sudah diserap", html);   // judul berkasnya diganti judul halaman
    }

    [Fact]
    public async Task BahasaInggris()
    {
        layanan.Preferensi.Bahasa = Bahasa.Inggris;
        Tulis("fisika/2026-09-parabola.md", "# Gerak Parabola\n");

        var html = await Html("kevin://belajar");
        Assert.Contains("<h1>Learn</h1>", html);
        Assert.Contains("<span class=\"jumlah\">1 note</span>", html);
        Assert.Contains("+ Write a note</a>", html);
        Assert.Contains("Edit</a>", await Html("kevin://belajar?m=fisika&c=2026-09-parabola"));
    }
}
