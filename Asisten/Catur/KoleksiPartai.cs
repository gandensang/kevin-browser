using System.Text;
using System.Text.Json;

namespace KevinBrowser.Asisten;

/// <summary>
/// Nama akun Lichess dan Chess.com yang partainya diambil. Hanya nama:
/// partai publik di kedua situs bisa diambil tanpa login, dan tidak ada
/// kata sandi atau token yang disimpan.
/// </summary>
public sealed class AkunCatur(string berkas)
{
    readonly object kunci = new();
    (string? Lichess, string? ChessCom)? isi;

    public string? Lichess => Isi().Lichess;

    public string? ChessCom => Isi().ChessCom;

    public bool Ada => Lichess is not null || ChessCom is not null;

    /// <summary>Huruf, angka, _ dan -, 2–30 huruf (aturan nama akun di kedua situs).</summary>
    public static bool NamaSah(string nama) =>
        nama.Length is >= 2 and <= 30 && nama.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    /// <summary>Kosong atau null = akun itu tidak dihubungkan.</summary>
    public void Simpan(string? lichess, string? chessCom)
    {
        static string? Rapikan(string? nama) => string.IsNullOrWhiteSpace(nama) ? null : nama.Trim();
        lock (kunci)
        {
            isi = (Rapikan(lichess), Rapikan(chessCom));
            var teks = (isi.Value.Lichess is { } l ? $"lichess\t{l}\n" : "") + (isi.Value.ChessCom is { } c ? $"chesscom\t{c}\n" : "");
            Directory.CreateDirectory(Path.GetDirectoryName(berkas)!);
            File.WriteAllText(berkas + ".baru", teks);
            File.Move(berkas + ".baru", berkas, true);
        }
    }

    (string? Lichess, string? ChessCom) Isi()
    {
        lock (kunci)
        {
            if (isi is { } ada)
                return ada;
            string? lichess = null, chessCom = null;
            if (File.Exists(berkas))
                foreach (var baris in File.ReadAllLines(berkas))
                {
                    var bagian = baris.Split('\t');
                    if (bagian.Length != 2 || !NamaSah(bagian[1]))
                        continue;
                    if (bagian[0] == "lichess")
                        lichess = bagian[1];
                    else if (bagian[0] == "chesscom")
                        chessCom = bagian[1];
                }
            isi = (lichess, chessCom);
            return isi.Value;
        }
    }
}

/// <summary>
/// Partai yang tersimpan di laptop: dari akun Lichess dan Chess.com
/// (<see cref="AkunCatur"/>) dan yang ditempel. Satu berkas PGN per sumber
/// di folder data, jadi daftar partai tetap ada tanpa internet dan partai
/// tidak diunduh ulang (hemat kuota). Paling banyak <see cref="MaksPerSumber"/>
/// partai terbaru per berkas. Berkas dibaca sekali lalu diingat selama
/// waktu ubahnya tetap; langkahnya baru diurai saat partai dibuka.
/// </summary>
public sealed class KoleksiPartai(string folder, IJaringan jaringan)
{
    public const int MaksPerSumber = 300;

    /// <summary>Banyaknya partai terbaru yang diminta dari Lichess sekali ambil.</summary>
    public const int AmbilLichessMaks = 30;

    const string JenisBiasa = "ultraBullet,bullet,blitz,rapid,classical,correspondence";

    // Chess.com meminta aplikasi memperkenalkan diri.
    static readonly (string, string) Agen = ("User-Agent", $"KevinBrowser/{HalamanBawaan.Versi} (+https://github.com/gandensang/kevin-browser)");

    readonly object kunci = new();
    readonly Dictionary<string, (DateTime Ubah, long Ukuran, List<Partai> Isi)> ingatan = [];

    public AkunCatur Akun { get; } = new(Path.Combine(folder, "akun.tsv"));

    /// <summary>Skor tebak langkah yang sudah selesai, pembanding latihan berikutnya.</summary>
    public RiwayatSkor Skor { get; } = new(Path.Combine(folder, "skor.tsv"));

    string BerkasLichess(string nama) => Path.Combine(folder, $"lichess-{nama.ToLowerInvariant()}.pgn");

    string BerkasChessCom(string nama) => Path.Combine(folder, $"chesscom-{nama.ToLowerInvariant()}.pgn");

    string BerkasTempel => Path.Combine(folder, "tempel.pgn");

    /// <summary>
    /// Partai akun yang sedang dihubungkan dan partai tempelan, tanpa dobel,
    /// yang terbaru dulu (tempelan tanpa tanggal paling akhir).
    /// </summary>
    public List<Partai> Semua()
    {
        var sumber = new List<Partai>();
        if (Akun.Lichess is { } l)
            sumber.AddRange(BacaBerkas(BerkasLichess(l)));
        if (Akun.ChessCom is { } c)
            sumber.AddRange(BacaBerkas(BerkasChessCom(c)));
        sumber.AddRange(BacaBerkas(BerkasTempel));
        var terlihat = new HashSet<string>(StringComparer.Ordinal);
        return [.. sumber.Where(p => CaturBiasa(p) && terlihat.Add(p.Id)).OrderByDescending(p => p.Waktu ?? DateTime.MinValue)];
    }

    public Partai? Cari(string id) => Semua().FirstOrDefault(p => p.Id == id);

    /// <summary>
    /// Partai terbaru akun Lichess itu, dengan nilai mesin kalau partainya
    /// pernah dianalisis di lichess.org. Hasilnya banyaknya partai yang baru.
    /// Galat jaringan dan jawaban selain 200 dilempar sebagai <see cref="GalatAi"/>.
    /// </summary>
    public async Task<int> AmbilLichess(string nama, CancellationToken batal)
    {
        // Hanya catur biasa (perfType): varian seperti Chess960 tidak ikut diunduh.
        var pgn = await Teks($"https://lichess.org/api/games/user/{Uri.EscapeDataString(nama)}?max={AmbilLichessMaks}&perfType={JenisBiasa}&opening=true&evals=true&clocks=false",
            "application/x-chess-pgn", batal);
        return Gabung(BerkasLichess(nama), Pgn.Pisah(pgn), urutWaktu: true);
    }

    /// <summary>
    /// Partai akun Chess.com itu dari arsip bulanannya: dua bulan terakhir
    /// pada pengambilan pertama, sesudahnya bulan terakhir saja.
    /// </summary>
    public async Task<int> AmbilChessCom(string nama, CancellationToken batal)
    {
        var dasar = $"https://api.chess.com/pub/player/{Uri.EscapeDataString(nama.ToLowerInvariant())}/games/";
        using var dok = JsonDocument.Parse(await Teks(dasar + "archives", "application/json", batal));
        // Hanya alamat arsip akun ini yang diikuti, bukan alamat apa saja dari jawabannya.
        var arsip = dok.RootElement.TryGetProperty("archives", out var daftar) && daftar.ValueKind == JsonValueKind.Array
            ? daftar.EnumerateArray().Select(a => a.GetString() ?? "").Where(a => a.StartsWith(dasar, StringComparison.OrdinalIgnoreCase)).ToList()
            : [];
        var berkas = BerkasChessCom(nama);
        var baru = new List<Partai>();
        foreach (var bulan in arsip.TakeLast(File.Exists(berkas) ? 1 : 2))
            baru.AddRange(Pgn.Pisah(await Teks(bulan + "/pgn", "application/x-chess-pgn", batal)));
        return Gabung(berkas, baru, urutWaktu: true);
    }

    /// <summary>
    /// "https://lichess.org/abcdEFGH" → "abcdEFGH", juga dengan sisi
    /// ("…/black"), nomor langkah ("#32"), atau 12 huruf (tautan pemain).
    /// </summary>
    public static string? IdLichess(string teks)
    {
        var t = teks.Trim();
        var awal = t.IndexOf("lichess.org/", StringComparison.OrdinalIgnoreCase);
        if (awal < 0 || t.Contains('\n') || t.Contains(' '))
            return null;
        var bagian = t[(awal + 12)..];
        var akhir = bagian.IndexOfAny(['/', '#', '?']);
        var id = akhir < 0 ? bagian : bagian[..akhir];
        return id.Length is 8 or 12 && id.All(char.IsAsciiLetterOrDigit) && id[..8] is not ("analysis" or "training" or "practice" or "streamer")
            ? id[..8]
            : null;
    }

    /// <summary>Satu partai Lichess dari tautannya, disimpan bersama partai tempelan.</summary>
    public async Task<Partai?> AmbilSatuLichess(string id, CancellationToken batal)
    {
        var pgn = await Teks($"https://lichess.org/game/export/{id}?evals=true&opening=true&clocks=false", "application/x-chess-pgn", batal);
        var partai = Pgn.Pisah(pgn).FirstOrDefault();
        if (partai is null || partai.Urai().Langkah.Count == 0)
            return null;
        Gabung(BerkasTempel, [partai], urutWaktu: false);
        return partai;
    }

    /// <summary>
    /// Menyimpan partai dari PGN yang ditempel. Hasilnya partai pertama yang
    /// sah, atau galat langkah pertama yang tidak sah ("12... Nf6").
    /// </summary>
    public (Partai? Partai, string? Galat) Tempel(string pgn)
    {
        var sah = new List<Partai>();
        string? galat = null;
        foreach (var p in Pgn.Pisah(pgn))
        {
            var u = p.Urai();
            if (u.Galat is not null)
                galat ??= u.Galat;
            else if (u.Langkah.Count > 0)
                sah.Add(p);
        }
        if (sah.Count == 0)
            return (null, galat ?? "");
        Gabung(BerkasTempel, sah, urutWaktu: false);
        return (sah[0], null);
    }

    async Task<string> Teks(string alamat, string jenis, CancellationToken batal)
    {
        var teks = new StringBuilder();
        var status = await jaringan.Kirim(new PermintaanHttp("GET", alamat, [("Accept", jenis), Agen]),
            baris => teks.Append(baris).Append('\n'), batal);
        if (status != 200)
            throw new GalatAi(status, teks.Length > 300 ? teks.ToString(0, 300) : teks.ToString());
        return teks.ToString();
    }

    // Partai baru menggantikan yang sama (mis. sesudah dianalisis), lalu yang
    // terbaru dulu, paling banyak MaksPerSumber. Hasilnya banyaknya yang baru.
    int Gabung(string berkas, List<Partai> baru, bool urutWaktu)
    {
        // Varian (Chess960, Crazyhouse, …) punya aturan lain; Papan hanya catur biasa.
        baru = baru.Where(CaturBiasa).ToList();
        lock (kunci)
        {
            var lama = BacaBerkas(berkas);
            var idLama = lama.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
            var idBaru = baru.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
            IEnumerable<Partai> semua = [.. baru.DistinctBy(p => p.Id), .. lama.Where(p => !idBaru.Contains(p.Id))];
            if (urutWaktu)
                semua = semua.OrderByDescending(p => p.Waktu ?? DateTime.MinValue);
            var simpan = semua.Take(MaksPerSumber).ToList();
            Directory.CreateDirectory(folder);
            File.WriteAllText(berkas + ".baru", string.Join('\n', simpan.Select(p => p.Pgn)));
            File.Move(berkas + ".baru", berkas, true);
            var info = new FileInfo(berkas);
            lock (ingatan)
                ingatan[berkas] = (info.LastWriteTimeUtc, info.Length, simpan);
            return idBaru.Count(id => !idLama.Contains(id));
        }
    }

    /// <summary>Catur biasa, juga dari posisi tertentu; bukan Chess960 dan varian lain.</summary>
    public static bool CaturBiasa(Partai p) => p["Variant"] is null or "Standard" or "From Position";

    List<Partai> BacaBerkas(string jalur)
    {
        var info = new FileInfo(jalur);
        if (!info.Exists)
            return [];
        lock (ingatan)
            if (ingatan.TryGetValue(jalur, out var lama) && lama.Ubah == info.LastWriteTimeUtc && lama.Ukuran == info.Length)
                return lama.Isi;
        var isi = Pgn.Pisah(File.ReadAllText(jalur));
        lock (ingatan)
            ingatan[jalur] = (info.LastWriteTimeUtc, info.Length, isi);
        return isi;
    }
}
