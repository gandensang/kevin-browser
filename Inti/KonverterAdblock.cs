using System.Text;
using System.Text.Json;

namespace KevinBrowser;

/// <summary>Ringkasan konversi: jumlah aturan WebKit dan yang dilewati menurut alasannya.</summary>
public sealed class HasilKonversi
{
    public int Aturan { get; internal set; }
    public int Duplikat { get; internal set; }
    public SortedDictionary<string, int> Dilewati { get; } = new(StringComparer.Ordinal);

    internal void Lewati(string alasan) => Dilewati[alasan] = Dilewati.GetValueOrDefault(alasan) + 1;
}

/// <summary>
/// Mengubah daftar filter berformat Adblock (EasyList, EasyPrivacy, ABPindo)
/// menjadi aturan pemblokir konten WebKit, langsung ditulis ke JSON. Hanya
/// aturan jaringan: aturan sembunyikan-elemen (##) dilewati, karena yang
/// berlaku umum dipasang di setiap halaman dan memperberat laptop lama.
/// </summary>
/// <remarks>
/// Batas WebKit (dicoba di 2.52): url-filter tanpa "|", tanpa {n,m}, tanpa
/// backreference, hanya ASCII — dan SATU regex yang tidak sah menggagalkan
/// kompilasi seluruh daftar. Jadi semua karakter literal di-escape, dan
/// aturan yang tidak bisa diterjemahkan dilewati di sini. Maksimal 150.000
/// aturan per filter.
/// </remarks>
public sealed class KonverterAdblock(Utf8JsonWriter json)
{
    public const int AturanMaks = 150_000;

    // Seperti Adblock Plus: aturan tanpa opsi jenis tidak memblokir halaman
    // utama (top-document) dan popup. Mengklik tautan ke domain iklan tetap
    // terbuka; yang diblokir hanya isi halaman.
    static readonly string[] JenisBawaan =
        ["child-document", "image", "style-sheet", "script", "font", "raw", "websocket", "fetch",
         "other", "svg-document", "media", "ping", "csp-report"];

    static readonly Dictionary<string, string[]> JenisOpsi = new(StringComparer.Ordinal)
    {
        ["script"] = ["script"],
        ["image"] = ["image"],
        ["stylesheet"] = ["style-sheet"],
        ["css"] = ["style-sheet"],
        ["xmlhttprequest"] = ["raw", "fetch"],
        ["xhr"] = ["raw", "fetch"],
        ["subdocument"] = ["child-document"],
        ["frame"] = ["child-document"],
        ["media"] = ["media"],
        ["font"] = ["font"],
        ["ping"] = ["ping"],
        ["beacon"] = ["ping"],
        ["websocket"] = ["websocket"],
        ["other"] = ["other"],
        ["object"] = ["other"],
        ["object-subrequest"] = ["other"],
        ["popup"] = ["popup"],
    };

    // Opsi yang tidak mengubah arti aturan bagi WebKit.
    static readonly HashSet<string> OpsiAbaikan = new(StringComparer.Ordinal) { "important" };

    readonly List<Pemicu> pengecualian = [];
    readonly HashSet<ulong> sudah = [];

    public HasilKonversi Hasil { get; } = new();

    public void Tambah(IEnumerable<string> daftar)
    {
        foreach (var baris in daftar)
            Ubah(baris.Trim());
    }

    /// <summary>Menulis pengecualian (harus sesudah semua aturan blokir).</summary>
    public HasilKonversi Selesai()
    {
        foreach (var p in pengecualian)
            Tulis(p, "ignore-previous-rules");
        pengecualian.Clear();
        return Hasil;
    }

    void Ubah(string baris)
    {
        if (baris.Length == 0 || baris[0] == '!' || baris[0] == '[')
            return;
        if (baris.Contains("##") || baris.Contains("#@#") || baris.Contains("#?#") || baris.Contains("#$#")
            || baris.Contains("#%#") || baris.Contains("#@?#") || baris.Contains("#@$#") || baris.Contains("$$"))
        {
            Hasil.Lewati("sembunyikan elemen");
            return;
        }

        var kecuali = baris.StartsWith("@@", StringComparison.Ordinal);
        var aturan = kecuali ? baris[2..] : baris;

        var pola = aturan;
        var opsi = "";
        var dolar = aturan.LastIndexOf('$');
        if (dolar >= 0 && dolar < aturan.Length - 1 && !aturan.EndsWith('/'))
        {
            pola = aturan[..dolar];
            opsi = aturan[(dolar + 1)..];
        }

        var pemicu = new Pemicu();
        string[]? jenis = null;
        var jenisDinegasi = new List<string>();
        var dokumen = false;

        foreach (var mentah in opsi.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var o = mentah.Trim().ToLowerInvariant();
            var nyata = !o.StartsWith('~');
            var nama = nyata ? o : o[1..];

            if (nama is "third-party" or "3p")
                pemicu.JenisMuat = nyata ? "third-party" : "first-party";
            else if (nama is "first-party" or "1p")
                pemicu.JenisMuat = nyata ? "first-party" : "third-party";
            else if (nama == "match-case")
                pemicu.BedaHuruf = true;
            else if (nama.StartsWith("domain=", StringComparison.Ordinal))
            {
                if (!UraiDomain(nama[7..], pemicu))
                {
                    Hasil.Lewati("domain tidak didukung");
                    return;
                }
            }
            else if (nama == "document" && kecuali && nyata)
                dokumen = true;
            else if (JenisOpsi.TryGetValue(nama, out var j))
            {
                if (nyata)
                    jenis = [.. (jenis ?? []), .. j];
                else
                    jenisDinegasi.AddRange(j);
            }
            else if (!OpsiAbaikan.Contains(nama))
            {
                Hasil.Lewati("opsi " + nama.Split('=')[0]);
                return;
            }
        }

        if (KeRegex(pola) is not { } regex)
        {
            Hasil.Lewati(pola.Length > 1 && pola[0] == '/' && pola[^1] == '/' ? "regex" : "pola tidak didukung");
            return;
        }

        if (dokumen)
        {
            // @@||situs^$document: seluruh halaman situs itu dikecualikan.
            pemicu.UrlFilter = ".*";
            pemicu.JikaUrlAtas = regex;
            pengecualian.Add(pemicu);
            return;
        }

        pemicu.UrlFilter = regex;
        pemicu.Jenis = jenis is not null
            ? [.. jenis.Distinct()]
            : jenisDinegasi.Count > 0 ? [.. JenisBawaan.Except(jenisDinegasi)] : JenisBawaan;

        if (kecuali)
            pengecualian.Add(pemicu);
        else
            Tulis(pemicu, "block");
    }

    // a.com|~b.com|c.org → if-domain *a.com, *c.org. WebKit tidak bisa
    // memakai if-domain dan unless-domain sekaligus; kalau ada keduanya,
    // pengecualian domain dibuang (sedikit lebih banyak yang diblokir).
    static bool UraiDomain(string daftar, Pemicu pemicu)
    {
        var jika = new List<string>();
        var kecuali = new List<string>();
        foreach (var d in daftar.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var nyata = !d.StartsWith('~');
            var domain = nyata ? d : d[1..];
            if (domain.Length == 0 || domain.EndsWith(".*", StringComparison.Ordinal) || !domain.All(c => c < 128 && (char.IsAsciiLetterOrDigit(c) || c is '.' or '-')))
                return false;
            (nyata ? jika : kecuali).Add("*" + domain);
        }
        if (jika.Count > 0)
            pemicu.JikaDomain = jika;
        else if (kecuali.Count > 0)
            pemicu.KecualiDomain = kecuali;
        return true;
    }

    /// <summary>Pola Adblock → url-filter WebKit, atau null kalau tidak bisa.</summary>
    public static string? KeRegex(string pola)
    {
        if (pola.Length == 0 || pola == "*")
            return ".*";
        if (pola.Length > 1 && pola[0] == '/' && pola[^1] == '/')
            return null;
        if (pola.Any(c => c >= 128 || char.IsControl(c) || c == ' '))
            return null;

        var sb = new StringBuilder();
        var i = 0;
        var diHost = false;
        if (pola.StartsWith("||", StringComparison.Ordinal))
        {
            sb.Append(@"^[a-z][a-z0-9.+-]*://([^/:]+\.)?");
            i = 2;
            diHost = true;
        }
        else if (pola[0] == '|')
        {
            sb.Append('^');
            i = 1;
        }

        var jangkarAkhir = pola.Length > i && pola[^1] == '|';
        var akhir = jangkarAkhir ? pola.Length - 1 : pola.Length;
        if (i >= akhir)
            return null;

        for (; i < akhir; i++)
        {
            var c = pola[i];
            if (c == '/')
                diHost = false;
            if (c == '*')
                sb.Append(".*");
            else if (c == '^')
            {
                // Pemisah Adblock: karakter apa pun selain huruf, angka, _ - . %,
                // atau ujung alamat. Sesudah nama host, WebKit selalu menulis
                // "/" atau ":", jadi cukup [/:].
                if (diHost)
                    sb.Append("[/:]");
                else if (i == akhir - 1 && !jangkarAkhir)
                    sb.Append("([^a-zA-Z0-9_.%-].*)?$");
                else
                    sb.Append("[^a-zA-Z0-9_.%-]");
                diHost = false;
            }
            else
                sb.Append(Escape(c));
        }
        if (jangkarAkhir)
            sb.Append('$');
        return sb.ToString();
    }

    public static string Escape(string teks) => string.Concat(teks.Select(Escape));

    static string Escape(char c) => c switch
    {
        '\\' or '.' or '+' or '?' or '(' or ')' or '[' or ']' or '{' or '}' or '$' or '|' or '^' or '*' => "\\" + c,
        _ => c.ToString(),
    };

    void Tulis(Pemicu p, string aksi)
    {
        if (Hasil.Aturan >= AturanMaks)
        {
            Hasil.Lewati("melebihi batas WebKit");
            return;
        }
        if (!sudah.Add(Sidik(p, aksi)))
        {
            Hasil.Duplikat++;
            return;
        }

        json.WriteStartObject();
        json.WriteStartObject("trigger");
        json.WriteString("url-filter", p.UrlFilter);
        if (p.BedaHuruf)
            json.WriteBoolean("url-filter-is-case-sensitive", true);
        if (p.JenisMuat is { } muat)
            Larik("load-type", [muat]);
        if (p.Jenis is { Count: > 0 } jenis)
            Larik("resource-type", jenis);
        if (p.JikaDomain is { } jika)
            Larik("if-domain", jika);
        else if (p.KecualiDomain is { } kecuali)
            Larik("unless-domain", kecuali);
        if (p.JikaUrlAtas is { } atas)
            Larik("if-top-url", [atas]);
        json.WriteEndObject();
        json.WriteStartObject("action");
        json.WriteString("type", aksi);
        json.WriteEndObject();
        json.WriteEndObject();
        Hasil.Aturan++;
    }

    void Larik(string nama, IReadOnlyList<string> isi)
    {
        json.WriteStartArray(nama);
        foreach (var s in isi)
            json.WriteStringValue(s);
        json.WriteEndArray();
    }

    // FNV-1a 64-bit: cukup kecil untuk 100 ribu aturan, tanpa menyimpan teksnya.
    static ulong Sidik(Pemicu p, string aksi)
    {
        var h = 14695981039346656037UL;
        void Campur(string? s)
        {
            foreach (var c in s ?? "")
                h = (h ^ c) * 1099511628211UL;
            h = (h ^ 0xFF) * 1099511628211UL;
        }
        Campur(aksi);
        Campur(p.UrlFilter);
        Campur(p.JenisMuat);
        Campur(p.BedaHuruf ? "1" : "0");
        Campur(p.JikaUrlAtas);
        foreach (var x in p.Jenis ?? [])
            Campur(x);
        foreach (var x in p.JikaDomain ?? [])
            Campur(x);
        foreach (var x in p.KecualiDomain ?? [])
            Campur("~" + x);
        return h;
    }

    sealed class Pemicu
    {
        public string UrlFilter = "";
        public bool BedaHuruf;
        public string? JenisMuat;
        public IReadOnlyList<string>? Jenis;
        public List<string>? JikaDomain;
        public List<string>? KecualiDomain;
        public string? JikaUrlAtas;
    }
}
