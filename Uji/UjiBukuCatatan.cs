using KevinBrowser.Asisten;

namespace Uji;

public sealed class UjiBukuCatatan : IDisposable
{
    readonly string folder = Directory.CreateTempSubdirectory("kevin-catatan-uji-").FullName;
    readonly BukuCatatan buku;

    public UjiBukuCatatan() => buku = new BukuCatatan(folder);

    public void Dispose() => Directory.Delete(folder, true);

    void Tulis(string jalur, string isi)
    {
        var lengkap = Path.Combine(folder, jalur);
        Directory.CreateDirectory(Path.GetDirectoryName(lengkap)!);
        File.WriteAllText(lengkap, isi);
    }

    [Fact]
    public void FolderBelumAdaBerartiKosong()
    {
        var kosong = new BukuCatatan(Path.Combine(folder, "belum-ada"));
        Assert.Empty(kosong.Semua());
        Assert.Empty(kosong.Mapel());
        Assert.Empty(kosong.Cari("apa saja"));
        Assert.Null(kosong.BacaSumber());
        Assert.False(kosong.AdaSumber);
    }

    [Fact]
    public void DaftarCatatanPerMapel()
    {
        Tulis("fisika/2026-09-gerak-parabola-rumus.md", "Sumber: handout\n\n# Gerak Parabola — konsep dan rumus\n\nisi");
        Tulis("fisika/catatan-tanpa-judul.md", "isi saja");
        Tulis("catur/2026-09-23-sisilia-najdorf.md", "# Sisilia Najdorf\n");
        Tulis("lepas.md", "# Catatan lepas\n");
        Tulis("sumber.md", "# Dokumen\n");
        Tulis("fisika/gambar.png", "bukan catatan");
        Tulis(".git/config.md", "# folder git");
        Tulis("fisika/.sementara.md", "# setengah tertulis");

        var semua = buku.Semua();

        Assert.Equal(["catur", "fisika"], buku.Mapel());
        Assert.Equal(4, semua.Count);
        Assert.Contains(semua, c => c is { Mapel: "fisika", Nama: "2026-09-gerak-parabola-rumus", Judul: "Gerak Parabola — konsep dan rumus" });
        Assert.Contains(semua, c => c is { Mapel: "fisika", Nama: "catatan-tanpa-judul", Judul: "Catatan tanpa judul" });
        Assert.Contains(semua, c => c is { Mapel: null, Nama: "lepas", Judul: "Catatan lepas" });
        Assert.DoesNotContain(semua, c => c.Nama == "sumber");
        Assert.True(buku.AdaSumber);
    }

    [Theory]
    [InlineData("2026-09-hukum-newton", "Hukum newton")]
    [InlineData("2026-09-23-sisilia-najdorf", "Sisilia najdorf")]
    [InlineData("rangkuman_bab_1", "Rangkuman bab 1")]
    [InlineData("2026-09", "2026 09")]
    public void JudulDariNamaBerkas(string nama, string judul) => Assert.Equal(judul, BukuCatatan.JudulDariNama(nama));

    [Theory]
    [InlineData("# Hukum Newton", "Hukum Newton")]
    [InlineData("   # Hukum Newton ##", "Hukum Newton")]
    [InlineData("## Subjudul", null)]
    [InlineData("#Tanpa spasi", null)]
    [InlineData("    # menjorok empat", null)]
    public void JudulBaris(string baris, string? judul) => Assert.Equal(judul, BukuCatatan.JudulBaris(baris));

    [Theory]
    [InlineData("..")]
    [InlineData("../rahasia")]
    [InlineData("fisika/x")]
    [InlineData("a\\b")]
    [InlineData(".git")]
    [InlineData("")]
    [InlineData(" spasi")]
    [InlineData("baris\nbaru")]
    public void NamaYangTidakBoleh(string nama) => Assert.False(BukuCatatan.NamaAman(nama));

    [Fact]
    public void TidakBisaKeluarDariFolderCatatan()
    {
        var luar = folder + "-luar.md";
        File.WriteAllText(luar, "# rahasia");
        try
        {
            var nama = Path.GetFileNameWithoutExtension(luar);
            Assert.Null(buku.Baca(null, "../" + nama));
            Assert.Null(buku.Baca("..", nama));
            Assert.Null(buku.Ambil("..", nama));
            Assert.Equal(HasilSimpan.Ditolak, buku.Simpan("..", nama, "ditimpa", ""));
            Assert.Null(buku.Tulis("..", "Judul", "isi", DateTime.Now));
            Assert.Equal("# rahasia", File.ReadAllText(luar));
        }
        finally
        {
            File.Delete(luar);
        }
    }

    [Fact]
    public void TautanSimbolikDiabaikan()
    {
        var luar = Directory.CreateTempSubdirectory("kevin-luar-").FullName;
        try
        {
            var rahasia = Path.Combine(luar, "rahasia.md");
            File.WriteAllText(rahasia, "# Rahasia");
            Directory.CreateDirectory(folder);
            Directory.CreateSymbolicLink(Path.Combine(folder, "tautan"), luar);
            File.CreateSymbolicLink(Path.Combine(folder, "rahasia.md"), rahasia);

            Assert.Empty(buku.Semua());
            Assert.Empty(buku.Mapel());
            Assert.Null(buku.Baca("tautan", "rahasia"));
            Assert.Null(buku.Baca(null, "rahasia"));
            Assert.Equal(HasilSimpan.Ditolak, buku.Simpan("tautan", "rahasia", "ditimpa", ""));
            Assert.Equal(HasilSimpan.Ditolak, buku.Simpan(null, "rahasia", "ditimpa", ""));
            Assert.Equal("# Rahasia", File.ReadAllText(rahasia));
        }
        finally
        {
            Directory.Delete(luar, true);
        }
    }

    [Fact]
    public void TulisCatatanBaru()
    {
        var jam = new DateTime(2026, 10, 3, 9, 0, 0);
        var c = buku.Tulis("Bahasa Inggris", "Simple Past Tense", "Rumus:\r\n- S + V2\r\n", jam);

        Assert.NotNull(c);
        Assert.Equal(("bahasa-inggris", "2026-10-simple-past-tense", "Simple Past Tense"), (c.Mapel, c.Nama, c.Judul));
        Assert.Equal("# Simple Past Tense\n\nRumus:\n- S + V2\n",
            File.ReadAllText(Path.Combine(folder, "bahasa-inggris", "2026-10-simple-past-tense.md")));

        // Judul yang sama: nama berkas lain. Mata pelajaran yang sudah ada dipakai lagi.
        var kedua = buku.Tulis("bahasa inggris", "Simple Past Tense", "", jam);
        Assert.Equal(("bahasa-inggris", "2026-10-simple-past-tense-2"), (kedua!.Mapel, kedua.Nama));
        Assert.Equal("# Simple Past Tense\n", buku.Baca("bahasa-inggris", kedua.Nama));
    }

    [Fact]
    public void FolderMapelBuatanSendiriDipakaiApaAdanya()
    {
        Directory.CreateDirectory(Path.Combine(folder, "Kimia Organik"));
        Assert.Equal("Kimia Organik", buku.Tulis("kimia organik", "Alkana", "", new DateTime(2026, 10, 3))!.Mapel);
    }

    [Theory]
    [InlineData("", "Judul")]
    [InlineData("Fisika", "   ")]
    [InlineData("!!!", "Judul")]
    public void TulisDitolak(string mapel, string judul) => Assert.Null(buku.Tulis(mapel, judul, "isi", DateTime.Now));

    [Fact]
    public void SimpanMenolakMenimpaBerkasYangBerubah()
    {
        Tulis("fisika/newton.md", "# Newton\n\nversi 1\n");
        var sidik = buku.Sidik("fisika", "newton");

        File.WriteAllText(Path.Combine(folder, "fisika", "newton.md"), "# Newton\n\nversi dari aplikasi lain\n");
        Assert.Equal(HasilSimpan.BerubahDiDisk, buku.Simpan("fisika", "newton", "versi 2", sidik));
        Assert.Contains("aplikasi lain", buku.Baca("fisika", "newton"));

        Assert.Equal(HasilSimpan.Tersimpan, buku.Simpan("fisika", "newton", "# Newton\r\n\r\nversi 2", buku.Sidik("fisika", "newton")));
        Assert.Equal("# Newton\n\nversi 2\n", buku.Baca("fisika", "newton"));
        Assert.Empty(Directory.GetFiles(Path.Combine(folder, "fisika"), ".*"));   // berkas sementara tidak tertinggal
    }

    [Fact]
    public void BerkasYangHilangBisaDisimpanLagi()
    {
        Assert.Equal("", buku.Sidik("fisika", "baru"));
        Assert.Equal(HasilSimpan.Tersimpan, buku.Simpan("fisika", "baru", "# Baru", ""));
        Assert.Equal("# Baru\n", buku.Baca("fisika", "baru"));
    }

    [Fact]
    public void BerkasTerlaluBesarBukanCatatan()
    {
        Tulis("fisika/besar.md", "# Besar\n" + new string('x', BukuCatatan.UkuranMaks));
        Assert.Empty(buku.Semua());
        Assert.Null(buku.Baca("fisika", "besar"));
        Assert.Equal(HasilSimpan.Ditolak, buku.Simpan("fisika", "kecil", new string('x', BukuCatatan.UkuranMaks + 1), ""));
    }

    [Fact]
    public void CariSemuaKataTanpaPedulikanHurufBesar()
    {
        Tulis("fisika/2026-09-parabola.md", "# Gerak Parabola\n\nRumus jangkauan R = v0^2 sin 2θ / g\n");
        Tulis("fisika/2026-08-newton.md", "# Hukum Newton\n\nGaya dan percepatan. Parabola belum dibahas.\n");
        Tulis("matematika/2026-09-trigonometri.md", "# Trigonometri\n\nsin, cos, tan\n");

        var hasil = buku.Cari("PARABOLA");
        Assert.Equal(["Gerak Parabola", "Hukum Newton"], hasil.Select(h => h.Catatan.Judul));   // cocok di judul lebih dulu
        Assert.Contains("Parabola belum dibahas", hasil[1].Cuplikan);

        Assert.Equal(["Gerak Parabola"], buku.Cari("parabola jangkauan").Select(h => h.Catatan.Judul));
        Assert.Equal(["Trigonometri"], buku.Cari("matematika").Select(h => h.Catatan.Judul));   // nama mata pelajaran ikut dicari
        Assert.Empty(buku.Cari("   "));
        Assert.Empty(buku.Cari("kimia"));
    }

    [Fact]
    public void CariSebagianKata()
    {
        Tulis("fisika/2026-09-parabola.md", "# Gerak Parabola\n\nJangkauan terjauh pada sudut 45°.\n");
        Tulis("fisika/2026-08-newton.md", "# Hukum Newton\n\nGaya dan percepatan.\n");

        Assert.Empty(buku.Cari("parabola sudut gaya"));
        var hasil = buku.Cari("parabola sudut gaya", semuaKata: false);
        Assert.Equal(["Gerak Parabola", "Hukum Newton"], hasil.Select(h => h.Catatan.Judul));   // dua kata cocok lebih dulu
        Assert.Contains("Gaya dan percepatan", hasil[1].Cuplikan);
        Assert.Empty(buku.Cari("kimia organik", semuaKata: false));
    }

    [Fact]
    public void CuplikanTidakMemotongKata()
    {
        Tulis("sejarah/panjang.md", "# Panjang\n\n" + string.Join(' ', Enumerable.Repeat("kata", 60)) + " proklamasi " + string.Join(' ', Enumerable.Repeat("lain", 60)));
        var cuplikan = buku.Cari("proklamasi").Single().Cuplikan;
        Assert.StartsWith("…kata ", cuplikan);
        Assert.EndsWith(" lain…", cuplikan);
        Assert.Contains(" proklamasi ", cuplikan);
    }

    [Fact]
    public void PenautMendahulukanMapelYangSama()
    {
        Tulis("fisika/rumus.md", "# Rumus fisika");
        Tulis("kimia/rumus.md", "# Rumus kimia");
        Tulis("kimia/ikatan.md", "# Ikatan");

        Assert.Equal("fisika", buku.Penaut("fisika")("rumus")!.Mapel);
        Assert.Equal("kimia", buku.Penaut("kimia")("rumus.md")!.Mapel);
        Assert.Equal("kimia", buku.Penaut("fisika")("kimia/rumus")!.Mapel);
        Assert.Equal("ikatan", buku.Penaut("fisika")("IKATAN")!.Nama);
        Assert.Null(buku.Penaut("fisika")("tidak-ada"));
    }

    [Fact]
    public void JudulDiingatSampaiBerkasnyaBerubah()
    {
        Tulis("fisika/a.md", "# Judul lama");
        Assert.Equal("Judul lama", buku.Semua().Single().Judul);
        File.WriteAllText(Path.Combine(folder, "fisika", "a.md"), "# Judul baru yang lebih panjang");
        Assert.Equal("Judul baru yang lebih panjang", buku.Semua().Single().Judul);
    }

    [Theory]
    [InlineData("Hukum Newton (bag. 2)", "hukum-newton-bag-2")]
    [InlineData("  Bahasa   Indonesia ", "bahasa-indonesia")]
    [InlineData("Persamaan Kuadrat: x² + 1", "persamaan-kuadrat-x-1")]
    [InlineData("!!!", "")]
    public void Slug(string teks, string slug) => Assert.Equal(slug, BukuCatatan.Slug(teks));

    [Theory]
    [InlineData("2026-09-hukum-newton", "2026-09")]
    [InlineData("2026-09-23-partai-klub", "2026-09-23")]
    [InlineData("hukum-newton", "")]
    public void AwalanTanggal(string nama, string awalan) => Assert.Equal(awalan, BukuCatatan.AwalanTanggal(nama));
}
