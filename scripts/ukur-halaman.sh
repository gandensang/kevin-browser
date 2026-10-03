#!/usr/bin/env bash
# Mengukur RAM dan CPU satu halaman dengan binary rilis, dua kali dengan
# satu pengaturan yang dibandingkan. Profil pemakai tidak disentuh.
#
#   scripts/ukur-halaman.sh https://www.detik.com/
#   TUNGGU=40 scripts/ukur-halaman.sh kevin://beranda
#   BANDING=putar scripts/ukur-halaman.sh https://www.kompas.tv/
#
# BANDING=penyaring (bawaan): tanpa lalu dengan pemblokir iklan dan pelacak.
# BANDING=putar: video boleh berputar sendiri (bawaan WebKit) lalu
# menunggu klik (bawaan Kevin Browser), pemblokir menyala di keduanya.
#
# Tiap pengukuran memakai profil kosong (tanpa cache): buka alamatnya,
# tunggu TUNGGU detik (bawaan 25), catat baris PSS+GPU dari ukur-ram.sh,
# lalu tutup. CPU = rata-rata 10 detik terakhir sebelum PSS dicatat,
# seluruh pohon proses, 100% = satu inti. Daftar pemblokir disiapkan sekali
# lewat proses pembaru (±10 detik) dan dipakai ulang selama belum berumur
# 6 hari dan masih dari WebKit yang sama. Daftar yang lebih tua akan
# diperbarui browser sendiri di tengah pengukuran, dan kompilasinya ikut
# terhitung.
set -euo pipefail
cd "$(dirname "$0")/.."

bin=Linux/bin/Release/net10.0/linux-x64/publish/kevin-browser
if [[ ! -x $bin ]]; then
  echo "Build rilis dulu: dotnet publish Linux -c Release" >&2
  exit 1
fi
# Tiap pengukuran berjalan di sesi D-Bus sendiri, jadi kevin-browser yang
# sedang terbuka tidak menerima alamatnya (instans tunggal). Tetapi pustaka
# bersamanya ikut dibagi, jadi PSS lebih kecil daripada saat ia ditutup.
if pgrep -x kevin-browser > /dev/null; then
  echo "Peringatan: kevin-browser lain sedang berjalan. PSS jadi lebih kecil;" \
    "bandingkan hanya dengan angka yang diukur dalam keadaan yang sama." >&2
fi

url=$1
tunggu=${TUNGGU:-25}
kerja=${XDG_CACHE_HOME:-$HOME/.cache}/kevin-browser-ukur
filter=$kerja/penyaring
webkit=$(dpkg-query -W -f='${Version}' libwebkitgtk-6.0-4 | cut -d- -f1)

if [[ ! -f $filter/info.json || -n $(find "$filter/info.json" -mtime +5) ]] \
    || ! grep -q "\"webkit\":\"$webkit\"" "$filter/info.json"; then
  echo "Menyiapkan daftar pemblokir…" >&2
  "$bin" --perbarui-penyaring "$filter"
fi

case ${BANDING:-penyaring} in
  penyaring) varian=("tanpa pemblokir|KEVIN_BROWSER_PENYARING=0" "dengan pemblokir|KEVIN_BROWSER_PENYARING=1") ;;
  putar) varian=("video berputar sendiri|KEVIN_BROWSER_PUTAR_OTOMATIS=1" "video menunggu klik|KEVIN_BROWSER_PUTAR_OTOMATIS=0") ;;
  *) echo "BANDING harus penyaring atau putar" >&2; exit 1 ;;
esac

# utime+stime (tick) semua proses turunan PID $1, termasuk dirinya.
tick_pohon() {
  ps -e -o pid=,ppid= | awk -v akar="$1" '
    { anak[$2] = anak[$2] " " $1 }
    END {
      antre[1] = akar; n = 1
      for (i = 1; i <= n; i++) {
        print antre[i]
        k = split(anak[antre[i]], a, " ")
        for (j = 1; j <= k; j++) antre[++n] = a[j]
      }
    }' | while read -r p; do
    # Setelah "pid (comm) ", kolom 12 dan 13 = utime dan stime.
    sed 's/^.*) //' /proc/"$p"/stat 2> /dev/null | awk '{ print $12 + $13 }'
  done | awk '{ t += $1 } END { print t + 0 }'
}
hz=$(getconf CLK_TCK)

for v in "${varian[@]}"; do
  profil=$kerja/profil
  rm -rf "$profil"
  mkdir -p "$profil/data/kevin-browser" "$profil/cache"
  cp -al "$filter" "$profil/data/kevin-browser/"

  dbus-run-session -- bash -c 'echo $$ > "$1"; shift; exec "$@"' _ "$kerja/pid" \
    env XDG_DATA_HOME="$profil/data" XDG_CACHE_HOME="$profil/cache" "${v#*|}" \
    "$bin" "$url" > /dev/null 2>&1 &
  sesi=$!
  sleep 1
  pid=$(cat "$kerja/pid")
  sleep $(( tunggu - 11 ))
  t1=$(tick_pohon "$pid")
  sleep 10
  t2=$(tick_pohon "$pid")
  printf "%-24s %s   CPU %d%%\n" "${v%%|*}" "$(scripts/ukur-ram.sh "$pid" | tail -1)" \
    $(( (t2 - t1) * 100 / (10 * hz) ))
  kill "$pid" 2> /dev/null || true
  wait "$sesi" 2> /dev/null || true
done
rm -rf "$kerja/profil" "$kerja/pid"
