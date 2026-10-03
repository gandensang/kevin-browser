#!/usr/bin/env bash
# Uji cepat sesudah WebKitGTK diperbarui (Update Manager) atau sebelum
# merilis: apakah pemblokir, tambalan kebocoran proses, dan gambar halaman
# masih bekerja dengan WebKit yang terpasang. Profil pemakai tidak disentuh.
#
#   scripts/uji-cepat.sh                        # binary rilis hasil build
#   BIN=/usr/bin/kevin-browser scripts/uji-cepat.sh
#
# Jendela uji muncul di layar dan disetir dengan klik/ketikan X11 sungguhan
# (scripts/klik.py) selama ±3 menit: jangan memakai mouse dan keyboard
# selama itu. Hasilnya baris OK / GAGAL / INFO; kode keluar 1 kalau ada
# yang GAGAL.
set -euo pipefail
cd "$(dirname "$0")/.."

bin=${BIN:-Linux/bin/Release/net10.0/linux-x64/publish/kevin-browser}
if [[ ! -x $bin ]]; then
  echo "Build rilis dulu: dotnet publish Linux -c Release" >&2
  exit 1
fi
bin=$(realpath "$bin")

kerja=$(mktemp -d "${TMPDIR:-/tmp}/kevin-browser-uji-XXXXXX")
profil=$kerja/profil
mkdir -p "$profil/data/kevin-browser" "$profil/cache"
log=$kerja/stderr.log
gagal=0
pid=

ok() { echo "OK     $*"; }
salah() { echo "GAGAL  $*"; gagal=1; }
info() { echo "INFO   $*"; }

beres() {
  # Proses browser uji saja, lewat PID-nya: `pkill -f` dengan path binary
  # ikut mematikan kevin-browser pemakai kalau BIN=/usr/bin/kevin-browser.
  [[ -n $pid ]] && kill "$pid" 2> /dev/null && sleep 1
  if [[ -n ${SIMPAN:-} ]]; then echo "Profil dan log uji: $kerja"; else rm -rf "$kerja"; fi
}
trap beres EXIT

versi=$(dpkg-query -W -f='${Version}' libwebkitgtk-6.0-4 2> /dev/null || echo "?")
info "WebKitGTK $versi, binary $bin"

# 1. Proses anak pembaru pemblokir, langsung (seperti yang dijalankan
#    browser seminggu sekali): unduh, ubah, kompilasi dengan WebKit ini.
penyaring=$profil/data/kevin-browser/penyaring
if "$bin" --perbarui-penyaring "$penyaring" 2> "$kerja/penyaring.log" \
    && grep -q "\"webkit\":\"${versi%%-*}\"" "$penyaring/info.json"; then
  ok "pemblokir dikompilasi: $(grep -o '[a-z]*: [0-9]* aturan' "$kerja/penyaring.log" | paste -sd, | sed 's/,/, /g')"
else
  salah "pemblokir tidak terkompilasi:"
  sed 's/^/         /' "$kerja/penyaring.log"
fi

# 2. Browser di profil sementara, sesi D-Bus sendiri (instans tunggal tidak
#    mengoper alamatnya ke jendela pemakai). fatal-criticals: peringatan
#    "assertion … failed" dari GTK/WebKit menghentikan browser, supaya
#    ketahuan. Tanpa GTK_A11Y=none, bus aksesibilitas yang tidak ada di sesi
#    D-Bus baru juga dianggap critical.
dbus-run-session -- bash -c 'echo $$ > "$1"/pid; shift; exec "$@"' _ "$kerja" \
  env XDG_DATA_HOME="$profil/data" XDG_CACHE_HOME="$profil/cache" KEVIN_BROWSER_CATAT=1 \
  G_DEBUG=fatal-criticals GTK_A11Y=none "$bin" https://example.com/ > /dev/null 2> "$log" &
sleep 1
pid=$(cat "$kerja/pid")
berhenti() {
  salah "$1. Akhir log:"
  tail -15 "$log" | sed 's/^/         /'
  exit 1
}
for _ in $(seq 20); do
  wmctrl -lp | awk -v p="$pid" '$3 == p { ada = 1 } END { exit !ada }' && break
  sleep 1
done

# 3. Satu tab berpindah ke lima situs: tiap pindah situs WebKit membuat
#    proses baru, dan di WebKitGTK 2.52 menyisakan proses "prewarm".
for situs in id.wikipedia.org www.detik.com lichess.org m.youtube.com example.com; do
  PID=$pid scripts/klik.py tombol:ctrl+l ketik:"$situs" tombol:Return tunggu:9 > /dev/null 2>&1 \
    || berhenti "jendela browser hilang saat membuka $situs"
done
PID=$pid scripts/tangkap.py "$kerja/example.png" > "$kerja/tangkap.txt" 2>&1 || true

# Pemulung menyapu tiap 30 detik, proses yang berumur ≥20 detik.
sleep 65

kill -0 "$pid" 2> /dev/null || berhenti "browser berhenti di tengah uji"
ok "browser masih berjalan setelah 5 situs"

if grep -q "penyaring dimuat: easylist, easyprivacy, abpindo" "$log"; then
  ok "pemblokir dimuat browser"
else
  salah "pemblokir tidak dimuat: $(grep -m1 penyaring "$log" || echo 'tidak ada catatan')"
fi

mati=$(grep -c "proses web mati" "$log" || true)
if [[ $mati == 0 ]]; then ok "tidak ada proses halaman yang mati"; else salah "$mati proses halaman mati (crash):"; grep "proses web mati" "$log" | sed 's/^/         /'; fi

kecerahan=$(grep -o 'kecerahan konten [0-9.]*' "$kerja/tangkap.txt" | awk '{print int($3)}')
if [[ ${kecerahan:-0} -gt 20 ]]; then ok "halaman tergambar (kecerahan $kecerahan)"; else salah "halaman hitam/kosong (kecerahan ${kecerahan:-?}); lihat SIMPAN=1"; fi

ram=$(scripts/ukur-ram.sh "$pid" | tail -1)
web=$(grep -o 'proses web: [0-9]*' <<< "$ram" | awk '{print $3}')
# Satu proses halaman, ditambah service worker situs yang memasangnya.
if [[ $web -le 2 ]]; then ok "$ram"; else salah "$ram (lebih dari 2: kebocoran proses?)"; fi

yatim=$(grep -c "proses web yatim dimatikan" "$log" || true)
info "pemulung mematikan $yatim proses prewarm. Kalau 0 di WebKit baru, kebocorannya mungkin sudah diperbaiki: cek ulang apakah Pemulung masih perlu (lihat komentar di Linux/Pemulung.cs)."

exit $gagal
