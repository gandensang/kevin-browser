using KevinBrowser;

namespace Uji;

public class UjiAlamat
{
    [Theory]
    [InlineData("google.com", "https://google.com")]
    [InlineData("www.detik.com/berita", "https://www.detik.com/berita")]
    [InlineData("belajar.kemdikbud.go.id", "https://belajar.kemdikbud.go.id")]
    [InlineData("EXAMPLE.COM", "https://EXAMPLE.COM")]
    [InlineData("contoh.com:8080/a?b=c", "https://contoh.com:8080/a?b=c")]
    [InlineData("  wikipedia.org  ", "https://wikipedia.org")]
    public void DomainTerdaftarDibukaDenganHttps(string masukan, string hasil) =>
        Assert.Equal(hasil, Alamat.Tafsirkan(masukan));

    [Theory]
    [InlineData("localhost:5000", "http://localhost:5000")]
    [InlineData("192.168.1.1", "http://192.168.1.1")]
    [InlineData("[::1]:8080/tes", "http://[::1]:8080/tes")]
    public void ServerLokalDibukaDenganHttp(string masukan, string hasil) =>
        Assert.Equal(hasil, Alamat.Tafsirkan(masukan));

    [Theory]
    [InlineData("https://example.com/a?b=c")]
    [InlineData("file:///home/pc/tugas.html")]
    [InlineData("about:blank")]
    [InlineData("mailto:kevin@example.com")]
    public void AlamatBerskemaTidakDiubah(string masukan) =>
        Assert.Equal(masukan, Alamat.Tafsirkan(masukan));

    [Fact]
    public void PathLokalJadiFile() =>
        Assert.Equal("file:///home/pc/tugas%20saya.html", Alamat.Tafsirkan("/home/pc/tugas saya.html"));

    [Theory]
    [InlineData("cara membuat kue", "cara%20membuat%20kue")]
    [InlineData("node.js", "node.js")]           // .js bukan TLD
    [InlineData("tugas.pdf", "tugas.pdf")]
    [InlineData("1.2", "1.2")]                   // bukan IPv4 utuh
    [InlineData("kevin", "kevin")]
    [InlineData("C#", "C%23")]
    [InlineData("kata:kunci", "kata%3Akunci")]
    [InlineData("apa itu http://", "apa%20itu%20http%3A%2F%2F")]
    [InlineData("javascript:alert(1)", "javascript%3Aalert%281%29")]  // diketik → dicari, tidak dijalankan
    public void SelainAlamatDicariDiGoogle(string masukan, string kataKunci) =>
        Assert.Equal("https://www.google.com/search?q=" + kataKunci, Alamat.Tafsirkan(masukan));

    [Fact]
    public void KosongKeBeranda() =>
        Assert.Equal(Alamat.Beranda, Alamat.Tafsirkan("   "));

    [Theory]
    [InlineData("detik.com", "https://detik.com")]
    [InlineData("cara membuat kue", "https://www.google.com/search?q=cara%20membuat%20kue")]
    [InlineData("https://example.com/a?b=c", "https://example.com/a?b=c")]
    [InlineData("kevin://kisah", "kevin://kisah")]
    [InlineData("tugas.pdf", "tugas.pdf")]         // ada di folder kerja → tetap berkas
    [InlineData("--help", "--help")]               // opsi GApplication
    public void ArgumenBarisPerintah(string argumen, string hasil) =>
        Assert.Equal(hasil, Alamat.DariBarisPerintah(argumen, berkas => berkas == "tugas.pdf"));

    [Theory]
    [InlineData("https://web.whatsapp.com/", true)]
    [InlineData("https://web.whatsapp.com/send?phone=628123", true)]
    [InlineData("http://web.whatsapp.com/", false)]             // tanpa https
    [InlineData("https://web.whatsapp.com:8443/", false)]
    [InlineData("https://whatsapp.com/", false)]
    [InlineData("https://web.whatsapp.com.contoh.id/", false)]
    [InlineData("https://contoh.id/web.whatsapp.com", false)]
    [InlineData("https://meet.google.com/abc-defg-hij", false)]  // tanpa WebRTC, panggilan tidak bisa
    [InlineData("kevin://beranda", false)]
    [InlineData(null, false)]
    public void KameraMikrofonHanyaUntukWhatsApp(string? uri, bool boleh) =>
        Assert.Equal(boleh, IzinMedia.Boleh(uri));

    [Theory]
    [InlineData("https://web.whatsapp.com/", true)]
    [InlineData("http://web.whatsapp.com/", false)]
    [InlineData("https://web.whatsapp.com:8443/", false)]
    [InlineData("https://web.whatsapp.com.contoh.id/", false)]
    [InlineData("https://www.detik.com/", false)]
    [InlineData("kevin://beranda", false)]
    [InlineData(null, false)]
    public void NotifikasiHanyaUntukWhatsApp(string? uri, bool boleh) =>
        Assert.Equal(boleh, IzinNotifikasi.Boleh(uri));

    [Fact]
    public void AsalNotifikasiSamaDenganYangDiizinkan() =>
        Assert.All(IzinNotifikasi.Asal, asal => Assert.True(IzinNotifikasi.Boleh(asal + "/")));

    [Theory]
    [InlineData("https://web.whatsapp.com/", true)]
    [InlineData("https://m.youtube.com/watch?v=abc", false)]   // YouTube juga menunggu diketuk
    [InlineData("https://www.youtube.com/", false)]
    [InlineData("https://www.detik.com/", false)]
    [InlineData("https://web.whatsapp.com.contoh.id/", false)]
    [InlineData("https://contoh.id/web.whatsapp.com", false)]
    [InlineData("kevin://beranda", false)]
    [InlineData(null, false)]
    public void VideoBerputarSendiriHanyaDiWhatsApp(string? uri, bool boleh) =>
        Assert.Equal(boleh, PutarOtomatis.Boleh(uri));
}
