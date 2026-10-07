using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KevinBrowser.Asisten;

/// <summary>
/// Satu langkah dalam partai: notasinya, posisi sesudahnya, dan yang ikut di
/// PGN: nilai mesin dari sudut putih (dari analisis Lichess, <c>[%eval]</c>),
/// tanda (?, ??, ?!, !, …), dan komentar.
/// </summary>
public sealed record LangkahPartai(string San, Langkah Langkah, string Fen, double? Nilai, int? Mat, string? Tanda, string? Komentar);

/// <summary>Langkah-langkah satu partai; <see cref="Galat"/> kalau berhenti di langkah yang tidak sah.</summary>
public sealed record UraianPartai(string FenAwal, IReadOnlyList<LangkahPartai> Langkah, string? Galat);

/// <summary>
/// Satu partai dari PGN. Kepalanya dibaca langsung (untuk daftar partai),
/// langkahnya baru diurai saat partai dibuka.
/// </summary>
public sealed class Partai
{
    UraianPartai? uraian;

    internal Partai(IReadOnlyDictionary<string, string> kepala, string teksLangkah, string pgn)
    {
        Kepala = kepala;
        TeksLangkah = teksLangkah;
        Pgn = pgn;
        Id = BuatId();
    }

    public IReadOnlyDictionary<string, string> Kepala { get; }

    /// <summary>Teks PGN partai ini apa adanya.</summary>
    public string Pgn { get; }

    internal string TeksLangkah { get; }

    /// <summary>"lichess-abcdEFGH", "chesscom-123456789", atau "pgn-…" (sidik isinya).</summary>
    public string Id { get; }

    /// <summary>Nilai kepala; null kalau tidak ada, kosong, "?", atau "-".</summary>
    public string? this[string nama] =>
        Kepala.TryGetValue(nama, out var nilai) && nilai.Trim('?', '.', '-', ' ').Length > 0 ? nilai : null;

    public string Putih => this["White"] ?? "?";

    public string Hitam => this["Black"] ?? "?";

    /// <summary>"1-0", "0-1", "1/2-1/2", atau "*".</summary>
    public string Hasil => this["Result"] ?? "*";

    /// <summary>Tautan ke partai aslinya di Lichess atau Chess.com, kalau ada.</summary>
    public string? Tautan =>
        this["Link"] is { } link && link.StartsWith("https://", StringComparison.Ordinal) ? link
        : this["Site"] is { } situs && situs.StartsWith("https://", StringComparison.Ordinal) ? situs
        : null;

    /// <summary>Waktu mulai (UTC) dari UTCDate/UTCTime, atau Date; null kalau tidak diketahui.</summary>
    /// <remarks>
    /// Diurai sendiri: DateTime.TryParseExact dan TimeSpan.TryParseExact
    /// menyeret ±15 KB kode pengurai format ke binary.
    /// </remarks>
    public DateTime? Waktu
    {
        get
        {
            // "2026.10.05" dan "13:01:02"
            var tanggal = this["UTCDate"] ?? this["Date"];
            if (tanggal is not { Length: 10 } || tanggal[4] != '.' || tanggal[7] != '.'
                || Angka(tanggal, 0, 4) is not (>= 1 and <= 9999 and var tahun) || Angka(tanggal, 5, 2) is not (>= 1 and <= 12 and var bulan)
                || Angka(tanggal, 8, 2) is not (>= 1 and var hari) || hari > DateTime.DaysInMonth(tahun, bulan))
                return null;
            var waktu = new DateTime(tahun, bulan, hari);
            return this["UTCTime"] is { Length: 8 } jam && jam[2] == ':' && jam[5] == ':'
                && Angka(jam, 0, 2) is >= 0 and < 24 and var j && Angka(jam, 3, 2) is >= 0 and < 60 and var m && Angka(jam, 6, 2) is >= 0 and < 60 and var d
                ? waktu.AddSeconds(j * 3600 + m * 60 + d)
                : waktu;
        }
    }

    // Bilangan dari angka saja; -1 kalau ada yang bukan angka.
    static int Angka(string teks, int awal, int panjang)
    {
        var n = 0;
        for (var i = awal; i < awal + panjang; i++)
        {
            if (!char.IsAsciiDigit(teks[i]))
                return -1;
            n = n * 10 + (teks[i] - '0');
        }
        return n;
    }

    /// <summary>Langkah-langkahnya, diurai sekali lalu diingat.</summary>
    public UraianPartai Urai() => uraian ??= UraiPgn.Urai(this);

    string BuatId()
    {
        foreach (var tautan in new[] { this["Site"], this["Link"] })
        {
            if (tautan is null)
                continue;
            if (tautan.StartsWith("https://lichess.org/", StringComparison.Ordinal) && tautan.Length >= 28)
                return "lichess-" + tautan[20..28];
            var game = tautan.IndexOf("chess.com/game/", StringComparison.Ordinal);
            if (game >= 0 && tautan.LastIndexOf('/') is var garis && garis > game && tautan[(garis + 1)..].All(char.IsAsciiDigit))
                return "chesscom-" + tautan[(garis + 1)..];
        }
        // Spasi dan nomor langkah yang menempel ("1.d4" atau "1. d4") tidak mengubah sidiknya.
        var rapi = string.Join(' ', TeksLangkah.Replace(".", ". ").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var sidik = SHA256.HashData(Encoding.UTF8.GetBytes(rapi));
        return "pgn-" + Convert.ToHexStringLower(sidik)[..12];
    }
}

/// <summary>
/// Pembaca PGN, format partai catur yang diekspor Lichess dan Chess.com:
/// kepala <c>[Nama "nilai"]</c>, lalu langkah dengan nomor, komentar
/// <c>{…}</c>, variasi <c>(…)</c>, dan NAG <c>$2</c>. Variasi dilewati.
/// </summary>
public static class Pgn
{
    /// <summary>Semua partai dalam teks, urut seperti di teks.</summary>
    public static List<Partai> Pisah(string teks)
    {
        var hasil = new List<Partai>();
        var kepala = new Dictionary<string, string>(StringComparer.Ordinal);
        var barisKepala = new List<string>();
        var langkah = new StringBuilder();
        var kurawal = 0;

        void Selesai()
        {
            var teksLangkah = langkah.ToString().Trim();
            if (kepala.Count > 0 || teksLangkah.Length > 0)
                hasil.Add(new Partai(kepala, teksLangkah, (barisKepala.Count > 0 ? string.Join('\n', barisKepala) + "\n\n" : "") + teksLangkah + "\n"));
            kepala = new Dictionary<string, string>(StringComparer.Ordinal);
            barisKepala.Clear();
            langkah.Clear();
        }

        foreach (var mentah in teks.Replace("\r\n", "\n").Split('\n'))
        {
            var baris = mentah.Trim();
            // Kepala hanya di luar komentar; kepala sesudah langkah = partai berikutnya.
            if (kurawal == 0 && baris.StartsWith('[') && baris.EndsWith(']'))
            {
                if (langkah.ToString().Trim().Length > 0)
                    Selesai();
                if (BacaKepala(baris) is var (nama, nilai))
                {
                    kepala[nama] = nilai;
                    barisKepala.Add(baris);
                }
                continue;
            }
            foreach (var c in baris)
                kurawal = c == '{' ? kurawal + 1 : c == '}' && kurawal > 0 ? kurawal - 1 : kurawal;
            langkah.Append(baris).Append('\n');
        }
        Selesai();
        return hasil;
    }

    // [Nama "nilai"], dengan \" dan \\ di dalam nilai.
    static (string, string)? BacaKepala(string baris)
    {
        var kutip = baris.IndexOf('"');
        if (kutip < 2)
            return null;
        var nama = baris[1..kutip].Trim();
        var nilai = new StringBuilder();
        for (var i = kutip + 1; i < baris.Length; i++)
        {
            var c = baris[i];
            if (c == '\\' && i + 1 < baris.Length)
                nilai.Append(baris[++i]);
            else if (c == '"')
                return nama.Length == 0 ? null : (nama, nilai.ToString());
            else
                nilai.Append(c);
        }
        return null;
    }
}

// Penguraian langkah satu partai: SAN diperiksa terhadap posisi, dan yang
// tidak sah menghentikan penguraian (Galat menyebut langkahnya).
static class UraiPgn
{
    static readonly string[] TandaNag = ["", "!", "?", "!!", "??", "!?", "?!"];

    public static UraianPartai Urai(Partai partai)
    {
        var fenAwal = partai["FEN"] is { } fen && Papan.DariFen(fen) is not null ? fen : Papan.FenAwal;
        var papan = Papan.DariFen(fenAwal)!;
        var hasil = new List<LangkahPartai>();
        var s = partai.TeksLangkah;
        var i = 0;

        void Ubah(Func<LangkahPartai, LangkahPartai> ubah)
        {
            if (hasil.Count > 0)
                hasil[^1] = ubah(hasil[^1]);
        }

        while (i < s.Length)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }
            if (c == '{')
            {
                var tutup = s.IndexOf('}', i + 1);
                var isi = tutup < 0 ? s[(i + 1)..] : s[(i + 1)..tutup];
                i = tutup < 0 ? s.Length : tutup + 1;
                Ubah(l => Komentar(l, isi));
                continue;
            }
            if (c == ';' || (c == '%' && (i == 0 || s[i - 1] == '\n')))
            {
                var akhir = s.IndexOf('\n', i);
                i = akhir < 0 ? s.Length : akhir + 1;
                continue;
            }
            if (c == '(')
            {
                i = LewatiVariasi(s, i);
                continue;
            }
            if (c == '$')
            {
                var j = i + 1;
                while (j < s.Length && char.IsAsciiDigit(s[j]))
                    j++;
                if (int.TryParse(s.AsSpan(i + 1, j - i - 1), out var nag) && nag > 0 && nag < TandaNag.Length)
                    Ubah(l => l with { Tanda = TandaNag[nag] });
                i = j;
                continue;
            }

            var awal = i;
            while (i < s.Length && !char.IsWhiteSpace(s[i]) && "{}();$".IndexOf(s[i]) < 0)
                i++;
            var token = s[awal..i];
            if (token is "1-0" or "0-1" or "1/2-1/2" or "½-½" or "*")
                break;
            // Nomor langkah, menempel atau tidak: "12.", "12...", "12.e4".
            var mulai = 0;
            while (mulai < token.Length && char.IsAsciiDigit(token[mulai]))
                mulai++;
            if (mulai < token.Length && token[mulai] == '.')
            {
                while (mulai < token.Length && token[mulai] == '.')
                    mulai++;
                token = token[mulai..];
            }
            if (token.Length == 0)
                continue;

            var tanda = AkhiranTanda(token);
            if (papan.DariNotasi(token) is not { } langkah)
            {
                var nomor = papan.NomorLangkah + (papan.GiliranPutih ? "." : "...");
                return new UraianPartai(fenAwal, hasil, $"{nomor} {token}");
            }
            var san = papan.San(langkah);
            papan = papan.Jalankan(langkah);
            hasil.Add(new LangkahPartai(san, langkah, papan.Fen(), null, null, tanda, null));
        }
        return new UraianPartai(fenAwal, hasil, null);
    }

    // "Nf3?!" → "?!"
    static string? AkhiranTanda(string token)
    {
        var j = token.Length;
        while (j > 0 && token[j - 1] is '!' or '?')
            j--;
        return j < token.Length ? token[j..] : null;
    }

    static int LewatiVariasi(string s, int i)
    {
        var dalam = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '{')
            {
                var tutup = s.IndexOf('}', i + 1);
                i = tutup < 0 ? s.Length : tutup + 1;
                continue;
            }
            if (c == '(')
                dalam++;
            else if (c == ')' && --dalam == 0)
                return i + 1;
            i++;
        }
        return i;
    }

    // { [%eval 0.17] } atau { [%eval #-3] }; [%clk …] dan perintah lain dilewati;
    // sisanya jadi komentar.
    static LangkahPartai Komentar(LangkahPartai l, string isi)
    {
        var teks = new StringBuilder();
        var i = 0;
        while (i < isi.Length)
        {
            var buka = isi.IndexOf("[%", i, StringComparison.Ordinal);
            if (buka < 0)
            {
                teks.Append(isi[i..]);
                break;
            }
            teks.Append(isi[i..buka]);
            var tutup = isi.IndexOf(']', buka);
            if (tutup < 0)
                break;
            var perintah = isi[(buka + 2)..tutup].Trim();
            if (perintah.StartsWith("eval ", StringComparison.Ordinal))
            {
                var nilai = perintah[5..].Trim();
                if (nilai.StartsWith('#') && int.TryParse(nilai[1..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var mat))
                    l = l with { Mat = mat, Nilai = null };
                else if (double.TryParse(nilai, NumberStyles.Float, CultureInfo.InvariantCulture, out var pion))
                    l = l with { Nilai = pion, Mat = null };
            }
            i = tutup + 1;
        }
        var bersih = string.Join(' ', teks.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (bersih.Length == 0)
            return l;
        if (bersih.Length > 300)
            bersih = bersih[..300] + "…";
        return l with { Komentar = l.Komentar is null ? bersih : l.Komentar + " " + bersih };
    }
}
