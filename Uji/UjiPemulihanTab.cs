using KevinBrowser;

namespace Uji;

public sealed class UjiTabTerbuka : IDisposable
{
    readonly string folder = Directory.CreateTempSubdirectory("kevin-browser-uji-").FullName;
    string Berkas => Path.Combine(folder, "tab.tsv");

    public void Dispose() => Directory.Delete(folder, true);

    [Fact]
    public void DisimpanLaluDibacaUrutDanUtuh()
    {
        var tab = new TabTerbuka(Berkas);
        tab.Simpan(
        [
            new("https://lichess.org/", "Main catur\tdi Lichess", [1, 2, 3, 250]),
            new("https://web.whatsapp.com/", "(3) WhatsApp", null),
        ]);

        var dibaca = new TabTerbuka(Berkas).Baca();
        Assert.Equal(2, dibaca.Count);
        Assert.Equal("https://lichess.org/", dibaca[0].Uri);
        Assert.Equal("Main catur di Lichess", dibaca[0].Judul);   // TAB di judul jadi spasi
        Assert.Equal(new byte[] { 1, 2, 3, 250 }, dibaca[0].Riwayat);
        Assert.Equal("(3) WhatsApp", dibaca[1].Judul);
        Assert.Null(dibaca[1].Riwayat);
    }

    [Fact]
    public void IsiSamaTidakDitulisUlang()
    {
        var tab = new TabTerbuka(Berkas);
        TabTersimpan[] daftar = [new("https://detik.com/", "Berita", null)];
        Assert.True(tab.Simpan(daftar));
        Assert.False(tab.Simpan(daftar));
        Assert.True(tab.Simpan([new("https://kompas.com/", "Berita", null)]));
    }

    [Fact]
    public void SemuaTabDitutupBerkasnyaDihapus()
    {
        var tab = new TabTerbuka(Berkas);
        tab.Simpan([new("https://detik.com/", "Berita", null)]);
        tab.Simpan([]);
        Assert.False(File.Exists(Berkas));
        Assert.Empty(tab.Baca());
    }

    [Fact]
    public void BarisRusakDilewati()
    {
        File.WriteAllText(Berkas,
            "https://detik.com/\tBerita\tbukan-base64!\n"
            + "baris tanpa tab\n"
            + "\tTanpa alamat\t\n"
            + "https://kompas.com/\tKompas\tAQID\n");
        var dibaca = new TabTerbuka(Berkas).Baca();
        Assert.Equal(["https://detik.com/", "https://kompas.com/"], dibaca.Select(t => t.Uri));
        Assert.Null(dibaca[0].Riwayat);   // riwayat rusak: dibuka di alamatnya saja
        Assert.Equal(new byte[] { 1, 2, 3 }, dibaca[1].Riwayat);
    }

    [Fact]
    public void BelumPernahDisimpan() => Assert.Empty(new TabTerbuka(Berkas).Baca());
}

public class UjiTabTertutup
{
    [Fact]
    public void YangTerakhirDitutupDibukaLebihDulu()
    {
        var tertutup = new TabTertutup();
        tertutup.Tambah(new("https://a.id/", "A", null), 1);
        tertutup.Tambah(new("https://b.id/", "B", null), 3);
        var b = tertutup.Ambil();
        Assert.Equal("https://b.id/", b?.Tab.Uri);
        Assert.Equal(3, b?.Posisi);
        Assert.Equal("https://a.id/", tertutup.Ambil()?.Tab.Uri);
        Assert.Null(tertutup.Ambil());
    }

    [Fact]
    public void YangPalingLamaDibuangSetelahSepuluh()
    {
        var tertutup = new TabTertutup();
        for (var i = 0; i < TabTertutup.JumlahMaks + 2; i++)
            tertutup.Tambah(new($"https://{i}.id/", "", null), 0);
        Assert.Equal(TabTertutup.JumlahMaks, tertutup.Jumlah);
        string? terakhir = null;
        while (tertutup.Ambil() is { } t)
            terakhir = t.Tab.Uri;
        Assert.Equal("https://2.id/", terakhir);
    }
}
