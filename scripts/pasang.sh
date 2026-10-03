#!/usr/bin/env bash
# Build rilis (NativeAOT) lalu pasang untuk pemakai ini:
#   ~/.local/bin/kevin-browser
#   ~/.local/share/applications/lokal.kevin.Browser.desktop   (muncul di menu)
#
# Laptop lain cukup disalin dua berkas itu (Exec di .desktop harus menunjuk
# binary-nya). Tidak perlu .NET; yang perlu libwebkitgtk-6.0-4 dan libgtk-4-1
# — cek dengan: dpkg -s libwebkitgtk-6.0-4 libgtk-4-1
set -euo pipefail
cd "$(dirname "$0")/.."

dotnet publish Linux -c Release

install -Dm755 Linux/bin/Release/net10.0/linux-x64/publish/kevin-browser ~/.local/bin/kevin-browser
desktop=~/.local/share/applications/lokal.kevin.Browser.desktop
install -Dm644 Linux/lokal.kevin.Browser.desktop "$desktop"
# Path lengkap: ~/.local/bin belum tentu ada di PATH milik menu desktop.
sed -i "s|^Exec=.*|Exec=$HOME/.local/bin/kevin-browser %U|" "$desktop"
update-desktop-database ~/.local/share/applications 2>/dev/null || true

echo "Terpasang."
echo "Kalau kevin-browser sedang terbuka, tutup dulu — instans tunggal: yang lama tetap dipakai."
echo "Jadikan peramban bawaan: xdg-settings set default-web-browser lokal.kevin.Browser.desktop"
