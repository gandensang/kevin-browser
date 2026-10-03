using System.Text.Json;
using KevinBrowser;

namespace Uji;

public class UjiKonverter
{
    static (List<JsonElement> Aturan, HasilKonversi Hasil) Ubah(params string[] baris)
    {
        using var aliran = new MemoryStream();
        HasilKonversi hasil;
        using (var json = new Utf8JsonWriter(aliran))
        {
            json.WriteStartArray();
            var konverter = new KonverterAdblock(json);
            konverter.Tambah(baris);
            hasil = konverter.Selesai();
            json.WriteEndArray();
        }
        var dok = JsonDocument.Parse(aliran.ToArray());
        return ([.. dok.RootElement.EnumerateArray()], hasil);
    }

    static string Filter(JsonElement aturan) => aturan.GetProperty("trigger").GetProperty("url-filter").GetString()!;
    static string Aksi(JsonElement aturan) => aturan.GetProperty("action").GetProperty("type").GetString()!;
    static string[] Larik(JsonElement aturan, string nama) =>
        aturan.GetProperty("trigger").TryGetProperty(nama, out var x) ? [.. x.EnumerateArray().Select(e => e.GetString()!)] : [];

    [Fact]
    public void DomainBerjangkar()
    {
        var aturan = Assert.Single(Ubah("||iklan.example.com^").Aturan);
        Assert.Equal(@"^[a-z][a-z0-9.+-]*://([^/:]+\.)?iklan\.example\.com[/:]", Filter(aturan));
        Assert.Equal("block", Aksi(aturan));
        // Seperti Adblock Plus: halaman utama dan popup tidak ikut diblokir.
        Assert.DoesNotContain("top-document", Larik(aturan, "resource-type"));
        Assert.DoesNotContain("popup", Larik(aturan, "resource-type"));
        Assert.Contains("child-document", Larik(aturan, "resource-type"));
    }

    [Theory]
    [InlineData("/banner/*/img^", @"/banner/.*/img([^a-zA-Z0-9_.%-].*)?$")]
    [InlineData("|https://x.com/a.js|", @"^https://x\.com/a\.js$")]
    [InlineData("-iklan-300x250.", @"-iklan-300x250\.")]
    [InlineData("/ads?id=(1)[2]{3}+", @"/ads\?id=\(1\)\[2\]\{3\}\+")]
    [InlineData("||t.co^*/pixel", @"^[a-z][a-z0-9.+-]*://([^/:]+\.)?t\.co[/:].*/pixel")]
    public void PolaJadiRegexWebKit(string pola, string regex) =>
        Assert.Equal(regex, Filter(Assert.Single(Ubah(pola).Aturan)));

    [Fact]
    public void OpsiPihakKetigaDanJenis()
    {
        var aturan = Assert.Single(Ubah("||pelacak.id^$third-party,script,xmlhttprequest").Aturan);
        Assert.Equal(["third-party"], Larik(aturan, "load-type"));
        Assert.Equal(["script", "raw", "fetch"], Larik(aturan, "resource-type"));
    }

    [Fact]
    public void JenisDinegasi()
    {
        var jenis = Larik(Assert.Single(Ubah("||cdn.id^$~image,~stylesheet").Aturan), "resource-type");
        Assert.DoesNotContain("image", jenis);
        Assert.DoesNotContain("style-sheet", jenis);
        Assert.Contains("script", jenis);
    }

    [Fact]
    public void Domain()
    {
        var aturan = Ubah("/iklan/*$domain=detik.com|kompas.com", "/sponsor/*$domain=~sekolah.sch.id").Aturan;
        Assert.Equal(["*detik.com", "*kompas.com"], Larik(aturan[0], "if-domain"));
        Assert.Equal(["*sekolah.sch.id"], Larik(aturan[1], "unless-domain"));
    }

    [Fact]
    public void PengecualianDitulisPalingAkhir()
    {
        var aturan = Ubah("@@||baik.id^", "||iklan.id^", "||pelacak.id^").Aturan;
        Assert.Equal(["block", "block", "ignore-previous-rules"], aturan.Select(Aksi));
        Assert.Contains(@"baik\.id", Filter(aturan[2]));
    }

    [Fact]
    public void PengecualianSeluruhHalaman()
    {
        var aturan = Assert.Single(Ubah("@@||sekolah.id^$document").Aturan);
        Assert.Equal("ignore-previous-rules", Aksi(aturan));
        Assert.Equal(".*", Filter(aturan));
        Assert.Equal([@"^[a-z][a-z0-9.+-]*://([^/:]+\.)?sekolah\.id[/:]"], Larik(aturan, "if-top-url"));
    }

    [Theory]
    [InlineData("detik.com##.iklan", "sembunyikan elemen")]
    [InlineData("##.banner-iklan", "sembunyikan elemen")]
    [InlineData("example.com#@#.ad", "sembunyikan elemen")]
    [InlineData("example.com##+js(set, x, 1)", "sembunyikan elemen")]
    [InlineData("/banner[0-9]+/", "regex")]
    [InlineData("||ads.com^$csp=script-src 'none'", "opsi csp")]
    [InlineData("||ads.com^$redirect=noopjs", "opsi redirect")]
    [InlineData("@@||example.com^$elemhide", "opsi elemhide")]
    [InlineData("||iklan.id^$domain=example.*", "domain tidak didukung")]
    [InlineData("||iklän.id^", "pola tidak didukung")]
    public void YangTidakBisaDilewati(string baris, string alasan)
    {
        var (aturan, hasil) = Ubah(baris);
        Assert.Empty(aturan);
        Assert.Equal(1, hasil.Dilewati[alasan]);
    }

    [Fact]
    public void KomentarDanKepalaDiabaikan()
    {
        var (aturan, hasil) = Ubah("[Adblock Plus 2.0]", "! Title: EasyList", "", "||iklan.id^");
        Assert.Single(aturan);
        Assert.Empty(hasil.Dilewati);
    }

    [Fact]
    public void DuplikatDibuang()
    {
        var (aturan, hasil) = Ubah("||iklan.id^", "||iklan.id^", "||IKLAN.id^");
        Assert.Equal(2, aturan.Count);     // beda huruf = beda teks; WebKit tetap mencocokkannya sama
        Assert.Equal(1, hasil.Duplikat);
    }
}
