# Merilis versi baru

Panduan untuk pemelihara. Versi baru disebar lewat repo APT
[gandensang/kevin-browser-apt](https://github.com/gandensang/kevin-browser-apt),
disajikan GitHub Pages di <https://gandensang.github.io/kevin-browser-apt>
(suite `stabil`, komponen `main`). Laptop yang sudah memasang Kevin Browser
mendapatnya lewat Update Manager.

1. Jalankan `scripts/uji-cepat.sh` (±3 menit). Skrip ini juga perlu
   dijalankan setiap kali Update Manager membawa WebKitGTK versi baru: ia
   memeriksa pemblokir, kebocoran proses, dan gambar halaman dengan WebKit
   yang terpasang. Jendela ujinya disetir dengan klik dan ketikan X11
   sungguhan, jadi jangan memakai mouse dan keyboard selama itu, atau
   jalankan di layar bersarang (`DISPLAY=:9`, lihat CONTRIBUTING.md).
2. Naikkan `<Version>` di `Directory.Build.props`. apt hanya memperbarui ke
   versi yang lebih tinggi. Nomor ini juga tampil di kaki setiap halaman
   `kevin://`.
3. Jalankan `scripts/rilis-apt.sh`. Skrip ini membuat paket .deb
   (`scripts/buat-deb.sh`), menaruhnya di clone `../kevin-browser-apt`,
   memperbarui dan menandatangani indeksnya, lalu push. Sekitar satu menit
   kemudian versinya terlihat di Update Manager.

Kolom `Maintainer` paket diambil dari `git config user.name` dan
`user.email` repo ini.

## Kunci penanda tangan

Indeks repo APT ditandatangani kunci GPG "Kevin Browser (penanda tangan
paket APT)", sidik jari `48CE 7E5E 6937 C0EC F870 D17F 688E 09E8 EB90 B1A5`.
Kunci publiknya ada di `paket/kevin-browser.gpg` dan dipasang bersama
paket. Kunci rahasianya hanya ada di laptop pemelihara, tidak pernah di
repo mana pun. Cadangkan ke media terpisah dan simpan di tempat aman:

    gpg --export-secret-keys --armor 48CE7E5E6937C0ECF870D17F688E09E8EB90B1A5 > kunci-rahasia-kevin-browser.asc

Kalau kunci itu hilang, rilis baru tidak bisa ditandatangani lagi, dan
setiap laptop pemakai harus memasang kunci baru.

`paket/kevin-browser.gpg` dan `paket/kevin-browser.sources` harus sama
persis byte-per-byte dengan salinannya di repo APT (disalin
`scripts/rilis-apt.sh`), supaya dpkg tidak bertanya saat paket menimpa
berkas yang dipasang manual.

## Mesin catur (Stockfish)

Mesin tebak langkah tidak ikut paket. `MesinCatur` mengunduhnya dari rilis
GitHub repo ini, `stockfish-19-tanpa-simd`, saat pemakai menekan tombol
pasang. Isinya Stockfish yang kita bangun sendiri tanpa WASM SIMD, karena
WebKit mematikan WASM SIMD di prosesor tanpa AVX dan versi resmi
stockfish.js tidak jalan di sana (CONTRIBUTING.md, "Pitfalls we've already hit"). Rilis itu
hanya perlu dibuat ulang kalau Stockfish diperbarui:

1. `scripts/bangun-stockfish.sh` (±1 menit; pertama kali ditambah unduhan
   Emscripten ±300 MB). Hasilnya di `dist/stockfish/`: dua berkas mesin dan
   arsip sumbernya. Membangun ulang sumber yang sama menghasilkan berkas
   yang persis sama.
2. Commit dan push perubahan skrip atau tambalannya dulu, lalu unggah
   ketiga berkas ke rilis bertag baru yang menunjuk ke commit itu, mis.:

       gh release create stockfish-20-tanpa-simd dist/stockfish/* --target belajar --latest=false \
           --title "Stockfish 20 tanpa SIMD (mesin tebak langkah)" --notes "…"

   Arsip sumbernya wajib ikut (GPL-3.0).
3. Tulis nama berkas, ukuran, dan sidik SHA-256 yang dicetak skrip ke
   `MesinCatur` (`Skrip`, `Wasm`, `Berkas`, `AsalRilis`, `HalamanRilis`).
   Jangan menimpa berkas di rilis yang masih dipakai versi browser yang
   beredar: browser itu menolak berkas yang sidiknya lain.
