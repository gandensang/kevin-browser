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

    public static (int, IEnumerable<string>) Galat(int status, string pesan) =>
        (status, [$$$"""{"error":{"message":"{{{pesan}}}","type":"invalid_request_error","code":null}}"""]);
}
