#!/usr/bin/env bash
# Membangun mesin catur tebak langkah: Stockfish 19 "lite single" versi
# WebAssembly TANPA SIMD, dari stockfish.js (github.com/nmrugg/stockfish.js,
# GPL-3.0) ditambah scripts/stockfish-tanpa-simd.patch.
#
# Kenapa dibangun sendiri: WebKit mematikan WASM SIMD di prosesor x86-64
# tanpa AVX (banyak Celeron/Pentium/Atom lama), dan versi resmi stockfish.js
# butuh SIMD, jadi di laptop seperti itu mesinnya diam saja.
#
#   scripts/bangun-stockfish.sh [FOLDER_KERJA]
#
# Butuh git, make, python3, dan internet. Emscripten 3.1.7 (versi yang
# diminta stockfish.js) dipasang lewat emsdk di FOLDER_KERJA (±300 MB unduhan,
# 1,5 GB terpasang), kecuali EMSDK sudah menunjuk ke emsdk lain. Hasilnya di
# dist/stockfish/: dua berkas mesin dan arsip sumbernya (wajib ikut diunggah,
# GPL), lalu baris untuk MesinCatur.Berkas. Cara mengunggahnya: RILIS.md.
set -euo pipefail
cd "$(dirname "$0")/.."
akar=$PWD

versi=v19.0.0
emscripten=3.1.7
nama=stockfish-19-lite-tanpa-simd
kerja=${1:-${XDG_CACHE_HOME:-$HOME/.cache}/kevin-browser-stockfish}
mkdir -p "$kerja"
kerja=$(cd "$kerja" && pwd)

if [ -z "${EMSDK:-}" ]; then
    [ -d "$kerja/emsdk" ] || git clone --depth 1 https://github.com/emscripten-core/emsdk.git "$kerja/emsdk"
    EMSDK=$kerja/emsdk
fi
"$EMSDK/emsdk" install "$emscripten"
"$EMSDK/emsdk" activate "$emscripten" > /dev/null
# shellcheck disable=SC1091
source "$EMSDK/emsdk_env.sh" > /dev/null 2>&1

sumber=$kerja/stockfish.js
rm -rf "$sumber"
git clone --depth 1 --branch "$versi" https://github.com/nmrugg/stockfish.js.git "$sumber"
git -C "$sumber" apply "$akar/scripts/stockfish-tanpa-simd.patch"
# Jaringan saraf "lite" (±1,1 MB) tidak ada di repo stockfish.js; skrip
# Stockfish mengunduhnya dan memeriksa sidiknya (namanya = awal SHA-256).
make -C "$sumber/src" net LITE_NET=yes

# Arsip sumber yang sesuai dengan berkas mesinnya: sumber stockfish.js yang
# sudah ditambal, jaringan sarafnya, skrip ini, dan tambalannya.
mkdir -p "$akar/dist/stockfish" "$sumber/kevin-browser"
cp "$akar/scripts/bangun-stockfish.sh" "$akar/scripts/stockfish-tanpa-simd.patch" "$sumber/kevin-browser/"
tar --exclude=.git -czf "$akar/dist/stockfish/$nama-sumber.tar.gz" -C "$kerja" stockfish.js

(cd "$sumber" && NOSIMD=yes node build.js --lite --single-threaded --strict-em-check)

cp "$sumber/src/stockfish-19-lite-single.js" "$akar/dist/stockfish/$nama.js"
cp "$sumber/src/stockfish-19-lite-single.wasm" "$akar/dist/stockfish/$nama.wasm"

echo
echo "Selesai: dist/stockfish/"
ls -l "$akar/dist/stockfish/"
echo
echo "Ukuran dan sidik untuk MesinCatur.Berkas:"
for berkas in "$nama.js" "$nama.wasm"; do
    jalur=$akar/dist/stockfish/$berkas
    printf '  %s  %s bait  %s\n' "$berkas" "$(stat -c %s "$jalur")" "$(sha256sum "$jalur" | cut -d' ' -f1)"
done
