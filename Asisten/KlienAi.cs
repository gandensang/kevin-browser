using System.Globalization;
using System.Text;
using System.Text.Json;

namespace KevinBrowser.Asisten;

/// <summary>Satu permintaan HTTP dari asisten.</summary>
public sealed record PermintaanHttp(string Metode, string Alamat, IReadOnlyList<(string Nama, string Nilai)> Kepala,
    byte[]? Isi = null, string? JenisIsi = null);

/// <summary>
/// Pengirim HTTP milik platform. Di Linux lewat libsoup, pustaka yang memang
/// sudah dimuat WebKit; HttpClient .NET sengaja tidak dipakai karena
/// menambah beberapa MB ke binary.
/// </summary>
public interface IJaringan
{
    /// <summary>
    /// Mengirim <paramref name="permintaan"/> dan memberikan isi jawabannya baris
    /// demi baris ke <paramref name="perBaris"/> selagi masih datang. Hasilnya
    /// kode status HTTP. Galat jaringan dilempar sebagai <see cref="GalatAi"/>
    /// berkode 0. Dipanggil di luar thread utama.
    /// </summary>
    Task<int> Kirim(PermintaanHttp permintaan, Action<string> perBaris, CancellationToken batal);
}

/// <summary>Galat dari layanan AI, dengan kode status HTTP-nya (0 = jaringan atau jawaban rusak).</summary>
public sealed class GalatAi(int kode, string pesan) : Exception(pesan)
{
    public int Kode => kode;
}

/// <summary>Satu jawaban AI: isinya, pemakaian token, dan alasan berhenti ("stop", "length", …).</summary>
public sealed record JawabanAi(string Isi, int TokenCache, int TokenBaru, int TokenKeluar, string? AlasanBerhenti);

public sealed record SaldoAi(bool BisaDipakai, string MataUang, double Jumlah);

/// <summary>
/// Klien API chat bergaya OpenAI, untuk DeepSeek. Jawaban dialirkan
/// (stream), supaya kemajuannya bisa ditampilkan dan sambungan tidak diam
/// lama selagi dokumen panjang diolah.
/// </summary>
public sealed class KlienAi(IJaringan jaringan)
{
    /// <summary>Saldo akun DeepSeek (gratis, tanpa token). USD kalau ada.</summary>
    public async Task<SaldoAi> Saldo(string alamat, string kunci, CancellationToken batal)
    {
        var isi = new StringBuilder();
        var status = await jaringan.Kirim(new PermintaanHttp("GET", alamat + "/user/balance", Kepala(kunci)),
            baris => isi.Append(baris).Append('\n'), batal);
        if (status != 200)
            throw Galat(status, isi.ToString());
        try
        {
            using var dok = JsonDocument.Parse(isi.ToString());
            var akar = dok.RootElement;
            var bisa = akar.TryGetProperty("is_available", out var tersedia) && tersedia.ValueKind == JsonValueKind.True;
            var semua = akar.GetProperty("balance_infos").EnumerateArray().ToList();
            var info = semua.FirstOrDefault(i => i.GetProperty("currency").GetString() == "USD");
            if (info.ValueKind != JsonValueKind.Object)
                info = semua.Count > 0 ? semua[0] : default;
            return info.ValueKind != JsonValueKind.Object
                ? new SaldoAi(bisa, "", 0)
                : new SaldoAi(bisa, info.GetProperty("currency").GetString() ?? "",
                    double.Parse(info.GetProperty("total_balance").GetString() ?? "0", CultureInfo.InvariantCulture));
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new GalatAi(0, "jawaban saldo tidak terbaca");
        }
    }

    /// <summary>
    /// Satu tanya-jawab dengan jawaban JSON (response_format json_object),
    /// tanpa mode berpikir: lebih cepat, dan token berpikirnya tidak ikut
    /// dibayar. <paramref name="kemajuan"/> menerima jumlah huruf jawaban yang
    /// sudah datang.
    /// </summary>
    public async Task<JawabanAi> Chat(string alamat, string kunci, string model, string sistem, string pengguna,
        int maksToken, Action<int>? kemajuan, CancellationToken batal)
    {
        var isi = new StringBuilder();
        var lain = new StringBuilder();
        string? alasan = null;
        int cache = 0, baru = 0, keluar = 0;
        var permintaan = new PermintaanHttp("POST", alamat + "/chat/completions", Kepala(kunci),
            Badan(model, sistem, pengguna, maksToken), "application/json");
        int status;
        try
        {
            status = await jaringan.Kirim(permintaan, baris =>
            {
                if (!baris.StartsWith("data:", StringComparison.Ordinal))
                {
                    if (!baris.StartsWith(':'))   // ": keep-alive"
                        lain.Append(baris).Append('\n');
                    return;
                }
                var data = baris[5..].Trim();
                if (data.Length == 0 || data == "[DONE]")
                    return;
                using var dok = JsonDocument.Parse(data);
                var akar = dok.RootElement;
                if (akar.TryGetProperty("choices", out var pilihan) && pilihan.ValueKind == JsonValueKind.Array && pilihan.GetArrayLength() > 0)
                {
                    var p = pilihan[0];
                    if (p.TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                    {
                        isi.Append(c.GetString());
                        kemajuan?.Invoke(isi.Length);
                    }
                    if (p.TryGetProperty("finish_reason", out var akhir) && akhir.ValueKind == JsonValueKind.String)
                        alasan = akhir.GetString();
                }
                if (akar.TryGetProperty("usage", out var pakai) && pakai.ValueKind == JsonValueKind.Object)
                {
                    cache = Angka(pakai, "prompt_cache_hit_tokens");
                    baru = pakai.TryGetProperty("prompt_cache_miss_tokens", out _)
                        ? Angka(pakai, "prompt_cache_miss_tokens")
                        : Angka(pakai, "prompt_tokens") - cache;
                    keluar = Angka(pakai, "completion_tokens");
                }
            }, batal);
        }
        catch (JsonException)
        {
            throw new GalatAi(0, "jawaban tidak terbaca");
        }
        if (status != 200)
            throw Galat(status, lain.ToString());
        return new JawabanAi(isi.ToString(), cache, baru, keluar, alasan);
    }

    static (string, string)[] Kepala(string kunci) => [("Authorization", "Bearer " + kunci)];

    static byte[] Badan(string model, string sistem, string pengguna, int maksToken)
    {
        using var aliran = new MemoryStream();
        using (var json = new Utf8JsonWriter(aliran))
        {
            json.WriteStartObject();
            json.WriteString("model", model);
            json.WriteStartArray("messages");
            foreach (var (peran, isi) in new[] { ("system", sistem), ("user", pengguna) })
            {
                json.WriteStartObject();
                json.WriteString("role", peran);
                json.WriteString("content", isi);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteBoolean("stream", true);
            json.WriteStartObject("stream_options");
            json.WriteBoolean("include_usage", true);
            json.WriteEndObject();
            json.WriteStartObject("response_format");
            json.WriteString("type", "json_object");
            json.WriteEndObject();
            json.WriteStartObject("thinking");
            json.WriteString("type", "disabled");
            json.WriteEndObject();
            json.WriteNumber("max_tokens", maksToken);
            json.WriteEndObject();
        }
        return aliran.ToArray();
    }

    static int Angka(JsonElement objek, string nama) =>
        objek.TryGetProperty(nama, out var nilai) && nilai.TryGetInt32(out var n) ? n : 0;

    // {"error":{"message":"…"}} kalau ada; kalau tidak, isinya apa adanya.
    static GalatAi Galat(int status, string isi)
    {
        try
        {
            using var dok = JsonDocument.Parse(isi);
            if (dok.RootElement.TryGetProperty("error", out var galat) && galat.TryGetProperty("message", out var pesan)
                && pesan.GetString() is { Length: > 0 } teks)
                return new GalatAi(status, teks);
        }
        catch (JsonException)
        {
        }
        var polos = isi.Trim();
        return new GalatAi(status, polos.Length > 200 ? polos[..200] + "…" : polos);
    }
}

/// <summary>
/// Harga DeepSeek per sejuta token (USD), dari halaman harganya 3 Okt 2026.
/// Jam sibuk 01.00–04.00 dan 06.00–10.00 UTC, Senin–Jumat (08.00–11.00 dan
/// 13.00–17.00 WIB); di luar itu semuanya separuh harga.
/// </summary>
public static class HargaAi
{
    // Token masuk dari cache, token masuk baru, token keluar; jam sibuk.
    // double, bukan decimal: cukup untuk perkiraan, dan aritmetika decimal
    // menambah binary.
    static (double Cache, double Baru, double Keluar) Sibuk(string model) => model switch
    {
        "deepseek-flash" => (0.006, 0.30, 1.20),
        _ => (0.044, 1.32, 3.96),
    };

    public static bool JamSibuk(DateTimeOffset waktu)
    {
        var utc = waktu.UtcDateTime;
        return utc.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && utc.Hour is >= 1 and < 4 or >= 6 and < 10;
    }

    public static double Biaya(string model, int cache, int baru, int keluar, DateTimeOffset waktu)
    {
        var harga = Sibuk(model);
        var biaya = (cache * harga.Cache + baru * harga.Baru + keluar * harga.Keluar) / 1_000_000;
        return JamSibuk(waktu) ? biaya : biaya / 2;
    }

    /// <summary>
    /// Perkiraan kasar sebelum dikirim: satu token ±3 huruf (teks Indonesia
    /// lebih boros token daripada Inggris), catatannya ±1/8 panjang dokumen.
    /// </summary>
    public static (int Masuk, int Keluar) PerkiraanToken(int hurufTeks, int hurufPetunjuk) =>
        ((hurufTeks + hurufPetunjuk) / 3, Math.Clamp(hurufTeks / 24, 1500, Penyerap.MaksTokenKeluar));
}
