namespace KevinBrowser.Asisten;

/// <summary>
/// Pilihan asisten AI untuk menyerap materi: model DeepSeek dan kunci API
/// milik pemakai sendiri (BYOK). Disimpan di folder data browser, bukan di
/// folder catatan (yang bisa ikut tersalin atau di-commit aplikasi lain),
/// dalam berkas yang hanya bisa dibaca akun pemakai. Kuncinya hanya dikirim
/// ke <see cref="Alamat"/>.
/// </summary>
/// <param name="alamat">Alamat API; selain di build Debug untuk uji, selalu <see cref="AlamatDeepSeek"/>.</param>
public sealed class PengaturanAi(string berkas, string alamat = PengaturanAi.AlamatDeepSeek)
{
    public const string AlamatDeepSeek = "https://api.deepseek.com";

    public string Alamat => alamat;

    /// <summary>Yang pertama bawaan: lebih teliti menilai mana yang penting.</summary>
    public static readonly string[] SemuaModel = ["deepseek-v4-pro", "deepseek-flash"];

    public string Berkas => berkas;

    /// <summary>Untuk menyerap materi: sekali per dokumen, penilaiannya menentukan mutu catatan.</summary>
    public string Model => Baca().GetValueOrDefault("model") is { } model && SemuaModel.Contains(model) ? model : SemuaModel[0];

    /// <summary>Untuk tanya-jawab: dipakai sering, jadi bawaannya yang murah.</summary>
    public string ModelTanya => Baca().GetValueOrDefault("model-tanya") is { } model && SemuaModel.Contains(model) ? model : "deepseek-flash";

    public string? Kunci => Baca().GetValueOrDefault("kunci") is { Length: > 0 } kunci ? kunci : null;

    /// <summary>"sk-…a1b2": cukup untuk mengenali kunci tanpa menampilkannya.</summary>
    public string? KunciTersamar => Kunci is { } kunci ? (kunci.Length > 10 ? $"{kunci[..3]}…{kunci[^4..]}" : "…") : null;

    /// <summary>Kunci API wajar: satu kata, tanpa spasi atau karakter kendali.</summary>
    public static bool KunciSah(string kunci) =>
        kunci.Length is >= 10 and <= 200 && !kunci.Any(c => char.IsWhiteSpace(c) || char.IsControl(c));

    /// <summary>Model dan kunci baru; kunci null = kunci lama tetap, model tak dikenal diabaikan.</summary>
    public void Simpan(string model, string? kunci, string? modelTanya = null)
    {
        var isi = Baca();
        if (SemuaModel.Contains(model))
            isi["model"] = model;
        if (modelTanya is not null && SemuaModel.Contains(modelTanya))
            isi["model-tanya"] = modelTanya;
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
