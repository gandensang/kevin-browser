using System.Globalization;

namespace KevinBrowser;

/// <summary>
/// Bahasa tampilan browser: toolbar, pesan, dan halaman kevin://. Bawaannya
/// Indonesia; diganti di kevin://pengaturan (<see cref="Preferensi"/>).
/// Situs web tidak terpengaruh.
/// </summary>
public enum Bahasa
{
    Indonesia,
    Inggris,
}

/// <summary>
/// Teks dalam bahasa pilihan pemakai. Setiap teks ditulis dua bahasa di
/// tempat ia dipakai, <c>t["Simpan", "Save"]</c>, jadi tidak ada yang
/// tertinggal tanpa terjemahan. Halaman tetap (beranda, panduan, …) punya
/// berkas Inggris sendiri di Halaman/en/.
/// </summary>
public readonly record struct Teks(Bahasa Bahasa)
{
    static readonly string[] NamaHari = ["Minggu", "Senin", "Selasa", "Rabu", "Kamis", "Jumat", "Sabtu"];
    static readonly string[] NamaBulan =
        ["Januari", "Februari", "Maret", "April", "Mei", "Juni", "Juli", "Agustus", "September", "Oktober", "November", "Desember"];
    static readonly string[] NamaHariInggris = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
    static readonly string[] NamaBulanInggris =
        ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

    public bool Inggris => Bahasa == Bahasa.Inggris;

    public string this[string indonesia, string inggris] => Inggris ? inggris : indonesia;

    /// <summary>Kode untuk atribut lang: "id" atau "en".</summary>
    public string Kode => Inggris ? "en" : "id";

    /// <summary>"56.899" atau "56,899". Tanpa ICU (InvariantGlobalization), jadi diatur sendiri.</summary>
    public string Angka(long n)
    {
        var teks = n.ToString("N0", CultureInfo.InvariantCulture);
        return Inggris ? teks : teks.Replace(',', '.');
    }

    /// <summary>"Kamis, 1 Oktober 2026" atau "Thursday, 1 October 2026".</summary>
    public string Tanggal(DateTime t) => Inggris
        ? $"{NamaHariInggris[(int)t.DayOfWeek]}, {t.Day} {NamaBulanInggris[t.Month - 1]} {t.Year}"
        : $"{NamaHari[(int)t.DayOfWeek]}, {t.Day} {NamaBulan[t.Month - 1]} {t.Year}";

    /// <summary>"3 Okt 2026" atau "3 Oct 2026"; tanpa tahun: "3 Okt".</summary>
    public string TanggalSingkat(DateTime t, bool denganTahun = true)
    {
        var bulan = (Inggris ? NamaBulanInggris : NamaBulan)[t.Month - 1][..3];
        return denganTahun ? $"{t.Day} {bulan} {t.Year}" : $"{t.Day} {bulan}";
    }

    /// <summary>"09.05" atau "09:05".</summary>
    public string Jam(DateTimeOffset t) => t.ToString(Inggris ? "HH:mm" : "HH.mm", CultureInfo.InvariantCulture);

    /// <summary>"76 MB", "2,7 MB" (Indonesia) atau "2.7 MB" (Inggris), "450 KB".</summary>
    public string Ukuran(long bait)
    {
        if (bait < 1024)
            return $"{bait} B";
        if (bait < 1024 * 1024)
            return $"{bait / 1024} KB";
        var mb = bait / (1024.0 * 1024);
        var teks = (mb < 10 ? mb.ToString("0.0", CultureInfo.InvariantCulture) : mb.ToString("0", CultureInfo.InvariantCulture)) + " MB";
        return Inggris ? teks : teks.Replace('.', ',');
    }
}
