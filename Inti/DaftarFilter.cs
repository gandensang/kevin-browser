using System.Text.Json;

namespace KevinBrowser;

/// <summary>Daftar filter yang diunduh untuk pemblokir iklan dan pelacak.</summary>
public static class DaftarFilter
{
    /// <summary>Daftar diunduh lagi kalau sudah lebih tua dari ini.</summary>
    public static readonly TimeSpan UmurMaks = TimeSpan.FromDays(7);

    public static readonly (string Id, string Nama, string NamaInggris, string Url)[] Semua =
    [
        ("easylist", "iklan (EasyList)", "ads (EasyList)", "https://easylist-downloads.adblockplus.org/easylist.txt"),
        ("easyprivacy", "pelacak (EasyPrivacy)", "trackers (EasyPrivacy)", "https://easylist-downloads.adblockplus.org/easyprivacy.txt"),
        ("abpindo", "iklan situs Indonesia (ABPindo)", "Indonesian site ads (ABPindo)", "https://easylist-downloads.adblockplus.org/abpindo.txt"),
    ];
}

/// <summary>Catatan pembaruan terakhir, disimpan di samping filter yang sudah dikompilasi.</summary>
/// <param name="VersiWebKit">
/// WebKit yang mengompilasi filternya ("2.52.6"); null di berkas dari
/// sebelum versi ini dicatat.
/// </param>
public sealed record InfoFilter(DateTimeOffset Diperbarui, Dictionary<string, int> Aturan, string? VersiWebKit = null)
{
    public static InfoFilter? Baca(string berkas)
    {
        try
        {
            using var dok = JsonDocument.Parse(File.ReadAllText(berkas));
            var akar = dok.RootElement;
            return new InfoFilter(
                DateTimeOffset.FromUnixTimeSeconds(akar.GetProperty("diperbarui").GetInt64()),
                akar.GetProperty("aturan").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32()),
                akar.TryGetProperty("webkit", out var webkit) ? webkit.GetString() : null);
        }
        catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    public void Tulis(string berkas)
    {
        using var aliran = File.Create(berkas);
        using var json = new Utf8JsonWriter(aliran);
        json.WriteStartObject();
        json.WriteNumber("diperbarui", Diperbarui.ToUnixTimeSeconds());
        if (VersiWebKit is not null)
            json.WriteString("webkit", VersiWebKit);
        json.WriteStartObject("aturan");
        foreach (var (id, jumlah) in Aturan)
            json.WriteNumber(id, jumlah);
        json.WriteEndObject();
        json.WriteEndObject();
    }
}
