namespace KevinBrowser.Asisten;

/// <summary>
/// Pilihan asisten AI untuk menyerap materi: model DeepSeek dan kunci API
/// milik pemakai sendiri (BYOK). Disimpan di folder data browser, bukan di
/// folder catatan (yang bisa ikut tersalin atau di-commit aplikasi lain),
/// dalam berkas yang hanya bisa dibaca akun pemakai. Kuncinya hanya dikirim
/// ke <see cref="Alamat"/>.
/// </summary>
public sealed class PengaturanAi(string berkas)
{
    public const string Alamat = "https://api.deepseek.com";

    /// <summary>Yang pertama bawaan: lebih teliti menilai mana yang penting.</summary>
    public static readonly string[] SemuaModel = ["deepseek-v4-pro", "deepseek-flash"];

    public string Berkas => berkas;

    public string Model => Baca().GetValueOrDefault("model") is { } model && SemuaModel.Contains(model) ? model : SemuaModel[0];

    public string? Kunci => Baca().GetValueOrDefault("kunci") is { Length: > 0 } kunci ? kunci : null;

    /// <summary>"sk-…a1b2": cukup untuk mengenali kunci tanpa menampilkannya.</summary>
    public string? KunciTersamar => Kunci is { } kunci ? (kunci.Length > 10 ? $"{kunci[..3]}…{kunci[^4..]}" : "…") : null;

    /// <summary>Kunci API wajar: satu kata, tanpa spasi atau karakter kendali.</summary>
    public static bool KunciSah(string kunci) =>
        kunci.Length is >= 10 and <= 200 && !kunci.Any(c => char.IsWhiteSpace(c) || char.IsControl(c));

    /// <summary>Model dan kunci baru; kunci null = kunci lama tetap.</summary>
    public void Simpan(string model, string? kunci)
    {
        var isi = Baca();
        if (SemuaModel.Contains(model))
            isi["model"] = model;
        if (kunci is not null && KunciSah(kunci))
            isi["kunci"] = kunci;
        Tulis(isi);
    }

    public void HapusKunci()
    {
        var isi = Baca();
        if (isi.Remove("kunci"))
            Tulis(isi);
    }

    Dictionary<string, string> Baca()
    {
        var hasil = new Dictionary<string, string>();
        try
        {
            foreach (var baris in File.ReadLines(berkas))
                if (baris.Split('\t', 2) is [var kunci, var nilai])
                    hasil[kunci] = nilai;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
        }
        return hasil;
    }

    // Berkas baru dibuat langsung dengan izin 600 (hanya pemiliknya), lalu
    // menggantikan yang lama, jadi kuncinya tidak pernah terbaca akun lain.
    void Tulis(Dictionary<string, string> isi)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(berkas))!);
        var sementara = berkas + ".tmp";
        File.Delete(sementara);
        var opsi = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
            opsi.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var tulis = new StreamWriter(new FileStream(sementara, opsi)))
            foreach (var (kunci, nilai) in isi)
                tulis.Write($"{kunci}\t{nilai}\n");
        File.Move(sementara, berkas, true);
    }
}
