# Kevin Browser

Peramban web ringan untuk Linux, untuk laptop lama dengan RAM kecil. Tetap
bisa membuka web modern (YouTube, Google Docs, WhatsApp Web, situs berita),
tetapi dibuat sehemat mungkin: tab tidur, pemblokir iklan dan pelacak, dan
video yang tidak berputar sendiri. Mesin pencari bawaannya Google.

    kevin-browser                      # jendela baru, di beranda
    kevin-browser detik.com            # langsung membuka alamat
    kevin-browser "cara membuat kue"   # dicari di Google, seperti di kotak alamat

Dibuat dengan C#/.NET 10, GTK4, dan WebKitGTK 6.0 (WebKit, mesin yang juga
dipakai Safari), lewat binding [GirCore](https://github.com/gircore/gir.core).
Hasilnya satu berkas program ±4,9 MB yang tidak butuh .NET di laptop tujuan.

*[In English](#in-english)*

## Kenapa proyek ini lahir

Kevin Browser lahir dari keterbatasan.

Ini proyek keluarga. Kevin adalah nama anak kami. Saat browser ini dibuat,
tahun 2026, ia duduk di kelas 1 SMA. Kevin butuh laptop, tapi uangnya tidak
ada. Akhirnya kami membeli laptop bekas jadul yang harganya sangat murah,
lalu memasang Linux di dalamnya supaya ringan.

Linux-nya memang ringan. Kendalanya ada di browser: rata-rata browser
sekarang rakus memori. Dari situ Kevin mencetuskan ide untuk membuat
browser sendiri. Maka lahirlah Kevin Browser: browser yang tetap bisa
membuka web modern, tapi dibuat sehemat mungkin untuk laptop seperti milik
Kevin.

Kalau Anda juga memakai laptop lama, browser ini untuk Anda juga. Dan
kalau Anda programmer, proyek ini sangat terbuka untuk dikembangkan
bersama: mencoba di laptop lain, melapor, memperbaiki, atau membangun fitur
baru. Lihat [Ikut mengembangkan](#ikut-mengembangkan).

## Yang membuatnya hemat

- **Tab tidur.** Hanya 2–4 tab yang hidup sekaligus, tergantung RAM laptop
  (2 tab untuk RAM 2 GB). Tab yang paling lama tidak dilihat ditidurkan:
  halamannya dibuang dari memori, alamat dan riwayat mundur/majunya
  disimpan. Tab latar yang 5 menit tidak dilihat juga ditidurkan, dan semua
  tab latar langsung ditidurkan kalau RAM laptop tinggal di bawah 15%.
  WhatsApp Web, tab yang sedang memutar suara, tab yang memakai mikrofon
  atau kamera, dan tab berisi ketikan formulir tidak ikut ditidurkan.
- **Tab latar tidak dimuat** sebelum dilihat, dan tab baru yang kosong
  tidak punya proses web sama sekali.
- **Iklan dan pelacak diblokir** (EasyList, EasyPrivacy, dan ABPindo untuk
  iklan situs Indonesia). Di situs berita, iklan memakan lebih banyak memori
  daripada beritanya (lihat angkanya di bawah). Parameter pelacak di alamat
  (`utm_…`, `fbclid`, `gclid`, …) ikut dibuang.
- **Video tidak berputar sendiri**, termasuk di YouTube (WhatsApp Web
  dikecualikan, supaya GIF tetap bergerak). Di sebuah halaman video
  detik.com, memori seluruh browser ±820 MB lebih kecil dan CPU ±60% satu
  inti lebih rendah daripada saat videonya berputar sendiri.
- **YouTube versi mobile.** Halaman tontonnya separuh memori versi desktop.
  Musik tetap berbunyi saat pindah tab. Versi desktop tetap bisa dipilih.
- **Video diurai kartu grafis kalau bisa** (H.264 lewat VA-API), dan paling
  tinggi 720p.
- **Batas memori per proses WebKit** dan heap JavaScript yang lebih sering
  dibersihkan.
- **Dua kebocoran proses WebKitGTK 2.52 ditambal.** Tanpa tambalan ini,
  setiap situs yang pernah dibuka meninggalkan prosesnya di memori.
- **Satu berkas NativeAOT**: proses tampilan tanpa JIT, ±20 MB lebih kecil.

Selain itu: tab yang terbuka dibuka lagi saat browser dibuka berikutnya
(juga setelah laptop mati mendadak), Ctrl+Shift+T membuka lagi tab yang
baru ditutup, notifikasi WhatsApp Web, dan tampilan dalam bahasa Indonesia
atau Inggris.

## Angka

Diukur di laptop pengembang (Intel HD 4600, RAM 8 GB): PSS seluruh proses
browser ditambah memori grafis, 25 detik setelah halaman dibuka di profil
kosong, rata-rata dua kali ukur dengan `scripts/ukur-halaman.sh`.

| Halaman | Dengan pemblokir (bawaan) | Tanpa pemblokir |
|---|---|---|
| Beranda, baru dibuka | 313 MB | 310 MB |
| google.com | 408 MB | 400 MB |
| detik.com | 497 MB | 1150 MB |
| kompas.com | 578 MB | 899 MB |
| tribunnews.com | 433 MB | 674 MB |
| Halaman tonton YouTube (versi mobile) | 537 MB | 542 MB |

YouTube versi desktop, diukur dengan cara yang sama: 1067 MB.

Harganya juga ditulis terang-terangan di halaman *Kelebihan & Kekurangan*
di dalam browser: panggilan video belum bisa (WebKitGTK dari Ubuntu/Mint
dibangun tanpa WebRTC), tab yang tidur harus dimuat ulang saat dibuka, dan
video harus diklik dulu.

## Memasang

### Lewat apt (disarankan)

Untuk Linux Mint 22 atau Ubuntu 24.04 ke atas, 64-bit:

    sudo wget -qO /usr/share/keyrings/kevin-browser.gpg https://gandensang.github.io/kevin-browser-apt/kevin-browser.gpg
    sudo wget -qO /etc/apt/sources.list.d/kevin-browser.sources https://gandensang.github.io/kevin-browser-apt/kevin-browser.sources
    sudo apt update
    sudo apt install kevin-browser

apt ikut memasang WebKitGTK 6.0, GTK4, dan codec video kalau belum ada.
Setelah itu Kevin Browser muncul di menu. Versi baru datang lewat Update
Manager, bersama pembaruan lain. Melepasnya: `sudo apt purge kevin-browser`.

Paketnya disajikan dari repo
[gandensang/kevin-browser-apt](https://github.com/gandensang/kevin-browser-apt)
lewat GitHub Pages, dengan indeks yang ditandatangani. Paket itu dibangun
dari kode di repo ini.

### Dari berkas .deb

Buat paketnya dengan `scripts/buat-deb.sh` (butuh .NET 10 SDK), salin
`dist/kevin-browser_VERSI_amd64.deb` ke laptop tujuan, lalu:

    sudo apt install ./kevin-browser_VERSI_amd64.deb

Paket ini juga memasang alamat repo APT di atas beserta kuncinya, jadi
versi berikutnya datang lewat Update Manager.

### Satu berkas saja

Yang dibawa cukup `Linux/bin/Release/net10.0/linux-x64/publish/kevin-browser`
hasil `dotnet publish Linux -c Release`. Laptop tujuan butuh Linux 64-bit
dengan glibc 2.34 atau lebih baru, dan pustaka WebKitGTK 6.0
(`sudo apt install libwebkitgtk-6.0-4`, ikut membawa GTK4 dan GStreamer).
Kalau video tidak mau diputar, pasang codec (`sudo apt install
mint-meta-codecs`); supaya video diurai kartu grafis, pasang
`va-driver-all`. Flashdisk FAT32/exFAT menghilangkan izin eksekusi, jadi
setelah disalin jalankan `chmod +x kevin-browser`.

Kevin Browser hanya berjalan satu instans: membukanya lagi menambah tab di
jendela yang sudah ada.

## Memakai

Saat dibuka, Kevin Browser menampilkan berandanya sendiri (`kevin://beranda`)
dengan kotak cari Google dan bookmark. Menu atas hanya berisi Beranda,
Pengaturan, dan tempat untuk fitur yang akan datang ("Segera hadir").
Halaman tentang browser ini (Kisah, Kelebihan & Kekurangan, Panduan,
Rencana, Tentang Kami) ada di bagian bawah setiap halaman. Semuanya
tertanam di dalam program, jadi bisa dibuka tanpa internet.

Kotak alamat menerima alamat maupun kata kunci: `detik.com` dibuka
langsung, `cara membuat kue` dicari di Google.

| Tombol | Aksi |
|---|---|
| Ctrl+L, Alt+D, F6 | ke kotak alamat |
| Ctrl+T / Ctrl+W | tab baru / tutup tab |
| Ctrl+Shift+T | buka lagi tab yang baru ditutup |
| Ctrl+Tab / Ctrl+Shift+Tab | tab berikut / sebelumnya |
| Alt+Kiri / Alt+Kanan | mundur / maju |
| Alt+Home | beranda |
| Ctrl+H | riwayat |
| Ctrl+F | cari di halaman |
| Ctrl+D / Ctrl+Shift+O | simpan bookmark / kelola bookmark |
| Ctrl+Shift+Delete | hapus data penjelajahan |
| F5 / Shift+F5 | muat ulang / muat ulang tanpa cache |
| Ctrl+Plus / Ctrl+Minus / Ctrl+0 | perbesar / perkecil / ukuran asli |
| Ctrl+P / F11 | cetak / layar penuh |
| Klik tengah atau Ctrl+klik tautan | buka di tab latar |

Panduan lengkapnya ada di dalam browser (`kevin://panduan`). Semua data
pemakai (riwayat, bookmark, cookie, daftar tab, pilihan) disimpan di
`~/.local/share/kevin-browser/`, cache di `~/.cache/kevin-browser/`, dan
bisa dihapus dari `kevin://pengaturan`. Kevin Browser sendiri tidak
mengirim apa pun ke server mana pun; selain situs yang Anda buka, yang
diunduhnya hanya daftar pemblokir, seminggu sekali.

## Rencana

Kevin Browser masih muda. Menu atas sengaja dikosongkan untuk fitur-fitur
khusus, yang bentuknya masih dipikirkan:

- **Fungsi agentik.** Asisten AI yang bisa diajak bekerja di dalam browser,
  bukan sekadar menjawab pertanyaan.
- **Catur.** Catur adalah hobi anak-anak kami. Akan ada sesuatu yang
  berhubungan dengan catur.
- **Versi Windows.** Logika browser sudah dipisahkan di `Inti/` supaya bisa
  dipakai ulang untuk aplikasi Windows (WinForms + WebView2).

Punya ide, atau ingin ikut membangun salah satunya? Buka Issue untuk
berdiskusi.

## Ikut mengembangkan

Kontribusi dalam bentuk apa pun disambut: mencoba di laptop lama dan
melaporkan hasilnya, memperbaiki bug, mengukur memori, menerjemahkan, atau
mengerjakan fitur di bagian Rencana. Mulai dari
[CONTRIBUTING.md](CONTRIBUTING.md): cara membangun, struktur kode,
kebiasaan proyek ini, dan jebakan WebKitGTK yang sudah pernah ditemui.

Singkatnya, dengan .NET 10 SDK dan `libwebkitgtk-6.0-4` terpasang:

    dotnet build
    dotnet test Uji
    dotnet run --project Linux -- https://contoh.com

Langkah merilis versi baru ke repo APT ada di [RILIS.md](RILIS.md).

## Lisensi

[MIT](LICENSE). Bebas dipakai, diubah, dan dibagikan, termasuk untuk
membuat browser Anda sendiri.

## In English

Kevin Browser is a lightweight web browser for Linux, made for old laptops
with little RAM. It was born out of limitation: a family bought a very
cheap, old second-hand laptop for their son Kevin and installed Linux on
it, but modern browsers were too hungry for memory. So Kevin came up with
the idea of building his own.

It opens the modern web (YouTube, Google Docs, WhatsApp Web) while saving
memory: sleeping tabs, an ad and tracker blocker, videos that don't play
by themselves, the mobile version of YouTube, and hardware video decoding
when available. It is written in C#/.NET 10 with GTK4 and WebKitGTK 6.0,
and ships as a single 4.9 MB file. The interface is available in
Indonesian and English.

Install it on Linux Mint 22 / Ubuntu 24.04 or later with the apt commands
in [Memasang](#memasang). Contributions are very welcome. Code identifiers,
comments, and commit messages are in Indonesian, but issues and pull
requests in English are fine. See [CONTRIBUTING.md](CONTRIBUTING.md).
Licensed under [MIT](LICENSE).
