using System.Security.Cryptography;

namespace KevinBrowser;

/// <summary>Bagian <c>?a=b&amp;c=d</c> dari alamat kevin://, seperti dikirim formulir GET.</summary>
sealed class Kueri
{
    readonly List<(string Kunci, string Nilai)> isi = [];

    public Kueri(string uri)
    {
        var tanya = uri.IndexOf('?');
        if (tanya < 0)
            return;
        var pagar = uri.IndexOf('#', tanya);
        var bagian = pagar < 0 ? uri[(tanya + 1)..] : uri[(tanya + 1)..pagar];
        foreach (var pasangan in bagian.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var sama = pasangan.IndexOf('=');
            isi.Add(sama < 0
                ? (Urai(pasangan), "")
                : (Urai(pasangan[..sama]), Urai(pasangan[(sama + 1)..])));
        }
    }

    public string? this[string kunci] => isi.FirstOrDefault(p => p.Kunci == kunci).Nilai;

    public IEnumerable<string> Semua(string kunci) => isi.Where(p => p.Kunci == kunci).Select(p => p.Nilai);

    static string Urai(string teks) => Uri.UnescapeDataString(teks.Replace('+', ' '));
}

/// <summary>
/// Token sekali pakai untuk aksi yang menghapus data. Situs biasa memang
/// tidak bisa membuka halaman kevin:// (skema lokal); token ini lapis kedua.
/// Karena sekali pakai, memuat ulang halaman hasil (F5, mundur/maju) tidak
/// menghapus apa-apa lagi.
/// </summary>
static class TokenSekali
{
    static readonly List<string> aktif = [];

    public static string Buat()
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        lock (aktif)
        {
            aktif.Add(token);
            if (aktif.Count > 32)
                aktif.RemoveAt(0);
        }
        return token;
    }

    public static bool Pakai(string? token)
    {
        if (token is null)
            return false;
        lock (aktif)
            return aktif.Remove(token);
    }
}
