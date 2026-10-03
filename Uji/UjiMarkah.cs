using KevinBrowser.Asisten;

namespace Uji;

public sealed class UjiMarkah
{
    static string Html(string md, Func<string, string?>? penaut = null) => Markah.KeHtml(md, penaut);

    [Fact]
    public void HtmlDiCatatanDiEscape()
    {
        var html = Html("<script>alert(1)</script>\n\n**<img src=x onerror=alert(1)>**");
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<img", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("<strong>&lt;img", html);
    }

    [Theory]
    [InlineData("[klik](javascript:alert(1))")]
    [InlineData("[klik](javascript:alert)")]
    [InlineData("[klik](data:text/html,x)")]
    [InlineData("[klik](file:///etc/passwd)")]
    [InlineData("<javascript:alert(1)>")]
    [InlineData("[[x|<b>label</b>]]")]
    public void TautanBerbahayaTidakJadiTautan(string md)
    {
        var html = Html(md);
        Assert.DoesNotContain("<a ", html);
        Assert.DoesNotContain("<b>", html);
    }

    [Fact]
    public void TautanAman()
    {
        Assert.Contains("<a href=\"https://lichess.org/abc\">Lichess</a>", Html("[Lichess](https://lichess.org/abc)"));
        Assert.Contains("<a href=\"https://lichess.org/abc\">https://lichess.org/abc</a>.", Html("Lihat https://lichess.org/abc."));
        Assert.Contains("<a href=\"https://lichess.org\">https://lichess.org</a>)", Html("(https://lichess.org)"));
        Assert.Contains("<a href=\"https://id.wikipedia.org/wiki/Gerak_(fisika)\">", Html("https://id.wikipedia.org/wiki/Gerak_(fisika)"));
        Assert.Contains("<a href=\"https://contoh.id/?a=1&amp;b=2\">", Html("<https://contoh.id/?a=1&b=2>"));
    }

    [Fact]
    public void Judul()
    {
        Assert.Equal("<h1>Rumus</h1>\n", Html("# Rumus"));
        Assert.Equal("<h2>Rumus</h2>\n", Markah.KeHtml("# Rumus", geserJudul: 1));
        Assert.Equal("<h6>x</h6>\n", Markah.KeHtml("###### x", geserJudul: 1));
        Assert.Equal("<p>#5 bukan judul</p>\n", Html("#5 bukan judul"));
    }

    [Fact]
    public void TebalMiringDanKode()
    {
        Assert.Equal("<p><strong>tebal</strong> dan <em>miring</em> dan <em>ini</em></p>\n", Html("**tebal** dan *miring* dan _ini_"));
        Assert.Equal("<p>2*3*4 dan 2**3 dan t_puncak_maks</p>\n", Html("2*3*4 dan 2**3 dan t_puncak_maks"));
        Assert.Equal("<p><code>**bukan tebal**</code> dan <code>a`b</code></p>\n", Html("`**bukan tebal**` dan ``a`b``"));
        Assert.Equal("<p>*bukan miring*</p>\n", Html("\\*bukan miring\\*"));
        Assert.Equal("<p>5 * 3 = 15 dan 2 * 4 = 8</p>\n", Html("5 * 3 = 15 dan 2 * 4 = 8"));
    }

    [Fact]
    public void ParagrafDanPatahBaris()
    {
        Assert.Equal("<p>baris satu\nbaris dua</p>\n<p>paragraf baru</p>\n", Html("baris satu\nbaris dua\n\nparagraf baru"));
        Assert.Equal("<p>dengan<br>\npatah</p>\n", Html("dengan  \npatah"));
    }

    [Fact]
    public void DaftarSesudahParagrafTanpaBarisKosong()
    {
        var html = Html("Gerak parabola = dua gerak:\n- Sumbu X: GLB\n- Sumbu Y: GLBB\n  lanjutan baris\n\nSelesai.");
        Assert.Equal("<p>Gerak parabola = dua gerak:</p>\n<ul>\n<li>Sumbu X: GLB\n</li>\n<li>Sumbu Y: GLBB\nlanjutan baris\n</li>\n</ul>\n<p>Selesai.</p>\n", html);
    }

    [Fact]
    public void DaftarBersarangDanBernomor()
    {
        var html = Html("1. Satu\n   - a\n   - b\n2. Dua\n\n3) Lain");
        Assert.Contains("<ol>\n<li>Satu\n<ul>\n<li>a\n</li>\n<li>b\n</li>\n</ul>\n</li>\n<li>Dua\n</li>\n</ol>\n", html);
        Assert.Contains("<ol start=\"3\">", html);
    }

    [Fact]
    public void AngkaTahunDiTengahParagrafBukanDaftar() =>
        Assert.Equal("<p>Proklamasi dibacakan pada\n1945. Tahun yang sama …</p>\n", Html("Proklamasi dibacakan pada\n1945. Tahun yang sama …"));

    [Fact]
    public void GarisBukanDaftar() => Assert.Equal("<hr>\n", Html("- - -"));

    [Theory]
    [InlineData("# C#", "<h1>C#</h1>\n")]
    [InlineData("## Judul ##", "<h2>Judul</h2>\n")]
    [InlineData("1.5 juta orang", "<p>1.5 juta orang</p>\n")]
    [InlineData("-x bukan daftar", "<p>-x bukan daftar</p>\n")]
    [InlineData("*a **b** c*", "<p><em>a <strong>b</strong> c</em></p>\n")]
    [InlineData("**`kode`** tebal", "<p><strong><code>kode</code></strong> tebal</p>\n")]
    [InlineData("\"*kutip*\"", "<p>&quot;<em>kutip</em>&quot;</p>\n")]
    public void KasusTepi(string md, string html) => Assert.Equal(html, Html(md));

    [Fact]
    public void AlamatBerkurungDiTautanMarkdown() =>
        Assert.Contains("<a href=\"https://a.org/wiki/A_(b)\">", Html("[x](https://a.org/wiki/A_(b))"));

    [Fact]
    public void KodeMenjorokDanBerpagar()
    {
        var html = Html("Rumus:\n\n    t_puncak = v0 * sin(theta) / g\n    R = <besar>\n\n```\n1. e4 e5 *bukan miring*\n```");
        Assert.Contains("<pre><code>t_puncak = v0 * sin(theta) / g\nR = &lt;besar&gt;</code></pre>", html);
        Assert.Contains("<pre><code>1. e4 e5 *bukan miring*</code></pre>", html);
    }

    [Fact]
    public void KodeBerpagarDiDalamDaftar()
    {
        var html = Html("1. Langkah 12\n   ```\n   r . b q k . . r\n   ```\n2. Langkah 13");
        Assert.Contains("<li>Langkah 12\n<pre><code>r . b q k . . r</code></pre>\n</li>", html);
    }

    [Fact]
    public void TabelKutipanDanGaris()
    {
        var html = Html("| berkas | ukuran |\n|---|--:|\n| a.pdf | 12 |\n\n> kutipan **tebal**\n\n---");
        Assert.Contains("<th>berkas</th><th style=\"text-align:right\">ukuran</th>", html);
        Assert.Contains("<td>a.pdf</td><td style=\"text-align:right\">12</td>", html);
        Assert.Contains("<blockquote>\n<p>kutipan <strong>tebal</strong></p>\n</blockquote>", html);
        Assert.EndsWith("<hr>\n", html);
    }

    [Fact]
    public void TautanAntarCatatan()
    {
        var html = Html("Lihat [[2026-09-kesalahan-umum]] dan [[tidak-ada|yang hilang]].",
            nama => nama == "2026-09-kesalahan-umum" ? "kevin://belajar?m=fisika&c=2026-09-kesalahan-umum" : null);
        Assert.Contains("<a href=\"kevin://belajar?m=fisika&amp;c=2026-09-kesalahan-umum\">2026-09-kesalahan-umum</a>", html);
        Assert.Contains("<span class=\"putus\">yang hilang</span>", html);
    }

    // Catatan yang dibuat dari handout fisika (format catatan yang sudah dipakai).
    const string CatatanFisika = """
        Sumber: handout-gerak-parabola.md (Handout Fisika Kelas X)

        # Gerak Parabola — konsep dan rumus

        Gerak parabola = dua gerak yang saling bebas, dikerjakan terpisah:
        - Sumbu X: GLB, kecepatan tetap `vx = v0 · cos(theta)`
        - Sumbu Y: GLBB, percepatan `a = -g`

        ## Tiga rumus cepat

            t_puncak = v0 · sin(theta) / g
            R        = v0^2 · sin(2·theta) / g

        ## SYARAT PAKAI — jangan dilewati

        Tiga rumus di atas HANYA boleh dipakai kalau:
        1. titik jatuh sama tinggi dengan titik lempar, dan
        2. gesekan udara diabaikan.

        Lihat [[2026-09-gerak-parabola-kesalahan-umum]].

        Dilewati: kata pengantar, sejarah Galileo, contoh soal 2-4 (caranya sama
        persis dengan contoh 1, cuma angkanya beda), daftar pustaka.
        """;

    [Fact]
    public void CatatanSungguhanTerbacaUtuh()
    {
        var html = Markah.KeHtml(CatatanFisika, nama => $"kevin://belajar?m=fisika&c={nama}", geserJudul: 1);
        Assert.StartsWith("<p>Sumber: handout-gerak-parabola.md (Handout Fisika Kelas X)</p>\n<h2>Gerak Parabola", html);
        Assert.Contains("<li>Sumbu X: GLB, kecepatan tetap <code>vx = v0 &#183; cos(theta)</code>\n</li>", html);
        Assert.Contains("<h3>Tiga rumus cepat</h3>\n<pre><code>t_puncak = v0 &#183; sin(theta) / g\nR        = v0^2", html);
        Assert.Contains("<ol>\n<li>titik jatuh sama tinggi dengan titik lempar, dan\n</li>\n<li>gesekan udara diabaikan.\n</li>\n</ol>", html);
        Assert.Contains("<a href=\"kevin://belajar?m=fisika&amp;c=2026-09-gerak-parabola-kesalahan-umum\">", html);
        Assert.EndsWith("<p>Dilewati: kata pengantar, sejarah Galileo, contoh soal 2-4 (caranya sama\npersis dengan contoh 1, cuma angkanya beda), daftar pustaka.</p>\n", html);
    }
}
