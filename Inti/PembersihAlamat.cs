namespace KevinBrowser;

/// <summary>
/// Parameter pelacak di alamat (utm_…, fbclid, gclid, …). Dibuang di dua
/// tempat. Alamat yang masuk dari luar halaman (kotak alamat, tautan dari
/// aplikasi lain, klik tengah) dibersihkan dengan <see cref="Bersihkan"/>
/// sebelum dimuat, jadi servernya tidak pernah menerimanya. Alamat dari
/// dalam halaman (klik biasa, pengalihan) dibersihkan oleh <see cref="Skrip"/>
/// sebelum skrip halaman berjalan: server sudah menerimanya, tetapi piksel
/// pelacak di halaman tidak bisa membacanya, dan riwayat ikut bersih.
/// </summary>
/// <remarks>
/// Pemblokir konten WebKit punya aksi "redirect" yang bisa membuang
/// parameter, tetapi WebKitGTK hanya menjalankannya untuk ekstensi browser
/// (DocumentLoader::allowsActiveContentRuleListActionsForURL). Aturannya
/// dikompilasi tanpa galat lalu diam-diam tidak berlaku (terjadi). Membatalkan
/// navigasi di decide-policy juga tidak bisa: sinyal itu datang juga untuk
/// iframe, tanpa cara membedakannya dari bingkai utama.
/// </remarks>
public static class PembersihAlamat
{
    /// <summary>Selalu dibuang, di situs mana pun.</summary>
    public static readonly string[] Parameter =
    [
        "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content", "utm_id", "utm_name",
        "utm_cid", "utm_reader", "utm_referrer", "utm_social", "utm_social-type", "utm_brand",
        "utm_place", "utm_pubreferrer", "utm_swu", "utm_viz_id",
        "fbclid", "gclid", "gclsrc", "dclid", "gbraid", "wbraid", "msclkid", "yclid", "twclid",
        "ttclid", "li_fat_id", "mc_cid", "mc_eid", "_hsenc", "_hsmi", "__hssc", "__hstc", "__hsfp",
        "hsCtaTracking", "mkt_tok", "oly_anon_id", "oly_enc_id", "rb_clickid", "s_cid", "vero_conv",
        "vero_id", "wickedid", "_openstat", "ml_subscriber", "ml_subscriber_hash",
    ];

    /// <summary>
    /// Hanya di domain ini dan subdomainnya: di situs lain nama yang sama
    /// bisa berarti lain.
    /// </summary>
    public static readonly (string Domain, string[] Parameter)[] PerSitus =
    [
        ("youtube.com", ["si"]),
        ("youtu.be", ["si"]),
        ("open.spotify.com", ["si"]),
        ("instagram.com", ["igsh", "igshid"]),
    ];

    /// <summary>
    /// Alamat http(s) tanpa parameter pelacak, atau null kalau tidak ada yang
    /// dibuang. Parameter lain dan bagian #… tidak diubah sedikit pun.
    /// </summary>
    public static string? Bersihkan(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u) || (u.Scheme != "http" && u.Scheme != "https"))
            return null;

        var pagar = uri.IndexOf('#');
        var akhir = pagar < 0 ? uri.Length : pagar;
        var tanya = uri.IndexOf('?');
        if (tanya < 0 || tanya > akhir)
            return null;   // tidak ada kueri; "?" sesudah # bukan kueri

        var buang = Dibuang(u.Host);
        var semua = uri[(tanya + 1)..akhir].Split('&');
        var sisa = semua.Where(p => !buang.Contains(p.Split('=')[0])).ToArray();
        if (sisa.Length == semua.Length)
            return null;
        return uri[..tanya] + (sisa.Length > 0 ? "?" + string.Join('&', sisa) : "") + uri[akhir..];
    }

    static HashSet<string> Dibuang(string host)
    {
        var buang = new HashSet<string>(Parameter, StringComparer.Ordinal);
        foreach (var (domain, parameter) in PerSitus)
            if (host == domain || host.EndsWith("." + domain, StringComparison.Ordinal))
                buang.UnionWith(parameter);
        return buang;
    }

    /// <summary>
    /// JavaScript untuk awal dokumen di bingkai utama, sebelum skrip halaman:
    /// aturan yang sama dengan <see cref="Bersihkan"/>, lewat
    /// history.replaceState (tanpa memuat ulang).
    /// </summary>
    public static readonly string Skrip = $$"""
        (function () {
          var l = location;
          if ((l.protocol !== 'http:' && l.protocol !== 'https:') || l.search.length < 2) return;
          var buang = {{Larik(Parameter)}};
          var host = l.hostname;
          {{string.Join("\n  ", PerSitus.Select(s =>
              $"if (host === '{s.Domain}' || host.endsWith('.{s.Domain}')) buang = buang.concat({Larik(s.Parameter)});"))}}
          var semua = l.search.slice(1).split('&');
          var sisa = semua.filter(function (p) { return buang.indexOf(p.split('=')[0]) < 0; });
          if (sisa.length === semua.length) return;
          history.replaceState(history.state, '', l.pathname + (sisa.length ? '?' + sisa.join('&') : '') + l.hash);
        })();
        """;

    // Nama parameter hanya huruf, angka, _ dan - (dijaga tes), jadi aman tanpa escape.
    static string Larik(string[] isi) => "[" + string.Join(",", isi.Select(p => $"'{p}'")) + "]";
}
