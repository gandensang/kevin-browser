using KevinBrowser;

namespace Uji;

public class UjiTidurTab
{
    sealed record TabPalsu(string Nama, bool Hidup, bool Dilindungi, long TerakhirAktif) : ITab;

    static List<string> Pilih(TabPalsu aktif, int maks, params TabPalsu[] latar) =>
        TidurTab.PilihYangDitidurkan(latar.Append(aktif), aktif, maks, Sekarang, TidurSetelah).Select(t => t.Nama).ToList();

    // Dalam milidetik, seperti Environment.TickCount64.
    const long Sekarang = 100;
    static readonly TimeSpan TidurSetelah = TimeSpan.FromMinutes(5);
    const long Menit = 60_000;

    [Fact]
    public void DalamBatasTidakAdaYangDitidurkan()
    {
        var aktif = new TabPalsu("aktif", true, false, 100);
        Assert.Empty(Pilih(aktif, 3,
            new TabPalsu("a", true, false, 10),
            new TabPalsu("b", true, false, 20)));
    }

    [Fact]
    public void YangPalingLamaTidakDilihatDitidurkanLebihDulu()
    {
        var aktif = new TabPalsu("aktif", true, false, 100);
        Assert.Equal(["lama", "sedang"], Pilih(aktif, 2,
            new TabPalsu("baru", true, false, 90),
            new TabPalsu("lama", true, false, 10),
            new TabPalsu("sedang", true, false, 50)));
    }

    [Fact]
    public void TabDilindungiDilewati()
    {
        var aktif = new TabPalsu("aktif", true, false, 100);
        Assert.Equal(["biasa"], Pilih(aktif, 2,
            new TabPalsu("formulir", true, true, 10),
            new TabPalsu("biasa", true, false, 50)));
    }

    [Fact]
    public void TabYangSudahTidurTidakDihitung()
    {
        var aktif = new TabPalsu("aktif", true, false, 100);
        Assert.Empty(Pilih(aktif, 2,
            new TabPalsu("tidur1", false, false, 10),
            new TabPalsu("tidur2", false, false, 20),
            new TabPalsu("hidup", true, false, 50)));
    }

    [Fact]
    public void TabAktifTidakPernahDipilihWalauBatasNol()
    {
        var aktif = new TabPalsu("aktif", true, false, 1);
        Assert.Equal(["a"], Pilih(aktif, 0, new TabPalsu("a", true, false, 50)));
    }

    [Fact]
    public void TabKosongYangAktifTidakMemakaiJatah()
    {
        // Tab baru yang belum diisi alamat belum punya WebView.
        var aktif = new TabPalsu("kosong", false, false, 100);
        Assert.Empty(Pilih(aktif, 1, new TabPalsu("a", true, false, 50)));
    }

    [Fact]
    public void DiamLimaMenitDitidurkanWalauDalamBatas()
    {
        var aktif = new TabPalsu("aktif", true, false, Sekarang);
        Assert.Equal(["diam"], Pilih(aktif, 4,
            new TabPalsu("diam", true, false, Sekarang - 5 * Menit),
            new TabPalsu("baru", true, false, Sekarang - 4 * Menit)));
    }

    [Fact]
    public void DiamTapiDilindungiTetapHidup()
    {
        // Mis. WhatsApp Web, atau tab yang memutar musik.
        var aktif = new TabPalsu("aktif", true, false, Sekarang);
        Assert.Empty(Pilih(aktif, 4, new TabPalsu("whatsapp", true, true, Sekarang - 60 * Menit)));
    }

    [Fact]
    public void TabAktifTidakDitidurkanWalauLamaTerbuka()
    {
        var aktif = new TabPalsu("aktif", true, false, Sekarang - 60 * Menit);
        Assert.Empty(Pilih(aktif, 4));
    }

    [Fact]
    public void DiamDanKelebihanDijumlahTanpaDobel()
    {
        // Batas 2: setelah "diam" ditidurkan masih ada aktif + b + c = 3 tab
        // hidup, jadi satu lagi (yang paling lama tidak dilihat) ikut tidur.
        var aktif = new TabPalsu("aktif", true, false, Sekarang);
        Assert.Equal(["diam", "b"], Pilih(aktif, 2,
            new TabPalsu("c", true, false, Sekarang - 1 * Menit),
            new TabPalsu("diam", true, false, Sekarang - 9 * Menit),
            new TabPalsu("b", true, false, Sekarang - 2 * Menit)));
    }

    [Fact]
    public void TabDilindungiTetapMemakaiJatah()
    {
        // Laptop 2 GB (batas 2) dengan WhatsApp terbuka: yang hidup hanya
        // tab aktif dan WhatsApp.
        var aktif = new TabPalsu("aktif", true, false, Sekarang);
        Assert.Equal(["lain"], Pilih(aktif, 2,
            new TabPalsu("whatsapp", true, true, Sekarang - 1 * Menit),
            new TabPalsu("lain", true, false, Sekarang - 1 * Menit)));
    }

    [Fact]
    public void RamMenipisMenidurkanSemuaTabLatarYangTidakDilindungi()
    {
        // Masih dalam batas jumlah dan belum 5 menit, tetapi RAM hampir habis.
        var aktif = new TabPalsu("aktif", true, false, Sekarang);
        var latar = new[]
        {
            new TabPalsu("baru", true, false, Sekarang - 1 * Menit),
            new TabPalsu("whatsapp", true, true, Sekarang - 2 * Menit),
            new TabPalsu("lama", true, false, Sekarang - 3 * Menit),
            new TabPalsu("tidur", false, false, Sekarang - 4 * Menit),
        };
        Assert.Empty(Pilih(aktif, 4, latar));
        Assert.Equal(["lama", "baru"], TidurTab.PilihYangDitidurkan(latar.Append(aktif), aktif, 4, Sekarang, TidurSetelah, ramMenipis: true)
            .Select(t => t.Nama));
    }

    [Theory]
    [InlineData("https://web.whatsapp.com/", true)]
    [InlineData("http://web.whatsapp.com/", false)]
    [InlineData("https://whatsapp.com/", false)]
    [InlineData("https://web.whatsapp.com.contoh.id/", false)]
    [InlineData("https://www.detik.com/", false)]
    [InlineData("", false)]
    public void HanyaWhatsAppWebYangSelaluHidup(string uri, bool selaluHidup) =>
        Assert.Equal(selaluHidup, TidurTab.SelaluHidup(uri));
}
