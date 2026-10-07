using KevinBrowser;

namespace Uji;

public class UjiTeks
{
    static readonly Teks Id = new(Bahasa.Indonesia);
    static readonly Teks En = new(Bahasa.Inggris);

    [Fact]
    public void MemilihTeksSesuaiBahasa()
    {
        Assert.Equal("Simpan", Id["Simpan", "Save"]);
        Assert.Equal("Save", En["Simpan", "Save"]);
        Assert.Equal("id", Id.Kode);
        Assert.Equal("en", En.Kode);
    }

    [Fact]
    public void AngkaRibuan()
    {
        Assert.Equal("56.899", Id.Angka(56899));
        Assert.Equal("56,899", En.Angka(56899));
        Assert.Equal("7", Id.Angka(7));
    }

    [Fact]
    public void TanggalDanJam()
    {
        var kamis = new DateTime(2026, 10, 1);
        Assert.Equal("Kamis, 1 Oktober 2026", Id.Tanggal(kamis));
        Assert.Equal("Thursday, 1 October 2026", En.Tanggal(kamis));
        Assert.Equal("1 Okt 2026", Id.TanggalSingkat(kamis));
        Assert.Equal("1 Oct", En.TanggalSingkat(kamis, denganTahun: false));
        Assert.Equal("17 Agu", Id.TanggalSingkat(new DateTime(2026, 8, 17), false));

        var jam = new DateTimeOffset(2026, 10, 1, 9, 5, 0, TimeSpan.Zero);
        Assert.Equal("09.05", Id.Jam(jam));
        Assert.Equal("09:05", En.Jam(jam));
    }

    [Theory]
    [InlineData(500L, "500 B", "500 B")]
    [InlineData(2048L, "2 KB", "2 KB")]
    [InlineData(2831155L, "2,7 MB", "2.7 MB")]
    [InlineData(76L * 1024 * 1024, "76 MB", "76 MB")]
    public void UkuranBerkas(long bait, string indonesia, string inggris)
    {
        Assert.Equal(indonesia, Id.Ukuran(bait));
        Assert.Equal(inggris, En.Ukuran(bait));
    }
}

public sealed class UjiPreferensi : IDisposable
{
    readonly string folder = Directory.CreateTempSubdirectory("kevin-browser-uji-").FullName;
    string Berkas => Path.Combine(folder, "preferensi.tsv");

    public void Dispose() => Directory.Delete(folder, true);

    [Fact]
    public void BawaannyaIndonesia() => Assert.Equal(Bahasa.Indonesia, new Preferensi(Berkas).Bahasa);

    [Fact]
    public void TersimpanDanTerbacaLagi()
    {
        new Preferensi(Berkas).Bahasa = Bahasa.Inggris;
        Assert.Equal(Bahasa.Inggris, new Preferensi(Berkas).Bahasa);
        Assert.Equal(["bahasa\ten"], File.ReadAllLines(Berkas));
    }

    [Fact]
    public void PilihanLainDiBerkasDibiarkan()
    {
        File.WriteAllText(Berkas, "lain\tx\nbahasa\tid\n");
        new Preferensi(Berkas).Bahasa = Bahasa.Inggris;
        Assert.Equal(["lain\tx", "bahasa\ten"], File.ReadAllLines(Berkas));
    }

    [Fact]
    public void BerkasRusakDianggapBawaan()
    {
        File.WriteAllText(Berkas, "sampah tanpa tab\nbahasa\tklingon\n");
        Assert.Equal(Bahasa.Indonesia, new Preferensi(Berkas).Bahasa);
    }

    [Fact]
    public void BerubahHanyaSaatNilainyaBerubah()
    {
        var preferensi = new Preferensi(Berkas);
        var berubah = 0;
        preferensi.Berubah += () => berubah++;
        preferensi.Bahasa = Bahasa.Inggris;
        preferensi.Bahasa = Bahasa.Inggris;
        Assert.Equal(1, berubah);
        preferensi.Bahasa = Bahasa.Indonesia;
        Assert.Equal(2, berubah);
    }
}
