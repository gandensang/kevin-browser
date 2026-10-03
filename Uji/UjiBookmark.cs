using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using KevinBrowser;

namespace Uji;

[Collection(Koleksi.Halaman)]
public sealed partial class UjiBookmark : IDisposable
{
    static readonly DateTimeOffset T0 = LayananPalsu.Sekarang;

    readonly LayananPalsu layanan = new();

    public void Dispose() => layanan.Dispose();

    DaftarBookmark Daftar => layanan.Bookmark;

    async Task<string> Html(string uri)
    {
        var (isi, _) = await HalamanBawaan.Ambil(uri, layanan);
        return Encoding.UTF8.GetString(isi);
    }

    [GeneratedRegex("name=\"token\" value=\"([0-9A-F]+)\"")]
    private static partial Regex Token();

    [GeneratedRegex("href=\"(kevin://bookmark\\?hapus=[^\"]+)\"")]
    private static partial Regex TautanHapus();

    [Fact]
    public void UrutSesuaiWaktuDisimpan()
    {
        Assert.True(Daftar.Tambah("https://classroom.google.com/", "Google Classroom", T0));
        Assert.True(Daftar.Tambah("https://lichess.org/", "Lichess", T0.AddMinutes(1)));
        Assert.Equal(["Google Classroom", "Lichess"], Daftar.Semua().Select(b => b.Nama));
        Assert.True(Daftar.Ada("https://lichess.org/"));
    }

    [Theory]
    [InlineData("kevin://beranda")]
    [InlineData("file:///home/pc/tugas.pdf")]
    [InlineData("about:blank")]
    public void HanyaHalamanWeb(string uri) =>
        Assert.False(Daftar.Tambah(uri, "x", T0));

    [Fact]
    public void TidakDobel()
    {
        Assert.True(Daftar.Tambah("https://lichess.org/", "Lichess", T0));
        Assert.False(Daftar.Tambah("https://lichess.org/", "Lichess lagi", T0.AddMinutes(1)));
        Assert.Single(Daftar.Semua());
    }

    [Fact]
    public void IdTidakBentrokWalauWaktunyaSama()
    {
        Daftar.Tambah("https://a.id/", "A", T0);
        Daftar.Tambah("https://b.id/", "B", T0);
        Assert.Equal(2, Daftar.Semua().Select(b => b.Id).Distinct().Count());
    }

    [Fact]
    public void HapusDanGantiNama()
    {
        Daftar.Tambah("https://a.id/", "A", T0);
        Daftar.Tambah("https://b.id/", "B", T0.AddSeconds(1));
        Daftar.Tambah("https://c.id/", "C", T0.AddSeconds(2));

        Daftar.HapusSatu(Daftar.Semua()[0].Id);
        Daftar.Hapus("https://c.id/");
        Daftar.GantiNama(Daftar.Semua()[0].Id, "  Sekolah\tku  ");
        Assert.Equal(["Sekolah ku"], Daftar.Semua().Select(b => b.Nama));

        // Nama kosong: kembali memakai host.
        Daftar.GantiNama(Daftar.Semua()[0].Id, "");
        Assert.Equal("b.id", Daftar.Semua()[0].Nama);
    }

    [Fact]
    public void TersimpanDiBerkas()
    {
        Daftar.Tambah("https://lichess.org/", "Main\ncatur", T0);
        var dibacaUlang = new DaftarBookmark(Daftar.Berkas).Semua();
        Assert.Equal("Main catur", Assert.Single(dibacaUlang).Judul);
        Assert.Equal(T0, dibacaUlang[0].Waktu);
    }

    [Fact]
    public async Task BerandaMenampilkanPintasan()
    {
        Daftar.Tambah("https://lichess.org/", "<b>Catur</b>", T0);
        var html = await Html("kevin://beranda");
        Assert.Contains("href=\"https://lichess.org/\"", html);
        Assert.Contains("&lt;b&gt;Catur&lt;/b&gt;", html);
        Assert.DoesNotContain("<b>Catur</b>", html);
        Assert.Contains("Kelola bookmark", html);
    }

    [Fact]
    public async Task BerandaTanpaBookmarkMemberiPetunjuk() =>
        Assert.Contains("<kbd>Ctrl</kbd>+<kbd>D</kbd>", await Html("kevin://beranda"));

    [Fact]
    public async Task HapusLewatTautanBertoken()
    {
        Daftar.Tambah("https://a.id/", "A", T0);
        Daftar.Tambah("https://b.id/", "B", T0.AddSeconds(1));
        var tautan = WebUtility.HtmlDecode(TautanHapus().Match(await Html("kevin://bookmark")).Groups[1].Value);

        Assert.Contains("Satu bookmark dihapus.", await Html(tautan));
        Assert.Single(Daftar.Semua());

        // Tokennya sekali pakai: membuka tautan yang sama lagi tidak menghapus apa-apa.
        Assert.DoesNotContain("dihapus", await Html(tautan));
        Assert.Single(Daftar.Semua());
    }

    [Fact]
    public async Task GantiNamaLewatFormulir()
    {
        Daftar.Tambah("https://classroom.google.com/", "Google Classroom", T0);
        var id = Daftar.Semua()[0].Id;
        var token = Token().Match(await Html("kevin://bookmark")).Groups[1].Value;

        var html = await Html($"kevin://bookmark?ganti={id}&token={token}&judul=Kelas+X");

        Assert.Contains("Nama bookmark diubah.", html);
        Assert.Equal("Kelas X", Daftar.Semua()[0].Nama);
    }

    [Fact]
    public async Task TanpaTokenTidakAdaYangBerubah()
    {
        Daftar.Tambah("https://a.id/", "A", T0);
        await Html($"kevin://bookmark?hapus={Daftar.Semua()[0].Id}&token=SALAH");
        Assert.Single(Daftar.Semua());
    }
}
