using System.Net;
using System.Net.Sockets;

namespace KevinBrowser;

/// <summary>
/// Menafsirkan isi kotak alamat: yang berupa alamat web dibuka langsung,
/// selain itu dicari di Google.
/// </summary>
public static class Alamat
{
    /// <summary>Halaman bawaan, juga tujuan kalau kotak alamat dikosongkan.</summary>
    public const string Beranda = HalamanBawaan.Beranda;
    const string Pencarian = "https://www.google.com/search?q=";

    // Skema yang ditulis tanpa "//" tapi tetap berarti alamat, bukan kata kunci.
    static readonly string[] SkemaTanpaGaring = ["about:", "data:", "mailto:", "tel:"];

    public static string Tafsirkan(string masukan)
    {
        var teks = masukan.Trim();
        if (teks.Length == 0)
            return Beranda;

        if (AdaSkema(teks))
            return teks;

        // Berkas lokal, mis. /home/pc/tugas.html.
        if (teks.StartsWith('/'))
            return new Uri(teks).AbsoluteUri;

        // Ada spasi berarti kalimat, bukan alamat.
        if (teks.Any(char.IsWhiteSpace))
            return Cari(teks);

        var host = AmbilHost(teks);
        // Server lokal dan halaman router umumnya belum https.
        if (host == "localhost" || ApakahIp(host))
            return "http://" + teks;
        if (DomainTerdaftar(host))
            return "https://" + teks;
        return Cari(teks);
    }

    /// <summary>
    /// Argumen baris perintah, mis. <c>kevin-browser detik.com</c>: berkas
    /// atau folder yang ada dibuka sebagai berkas, sisanya ditafsirkan seperti
    /// kotak alamat. Tanpa ini GApplication menganggap "detik.com" nama berkas
    /// di folder kerja (terjadi: yang dibuka file:///…/detik.com).
    /// </summary>
    public static string DariBarisPerintah(string argumen, Func<string, bool> berkasAda) =>
        argumen.StartsWith('-') || berkasAda(argumen) ? argumen : Tafsirkan(argumen);

    public static string Cari(string kataKunci) =>
        Pencarian + Uri.EscapeDataString(kataKunci);

    // "https://…", "file:///…" — tapi bukan "apa itu http://", dan bukan
    // "javascript:…" yang diketik (itu jadi pencarian, tidak dijalankan).
    static bool AdaSkema(string teks)
    {
        var i = teks.IndexOf("://", StringComparison.Ordinal);
        if (i > 0 && char.IsAsciiLetter(teks[0])
            && teks[..i].All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '-' or '.'))
            return true;
        return SkemaTanpaGaring.Any(s => teks.StartsWith(s, StringComparison.OrdinalIgnoreCase));
    }

    // "contoh.com:8080/a?b" → "contoh.com". Port yang bukan angka membuat
    // hasilnya kosong, supaya "kata:kunci" jatuh ke pencarian.
    static string AmbilHost(string teks)
    {
        var akhir = teks.IndexOfAny(['/', '?', '#']);
        var host = akhir < 0 ? teks : teks[..akhir];

        if (host.StartsWith('['))
        {
            var tutup = host.IndexOf(']');
            return tutup < 0 ? "" : host[..(tutup + 1)];
        }

        var titikDua = host.LastIndexOf(':');
        if (titikDua >= 0)
        {
            var port = host[(titikDua + 1)..];
            if (port.Length == 0 || !port.All(char.IsAsciiDigit))
                return "";
            host = host[..titikDua];
        }
        return host.ToLowerInvariant();
    }

    // IPv4 harus empat bagian utuh: IPAddress.TryParse menerima "1.2" sebagai
    // 1.0.0.2, padahal itu lebih mungkin kata kunci.
    static bool ApakahIp(string host)
    {
        if (host.StartsWith('['))
            return host.Length > 2
                && IPAddress.TryParse(host[1..^1], out var ip6)
                && ip6.AddressFamily == AddressFamily.InterNetworkV6;

        var bagian = host.Split('.');
        return bagian.Length == 4 && bagian.All(b =>
            b.Length is > 0 and <= 3 && b.All(char.IsAsciiDigit) && int.Parse(b) <= 255);
    }

    // Nama domain dianggap alamat hanya kalau akhirannya TLD yang benar-benar
    // terdaftar — "detik.com" dibuka, "node.js" dan "tugas.pdf" dicari.
    static bool DomainTerdaftar(string host)
    {
        var label = host.TrimEnd('.').Split('.');
        if (label.Length < 2 || label.Any(l => l.Length == 0 || l.StartsWith('-') || l.EndsWith('-')
                || !l.All(c => char.IsLetterOrDigit(c) || c == '-')))
            return false;

        var tld = label[^1];
        // TLD non-Latin (.рф, .中国) ada di daftar IANA dalam bentuk punycode;
        // tanpa ICU tidak bisa dikonversi, jadi diloloskan saja.
        if (!tld.All(char.IsAscii))
            return true;
        return DaftarTld.Value.Contains("\n" + tld.ToUpperInvariant() + "\n", StringComparison.Ordinal);
    }

    // Satu string, bukan HashSet: dibaca hanya saat Enter ditekan, dan begini
    // cuma ~20 KB di memori.
    static readonly Lazy<string> DaftarTld = new(() =>
    {
        using var aliran = typeof(Alamat).Assembly.GetManifestResourceStream("KevinBrowser.tld.txt")!;
        using var pembaca = new StreamReader(aliran);
        return "\n" + pembaca.ReadToEnd().Replace("\r", "");
    });
}
