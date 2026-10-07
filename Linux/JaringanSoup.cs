using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using KevinBrowser.Asisten;

namespace KevinBrowser.Linux;

/// <summary>
/// HTTP untuk asisten lewat libsoup, pustaka yang memang sudah dimuat WebKit
/// (HttpClient .NET menambah beberapa MB ke binary). Satu sesi per
/// permintaan, dengan API sinkron libsoup di thread latar; isi jawaban
/// dibaca sepotong-sepotong, jadi jawaban yang dialirkan bisa diikuti.
/// </summary>
sealed class JaringanSoup : IJaringan
{
    public Task<int> Kirim(PermintaanHttp permintaan, Action<string> perBaris, CancellationToken batal) =>
        Task.Run(() => KirimSinkron(permintaan, (aliran, batalGio) => Baca(aliran, batalGio, perBaris), batal), batal);

    public Task<int> Unduh(PermintaanHttp permintaan, Stream tujuan, long batas, CancellationToken batal) =>
        Task.Run(() => KirimSinkron(permintaan, (aliran, batalGio) => Salin(aliran, batalGio, tujuan, batas), batal), batal);

    static int KirimSinkron(PermintaanHttp p, Action<Gio.InputStream, Gio.Cancellable> baca, CancellationToken batal)
    {
        using var sesi = Soup.Session.New();
        // Diam paling lama 5 menit: dokumen panjang lama diolah sebelum
        // potongan jawaban pertama datang.
        sesi.SetTimeout(300);
        sesi.SetUserAgent("kevin-browser");
        using var pesan = Soup.Message.New(p.Metode, p.Alamat) ?? throw new GalatAi(0, "alamat tidak sah");
        var kepala = pesan.GetRequestHeaders();
        foreach (var (nama, nilai) in p.Kepala)
            kepala.Append(nama, nilai);
        // `using` juga menjaga pembungkusnya hidup selama libsoup memakainya.
        using var isi = p.Isi is null ? null : GLib.Bytes.New(p.Isi);
        if (isi is not null)
            pesan.SetRequestBodyFromBytes(p.JenisIsi ?? "application/octet-stream", isi);
        using var batalGio = Gio.Cancellable.New();
        using var daftar = batal.Register(batalGio.Cancel);
        try
        {
            using var aliran = sesi.Send(pesan, batalGio);
            baca(aliran, batalGio);
            return (int)pesan.GetStatus();
        }
        catch (GLib.GException e)
        {
            batal.ThrowIfCancellationRequested();
            throw new GalatAi(0, e.Message);
        }
    }

    static void Salin(Gio.InputStream aliran, Gio.Cancellable batal, Stream tujuan, long batas)
    {
        var penyangga = new byte[64 * 1024];
        long total = 0;
        for (int dibaca; (dibaca = (int)aliran.Read(penyangga, batal)) > 0;)
        {
            total += dibaca;
            if (total > batas)
                throw new GalatAi(0, "berkasnya lebih besar dari yang diharapkan");
            tujuan.Write(penyangga, 0, dibaca);
        }
    }

    // Per baris; UTF-8 yang terpotong di antara dua potongan disambung dulu.
    static void Baca(Gio.InputStream aliran, Gio.Cancellable batal, Action<string> perBaris)
    {
        var pengurai = Encoding.UTF8.GetDecoder();
        var penyangga = new byte[16 * 1024];
        var huruf = new char[Encoding.UTF8.GetMaxCharCount(penyangga.Length)];
        var baris = new StringBuilder();
        for (int dibaca; (dibaca = (int)aliran.Read(penyangga, batal)) > 0;)
        {
            var n = pengurai.GetChars(penyangga, 0, dibaca, huruf, 0);
            for (var i = 0; i < n; i++)
            {
                if (huruf[i] != '\n')
                    baris.Append(huruf[i]);
                else
                {
                    perBaris(baris.ToString().TrimEnd('\r'));
                    baris.Clear();
                }
            }
        }
        if (baris.Length > 0)
            perBaris(baris.ToString().TrimEnd('\r'));
    }
}

/// <summary>
/// Teks berkas PDF lewat pdftotext (paket poppler-utils, di Mint terpasang
/// bersama sistem cetak). Program terpisah: tidak menambah binary, dan
/// memorinya kembali begitu selesai.
/// </summary>
static class TeksPdf
{
    /// <summary>Teksnya (halaman dipisah \f); null kalau gagal dibaca. FileNotFoundException kalau pdftotext tidak ada.</summary>
    /// <remarks>
    /// Dibaca sinkron di thread latar, bukan dengan ReadToEndAsync: IO async
    /// pipa di .NET Linux menarik lapisan socket ke binary. Terukur 3 Okt
    /// 2026, bersama decimal → double di HargaAi: binary rilis 5.521.496 →
    /// 5.309.864 bait.
    /// </remarks>
    public static Task<string?> Ambil(string berkas, CancellationToken batal) => Task.Run(() =>
    {
        var info = new ProcessStartInfo("pdftotext")
        {
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
        };
        // Jalur lengkap selalu diawali "/", jadi tidak terbaca sebagai pilihan.
        foreach (var arg in new[] { "-enc", "UTF-8", "-q", Path.GetFullPath(berkas), "-" })
            info.ArgumentList.Add(arg);
        Process proses;
        try
        {
            proses = Process.Start(info) ?? throw new FileNotFoundException("pdftotext");
        }
        catch (Win32Exception)
        {
            throw new FileNotFoundException("pdftotext");
        }
        using (proses)
        using (batal.Register(() => Hentikan(proses)))
        {
            var teks = proses.StandardOutput.ReadToEnd();
            proses.WaitForExit();
            batal.ThrowIfCancellationRequested();
            return proses.ExitCode == 0 ? teks : null;
        }
    }, batal);

    static void Hentikan(Process proses)
    {
        try
        {
            proses.Kill();
        }
        catch (InvalidOperationException)
        {
            // sudah selesai
        }
    }
}
