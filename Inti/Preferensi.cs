namespace KevinBrowser;

/// <summary>
/// Pilihan pemakai dari kevin://pengaturan, di berkas teks di folder data
/// (satu baris per pilihan: kunci TAB nilai). Sekarang hanya bahasa.
/// Hanya proses ini yang menulis berkasnya (instans tunggal).
/// </summary>
public sealed class Preferensi(string berkas)
{
    Bahasa? bahasa;

    public string Berkas { get; } = berkas;

    /// <summary>Dipancarkan setelah pilihan berubah dan tersimpan.</summary>
    public event Action? Berubah;

    /// <summary>Bawaannya Indonesia, juga kalau berkasnya tidak ada atau rusak.</summary>
    public Bahasa Bahasa
    {
        get => bahasa ??= Baca("bahasa") == "en" ? Bahasa.Inggris : Bahasa.Indonesia;
        set
        {
            if (value == Bahasa)
                return;
            bahasa = value;
            Tulis("bahasa", value == Bahasa.Inggris ? "en" : "id");
            Berubah?.Invoke();
        }
    }

    public Teks Teks => new(Bahasa);

    string? Baca(string kunci)
    {
        try
        {
            foreach (var baris in File.ReadLines(Berkas))
                if (baris.Split('\t', 2) is [var k, var nilai] && k == kunci)
                    return nilai.Trim();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Belum ada atau tidak terbaca: pakai bawaan.
        }
        return null;
    }

    // Kunci lain di berkas yang sama dibiarkan apa adanya.
    void Tulis(string kunci, string nilai)
    {
        var baris = File.Exists(Berkas)
            ? File.ReadLines(Berkas).Where(b => b.Split('\t', 2)[0] != kunci).ToList()
            : [];
        baris.Add($"{kunci}\t{nilai}");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(Berkas))!);
        var sementara = Berkas + ".baru";
        File.WriteAllLines(sementara, baris);
        File.Move(sementara, Berkas, overwrite: true);
    }
}
