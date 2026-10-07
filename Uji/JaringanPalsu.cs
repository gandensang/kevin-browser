using System.Text.Json;
using KevinBrowser.Asisten;

namespace Uji;

/// <summary>IJaringan untuk tes: mencatat permintaan, menjawab seperti DeepSeek.</summary>
sealed class JaringanPalsu : IJaringan
{
    public const string CatatanBawaan = """
        {"catatan":[{"nama":"gerak-parabola","isi":"# Gerak Parabola\n\nDua gerak yang saling bebas. Lihat [[syarat-pakai]].\n\nDilewati: sejarah Galileo."},{"nama":"syarat-pakai","isi":"# Syarat pakai rumus\n\nHanya kalau titik jatuh sama tinggi dengan titik lempar.\n\nDilewati: tidak ada."}]}
        """;

    public const string Saldo = """
        {"is_available":true,"balance_infos":[{"currency":"CNY","total_balance":"35.00","granted_balance":"0.00","topped_up_balance":"35.00"},{"currency":"USD","total_balance":"4.87","granted_balance":"0.00","topped_up_balance":"4.87"}]}
        """;

    public List<PermintaanHttp> Permintaan { get; } = [];

    /// <summary>Jawaban buatan; null = jawaban bawaan (saldo atau catatan di atas).</summary>
    public Func<PermintaanHttp, (int Status, IEnumerable<string> Baris)>? Jawab { get; set; }

    /// <summary>Kalau diisi, Kirim menunggu ini (atau pembatalan) dulu.</summary>
    public TaskCompletionSource? Tahan { get; set; }

    public async Task<int> Kirim(PermintaanHttp permintaan, Action<string> perBaris, CancellationToken batal)
    {
        await Task.Yield();
        lock (Permintaan)
            Permintaan.Add(permintaan);
        if (Tahan is { } tahan)
            await tahan.Task.WaitAsync(batal);
        var (status, baris) = Jawab?.Invoke(permintaan)
            ?? (permintaan.Alamat.EndsWith("/user/balance", StringComparison.Ordinal) ? (200, [Saldo]) : (200, Aliran(CatatanBawaan)));
        foreach (var b in baris)
        {
            batal.ThrowIfCancellationRequested();
            perBaris(b);
        }
        return status;
    }

    /// <summary>Jawaban unduhan biner: alamat → (status, isi). Tanpa ini: 404.</summary>
    public Func<PermintaanHttp, (int Status, byte[] Isi)>? JawabUnduh { get; set; }

    public async Task<int> Unduh(PermintaanHttp permintaan, Stream tujuan, long batas, CancellationToken batal)
    {
        await Task.Yield();
        lock (Permintaan)
            Permintaan.Add(permintaan);
        var (status, isi) = JawabUnduh?.Invoke(permintaan) ?? (404, []);
        if (isi.Length > batas)
            throw new GalatAi(0, "berkasnya lebih besar dari yang diharapkan");
        await tujuan.WriteAsync(isi, batal);
        return status;
    }

    /// <summary>Dialirkan seperti DeepSeek: potongan isi, lalu potongan terakhir dengan pemakaian token.</summary>
    public static List<string> Aliran(string isi, string alasan = "stop", int cache = 1200, int baru = 3400, int keluar = 800)
    {
        var baris = new List<string> { ": keep-alive", "" };
        for (var i = 0; i < isi.Length; i += 9)
        {
            var potong = JsonEncodedText.Encode(isi.Substring(i, Math.Min(9, isi.Length - i))).ToString();
            baris.Add($$"""data: {"choices":[{"index":0,"delta":{"content":"{{potong}}"},"finish_reason":null}]}""");
            baris.Add("");
        }
        baris.Add($$$"""data: {"choices":[{"index":0,"delta":{"content":""},"finish_reason":"{{{alasan}}}"}],"usage":{"prompt_tokens":{{{cache + baru}}},"completion_tokens":{{{keluar}}},"prompt_cache_hit_tokens":{{{cache}}},"prompt_cache_miss_tokens":{{{baru}}}}}""");
        baris.Add("");
        baris.Add("data: [DONE]");
        return baris;
    }

    /// <summary>Jawaban tanpa stream seperti DeepSeek: teks, atau permintaan tool (nama, argumen JSON).</summary>
    public static (int, IEnumerable<string>) Tool(string? isi, params (string Nama, string Argumen)[] panggil) =>
        Tool(isi, panggil, panggil.Length > 0 ? "tool_calls" : "stop");

    public static (int, IEnumerable<string>) Tool(string? isi, (string Nama, string Argumen)[] panggil, string alasan,
        int cache = 900, int baru = 300, int keluar = 50)
    {
        using var aliran = new MemoryStream();
        using (var json = new Utf8JsonWriter(aliran))
        {
            json.WriteStartObject();
            json.WriteString("object", "chat.completion");
            json.WriteStartArray("choices");
            json.WriteStartObject();
            json.WriteNumber("index", 0);
            json.WriteStartObject("message");
            json.WriteString("role", "assistant");
            if (isi is null)
                json.WriteNull("content");
            else
                json.WriteString("content", isi);
            if (panggil.Length > 0)
            {
                json.WriteStartArray("tool_calls");
                for (var i = 0; i < panggil.Length; i++)
                {
                    json.WriteStartObject();
                    json.WriteString("id", $"call_{i}_{panggil[i].Nama}");
                    json.WriteString("type", "function");
                    json.WriteStartObject("function");
                    json.WriteString("name", panggil[i].Nama);
                    json.WriteString("arguments", panggil[i].Argumen);
                    json.WriteEndObject();
                    json.WriteEndObject();
                }
                json.WriteEndArray();
            }
            json.WriteEndObject();
            json.WriteString("finish_reason", alasan);
            json.WriteEndObject();
            json.WriteEndArray();
            json.WriteStartObject("usage");
            json.WriteNumber("prompt_tokens", cache + baru);
            json.WriteNumber("completion_tokens", keluar);
            json.WriteNumber("prompt_cache_hit_tokens", cache);
            json.WriteNumber("prompt_cache_miss_tokens", baru);
            json.WriteEndObject();
            json.WriteEndObject();
        }
        return (200, [System.Text.Encoding.UTF8.GetString(aliran.ToArray())]);
    }

    public static (int, IEnumerable<string>) Galat(int status, string pesan) =>
        (status, [$$$"""{"error":{"message":"{{{pesan}}}","type":"invalid_request_error","code":null}}"""]);
}
