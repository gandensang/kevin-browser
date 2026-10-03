using KevinBrowser;

namespace Uji;

public sealed class UjiNamaBerkas : IDisposable
{
    readonly string folder = Directory.CreateTempSubdirectory("kevin-browser-uji-").FullName;

    public void Dispose() => Directory.Delete(folder, true);

    [Fact]
    public void NamaBelumAdaDipakaiApaAdanya() =>
        Assert.Equal("tugas.pdf", NamaBerkas.Unik(folder, "tugas.pdf"));

    [Fact]
    public void NamaSudahAdaDiberiNomor()
    {
        File.WriteAllText(Path.Combine(folder, "tugas.pdf"), "");
        File.WriteAllText(Path.Combine(folder, "tugas (1).pdf"), "");
        Assert.Equal("tugas (2).pdf", NamaBerkas.Unik(folder, "tugas.pdf"));
    }

    [Theory]
    [InlineData("../../rahasia.txt", "rahasia.txt")]
    [InlineData("", "unduhan")]
    public void UsulanDibersihkan(string usulan, string hasil) =>
        Assert.Equal(hasil, NamaBerkas.Unik(folder, usulan));
}

public class UjiSetelan
{
    [Theory]
    [InlineData(1945, 2, 486)]    // laptop "2 GB"
    [InlineData(3800, 3, 950)]    // laptop "4 GB"
    [InlineData(7816, 4, 1024)]
    [InlineData(700, 2, 256)]
    public void BawaanMengikutiRam(long ramMB, int tabHidup, long batasMB)
    {
        Assert.Equal(tabHidup, Setelan.TabHidupBawaan(ramMB));
        Assert.Equal(batasMB, Setelan.BatasMemoriBawaan(ramMB));
    }

    [Theory]
    [InlineData(1945, 291)]   // laptop "2 GB"
    [InlineData(3800, 570)]   // laptop "4 GB"
    public void RamMenipisLimaBelasPersen(long ramMB, long ambangMB) =>
        Assert.Equal(ambangMB, Setelan.RamMenipisBawaan(ramMB));
}

public sealed class UjiInfoFilter : IDisposable
{
    readonly string berkas = Path.Combine(Directory.CreateTempSubdirectory("kevin-browser-uji-").FullName, "info.json");

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(berkas)!, true);

    [Fact]
    public void VersiWebKitIkutTersimpan()
    {
        var waktu = DateTimeOffset.FromUnixTimeSeconds(1790822610);
        new InfoFilter(waktu, new() { ["easylist"] = 52680 }, "2.52.6").Tulis(berkas);
        var info = InfoFilter.Baca(berkas)!;
        Assert.Equal(waktu, info.Diperbarui);
        Assert.Equal(52680, info.Aturan["easylist"]);
        Assert.Equal("2.52.6", info.VersiWebKit);
    }

    [Fact]
    public void BerkasLamaTanpaVersi()
    {
        // Ditulis sebelum versi WebKit dicatat: harus dianggap berbeda versi.
        File.WriteAllText(berkas, """{"diperbarui":1790822610,"aturan":{"easylist":52680}}""");
        var info = InfoFilter.Baca(berkas)!;
        Assert.Null(info.VersiWebKit);
        Assert.Equal(52680, info.Aturan["easylist"]);
    }

    [Fact]
    public void BerkasRusakDianggapTidakAda()
    {
        File.WriteAllText(berkas, """{"diperbarui":1790822610,"webkit":5,"aturan":{}}""");
        Assert.Null(InfoFilter.Baca(berkas));
    }
}
