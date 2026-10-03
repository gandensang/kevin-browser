#!/usr/bin/env bash
# Merilis versi baru ke repositori APT publik: membuat paket .deb,
# menaruhnya di pool/, memperbarui indeks, menandatanganinya, lalu commit dan
# push ke github.com/gandensang/kevin-browser-apt (disajikan GitHub Pages di
# https://gandensang.github.io/kevin-browser-apt). Laptop yang sudah memasang
# kevin-browser mendapat versi itu lewat Update Manager.
#
#   scripts/rilis-apt.sh [FOLDER_REPO_APT]     (bawaan: ../kevin-browser-apt)
#
# Naikkan <Version> di Directory.Build.props dulu. Penanda tangannya kunci
# GPG yang kunci publiknya ada di paket/kevin-browser.gpg; kunci rahasianya
# harus ada di laptop ini (gpg --list-secret-keys).
set -euo pipefail
cd "$(dirname "$0")/.."

repo=$(realpath "${1:-../kevin-browser-apt}")
kunci=$(gpg --show-keys --with-colons paket/kevin-browser.gpg | awk -F: '$1 == "fpr" { print $10; exit }')
versi=$(dotnet msbuild Linux -getProperty:Version)
pool=$repo/pool/main/k/kevin-browser

if [[ -e $pool/kevin-browser_${versi}_amd64.deb ]]; then
  echo "Versi $versi sudah ada di repositori. Naikkan <Version> di Directory.Build.props dulu." >&2
  exit 1
fi
if git -C "$repo" rev-parse --abbrev-ref '@{u}' > /dev/null 2>&1; then
  git -C "$repo" pull --ff-only
fi

scripts/buat-deb.sh
mkdir -p "$pool"
cp "dist/kevin-browser_${versi}_amd64.deb" "$pool/"
# Dua versi terakhir saja: GitHub Pages bukan tempat arsip.
ls -1v "$pool"/*.deb | head -n -2 | xargs -r rm --
# Untuk pemasangan manual (README repositori APT). Isinya harus sama persis
# dengan yang dipasang paket, supaya dpkg tidak bertanya saat menimpanya.
cp paket/kevin-browser.gpg paket/kevin-browser.sources "$repo/"

cd "$repo"
dists=dists/stabil
mkdir -p "$dists/main/binary-amd64"
apt-ftparchive packages pool > "$dists/main/binary-amd64/Packages"
gzip -9nkf "$dists/main/binary-amd64/Packages"

# Release ditulis ke luar dists/ dulu: apt-ftparchive ikut mendata berkas
# Release yang ditemukannya di folder yang sedang dibaca.
rm -f "$dists/Release" "$dists/InRelease" "$dists/Release.gpg"
release=$(mktemp)
apt-ftparchive \
  -o APT::FTPArchive::Release::Origin="Kevin Browser" \
  -o APT::FTPArchive::Release::Label="Kevin Browser" \
  -o APT::FTPArchive::Release::Suite=stabil \
  -o APT::FTPArchive::Release::Codename=stabil \
  -o APT::FTPArchive::Release::Architectures=amd64 \
  -o APT::FTPArchive::Release::Components=main \
  release "$dists" > "$release"
install -m644 "$release" "$dists/Release"
rm -f "$release"
gpg --batch --yes --local-user "$kunci" --clearsign -o "$dists/InRelease" "$dists/Release"
gpg --batch --yes --local-user "$kunci" --armor --detach-sign -o "$dists/Release.gpg" "$dists/Release"

git add -A
git commit -m "Kevin Browser $versi"
git push origin HEAD
echo "Dirilis: Kevin Browser $versi. GitHub Pages butuh ±1 menit sebelum versi ini terlihat."
