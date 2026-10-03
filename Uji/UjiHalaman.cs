using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using KevinBrowser;

namespace Uji;

[Collection(Koleksi.Halaman)]
public sealed partial class UjiHalaman : IDisposable
{
    static readonly string[] Menu =
    [
        "kevin://beranda", "kevin://belajar", "kevin://kisah", "kevin://kelebihan", "kevin://panduan",
        "kevin://rencana", "kevin://tentang", "kevin://pengaturan",
    ];

    public static TheoryData<string> HalamanMenu => new(Menu);

    readonly LayananPalsu layanan = new();

    public void Dispose() => layanan.Dispose();

    async Task<string> Html(string uri)
    {
        var (isi, jenis) = await HalamanBawaan.Ambil(uri, layanan);
        Assert.Equal("text/html", jenis);
        return Encoding.UTF8.GetString(isi);
    }

    [Theory]
    [MemberData(nameof(HalamanMenu))]
    public async Task HalamanMenuTerpasangDiKerangka(string uri)
    {
        var html = await Html(uri);
        Assert.Contains($"<a href=\"{uri}\" aria-current=\"page\">", html);
        Assert.Contains("<link rel=\"stylesheet\" href=\"kevin://gaya.css\">", html);
        Assert.DoesNotContain("{{", html);
        Assert.DoesNotContain("Halaman tidak ditemukan", html);
    }

    [Fact]
    public async Task JudulTab()
    {
        Assert.Contains("<title>Kevin Browser</title>", await Html("kevin://beranda"));
        Assert.Contains("<title>Kisah · Kevin Browser</title>", await Html("kevin://kisah"));
        Assert.Contains("<title>Riwayat · Kevin Browser</title>", await Html("kevin://riwayat"));
    }

    [Fact]
    public async Task RiwayatMenyorotMenuPengaturan() =>
        Assert.Contains("<a href=\"kevin://pengaturan\" aria-current=\"page\">", await Html("kevin://riwayat"));

    [Fact]
    public async Task TanpaLayananPengaturanTidakAda()
    {
        var (isi, _) = await HalamanBawaan.Ambil("kevin://pengaturan");
        Assert.Contains("Halaman tidak ditemukan", Encoding.UTF8.GetString(isi));
    }

    [Theory]
    [InlineData("kevin://Kisah/")]
    [InlineData("kevin://kisah?x=1#atas")]
    [InlineData("kevin:///kisah")]
    public async Task VariasiAlamatTetapKeHalamanYangSama(string uri) =>
        Assert.Contains("<a href=\"kevin://kisah\" aria-current=\"page\">", await Html(uri));

    [Fact]
    public async Task BerkasGayaDisajikanApaAdanya()
    {
        var (isi, jenis) = await HalamanBawaan.Ambil("kevin://gaya.css");
        Assert.Equal("text/css", jenis);
        Assert.Contains("--petak-gelap", Encoding.UTF8.GetString(isi));
    }

    [Theory]
    [InlineData("kevin://tidak-ada")]
    [InlineData("kevin://kerangka.html")]     // kerangka mentah tidak boleh tersaji
    [InlineData("kevin://beranda.html")]
    [InlineData("kevin://../../etc/passwd")]
    public async Task SelainItuHalamanTidakDitemukan(string uri) =>
        Assert.Contains("Halaman tidak ditemukan", await Html(uri));

    [Fact]
    public async Task AlamatDiHalamanTidakDitemukanDiEscape()
    {
        var html = await Html("kevin://<script>alert(1)</script>");
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>", html);
    }

    [GeneratedRegex("""(?:href|src)="(kevin://[^"]+)""")]
    private static partial Regex TautanKevin();

    [Fact]
    public async Task TidakAdaTautanKevinYangPatah()
    {
        var tautan = new HashSet<string>();
        foreach (var uri in Menu.Append("kevin://riwayat"))
            foreach (Match m in TautanKevin().Matches(await Html(uri)))
                tautan.Add(WebUtility.HtmlDecode(m.Groups[1].Value));

        Assert.NotEmpty(tautan);
        foreach (var uri in tautan)
        {
            var (isi, _) = await HalamanBawaan.Ambil(uri, layanan);
            Assert.DoesNotContain("Halaman tidak ditemukan", Encoding.UTF8.GetString(isi));
        }
    }

    // Isi satu elemen, mis. "header" atau "footer".
    static string Bagian(string html, string elemen)
    {
        var awal = html.IndexOf("<" + elemen, StringComparison.Ordinal);
        var akhir = html.IndexOf("</" + elemen + ">", awal, StringComparison.Ordinal);
        return html[awal..akhir];
    }

    static List<string> Tautan(string html) =>
        TautanKevin().Matches(html).Select(m => m.Groups[1].Value).ToList();

    [Fact]
    public async Task MenuAtasBerandaBelajarSegeraHadirDanPengaturan()
    {
        var kepala = Bagian(await Html("kevin://kisah"), "header");
        // Yang pertama tautan merek (logo).
        Assert.Equal(["kevin://beranda", "kevin://beranda", "kevin://belajar", "kevin://rencana", "kevin://pengaturan"], Tautan(kepala));
        Assert.Contains("<a href=\"kevin://belajar\">Belajar</a>", kepala);
        Assert.Contains("<a href=\"kevin://rencana\" class=\"segera\">Segera hadir</a>", kepala);
    }

    [Fact]
    public async Task HalamanTentangBrowserDiKaki()
    {
        var kaki = Bagian(await Html("kevin://beranda"), "footer");
        Assert.Equal(["kevin://kisah", "kevin://kelebihan", "kevin://panduan", "kevin://rencana", "kevin://tentang"], Tautan(kaki));
        Assert.Contains("<a href=\"kevin://kisah\">Kisah</a>", kaki);
    }

    [Fact]
    public async Task HalamanAktifDisorotDiKepalaDanKaki()
    {
        var html = await Html("kevin://rencana");
        Assert.Contains("<a href=\"kevin://rencana\" class=\"segera\" aria-current=\"page\">Segera hadir</a>", Bagian(html, "header"));
        Assert.Contains("<a href=\"kevin://rencana\" aria-current=\"page\">Rencana</a>", Bagian(html, "footer"));
    }

    // Kata-kata yang tidak mungkin ada di halaman berbahasa Inggris. Alamat
    // kevin:// ditulis huruf kecil, jadi tidak ikut tertangkap.
    internal static readonly string[] KataIndonesia =
        [" yang ", " dan ", " untuk ", " tidak ", " dengan ", " atau ", " di ", "Hapus", "Simpan", "Beranda",
         "Pengaturan", "Riwayat", "halaman", "Halaman", "Kembali", "Tekan ", "Cari "];

    public static TheoryData<string> SemuaHalaman => new([.. Menu, "kevin://riwayat", "kevin://bookmark", "kevin://tidak-ada",
        "kevin://belajar?baru", "kevin://belajar?cari=lichess", "kevin://belajar?cari=", "kevin://belajar?sumber",
        "kevin://belajar?c=tidak-ada", "kevin://belajar?serap", "kevin://belajar?serap&tempel", "kevin://belajar?ai",
        "kevin://belajar?serap&kerja=ABC", "kevin://belajar?tanya", "kevin://belajar?tanya&obrolan=ABC"]);

    [Theory]
    [MemberData(nameof(SemuaHalaman))]
    public async Task HalamanBahasaInggrisLengkap(string uri)
    {
        layanan.Preferensi.Bahasa = Bahasa.Inggris;
        layanan.Riwayat.Catat("https://lichess.org/", "Lichess", LayananPalsu.Sekarang);
        layanan.Bookmark.Tambah("https://lichess.org/", "Lichess", LayananPalsu.Sekarang);

        var html = await Html(uri);

        Assert.Contains("<html lang=\"en\">", html);
        Assert.Contains(">Home</a>", html);
        Assert.Contains(">Settings</a>", html);
        Assert.Contains(">Coming soon</a>", html);
        Assert.Contains(">Learn</a>", html);
        Assert.Contains($"Kevin Browser version {HalamanBawaan.Versi} ·", html);
        Assert.DoesNotContain("{{", html);
        foreach (var kata in KataIndonesia)
            Assert.False(html.Contains(kata, StringComparison.Ordinal), $"\"{kata}\" di {uri}");
    }

    [Fact]
    public async Task HalamanBahasaIndonesiaTetapBawaan()
    {
        var html = await Html("kevin://panduan");
        Assert.Contains("<html lang=\"id\">", html);
        Assert.Contains("<h1>Panduan</h1>", html);
    }

    [Theory]
    [MemberData(nameof(HalamanMenu))]
    public async Task KakiMenampilkanVersi(string uri)
    {
        Assert.Matches(@"^\d+\.\d+\.\d+$", HalamanBawaan.Versi);
        var html = await Html(uri);
        Assert.Contains($"Kevin Browser versi {HalamanBawaan.Versi} ·", html);
        Assert.DoesNotContain("{{", html);
    }
}
