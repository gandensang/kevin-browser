#!/usr/bin/env bash
# Mengukur RAM kevin-browser beserta semua proses turunannya (proses web,
# proses jaringan, bwrap, xdg-dbus-proxy).
#
#   scripts/ukur-ram.sh          # instans kevin-browser yang paling baru
#   scripts/ukur-ram.sh 12345    # pohon proses dari PID tertentu
#
# PSS = RAM yang benar-benar ditanggung proses (halaman pustaka bersama
#       dibagi rata ke semua pemakainya). Angka utama untuk membandingkan.
# RSS = pustaka bersama dihitung utuh di tiap proses; jumlahnya melebihi
#       kenyataan.
# GPU = buffer grafis milik proses (drm fdinfo, dikurangi yang dibagi ke
#       proses lain). Di GPU terintegrasi ini juga RAM sistem, tapi tidak
#       ikut terhitung di PSS maupun RSS.
set -euo pipefail

akar=${1:-$(pgrep -xn kevin-browser || true)}
if [[ -z $akar || ! -d /proc/$akar ]]; then
  echo "kevin-browser tidak berjalan" >&2
  exit 1
fi

# Semua turunan akar, dari pasangan pid-ppid.
mapfile -t pids < <(ps -e -o pid=,ppid= | awk -v akar="$akar" '
  { anak[$2] = anak[$2] " " $1 }
  END {
    antre[1] = akar; n = 1
    for (i = 1; i <= n; i++) {
      print antre[i]
      k = split(anak[antre[i]], a, " ")
      for (j = 1; j <= k; j++) antre[++n] = a[j]
    }
  }')

# KiB; satu klien drm bisa terbuka lewat beberapa fd, jadi dihitung sekali.
gpu_kib() {
  local pid=$1 total=0 fd info id
  local -A dilihat=()
  for fd in /proc/"$pid"/fd/*; do
    [[ $(readlink "$fd" 2>/dev/null) == /dev/dri/* ]] || continue
    info=/proc/$pid/fdinfo/${fd##*/}
    id=$(awk '/^drm-client-id:/ {print $2}' "$info" 2>/dev/null)
    [[ -n $id && -z ${dilihat[$id]:-} ]] || continue
    dilihat[$id]=1
    total=$(( total + $(awk '
      function kib(v, u) { return u == "MiB" ? v * 1024 : v }
      /^drm-resident-/ { r += kib($2, $3) }
      /^drm-shared-/   { s += kib($2, $3) }
      END { print int(r - s) }' "$info") ))
  done
  echo "$total"
}

for pid in "${pids[@]}"; do
  [[ -r /proc/$pid/smaps_rollup ]] || continue
  printf "%s %s %s %s %s\n" "$pid" "$(cat /proc/"$pid"/comm)" \
    "$(awk '/^Pss:/ {print $2}' /proc/"$pid"/smaps_rollup)" \
    "$(awk '/^Rss:/ {print $2}' /proc/"$pid"/smaps_rollup)" \
    "$(gpu_kib "$pid")"
done | awk '
  BEGIN { printf "%7s  %-20s %8s %8s %8s\n", "PID", "PROSES", "PSS MB", "RSS MB", "GPU MB" }
  {
    printf "%7s  %-20s %8.1f %8.1f %8.1f\n", $1, $2, $3/1024, $4/1024, $5/1024
    pss += $3; rss += $4; gpu += $5
    if ($2 == "WebKitWebProces") web++
  }
  END {
    printf "%7s  %-20s %8.1f %8.1f %8.1f\n", "", "TOTAL", pss/1024, rss/1024, gpu/1024
    printf "proses web: %d   PSS+GPU: %.1f MB\n", web, (pss + gpu)/1024
  }'
