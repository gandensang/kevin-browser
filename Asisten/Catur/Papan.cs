using System.Text;

namespace KevinBrowser.Asisten;

/// <summary>
/// Satu langkah: kotak asal dan tujuan (0 = a1, 7 = h1, 63 = h8), dan bidak
/// promosi ('q', 'r', 'b', 'n') kalau ada.
/// </summary>
public readonly record struct Langkah(int Dari, int Ke, char Promosi = '\0')
{
    /// <summary>Notasi UCI, mis. "e2e4" atau "e7e8q".</summary>
    public string Uci => Papan.NamaKotak(Dari) + Papan.NamaKotak(Ke) + (Promosi == '\0' ? "" : Promosi.ToString());
}

/// <summary>
/// Posisi catur dan aturannya: langkah sah, notasi SAN, dan FEN. Ditulis
/// sendiri, tanpa pustaka, supaya tidak ada yang harus dipasang terpisah;
/// kebenarannya diuji dengan hitungan perft yang sudah dikenal (UjiPapan).
/// Kecepatan bukan tujuan: dipakai untuk membaca dan memutar partai, bukan
/// untuk mesin pencari langkah.
/// </summary>
public sealed class Papan
{
    public const string FenAwal = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    static readonly (int F, int R)[] ArahKuda = [(1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2)];
    static readonly (int F, int R)[] ArahRaja = [(1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1)];
    static readonly (int F, int R)[] ArahGajah = [(1, 1), (-1, 1), (-1, -1), (1, -1)];
    static readonly (int F, int R)[] ArahBenteng = [(1, 0), (0, 1), (-1, 0), (0, -1)];

    // '\0' = kosong; huruf besar bidak putih, huruf kecil bidak hitam (seperti FEN).
    readonly char[] kotak = new char[64];

    // Hak rokade: 1 = K, 2 = Q, 4 = k, 8 = q.
    int rokade;

    Papan()
    {
    }

    public bool GiliranPutih { get; private set; }

    /// <summary>Kotak yang bisa dimakan en passant, atau -1.</summary>
    public int EnPassant { get; private set; } = -1;

    public int LangkahSetengah { get; private set; }

    public int NomorLangkah { get; private set; } = 1;

    /// <summary>Bidak di kotak itu ('\0' kalau kosong).</summary>
    public char this[int kotak] => this.kotak[kotak];

    public static Papan Awal() => DariFen(FenAwal)!;

    public static string NamaKotak(int i) => $"{(char)('a' + i % 8)}{(char)('1' + i / 8)}";

    /// <summary>"e4" → 28, atau -1.</summary>
    public static int Kotak(char file, char rank) =>
        file is >= 'a' and <= 'h' && rank is >= '1' and <= '8' ? (rank - '1') * 8 + (file - 'a') : -1;

    static bool Putih(char bidak) => bidak is >= 'A' and <= 'Z';

    bool Milik(int i, bool putih) => kotak[i] != '\0' && Putih(kotak[i]) == putih;

    /// <summary>Posisi dari FEN; null kalau tidak sah. Jam langkah boleh tidak ada.</summary>
    public static Papan? DariFen(string fen)
    {
        var bagian = fen.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (bagian.Length < 4)
            return null;
        var p = new Papan();
        int rank = 7, file = 0;
        foreach (var c in bagian[0])
        {
            if (c == '/')
            {
                if (file != 8 || rank == 0)
                    return null;
                rank--;
                file = 0;
            }
            else if (c is >= '1' and <= '8')
            {
                file += c - '0';
                if (file > 8)
                    return null;
            }
            else if ("PNBRQKpnbrqk".Contains(c) && file < 8)
                p.kotak[rank * 8 + file++] = c;
            else
                return null;
        }
        if (rank != 0 || file != 8 || bagian[1] is not ("w" or "b"))
            return null;
        p.GiliranPutih = bagian[1] == "w";
        if (bagian[2] != "-")
            foreach (var c in bagian[2])
                p.rokade |= c switch { 'K' => 1, 'Q' => 2, 'k' => 4, 'q' => 8, _ => 0 };
        if (bagian[3] != "-")
        {
            p.EnPassant = bagian[3].Length == 2 ? Kotak(bagian[3][0], bagian[3][1]) : -1;
            if (p.EnPassant < 0)
                return null;
        }
        if (bagian.Length > 4 && int.TryParse(bagian[4], out var setengah) && setengah >= 0)
            p.LangkahSetengah = setengah;
        if (bagian.Length > 5 && int.TryParse(bagian[5], out var nomor) && nomor > 0)
            p.NomorLangkah = nomor;
        return p.kotak.Count(c => c == 'K') == 1 && p.kotak.Count(c => c == 'k') == 1 ? p : null;
    }

    public string Fen()
    {
        var sb = new StringBuilder(90);
        for (var r = 7; r >= 0; r--)
        {
            var kosong = 0;
            for (var f = 0; f < 8; f++)
            {
                var c = kotak[r * 8 + f];
                if (c == '\0')
                {
                    kosong++;
                    continue;
                }
                if (kosong > 0)
                    sb.Append(kosong);
                kosong = 0;
                sb.Append(c);
            }
            if (kosong > 0)
                sb.Append(kosong);
            if (r > 0)
                sb.Append('/');
        }
        var hak = ((rokade & 1) != 0 ? "K" : "") + ((rokade & 2) != 0 ? "Q" : "") + ((rokade & 4) != 0 ? "k" : "") + ((rokade & 8) != 0 ? "q" : "");
        return sb.Append(GiliranPutih ? " w " : " b ").Append(hak.Length == 0 ? "-" : hak)
            .Append(' ').Append(EnPassant < 0 ? "-" : NamaKotak(EnPassant))
            .Append(' ').Append(LangkahSetengah).Append(' ').Append(NomorLangkah).ToString();
    }

    /// <summary>Raja yang sedang mendapat giliran sedang diskak.</summary>
    public bool Skak => RajaDiserang(GiliranPutih);

    /// <summary>Semua langkah sah untuk pihak yang mendapat giliran.</summary>
    public List<Langkah> LangkahSah()
    {
        var hasil = new List<Langkah>(48);
        foreach (var l in LangkahSemu())
            if (!Jalankan(l).RajaDiserang(GiliranPutih))
                hasil.Add(l);
        return hasil;
    }

    /// <summary>Posisi baru sesudah langkah itu. Langkahnya dianggap sah.</summary>
    public Papan Jalankan(Langkah l)
    {
        var p = new Papan
        {
            GiliranPutih = !GiliranPutih,
            rokade = rokade,
            LangkahSetengah = LangkahSetengah + 1,
            NomorLangkah = NomorLangkah + (GiliranPutih ? 0 : 1),
        };
        Array.Copy(kotak, p.kotak, 64);
        var bidak = kotak[l.Dari];
        var putih = Putih(bidak);
        var jenis = char.ToLowerInvariant(bidak);
        p.kotak[l.Dari] = '\0';
        p.kotak[l.Ke] = l.Promosi == '\0' ? bidak : putih ? char.ToUpperInvariant(l.Promosi) : l.Promosi;
        if (kotak[l.Ke] != '\0')
            p.LangkahSetengah = 0;
        if (jenis == 'p')
        {
            p.LangkahSetengah = 0;
            if (l.Ke == EnPassant && kotak[l.Ke] == '\0')
                p.kotak[l.Ke + (putih ? -8 : 8)] = '\0';
            if (Math.Abs(l.Ke - l.Dari) == 16)
                p.EnPassant = (l.Dari + l.Ke) / 2;
        }
        else if (jenis == 'k' && Math.Abs(l.Ke - l.Dari) == 2)
        {
            // Rokade: benteng melompati raja.
            var (asal, tujuan) = l.Ke > l.Dari ? (l.Dari + 3, l.Dari + 1) : (l.Dari - 4, l.Dari - 1);
            p.kotak[tujuan] = p.kotak[asal];
            p.kotak[asal] = '\0';
        }
        if (jenis == 'k')
            p.rokade &= putih ? ~3 : ~12;
        p.rokade &= ~(HakDiKotak(l.Dari) | HakDiKotak(l.Ke));
        return p;
    }

    /// <summary>
    /// Posisi yang sama dengan giliran berpindah tanpa langkah (langkah nol),
    /// untuk melihat ancaman pihak yang baru melangkah. Null kalau pihak yang
    /// mendapat giliran sedang diskak.
    /// </summary>
    public Papan? Lewat()
    {
        if (Skak)
            return null;
        var p = new Papan
        {
            GiliranPutih = !GiliranPutih,
            rokade = rokade,
            LangkahSetengah = LangkahSetengah + 1,
            NomorLangkah = NomorLangkah + (GiliranPutih ? 0 : 1),
        };
        Array.Copy(kotak, p.kotak, 64);
        return p;
    }

    // Benteng yang pindah atau dimakan menghapus hak rokade di sisinya.
    static int HakDiKotak(int i) => i switch { 7 => 1, 0 => 2, 63 => 4, 56 => 8, _ => 0 };

    /// <summary>
    /// Notasi SAN langkah sah ini, mis. "Nbd7", "exd5", "O-O", "e8=Q+".
    /// <paramref name="sah"/>: langkah sah posisi ini kalau sudah dihitung.
    /// </summary>
    public string San(Langkah l, List<Langkah>? sah = null)
    {
        var bidak = kotak[l.Dari];
        var jenis = char.ToUpperInvariant(bidak);
        var sb = new StringBuilder(8);
        if (jenis == 'K' && Math.Abs(l.Ke - l.Dari) == 2)
            sb.Append(l.Ke > l.Dari ? "O-O" : "O-O-O");
        else
        {
            var makan = kotak[l.Ke] != '\0' || (jenis == 'P' && l.Ke == EnPassant);
            if (jenis == 'P')
            {
                if (makan)
                    sb.Append((char)('a' + l.Dari % 8)).Append('x');
                sb.Append(NamaKotak(l.Ke));
                if (l.Promosi != '\0')
                    sb.Append('=').Append(char.ToUpperInvariant(l.Promosi));
            }
            else
            {
                sb.Append(jenis);
                var saingan = (sah ?? LangkahSah()).Where(m => m.Ke == l.Ke && m.Dari != l.Dari && kotak[m.Dari] == bidak).ToList();
                if (saingan.Count > 0)
                {
                    if (!saingan.Any(m => m.Dari % 8 == l.Dari % 8))
                        sb.Append((char)('a' + l.Dari % 8));
                    else if (!saingan.Any(m => m.Dari / 8 == l.Dari / 8))
                        sb.Append((char)('1' + l.Dari / 8));
                    else
                        sb.Append(NamaKotak(l.Dari));
                }
                if (makan)
                    sb.Append('x');
                sb.Append(NamaKotak(l.Ke));
            }
        }
        var sesudah = Jalankan(l);
        if (sesudah.Skak)
            sb.Append(sesudah.LangkahSah().Count == 0 ? '#' : '+');
        return sb.ToString();
    }

    /// <summary>
    /// Langkah dari notasinya; null kalau tidak sah atau tidak jelas. Pemaaf:
    /// SAN dengan atau tanpa x, +, #, !, ?, =, pembeda berlebih ("Ngf3"),
    /// rokade dengan nol ("0-0"), dan UCI ("e2e4", "e7e8q"). Promosi tanpa
    /// bidak dianggap menteri.
    /// </summary>
    public Langkah? DariNotasi(string notasi)
    {
        var s = notasi.Trim().TrimEnd('+', '#', '!', '?');
        var sah = LangkahSah();
        if (s is "O-O" or "0-0" or "O-O-O" or "0-0-0")
        {
            var arah = s.Length == 3 ? 2 : -2;
            foreach (var m in sah)
                if (char.ToUpperInvariant(kotak[m.Dari]) == 'K' && m.Ke - m.Dari == arah)
                    return m;
            return null;
        }

        if (s.Length is 4 or 5 && Kotak(s[0], s[1]) is var asalUci and >= 0 && Kotak(s[2], s[3]) is var keUci and >= 0
            && (s.Length == 4 || "qrbnQRBN".Contains(s[4])))
        {
            var promosiUci = s.Length == 5 ? char.ToLowerInvariant(s[4]) : '\0';
            foreach (var m in sah)
                if (m.Dari == asalUci && m.Ke == keUci && (m.Promosi == promosiUci || (promosiUci == '\0' && m.Promosi == 'q')))
                    return m;
            return null;
        }

        var promosi = '\0';
        var sama = s.IndexOf('=');
        if (sama >= 0)
        {
            if (sama + 1 < s.Length)
                promosi = char.ToLowerInvariant(s[sama + 1]);
            s = s[..sama];
        }
        else if (s.Length >= 3 && "QRBN".Contains(s[^1]) && char.IsAsciiDigit(s[^2]))
        {
            promosi = char.ToLowerInvariant(s[^1]);
            s = s[..^1];
        }
        if (s.Length < 2 || Kotak(s[^2], s[^1]) is not (var ke and >= 0))
            return null;

        var awal = s[..^2].Replace("x", "").Replace(":", "").Replace("-", "");
        var jenis = 'P';
        if (awal.Length > 0 && "KQRBN".Contains(awal[0]))
        {
            jenis = awal[0];
            awal = awal[1..];
        }
        int? fileAsal = null, rankAsal = null;
        foreach (var c in awal)
        {
            if (c is >= 'a' and <= 'h')
                fileAsal = c - 'a';
            else if (c is >= '1' and <= '8')
                rankAsal = c - '1';
            else
                return null;
        }

        Langkah? cocok = null;
        foreach (var m in sah)
        {
            if (m.Ke != ke || char.ToUpperInvariant(kotak[m.Dari]) != jenis
                || (fileAsal is { } f && m.Dari % 8 != f) || (rankAsal is { } r && m.Dari / 8 != r)
                || m.Promosi != (m.Promosi == '\0' ? '\0' : promosi == '\0' ? 'q' : promosi))
                continue;
            if (cocok is not null)
                return null;   // ada dua yang cocok: notasinya tidak jelas
            cocok = m;
        }
        return cocok;
    }

    List<Langkah> LangkahSemu()
    {
        var hasil = new List<Langkah>(64);
        var putih = GiliranPutih;
        for (var i = 0; i < 64; i++)
        {
            if (!Milik(i, putih))
                continue;
            int f = i % 8, r = i / 8;
            switch (char.ToLowerInvariant(kotak[i]))
            {
                case 'p':
                    Pion(i, f, r, putih, hasil);
                    break;
                case 'n':
                    Lompat(i, f, r, ArahKuda, putih, hasil);
                    break;
                case 'b':
                    Geser(i, f, r, ArahGajah, putih, hasil);
                    break;
                case 'r':
                    Geser(i, f, r, ArahBenteng, putih, hasil);
                    break;
                case 'q':
                    Geser(i, f, r, ArahGajah, putih, hasil);
                    Geser(i, f, r, ArahBenteng, putih, hasil);
                    break;
                case 'k':
                    Lompat(i, f, r, ArahRaja, putih, hasil);
                    Rokade(i, putih, hasil);
                    break;
            }
        }
        return hasil;
    }

    void Lompat(int i, int f, int r, (int F, int R)[] arah, bool putih, List<Langkah> hasil)
    {
        foreach (var (df, dr) in arah)
        {
            int nf = f + df, nr = r + dr;
            if (nf is >= 0 and <= 7 && nr is >= 0 and <= 7 && !Milik(nr * 8 + nf, putih))
                hasil.Add(new(i, nr * 8 + nf));
        }
    }

    void Geser(int i, int f, int r, (int F, int R)[] arah, bool putih, List<Langkah> hasil)
    {
        foreach (var (df, dr) in arah)
            for (int nf = f + df, nr = r + dr; nf is >= 0 and <= 7 && nr is >= 0 and <= 7; nf += df, nr += dr)
            {
                var ke = nr * 8 + nf;
                if (Milik(ke, putih))
                    break;
                hasil.Add(new(i, ke));
                if (kotak[ke] != '\0')
                    break;
            }
    }

    void Pion(int i, int f, int r, bool putih, List<Langkah> hasil)
    {
        var maju = putih ? 1 : -1;
        var nr = r + maju;
        if (nr is < 0 or > 7)
            return;
        var promosi = nr == (putih ? 7 : 0);
        if (kotak[nr * 8 + f] == '\0')
        {
            TambahPion(i, nr * 8 + f, promosi, hasil);
            if (r == (putih ? 1 : 6) && kotak[(r + 2 * maju) * 8 + f] == '\0')
                hasil.Add(new(i, (r + 2 * maju) * 8 + f));
        }
        for (var df = -1; df <= 1; df += 2)
        {
            var nf = f + df;
            if (nf is < 0 or > 7)
                continue;
            var ke = nr * 8 + nf;
            if (Milik(ke, !putih) || ke == EnPassant)
                TambahPion(i, ke, promosi, hasil);
        }
    }

    static void TambahPion(int dari, int ke, bool promosi, List<Langkah> hasil)
    {
        if (!promosi)
            hasil.Add(new(dari, ke));
        else
            foreach (var p in "qrbn")
                hasil.Add(new(dari, ke, p));
    }

    // Rokade hanya dari kotak awal raja, dengan bentengnya di tempat. Kotak
    // tujuan raja diperiksa LangkahSah seperti langkah lain.
    void Rokade(int i, bool putih, List<Langkah> hasil)
    {
        var dasar = putih ? 0 : 56;
        if (i != dasar + 4 || (rokade & (putih ? 3 : 12)) == 0 || DiSerang(i, !putih))
            return;
        var benteng = putih ? 'R' : 'r';
        if ((rokade & (putih ? 1 : 4)) != 0 && kotak[dasar + 7] == benteng && kotak[dasar + 5] == '\0' && kotak[dasar + 6] == '\0'
            && !DiSerang(dasar + 5, !putih))
            hasil.Add(new(i, dasar + 6));
        if ((rokade & (putih ? 2 : 8)) != 0 && kotak[dasar] == benteng && kotak[dasar + 1] == '\0' && kotak[dasar + 2] == '\0'
            && kotak[dasar + 3] == '\0' && !DiSerang(dasar + 3, !putih))
            hasil.Add(new(i, dasar + 2));
    }

    bool RajaDiserang(bool putih)
    {
        var raja = Array.IndexOf(kotak, putih ? 'K' : 'k');
        return raja >= 0 && DiSerang(raja, !putih);
    }

    /// <summary>Kotak itu diserang bidak pihak <paramref name="olehPutih"/>.</summary>
    public bool DiSerang(int sasaran, bool olehPutih)
    {
        int f = sasaran % 8, r = sasaran / 8;
        // Pion putih menyerang ke atas, jadi berdiri satu baris di bawah sasarannya.
        var pr = r + (olehPutih ? -1 : 1);
        var pion = olehPutih ? 'P' : 'p';
        if (pr is >= 0 and <= 7 && ((f > 0 && kotak[pr * 8 + f - 1] == pion) || (f < 7 && kotak[pr * 8 + f + 1] == pion)))
            return true;
        return Ada(f, r, ArahKuda, olehPutih ? 'N' : 'n')
            || Ada(f, r, ArahRaja, olehPutih ? 'K' : 'k')
            || Sinar(f, r, ArahGajah, olehPutih ? 'B' : 'b', olehPutih ? 'Q' : 'q')
            || Sinar(f, r, ArahBenteng, olehPutih ? 'R' : 'r', olehPutih ? 'Q' : 'q');
    }

    bool Ada(int f, int r, (int F, int R)[] arah, char bidak)
    {
        foreach (var (df, dr) in arah)
        {
            int nf = f + df, nr = r + dr;
            if (nf is >= 0 and <= 7 && nr is >= 0 and <= 7 && kotak[nr * 8 + nf] == bidak)
                return true;
        }
        return false;
    }

    bool Sinar(int f, int r, (int F, int R)[] arah, char bidak, char menteri)
    {
        foreach (var (df, dr) in arah)
            for (int nf = f + df, nr = r + dr; nf is >= 0 and <= 7 && nr is >= 0 and <= 7; nf += df, nr += dr)
            {
                var c = kotak[nr * 8 + nf];
                if (c == '\0')
                    continue;
                if (c == bidak || c == menteri)
                    return true;
                break;
            }
        return false;
    }
}
