using KevinBrowser;

namespace Uji;

public sealed class UjiRiwayat : IDisposable
{
    static readonly DateTimeOffset T0 = LayananPalsu.Sekarang;

    readonly string folder = Directory.CreateTempSubdirectory("kevin-browser-uji-").FullName;
    readonly Riwayat riwayat;

    public UjiRiwayat() => riwayat = new Riwayat(Path.Combine(folder, "riwayat.tsv"));

    public void Dispose() => Directory.Delete(folder, true);

    [Fact]
    public void TerbaruLebihDulu()
    {
        riwayat.Catat("https://a.id/", "A", T0.AddMinutes(-2));
        riwayat.Catat("https://b.id/", "B", T0.AddMinutes(-1));
        Assert.Equal(["B", "A"], riwayat.Baca().Select(k => k.Judul));
    }

    [Theory]
    [InlineData("kevin://beranda")]
    [InlineData("about:blank")]
    [InlineData("file:///home/pc/tugas.html")]
    [InlineData("data:text/plain,halo")]
    public void SelainHalamanWebTidakDicatat(string uri)
    {
        riwayat.Catat(uri, "x", T0);
        Assert.Empty(riwayat.Baca());
    }

    [Fact]
    public void TabDanBarisBaruDiJudulDibersihkan()
    {
        riwayat.Catat("https://a.id/", "satu\tdua\r\ntiga", T0);
        Assert.Equal("satu dua  tiga", Assert.Single(riwayat.Baca()).Judul);
    }

    [Fact]
    public void CariDiJudulDanAlamatTanpaBedaHurufBesar()
    {
        riwayat.Catat("https://lichess.org/", "Main catur", T0);
        riwayat.Catat("https://classroom.google.com/", "Kelas", T0.AddSeconds(1));
        riwayat.Catat("https://detik.com/", "Berita", T0.AddSeconds(2));
        Assert.Equal(["Main catur"], riwayat.Baca("CATUR").Select(k => k.Judul));
        Assert.Equal(["Kelas"], riwayat.Baca("classroom").Select(k => k.Judul));
    }

    [Fact]
    public void HapusSejakHanyaYangBaru()
    {
        riwayat.Catat("https://lama.id/", "lama", T0.AddHours(-3));
        riwayat.Catat("https://baru.id/", "baru", T0.AddMinutes(-10));
        riwayat.Hapus(T0.AddHours(-1));
        Assert.Equal(["lama"], riwayat.Baca().Select(k => k.Judul));
    }

    [Fact]
    public void HapusSemuaMembuangBerkas()
    {
        riwayat.Catat("https://a.id/", "A", T0);
        riwayat.Hapus(null);
        Assert.False(File.Exists(riwayat.Berkas));
        Assert.Empty(riwayat.Baca());
    }

    [Fact]
    public void HapusSatu()
    {
        riwayat.Catat("https://a.id/", "A", T0.AddSeconds(-1));
        riwayat.Catat("https://b.id/", "B", T0);
        riwayat.HapusSatu(T0.ToUnixTimeMilliseconds());
        Assert.Equal(["A"], riwayat.Baca().Select(k => k.Judul));
    }

    [Fact]
    public void RapikanMembuangYangLebihDari90Hari()
    {
        riwayat.Catat("https://tua.id/", "tua", T0.AddDays(-91));
        riwayat.Catat("https://muda.id/", "muda", T0.AddDays(-89));
        riwayat.Rapikan(T0);
        Assert.Equal(["muda"], riwayat.Baca().Select(k => k.Judul));
    }

    [Fact]
    public void BarisRusakDilewati()
    {
        riwayat.Catat("https://a.id/", "A", T0);
        File.AppendAllText(riwayat.Berkas, "bukan angka\thttps://x.id/\tX\nbaris tanpa tab\n");
        Assert.Equal(["A"], riwayat.Baca().Select(k => k.Judul));
    }

    [Fact]
    public void BerkasBelumAda() => Assert.Equal(0, riwayat.Jumlah());
}
