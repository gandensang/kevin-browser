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

    Task<JawabanTool> ChatTool(IReadOnlyList<PesanAi> pesan, IReadOnlyList<DefinisiTool> alat, bool bolehTool = true) =>
        Klien.ChatTool(Alamat, Kunci, "deepseek-flash", pesan, alat, bolehTool, 2000, CancellationToken.None);

    [Fact]
    public async Task ChatToolMembacaPermintaanTool()
    {
        jaringan.Jawab = _ => JaringanPalsu.Tool("Kucari dulu.", ("cari_catatan", """{"kata":"gaya gesek"}"""), ("baca_catatan", """{"nama":"x"}"""));
        var jawaban = await ChatTool([new("user", "tanya")], []);

        Assert.Equal("Kucari dulu.", jawaban.Isi);
        Assert.Equal([new PanggilTool("call_0_cari_catatan", "cari_catatan", """{"kata":"gaya gesek"}"""),
            new PanggilTool("call_1_baca_catatan", "baca_catatan", """{"nama":"x"}""")], jawaban.Panggil);
        Assert.Equal((900, 300, 50, "tool_calls"), (jawaban.TokenCache, jawaban.TokenBaru, jawaban.TokenKeluar, jawaban.AlasanBerhenti));
    }

    [Fact]
    public async Task IsiPermintaanChatTool()
    {
        jaringan.Jawab = _ => JaringanPalsu.Tool("Jawaban.");
        PesanAi[] pesan = [new("system", "petunjuk"), new("user", "tanya"),
            new("assistant", null, [new PanggilTool("call_1", "cari_catatan", """{"kata":"x"}""")]),
            new("tool", "[]", IdTool: "call_1")];
        DefinisiTool[] alat = [new("cari_catatan", "Cari.", """{"type":"object","properties":{"kata":{"type":"string"}},"required":["kata"]}""")];

        var jawaban = await ChatTool(pesan, alat, bolehTool: false);

        Assert.Equal("Jawaban.", jawaban.Isi);
        Assert.Empty(jawaban.Panggil);
        using var dok = JsonDocument.Parse(Assert.Single(jaringan.Permintaan).Isi!);
        var akar = dok.RootElement;
        Assert.False(akar.GetProperty("stream").GetBoolean());
        Assert.Equal("none", akar.GetProperty("tool_choice").GetString());
        Assert.Equal("disabled", akar.GetProperty("thinking").GetProperty("type").GetString());
        Assert.Equal(2000, akar.GetProperty("max_tokens").GetInt32());
        Assert.False(akar.TryGetProperty("response_format", out _));
        var fungsi = akar.GetProperty("tools")[0].GetProperty("function");
        Assert.Equal(("cari_catatan", "Cari."), (fungsi.GetProperty("name").GetString(), fungsi.GetProperty("description").GetString()));
        Assert.Equal("string", fungsi.GetProperty("parameters").GetProperty("properties").GetProperty("kata").GetProperty("type").GetString());
        var m = akar.GetProperty("messages");
        Assert.Equal(4, m.GetArrayLength());
        Assert.Equal(JsonValueKind.Null, m[2].GetProperty("content").ValueKind);
        var panggil = m[2].GetProperty("tool_calls")[0];
        Assert.Equal(("call_1", "function"), (panggil.GetProperty("id").GetString(), panggil.GetProperty("type").GetString()));
        Assert.Equal(("cari_catatan", """{"kata":"x"}"""),
            (panggil.GetProperty("function").GetProperty("name").GetString(), panggil.GetProperty("function").GetProperty("arguments").GetString()));
        Assert.Equal(("tool", "call_1", "[]"), (m[3].GetProperty("role").GetString(), m[3].GetProperty("tool_call_id").GetString(), m[3].GetProperty("content").GetString()));
        Assert.False(m[1].TryGetProperty("tool_call_id", out _));
    }

    [Fact]
    public async Task ChatToolGalat()
    {
        jaringan.Jawab = _ => JaringanPalsu.Galat(402, "Insufficient Balance");
        Assert.Equal(402, (await Assert.ThrowsAsync<GalatAi>(() => ChatTool([new("user", "x")], []))).Kode);
        jaringan.Jawab = _ => (200, ["""{"choices":[]}"""]);
        Assert.Equal(0, (await Assert.ThrowsAsync<GalatAi>(() => ChatTool([new("user", "x")], []))).Kode);
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
