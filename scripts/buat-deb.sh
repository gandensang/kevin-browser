#!/usr/bin/env bash
# Membuat paket Debian dari binary rilis: dist/kevin-browser_VERSI_amd64.deb
#
# Di laptop tujuan (Mint 22 / Ubuntu 24.04 ke atas, 64-bit):
#   sudo apt install ./kevin-browser_VERSI_amd64.deb
# apt ikut memasang WebKitGTK 6.0 dan GTK4 kalau belum ada. Paket ini juga
# memasang alamat repositori APT dan kuncinya (paket/), jadi versi baru
# datang lewat Update Manager tanpa langkah lain. Melepasnya:
#   sudo apt remove kevin-browser    (purge: alamat repositorinya ikut hilang)
#
# VERSI diambil dari <Version> di Directory.Build.props. Naikkan setiap
# membuat paket baru: apt hanya memperbarui ke versi yang lebih tinggi.
# Kolom Maintainer diambil dari git config user.name dan user.email.
set -euo pipefail
cd "$(dirname "$0")/.."
umask 022

dotnet publish Linux -c Release
versi=$(dotnet msbuild Linux -getProperty:Version)
pemelihara="$(git config user.name) <$(git config user.email)>"

akar=$(mktemp -d)
trap 'rm -rf "$akar"' EXIT
chmod 755 "$akar"   # mktemp membuat 700; folder ini menjadi / di laptop tujuan

install -Dm755 Linux/bin/Release/net10.0/linux-x64/publish/kevin-browser "$akar/usr/bin/kevin-browser"
install -Dm644 Linux/lokal.kevin.Browser.desktop "$akar/usr/share/applications/lokal.kevin.Browser.desktop"
install -Dm644 paket/kevin-browser.gpg "$akar/usr/share/keyrings/kevin-browser.gpg"
install -Dm644 paket/kevin-browser.sources "$akar/etc/apt/sources.list.d/kevin-browser.sources"
# Lisensi: milik Kevin Browser, dan milik gambar bidak catur (BSD, wajib ikut).
install -Dm644 LICENSE "$akar/usr/share/doc/kevin-browser/copyright"
install -Dm644 THIRD-PARTY-NOTICES.md "$akar/usr/share/doc/kevin-browser/THIRD-PARTY-NOTICES.md"
ukuran=$(du -sk "$akar/usr" "$akar/etc" | awk '{ s += $1 } END { print s }')

# GTK4 dan WebKitGTK dimuat GirCore saat jalan (dlopen), jadi dependensinya
# ditulis sendiri. Codec di Recommends adalah yang terpasang di laptop
# pengembang (lewat mint-meta-codecs) selama semua pengujian video.
# va-driver-all + libva-drm2: decode H.264 di kartu grafis (PenguraiVideo).
# poppler-utils: pdftotext, untuk menyerap materi PDF jadi catatan
# (kevin://belajar); di Mint sudah terpasang bersama sistem cetak.
mkdir "$akar/DEBIAN"
cat > "$akar/DEBIAN/control" << EOF
Package: kevin-browser
Version: $versi
Architecture: amd64
Maintainer: $pemelihara
Installed-Size: $ukuran
Depends: libc6 (>= 2.34), libgtk-4-1 (>= 4.14), libwebkitgtk-6.0-4 (>= 2.44)
Recommends: gstreamer1.0-libav, gstreamer1.0-plugins-bad, va-driver-all, libva-drm2, poppler-utils
Section: web
Priority: optional
Homepage: https://github.com/gandensang/kevin-browser
Description: peramban web ringan untuk laptop ber-RAM kecil
 Membuka web modern (JavaScript, YouTube, Google Docs) dengan memori
 seminim mungkin: tab tidur, pemblokir iklan dan pelacak, dan beranda
 berisi kisah serta panduan Kevin Browser.
EOF
# Berkas di /etc harus didaftarkan sendiri sebagai conffile (dpkg-deb tidak
# menebaknya): tidak ditimpa diam-diam kalau diubah pemakai, dan baru
# dihapus saat purge.
echo /etc/apt/sources.list.d/kevin-browser.sources > "$akar/DEBIAN/conffiles"

mkdir -p dist
paket=dist/kevin-browser_${versi}_amd64.deb
dpkg-deb --build --root-owner-group -Zxz "$akar" "$paket"
echo "Paket: $paket ($(du -h "$paket" | cut -f1))"
