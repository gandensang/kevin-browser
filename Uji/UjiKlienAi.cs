using System.Globalization;
using System.Text.Json;
using KevinBrowser.Asisten;

namespace Uji;

public sealed class UjiKlienAi
{
    const string Alamat = "https://api.deepseek.com";
    const string Kunci = "sk-uji-1234567890";

    readonly JaringanPalsu jaringan = new();

    KlienAi Klien => new(jaringan);

    Task<JawabanAi> Chat(string model = "deepseek-v4-pro", Action<int>? kemajuan = null) =>
        Klien.Chat(Alamat, Kunci, model, "petunjuk sistem", "isi dokumen", 16000, kemajuan, CancellationToken.None);

    [Fact]
    public async Task AliranDikumpulkanBesertaPemakaianToken()
    {
        var kemajuan = new List<int>();
        var jawaban = await Chat(kemajuan: kemajuan.Add);

        Assert.Equal(JaringanPalsu.CatatanBawaan, jawaban.Isi);
        Assert.Equal((1200, 3400, 800, "stop"), (jawaban.TokenCache, jawaban.TokenBaru, jawaban.TokenKeluar, jawaban.AlasanBerhenti));
        Assert.True(kemajuan.Count > 5);
        Assert.Equal(JaringanPalsu.CatatanBawaan.Length, kemajuan[^1]);
    }

    [Fact]
    public async Task IsiPermintaanChat()
    {
        await Chat("deepseek-flash");

        var p = Assert.Single(jaringan.Permintaan);
        Assert.Equal(("POST", Alamat + "/chat/completions", "application/json"), (p.Metode, p.Alamat, p.JenisIsi));
        Assert.Contains(("Authorization", "Bearer " + Kunci), p.Kepala);
        using var dok = JsonDocument.Parse(p.Isi!);
        var akar = dok.RootElement;
        Assert.Equal("deepseek-flash", akar.GetProperty("model").GetString());
        Assert.True(akar.GetProperty("stream").GetBoolean());
        Assert.True(akar.GetProperty("stream_options").GetProperty("include_usage").GetBoolean());
        Assert.Equal("json_object", akar.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Equal("disabled", akar.GetProperty("thinking").GetProperty("type").GetString());
        Assert.Equal(16000, akar.GetProperty("max_tokens").GetInt32());
        var pesan = akar.GetProperty("messages");
        Assert.Equal(("system", "petunjuk sistem"), (pesan[0].GetProperty("role").GetString(), pesan[0].GetProperty("content").GetString()));
        Assert.Equal(("user", "isi dokumen"), (pesan[1].GetProperty("role").GetString(), pesan[1].GetProperty("content").GetString()));
    }

    [Theory]
    [InlineData(401)]
    [InlineData(402)]
    [InlineData(503)]
    public async Task GalatDariServer(int status)
    {
        jaringan.Jawab = _ => JaringanPalsu.Galat(status, "Authentication Fails");
        var galat = await Assert.ThrowsAsync<GalatAi>(() => Chat());
        Assert.Equal((status, "Authentication Fails"), (galat.Kode, galat.Message));
    }

    [Fact]
    public async Task AliranRusak()
    {
        jaringan.Jawab = _ => (200, ["data: {rusak"]);
        Assert.Equal(0, (await Assert.ThrowsAsync<GalatAi>(() => Chat())).Kode);
    }

    [Fact]
    public async Task SaldoDalamDolar()
    {
        Assert.Equal(new SaldoAi(true, "USD", 4.87), await Klien.Saldo(Alamat, Kunci, CancellationToken.None));
        var p = Assert.Single(jaringan.Permintaan);
        Assert.Equal(("GET", Alamat + "/user/balance"), (p.Metode, p.Alamat));
    }

    [Fact]
    public async Task SaldoKunciSalah()
    {
        jaringan.Jawab = _ => JaringanPalsu.Galat(401, "Authentication Fails");
        Assert.Equal(401, (await Assert.ThrowsAsync<GalatAi>(() => Klien.Saldo(Alamat, Kunci, CancellationToken.None))).Kode);
    }

    // 5 Oktober 2026 hari Senin, 3 Oktober Sabtu.
    [Theory]
    [InlineData("2026-10-05T02:00:00Z", true)]
    [InlineData("2026-10-05T05:00:00Z", false)]
    [InlineData("2026-10-05T09:59:00Z", true)]
    [InlineData("2026-10-05T10:00:00Z", false)]
    [InlineData("2026-10-03T02:00:00Z", false)]
    public void JamSibuk(string waktu, bool sibuk) =>
        Assert.Equal(sibuk, HargaAi.JamSibuk(DateTimeOffset.Parse(waktu, CultureInfo.InvariantCulture)));

    [Fact]
    public void Biaya()
    {
        var sibuk = DateTimeOffset.Parse("2026-10-05T02:00:00Z", CultureInfo.InvariantCulture);
        var sepi = DateTimeOffset.Parse("2026-10-05T12:00:00Z", CultureInfo.InvariantCulture);
        Assert.Equal(5.324, HargaAi.Biaya("deepseek-v4-pro", 1_000_000, 1_000_000, 1_000_000, sibuk), 9);
        Assert.Equal(2.662, HargaAi.Biaya("deepseek-v4-pro", 1_000_000, 1_000_000, 1_000_000, sepi), 9);
        Assert.Equal(1.506, HargaAi.Biaya("deepseek-flash", 1_000_000, 1_000_000, 1_000_000, sibuk), 9);
    }
}
