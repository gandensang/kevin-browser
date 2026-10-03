namespace KevinBrowser;

public static class NamaBerkas
{
    /// <summary>
    /// Nama untuk berkas unduhan yang belum dipakai di <paramref name="folder"/>:
    /// "tugas.pdf", lalu "tugas (1).pdf", dst. Tanpa ini WebKit menolak
    /// mengunduh berkas yang namanya sudah ada.
    /// </summary>
    public static string Unik(string folder, string usulan)
    {
        var nama = Path.GetFileName(usulan.Trim());
        if (nama.Length == 0)
            nama = "unduhan";
        if (!Path.Exists(Path.Combine(folder, nama)))
            return nama;

        var dasar = Path.GetFileNameWithoutExtension(nama);
        var ekstensi = Path.GetExtension(nama);
        for (var i = 1; ; i++)
        {
            var calon = $"{dasar} ({i}){ekstensi}";
            if (!Path.Exists(Path.Combine(folder, calon)))
                return calon;
        }
    }
}
