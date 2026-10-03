namespace KevinBrowser;

/// <summary>
/// Situs yang boleh memakai kamera dan mikrofon (juga berbagi layar, kalau
/// suatu saat bisa) tanpa ditanya. Hanya WhatsApp Web, untuk pesan suara.
/// Situs lain, dan izin lain (lokasi, …), selalu ditolak. Notifikasi diatur
/// <see cref="IzinNotifikasi"/>.
/// </summary>
/// <remarks>
/// Panggilan video tidak bisa di situs mana pun: WebKitGTK dari Ubuntu/Mint
/// dibangun tanpa WebRTC (RTCPeerConnection tidak ada walau enable-webrtc
/// dinyalakan; sudah dicoba). Karena itu Google Meet dan Zoom tidak ada di
/// sini. Berbagi layar butuh portal ScreenCast, yang tidak disediakan portal
/// xapp maupun gtk di Mint X11.
/// </remarks>
public static class IzinMedia
{
    static readonly string[] Situs = ["web.whatsapp.com"];

    /// <param name="uri">Alamat bingkai utama tab yang meminta izin.</param>
    public static bool Boleh(string? uri) => SitusHttps(uri, Situs);

    /// <summary>https di port bawaan, dan host-nya persis salah satu <paramref name="situs"/>.</summary>
    internal static bool SitusHttps(string? uri, string[] situs) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var u)
        && u.Scheme == Uri.UriSchemeHttps
        && u.IsDefaultPort
        && situs.Contains(u.Host);
}
