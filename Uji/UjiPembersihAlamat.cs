using KevinBrowser;

namespace Uji;

public class UjiPembersihAlamat
{
    [Theory]
    [InlineData("https://example.com/?utm_source=uji&fbclid=abc&q=1", "https://example.com/?q=1")]
    [InlineData("https://example.com/a?q=a+b&utm_medium=x#bagian", "https://example.com/a?q=a+b#bagian")]
    [InlineData("https://example.com/?gclid=1", "https://example.com/")]
    [InlineData("https://example.com/?q=%20x&utm_source", "https://example.com/?q=%20x")]
    [InlineData("http://contoh.sch.id/berita?id=7&mc_cid=1&mc_eid=2", "http://contoh.sch.id/berita?id=7")]
    [InlineData("https://youtu.be/abc?si=XYZ", "https://youtu.be/abc")]
    [InlineData("https://www.youtube.com/watch?v=abc&si=XYZ", "https://www.youtube.com/watch?v=abc")]
    [InlineData("https://www.instagram.com/p/abc/?igsh=XYZ", "https://www.instagram.com/p/abc/")]
    public void ParameterPelacakDibuang(string kotor, string bersih) =>
        Assert.Equal(bersih, PembersihAlamat.Bersihkan(kotor));

    [Theory]
    [InlineData("https://example.com/?q=1")]
    [InlineData("https://example.com/")]
    [InlineData("https://contoh.com/?si=1")]                     // "si" hanya di YouTube/Spotify
    [InlineData("https://bukanyoutube.com/?si=1")]
    [InlineData("https://example.com/?myutm_source=1")]          // nama harus sama persis
    [InlineData("https://example.com/#/halaman?utm_source=x")]   // "?" sesudah # bukan kueri
    [InlineData("kevin://riwayat?utm_source=1")]
    public void AlamatLainTidakDiubah(string alamat) =>
        Assert.Null(PembersihAlamat.Bersihkan(alamat));

    [Fact]
    public void SkripMemuatDaftarYangSama()
    {
        var nama = PembersihAlamat.Parameter.Concat(PembersihAlamat.PerSitus.SelectMany(s => s.Parameter));
        // Ditulis ke JavaScript tanpa escape.
        Assert.All(nama, p => Assert.Matches("^[A-Za-z0-9_-]+$", p));
        Assert.All(PembersihAlamat.PerSitus, s => Assert.Matches("^[a-z0-9.-]+$", s.Domain));

        Assert.Contains("'fbclid'", PembersihAlamat.Skrip);
        Assert.Contains("host.endsWith('.youtube.com')", PembersihAlamat.Skrip);
        Assert.Contains("history.replaceState", PembersihAlamat.Skrip);
    }
}
