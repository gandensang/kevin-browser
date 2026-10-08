using System.Security.Cryptography;

namespace KevinBrowser.Asisten;

/// <summary>
/// Stockfish versi WebAssembly (stockfish.js, GPL-3.0), diunduh sekali dari
/// rilis GitHub Kevin Browser saat pemakai menekan tombol pasang, lalu
/// disimpan di folder data sebagai berkas terpisah (Kevin Browser tetap MIT).
/// Jalan di halaman catur sebagai Web Worker, satu thread, tanpa
/// SharedArrayBuffer.
/// </summary>
/// <remarks>
/// Varian "lite single" (±1,7 MB termasuk jaringan sarafnya, cocok untuk
/// laptop lemah dan kuota terbatas) yang dibangun sendiri TANPA WASM SIMD
/// (scripts/bangun-stockfish.sh, hasilnya sama persis tiap dibangun). Versi
/// resmi stockfish.js butuh SIMD, dan WebKit mematikan WASM SIMD di prosesor
/// x86-64 tanpa AVX: di laptop seperti itu mesinnya diam saja (terjadi 8 Okt
/// 2026). Harganya, terukur di laptop pengembang: ±4× lebih lambat dari versi
/// SIMD (500 ms → depth 11–12 lawan 15), tetapi ±2–4× lebih cepat dari versi
/// JavaScript murni stockfish.js. Hanya berkas yang sidik SHA-256-nya persis
/// sama dengan di bawah yang disimpan dan disajikan: kodenya jalan di halaman
/// kevin://, jadi tidak boleh ada yang bisa menyelipkan skrip lain.
/// </remarks>
public sealed class MesinCatur(string folder, IJaringan jaringan, string asal = MesinCatur.AsalRilis)
{
    /// <summary>Rilis GitHub tempat berkas mesin dan arsip sumbernya (GPL) diunggah.</summary>
    public const string AsalRilis = "https://github.com/gandensang/kevin-browser/releases/download/stockfish-19-tanpa-simd/";

    /// <summary>Halaman rilis itu: berkas mesin, sumber, dan cara membangunnya.</summary>
    public const string HalamanRilis = "https://github.com/gandensang/kevin-browser/releases/tag/stockfish-19-tanpa-simd";

    public const string Nama = "Stockfish 19";

    /// <summary>Berkas skrip; berkas WASM-nya disebut di belakang # alamatnya (lihat HalamanCatur.Perantara).</summary>
    public const string Skrip = "stockfish-19-lite-tanpa-simd.js";

    public const string Wasm = "stockfish-19-lite-tanpa-simd.wasm";

    internal static readonly (string Nama, long Ukuran, string Sha256)[] Berkas =
    [
        (Skrip, 21_415, "f141b71ed421870ffa226abfc89792d5c282768fefb31c510f0198f4911df28e"),
        (Wasm, 1_785_285, "7e4ed1cd9657607928347f1fb4aa3f1e3d12b819b8ea257e7aa3a40707494a4b"),
    ];

    /// <summary>Besar unduhan seluruhnya, dalam bait.</summary>
    public static long Ukuran => Berkas.Sum(b => b.Ukuran);

    public bool Terpasang => Berkas.All(b => new FileInfo(Path.Combine(folder, b.Nama)) is { Exists: true } info && info.Length == b.Ukuran);

    readonly object gembok = new();

    // Pemasangan yang sedang berjalan: klik kedua, atau formulir yang
    // terkirim dua kali, menunggu yang sama, bukan mengunduh lagi.
    Task? berjalan;
    long terunduh;

    /// <summary>Bait yang sudah diunduh dan besar seluruhnya; (0, 0) kalau tidak sedang memasang.</summary>
    public (long Bait, long Total) Kemajuan => berjalan is { IsCompleted: false } ? (Interlocked.Read(ref terunduh), Ukuran) : (0, 0);

    /// <summary>
    /// Mengunduh dan memeriksa semua berkas. Berkas yang sidiknya lain
    /// dibuang (InvalidDataException); jaringan atau jawaban selain 200 jadi GalatAi.
    /// </summary>
    public Task Pasang(CancellationToken batal)
    {
        lock (gembok)
        {
            if (berjalan is { IsCompleted: false })
                return berjalan;
            Interlocked.Exchange(ref terunduh, 0);
            return berjalan = Task.Run(() => Unduh(batal), batal);
        }
    }

    async Task Unduh(CancellationToken batal)
    {
        Directory.CreateDirectory(folder);
        long sebelumnya = 0;
        foreach (var (nama, ukuran, sha256) in Berkas)
        {
            var jalur = Path.Combine(folder, nama);
            if (new FileInfo(jalur) is not { Exists: true } ada || ada.Length != ukuran)
            {
                using var isi = new MemoryStream();
                var dasar = sebelumnya;
                var status = await jaringan.Unduh(new PermintaanHttp("GET", asal + nama, [("User-Agent", $"KevinBrowser/{HalamanBawaan.Versi}")]),
                    new AliranHitung(isi, n => Interlocked.Exchange(ref terunduh, dasar + n)), ukuran, batal);
                if (status != 200)
                    throw new GalatAi(status, nama);
                var bait = isi.ToArray();
                if (bait.Length != ukuran || Convert.ToHexStringLower(SHA256.HashData(bait)) != sha256)
                    throw new InvalidDataException($"{nama}: isinya tidak sama dengan yang diharapkan");
                File.WriteAllBytes(jalur + ".baru", bait);
                File.Move(jalur + ".baru", jalur, true);
            }
            sebelumnya += ukuran;
            Interlocked.Exchange(ref terunduh, sebelumnya);
        }
    }

    /// <summary>Isi berkas mesin untuk kevin://mesin/…; null kalau bukan berkas mesin atau belum terpasang.</summary>
    public byte[]? Baca(string nama)
    {
        if (!Berkas.Any(b => b.Nama == nama) || !Terpasang)
            return null;
        return File.ReadAllBytes(Path.Combine(folder, nama));
    }

    /// <summary>Melepas mesin (menghapus berkasnya).</summary>
    public void Hapus()
    {
        foreach (var (nama, _, _) in Berkas)
            File.Delete(Path.Combine(folder, nama));
    }
}

/// <summary>Aliran tulis yang meneruskan ke aliran lain sambil menghitung baitnya.</summary>
sealed class AliranHitung(Stream tujuan, Action<long> tiapTulis) : Stream
{
    long jumlah;

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => jumlah;
    public override long Position { get => jumlah; set => throw new NotSupportedException(); }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        tujuan.Write(buffer);
        jumlah += buffer.Length;
        tiapTulis(jumlah);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Write(buffer.Span);
        return ValueTask.CompletedTask;
    }

    public override void Flush() => tujuan.Flush();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
