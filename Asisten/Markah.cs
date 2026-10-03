using System.Text;
using static System.Net.WebUtility;

namespace KevinBrowser.Asisten;

/// <summary>
/// Markdown catatan → HTML untuk kevin://belajar. Hanya yang dipakai
/// catatan: judul (#), paragraf, daftar (-, *, +, 1.), kutipan (&gt;), kode
/// (```, ~~~, atau menjorok 4 spasi), tabel, garis (---), **tebal**,
/// *miring*, `kode`, [teks](alamat), alamat http(s) polos, dan
/// [[nama-catatan]].
/// </summary>
/// <remarks>
/// HTML di dalam catatan tidak pernah diteruskan: semuanya di-escape, dan
/// tautan hanya boleh http(s) atau kevin://belajar, karena isi catatan bisa
/// berasal dari situs. Bintang dan garis bawah di tengah kata bukan penanda
/// (<c>2*3*4</c>, <c>t_puncak</c>). Sengaja tanpa Regex. Terukur 3 Okt 2026:
/// versi pertama dengan 15 [GeneratedRegex] membuat binary rilis 334 KB
/// lebih besar daripada versi ini (5,46 lawan 5,12 MB), karena pustaka
/// Regex, kode hasil generator, dan tabel Unicode ikut masuk.
/// </remarks>
static class Markah
{
    /// <param name="penaut">Alamat untuk [[nama]]; null kalau catatannya tidak ada.</param>
    /// <param name="geserJudul">Ditambahkan ke tingkat judul: 1 berarti "#" jadi &lt;h2&gt;.</param>
    public static string KeHtml(string markdown, Func<string, string?>? penaut = null, int geserJudul = 0)
    {
        var html = new StringBuilder();
        Blok(Bersihkan(markdown).Split('\n'), html, new Konteks(penaut, geserJudul), false);
        return html.ToString();
    }

    sealed record Konteks(Func<string, string?>? Penaut, int GeserJudul);

    /// <param name="Penanda">-, * atau + untuk daftar biasa; . atau ) untuk daftar bernomor.</param>
    /// <param name="Geser">Kolom tempat isi butir dimulai; baris lanjutannya menjorok sejauh ini.</param>
    readonly record struct Butir(int Jorok, char Penanda, int? Nomor, int Geser, string Isi);

    // tanpaPPertama: isi butir daftar yang rapat, paragraf pertamanya tanpa <p>.
    static void Blok(string[] baris, StringBuilder html, Konteks k, bool tanpaPPertama)
    {
        var i = 0;
        var pertama = true;
        while (i < baris.Length)
        {
            var b = baris[i];
            if (Kosong(b))
            {
                i++;
                continue;
            }

            if (Pagar(b) is { } pagar)
            {
                var jorok = Jorok(b);
                var isi = new List<string>();
                for (i++; i < baris.Length && !PagarTutup(baris[i], pagar); i++)
                    isi.Add(HapusJorok(baris[i], jorok));
                i++;
                Kode(html, isi);
            }
            else if (Jorok(b) >= 4)
            {
                var isi = new List<string>();
                for (; i < baris.Length && (Kosong(baris[i]) || Jorok(baris[i]) >= 4); i++)
                    isi.Add(Kosong(baris[i]) ? "" : baris[i][4..]);
                while (isi.Count > 0 && isi[^1].Length == 0)
                    isi.RemoveAt(isi.Count - 1);
                Kode(html, isi);
            }
            else if (JudulAtx(b) is { } judul)
            {
                var tingkat = Math.Min(6, judul.Tingkat + k.GeserJudul);
                html.Append($"<h{tingkat}>{Sebaris(judul.Teks, k)}</h{tingkat}>\n");
                i++;
            }
            else if (Garis(b))
            {
                html.Append("<hr>\n");
                i++;
            }
            else if (Kutipan(b))
            {
                var isi = new List<string>();
                for (; i < baris.Length && !Kosong(baris[i]) && (Kutipan(baris[i]) || !MulaiBlok(baris[i])); i++)
                    isi.Add(Kutipan(baris[i]) ? TanpaKutipan(baris[i]) : baris[i]);
                html.Append("<blockquote>\n");
                Blok([.. isi], html, k, false);
                html.Append("</blockquote>\n");
            }
            else if (AwalTabel(baris, i))
                i = Tabel(baris, i, html, k);
            else if (ButirDaftar(b) is not null)
                i = Daftar(baris, i, html, k);
            else
            {
                var isi = new List<string>();
                for (; i < baris.Length && !Kosong(baris[i]) && (isi.Count == 0 || !MulaiBlok(baris[i])); i++)
                    isi.Add(baris[i].TrimStart());
                var teks = Sebaris(string.Join('\n', isi).TrimEnd(), k);
                html.Append(pertama && tanpaPPertama ? teks + "\n" : $"<p>{teks}</p>\n");
            }
            pertama = false;
        }
    }

    static int Daftar(string[] baris, int i, StringBuilder html, Konteks k)
    {
        var awal = ButirDaftar(baris[i])!.Value;
        var bernomor = awal.Nomor is not null;
        html.Append(!bernomor ? "<ul>\n" : awal.Nomor == 1 ? "<ol>\n" : $"<ol start=\"{awal.Nomor}\">\n");
        while (i < baris.Length && ButirDaftar(baris[i]) is { } butir
               && butir.Jorok <= awal.Jorok + 3 && butir.Penanda == awal.Penanda && (butir.Nomor is not null) == bernomor)
        {
            var isi = new List<string> { butir.Isi };
            var longgar = false;
            var habisKosong = false;
            for (i++; i < baris.Length; i++)
            {
                var b = baris[i];
                if (Kosong(b))
                {
                    isi.Add("");
                    habisKosong = true;
                }
                else if (Jorok(b) >= butir.Geser)
                {
                    isi.Add(b[butir.Geser..]);
                    longgar |= habisKosong;
                    habisKosong = false;
                }
                else if (!habisKosong && isi[^1].Length > 0 && ButirDaftar(b) is null && !MulaiBlok(b))
                    isi.Add(b.TrimStart());   // lanjutan paragraf tanpa jorok
                else
                    break;
            }
            while (isi.Count > 1 && isi[^1].Length == 0)
                isi.RemoveAt(isi.Count - 1);
            html.Append("<li>");
            Blok([.. isi], html, k, tanpaPPertama: !longgar);
            html.Append("</li>\n");
        }
        html.Append(bernomor ? "</ol>\n" : "</ul>\n");
        return i;
    }

    static bool AwalTabel(string[] baris, int i) =>
        i + 1 < baris.Length && baris[i].Contains('|') && PemisahTabel(baris[i + 1])
        && Sel(baris[i]).Count == Sel(baris[i + 1]).Count;

    // |---|:--:|--:|
    static bool PemisahTabel(string baris)
    {
        if (Jorok(baris) > 3 || !baris.Contains('-'))
            return false;
        foreach (var sel in Sel(baris))
        {
            var garis = sel.Trim(':');
            if (garis.Length == 0 || sel.Length - garis.Length > 2 || garis.Any(c => c != '-'))
                return false;
        }
        return true;
    }

    static int Tabel(string[] baris, int i, StringBuilder html, Konteks k)
    {
        var kepala = Sel(baris[i]);
        var rata = Sel(baris[i + 1]).Select(s => (s.StartsWith(':'), s.EndsWith(':')) switch
        {
            (true, true) => " style=\"text-align:center\"",
            (false, true) => " style=\"text-align:right\"",
            _ => "",
        }).ToArray();

        html.Append("<div class=\"tabel\"><table>\n<thead><tr>");
        for (var j = 0; j < kepala.Count; j++)
            html.Append($"<th{rata[j]}>{Sebaris(kepala[j], k)}</th>");
        html.Append("</tr></thead>\n<tbody>\n");
        for (i += 2; i < baris.Length && !Kosong(baris[i]) && !MulaiBlok(baris[i]); i++)
        {
            var sel = Sel(baris[i]);
            html.Append("<tr>");
            for (var j = 0; j < kepala.Count; j++)
                html.Append($"<td{rata[j]}>{(j < sel.Count ? Sebaris(sel[j], k) : "")}</td>");
            html.Append("</tr>\n");
        }
        html.Append("</tbody></table></div>\n");
        return i;
    }

    // "| a | b \| c |" → ["a", "b | c"]
    static List<string> Sel(string baris)
    {
        var teks = baris.Trim();
        if (teks.StartsWith('|'))
            teks = teks[1..];
        if (teks.EndsWith('|') && !teks.EndsWith("\\|", StringComparison.Ordinal))
            teks = teks[..^1];
        var hasil = new List<string>();
        var sel = new StringBuilder();
        for (var i = 0; i < teks.Length; i++)
        {
            if (teks[i] == '\\' && i + 1 < teks.Length && teks[i + 1] == '|')
            {
                sel.Append('|');
                i++;
            }
            else if (teks[i] == '|')
            {
                hasil.Add(sel.ToString().Trim());
                sel.Clear();
            }
            else
                sel.Append(teks[i]);
        }
        hasil.Add(sel.ToString().Trim());
        return hasil;
    }

    static void Kode(StringBuilder html, List<string> isi) =>
        html.Append("<pre><code>").Append(HtmlEncode(string.Join('\n', isi))).Append("</code></pre>\n");

    // Baris yang memulai blok baru, jadi tidak menyambung paragraf di atasnya.
    // Daftar bernomor hanya kalau mulai dari 1, supaya "2026. Tahun …" di
    // tengah paragraf tidak jadi daftar.
    static bool MulaiBlok(string baris) =>
        Pagar(baris) is not null || JudulAtx(baris) is not null || Garis(baris) || Kutipan(baris)
        || ButirDaftar(baris) is { } butir && butir.Nomor is null or 1 && butir.Isi.Length > 0;

    // "- isi", "* isi", "+ isi", "1. isi", "1) isi", paling banyak 3 spasi di depan.
    static Butir? ButirDaftar(string baris)
    {
        var jorok = Jorok(baris);
        if (jorok > 3 || jorok >= baris.Length || Garis(baris))
            return null;
        var i = jorok;
        char penanda;
        int? nomor = null;
        if (baris[i] is '-' or '*' or '+')
            penanda = baris[i++];
        else
        {
            while (i < baris.Length && i - jorok < 9 && char.IsAsciiDigit(baris[i]))
                i++;
            if (i == jorok || i >= baris.Length || baris[i] is not ('.' or ')'))
                return null;
            nomor = int.Parse(baris.AsSpan(jorok, i - jorok));
            penanda = baris[i++];
        }
        if (i < baris.Length && baris[i] != ' ')
            return null;
        var spasi = 0;
        while (i + spasi < baris.Length && baris[i + spasi] == ' ')
            spasi++;
        var isi = baris[(i + spasi)..];
        return new Butir(jorok, penanda, nomor, i + (spasi is >= 1 and <= 4 ? spasi : 1), isi);
    }

    // "## Judul ##" → (2, "Judul"). "#5" bukan judul: setelah # harus ada spasi.
    static (int Tingkat, string Teks)? JudulAtx(string baris)
    {
        var i = Jorok(baris);
        if (i > 3)
            return null;
        var n = 0;
        while (i + n < baris.Length && baris[i + n] == '#')
            n++;
        if (n is 0 or > 6 || (i + n < baris.Length && baris[i + n] != ' '))
            return null;
        var teks = baris[(i + n)..].Trim();
        var akhir = teks.Length;
        while (akhir > 0 && teks[akhir - 1] == '#')
            akhir--;
        if (akhir == 0)
            teks = "";
        else if (akhir < teks.Length && teks[akhir - 1] == ' ')
            teks = teks[..akhir].TrimEnd();
        return (n, teks);
    }

    // ---, ***, ___ (boleh berspasi), paling sedikit tiga tanda yang sama.
    static bool Garis(string baris)
    {
        if (Jorok(baris) > 3)
            return false;
        var tanda = '\0';
        var n = 0;
        foreach (var c in baris)
        {
            if (c == ' ')
                continue;
            if (c is not ('-' or '*' or '_') || (n > 0 && c != tanda))
                return false;
            tanda = c;
            n++;
        }
        return n >= 3;
    }

    // ``` atau ~~~ (paling sedikit tiga); info pagar ``` tidak boleh berisi `.
    static string? Pagar(string baris)
    {
        var i = Jorok(baris);
        if (i > 3 || i >= baris.Length || baris[i] is not ('`' or '~'))
            return null;
        var n = 0;
        while (i + n < baris.Length && baris[i + n] == baris[i])
            n++;
        if (n < 3 || (baris[i] == '`' && baris.IndexOf('`', i + n) >= 0))
            return null;
        return new string(baris[i], n);
    }

    static bool PagarTutup(string baris, string pagar)
    {
        var teks = baris.Trim();
        return Jorok(baris) <= 3 && teks.Length >= pagar.Length && teks.All(c => c == pagar[0]);
    }

    static bool Kutipan(string baris) => Jorok(baris) <= 3 && baris.TrimStart(' ').StartsWith('>');

    static string TanpaKutipan(string baris)
    {
        var teks = baris.TrimStart(' ')[1..];
        return teks.StartsWith(' ') ? teks[1..] : teks;
    }

    static bool Kosong(string baris) => string.IsNullOrWhiteSpace(baris);

    static int Jorok(string baris)
    {
        var n = 0;
        while (n < baris.Length && baris[n] == ' ')
            n++;
        return n;
    }

    static string HapusJorok(string baris, int n) => baris[Math.Min(n, Jorok(baris))..];

    // Baris baru gaya Unix, tab jadi empat spasi, karakter kendali dibuang
    // (termasuk \u0001 dan \u0002, penanda simpanan di Sebaris).
    static string Bersihkan(string teks)
    {
        var hasil = new StringBuilder(teks.Length);
        foreach (var c in teks.Replace("\r\n", "\n"))
        {
            if (c == '\t')
                hasil.Append("    ");
            else if (c is '\n' or '\r')
                hasil.Append('\n');
            else if (!char.IsControl(c))
                hasil.Append(c);
        }
        return hasil.ToString();
    }

    const string TandaBaca = "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~";

    // Bagian yang sudah jadi HTML (kode, tautan, tanda yang di-escape)
    // disimpan dulu dan diganti penanda \u0001N\u0002, supaya tidak ikut
    // di-escape atau diberi tebal/miring.
    static string Sebaris(string teks, Konteks k)
    {
        var simpanan = new List<string>();
        string Simpan(string html)
        {
            simpanan.Add(html);
            return $"\u0001{simpanan.Count - 1}\u0002";
        }
        static string Tautan(string alamat, string label) => $"<a href=\"{HtmlEncode(alamat)}\">{HtmlEncode(label)}</a>";

        var sb = new StringBuilder();
        for (var i = 0; i < teks.Length; i++)
        {
            var c = teks[i];
            if (c == '\\' && i + 1 < teks.Length && TandaBaca.Contains(teks[i + 1]))
            {
                sb.Append(Simpan(HtmlEncode(teks[i + 1].ToString())));
                i++;
            }
            else if (c == '`')
            {
                var n = 1;
                while (i + n < teks.Length && teks[i + n] == '`')
                    n++;
                var tutup = TutupKode(teks, i + n, n);
                if (tutup < 0)
                {
                    sb.Append(teks, i, n);
                    i += n - 1;
                    continue;
                }
                var kode = teks[(i + n)..tutup].Replace('\n', ' ');
                if (kode.Length > 1 && kode[0] == ' ' && kode[^1] == ' ' && kode.Trim().Length > 0)
                    kode = kode[1..^1];
                sb.Append(Simpan($"<code>{HtmlEncode(kode)}</code>"));
                i = tutup + n - 1;
            }
            else if (c == '[' && TautanWiki(teks, i) is { } wiki)
            {
                var label = (wiki.Label ?? wiki.Sasaran).Trim();
                sb.Append(Simpan(k.Penaut?.Invoke(wiki.Sasaran) is { } alamat
                    ? Tautan(alamat, label)
                    : $"<span class=\"putus\">{HtmlEncode(label)}</span>"));
                i += wiki.Panjang - 1;
            }
            else if (c == '[' && TautanMd(teks, i) is { } md)
            {
                sb.Append(AlamatAman(md.Alamat) is { } alamat ? Simpan(Tautan(alamat, md.Label)) : md.Label);
                i += md.Panjang - 1;
            }
            else if (c == '<' && TautanSudut(teks, i) is { } sudut)
            {
                sb.Append(Simpan(Tautan(sudut, sudut)));
                i += sudut.Length + 1;
            }
            else if (c == 'h' && AlamatPolos(teks, i) is { } polos)
            {
                sb.Append(Simpan(Tautan(polos, polos)));
                i += polos.Length - 1;
            }
            else
                sb.Append(c);
        }

        var hasil = HtmlEncode(sb.ToString());
        hasil = Tekanan(hasil, "**", "strong");
        hasil = Tekanan(hasil, "__", "strong");
        hasil = Tekanan(hasil, "*", "em");
        hasil = Tekanan(hasil, "_", "em");
        hasil = PatahBaris(hasil);
        return Kembalikan(hasil, simpanan);
    }

    // Deret ` sepanjang n berikutnya (bukan bagian deret yang lebih panjang).
    static int TutupKode(string teks, int mulai, int n)
    {
        for (var i = mulai; i < teks.Length; i++)
        {
            if (teks[i] != '`')
                continue;
            var m = 1;
            while (i + m < teks.Length && teks[i + m] == '`')
                m++;
            if (m == n)
                return i;
            i += m - 1;
        }
        return -1;
    }

    // [[sasaran]] atau [[sasaran|label]], dalam satu baris.
    static (string Sasaran, string? Label, int Panjang)? TautanWiki(string teks, int i)
    {
        var tutup = teks.AsSpan(i).StartsWith("[[") ? teks.IndexOf("]]", i + 2, StringComparison.Ordinal) : -1;
        if (tutup < 0)
            return null;
        var isi = teks[(i + 2)..tutup];
        if (isi.Length == 0 || isi.IndexOfAny(['[', ']', '\n']) >= 0)
            return null;
        var garis = isi.IndexOf('|');
        if (garis == 0 || garis == isi.Length - 1)
            return null;
        return garis < 0 ? (isi, null, tutup + 2 - i) : (isi[..garis], isi[(garis + 1)..], tutup + 2 - i);
    }

    // [label](alamat) atau [label](alamat "judul"); alamat tanpa spasi dan kurung.
    static (string Label, string Alamat, int Panjang)? TautanMd(string teks, int i)
    {
        var tutupLabel = teks.IndexOf(']', i + 1);
        if (tutupLabel <= i + 1 || tutupLabel + 1 >= teks.Length || teks[tutupLabel + 1] != '(')
            return null;
        var label = teks[(i + 1)..tutupLabel];
        if (label.IndexOfAny(['[', '\n']) >= 0)
            return null;
        var tutup = teks.IndexOf(')', tutupLabel + 2);
        if (tutup < 0)
            return null;
        var dalam = teks[(tutupLabel + 2)..tutup];
        var alamat = dalam;
        if (dalam.IndexOf(' ') is var spasi and > 0)
        {
            var judul = dalam[spasi..].Trim();
            if (judul.Length < 2 || judul[0] != '"' || judul[^1] != '"' || judul[1..^1].Contains('"'))
                return null;
            alamat = dalam[..spasi];
        }
        if (alamat.Length == 0 || alamat.Any(c => char.IsWhiteSpace(c) || c is '(' or ')'))
            return null;
        return (label, alamat, tutup + 1 - i);
    }

    // <https://…>
    static string? TautanSudut(string teks, int i)
    {
        var tutup = teks.IndexOf('>', i + 1);
        if (tutup < 0)
            return null;
        var alamat = teks[(i + 1)..tutup];
        return AwalanWeb(alamat) && !alamat.Any(c => char.IsWhiteSpace(c) || c == '<') ? alamat : null;
    }

    // Alamat http(s) polos di tengah teks. Tanda baca di ujung kalimat dan
    // kurung tutup yang tidak berpasangan tidak ikut.
    static string? AlamatPolos(string teks, int i)
    {
        if (!AwalanWeb(teks.AsSpan(i)) || (i > 0 && (char.IsLetterOrDigit(teks[i - 1]) || teks[i - 1] is '_' or '/' or '@')))
            return null;
        var akhir = i;
        while (akhir < teks.Length && !char.IsWhiteSpace(teks[akhir]) && teks[akhir] is not ('<' or '>' or '"' or '\'' or '`'))
            akhir++;
        var alamat = teks[i..akhir].TrimEnd('.', ',', ';', ':', '!', '?');
        if (alamat.EndsWith(')') && alamat.Count(c => c == '(') < alamat.Count(c => c == ')'))
            alamat = alamat[..^1];
        return alamat.IndexOf("://", StringComparison.Ordinal) + 3 < alamat.Length ? alamat : null;
    }

    static bool AwalanWeb(ReadOnlySpan<char> teks) => teks.StartsWith("https://") || teks.StartsWith("http://");

    static string? AlamatAman(string alamat) =>
        alamat.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || alamat.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || alamat.StartsWith(HalamanBawaan.Belajar, StringComparison.OrdinalIgnoreCase)
            ? alamat
            : null;

    // **x** → <strong>x</strong>, begitu juga __x__, *x* dan _x_. Penanda
    // pembuka tidak boleh menempel di belakang huruf atau angka dan harus
    // diikuti bukan-spasi; penutup kebalikannya. Teks sudah di-escape.
    static string Tekanan(string teks, string penanda, string tag)
    {
        if (!teks.Contains(penanda, StringComparison.Ordinal))
            return teks;
        var hasil = new StringBuilder(teks.Length + 16);
        var i = 0;
        while (i < teks.Length)
        {
            if (Buka(teks, i, penanda) && Tutup(teks, i + penanda.Length + 1, penanda) is var tutup and >= 0)
            {
                hasil.Append($"<{tag}>").Append(teks, i + penanda.Length, tutup - i - penanda.Length).Append($"</{tag}>");
                i = tutup + penanda.Length;
            }
            else
                hasil.Append(teks[i++]);
        }
        return hasil.ToString();
    }

    static bool Buka(string teks, int i, string penanda)
    {
        var sesudah = i + penanda.Length;
        return teks.AsSpan(i).StartsWith(penanda)
            && (i == 0 || !Menempel(teks[i - 1], penanda[0]) && teks[i - 1] != '\\')
            && sesudah < teks.Length && !char.IsWhiteSpace(teks[sesudah])
            && (penanda.Length == 2 || teks[sesudah] != penanda[0]);
    }

    static int Tutup(string teks, int mulai, string penanda)
    {
        for (var j = teks.IndexOf(penanda, mulai, StringComparison.Ordinal); j >= 0; j = teks.IndexOf(penanda, j + 1, StringComparison.Ordinal))
        {
            var sebelum = teks[j - 1];
            var sesudah = j + penanda.Length;
            if (!char.IsWhiteSpace(sebelum) && (penanda.Length == 2 || sebelum != penanda[0])
                && (sesudah >= teks.Length || !Menempel(teks[sesudah], penanda[0])))
                return j;
        }
        return -1;
    }

    static bool Menempel(char c, char penanda) => char.IsLetterOrDigit(c) || c == penanda;

    // Dua spasi atau \ di ujung baris: patah baris.
    static string PatahBaris(string teks)
    {
        if (!teks.Contains('\n'))
            return teks;
        var baris = teks.Split('\n');
        for (var i = 0; i < baris.Length - 1; i++)
        {
            if (baris[i].EndsWith("  ", StringComparison.Ordinal))
                baris[i] = baris[i].TrimEnd(' ') + "<br>";
            else if (baris[i].EndsWith('\\'))
                baris[i] = baris[i][..^1] + "<br>";
        }
        return string.Join('\n', baris);
    }

    static string Kembalikan(string teks, List<string> simpanan)
    {
        if (simpanan.Count == 0)
            return teks;
        var hasil = new StringBuilder(teks.Length + 64);
        for (var i = 0; i < teks.Length; i++)
        {
            var akhir = teks[i] == '\u0001' ? teks.IndexOf('\u0002', i) : -1;
            if (akhir > i && int.TryParse(teks.AsSpan(i + 1, akhir - i - 1), out var n) && n < simpanan.Count)
            {
                hasil.Append(simpanan[n]);
                i = akhir;
            }
            else
                hasil.Append(teks[i]);
        }
        return hasil.ToString();
    }
}
