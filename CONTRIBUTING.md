# Ikut mengembangkan Kevin Browser

Terima kasih sudah mampir. Kevin Browser dibuat untuk laptop lama dengan RAM
kecil, dan setiap bantuan berarti: laporan dari laptop lain, angka ukur,
perbaikan, terjemahan, atau fitur baru.

## Cara mulai

1. Buka Issue dulu untuk hal yang lebih besar dari perbaikan kecil, supaya
   arahnya bisa dibicarakan sebelum Anda menghabiskan waktu.
2. Fork, buat cabang, kirim Pull Request.
3. Issue dan Pull Request boleh berbahasa Indonesia atau Inggris.

Bantuan yang paling dicari:

- **Mencoba di laptop lama** (RAM 2–4 GB, kartu grafis lama, harddisk biasa)
  dan melaporkan apa yang lambat atau rusak, dengan spesifikasinya
  (`inxi -Fxz`).
- **Mengurangi memori proses halaman WebKit**, yang untuk halaman berat
  masih jauh lebih besar daripada yang diharapkan.
- **Fitur di bagian Rencana** di README: catur, asisten di dalam browser,
  dan versi Windows.
- **Terjemahan dan tulisan**: halaman bawaan ada dalam bahasa Indonesia dan
  Inggris.

## Membangun

Butuh .NET 10 SDK dan, untuk menjalankan, GTK4 dan WebKitGTK 6.0
(`sudo apt install libwebkitgtk-6.0-4`). Dikembangkan di Linux Mint 22.3
(GTK 4.14, WebKitGTK 2.52).

    dotnet build                                     # semua proyek
    dotnet run --project Linux -- https://contoh.com
    dotnet test Uji                                  # tes logika di Inti/
    dotnet test Uji --filter "FullyQualifiedName~UjiAlamat"
    dotnet publish Linux -c Release                  # satu berkas NativeAOT
    scripts/pasang.sh                                # publish + pasang ke ~/.local/bin dan menu

Build Debug memakai profil terpisah (`~/.local/share/kevin-browser-debug`)
dan tidak berjalan sebagai instans tunggal, jadi aman dijalankan di samping
versi yang terpasang. Build rilis berjalan sebagai instans tunggal: membuka
binary kedua hanya mengoper alamat ke jendela yang sudah ada.

## Struktur kode

- **`Inti/`**: logika tanpa GTK dan WebKit, supaya bisa dipakai ulang untuk
  versi Windows dan bisa dites tanpa jendela. Tafsir kotak alamat
  (`Alamat`), aturan tidur-tab (`TidurTab`), angka RAM (`Setelan`), halaman
  `kevin://` (`HalamanBawaan` dan halaman di `Inti/Halaman/`), riwayat,
  bookmark, pemulihan tab, pengubah daftar Adblock (`KonverterAdblock`),
  pembuang parameter pelacak, bahasa tampilan (`Teks`, `Preferensi`), dan
  aturan per situs (`IzinMedia`, `IzinNotifikasi`, `PutarOtomatis`).
- **`Linux/`**: aplikasinya. `Program` menyiapkan lingkungan, `Mesin`
  membuat satu WebContext dan NetworkSession untuk semua tab (semua
  pengaturan hemat-RAM global ada di sini), `Jendela` adalah toolbar dan
  deretan tab, `Tab` memegang satu WebView yang bisa ditidurkan dan
  dibangunkan. `Penyaring` mengurus pemblokir, `Pemulung` membereskan
  proses WebKit yang bocor, `Sinyal` dan `Asli` menyambung ke C.
- **`Uji/`**: tes xUnit untuk `Inti/`.
- **`scripts/`**: paket .deb, rilis APT, alat ukur RAM, dan alat uji jendela.

## Kebiasaan proyek ini

- **Bahasa Indonesia** untuk nama kelas, method, variabel, komentar, dan
  pesan commit.
- **Teks yang dilihat pemakai ditulis dua bahasa.** Di kode:
  `t["Simpan", "Save"]` (lihat `Inti/Bahasa.cs`). Halaman tetap punya
  versi Inggris di `Inti/Halaman/en/` dengan nama berkas yang sama. Tes
  `HalamanBahasaInggrisLengkap` menangkap kata Indonesia yang tertinggal di
  halaman Inggris.
- **Setiap klaim soal RAM atau CPU harus diukur**, dan hasilnya ditulis di
  komentar di samping kodenya ("Terukur: …"). Beberapa dugaan di proyek ini
  ternyata salah setelah dicoba. Angka di README, `beranda.html`, dan
  `kelebihan.html` (dan versi Inggrisnya) harus sama.
- **Peringatan dianggap galat** (`TreatWarningsAsErrors`).
- **Teks publik tidak membandingkan Kevin Browser dengan browser lain
  bermerek.** Bandingkan dengan angka sendiri. Penyebutan fakta boleh, mis.
  "WebKit, mesin yang juga dipakai Safari".
- **Halaman Kisah adalah cerita keluarga dengan kata-kata mereka sendiri.**
  Jangan menambah detail yang tidak mereka ceritakan.

## Jebakan yang sudah pernah terjadi

Semua ini pernah membuang waktu berjam-jam. Baca sebelum menyentuh
`Linux/`.

- **Jangan pakai event GirCore** (`web.OnLoadChanged`, `tombol.OnClicked`,
  …). Setiap pemancaran sinyal membocorkan satu referensi ke pengirimnya,
  sehingga WebView tidak pernah dihancurkan dan proses web-nya tidak pernah
  berhenti. Pakai `Sinyal.Sambung(objek, "nama-sinyal", …)`, dengan bentuk
  delegate yang persis sama dengan tanda tangan C sinyalnya. Pointer di
  argumen sinyal hanya berlaku selama handler berjalan.
- **Objek boxed GirCore yang kita miliki** (`WebViewSessionState`,
  `GObject.Value`, …) harus di-Dispose sendiri di thread utama.
- **Pembungkus GirCore yang hanya diambil pointernya bisa dipungut GC** di
  build AOT, dan objek aslinya ikut dibebaskan. Jaga pembungkusnya tetap
  hidup (`using`, field) selama pointernya dipakai.
- **Env untuk GTK/WebKit harus lewat `GLib.Functions.Setenv`.**
  `Environment.SetEnvironmentVariable` tidak sampai ke kode C maupun ke
  proses anak WebKit.
- **`SetCacheModel` harus dipanggil sebelum WebContext dibuat.** Kalau
  terbalik, setiap situs yang ditinggalkan menyisakan prosesnya.
- **WebKitGTK 2.52 dengan sandbox membocorkan proses "prewarm"**, +1 proses
  setiap pindah situs. `Pemulung` membunuhnya. Kalau WebKit diperbarui,
  cek ulang apakah ini masih perlu (`scripts/uji-cepat.sh`).
- **Opsi JSC berangka pecahan dibaca menurut locale.** Di locale berdesimal
  koma, `1.5` terbaca `1`. Pakai opsi bilangan bulat, dan selalu lihat
  tangkapan layarnya.
- **`decide-policy` juga dipancarkan untuk iframe**, dan API-nya tidak bisa
  membedakannya dari bingkai utama.
- **Pemblokir konten WebKit**: satu regex yang tidak didukung (`|`,
  `{n,m}`, backreference, karakter non-ASCII) menggagalkan seluruh daftar;
  batasnya 150.000 aturan per filter; aksi `redirect` dan `modify-headers`
  tidak pernah dijalankan.
- **WebKitGTK dari Ubuntu/Mint dibangun tanpa WebRTC**, jadi panggilan video
  tidak mungkin, apa pun izinnya.
- **`NetworkSession` tidak menyimpan cookie ke disk** tanpa
  `SetPersistentStorage`.

## Menguji

- `dotnet test Uji` untuk semua logika di `Inti/`. Bagian GTK/WebKit tidak
  punya tes otomatis; jalankan aplikasinya.
- `KEVIN_BROWSER_CATAT=1` mencatat tab bangun/tidur, izin, dan proses yang
  dimatikan ke stderr.
- `KEVIN_BROWSER_UJI_JS='…'` (build Debug saja) menjalankan JavaScript di
  halaman pertama yang selesai dimuat.
- `scripts/klik.py` mengirim klik dan ketikan X11 sungguhan ke jendela
  kevin-browser terbaru, dan `scripts/tangkap.py` membuat tangkapan
  layarnya. `klik.py` menolak mengirim apa pun kalau jendela itu tidak
  fokus. Supaya tidak mengganggu jendela lain, jalankan di layar X
  bersarang:

      Xephyr :9 -screen 1366x768 -ac &
      env -u I3SOCK DISPLAY=:9 i3 &   # atau window manager lain; klik.py dan tangkap.py butuh wmctrl
      DISPLAY=:9 dotnet run --project Linux

  (`env -u I3SOCK`: kalau desktop Anda sendiri juga i3, tanpa ini i3 yang
  kedua mencoba memakai soket i3 Anda.)

- Mengukur memori: `scripts/ukur-ram.sh [PID]` (PSS dan memori grafis
  seluruh proses browser) dan `scripts/ukur-halaman.sh URL` (satu halaman di
  profil kosong, dengan dan tanpa pemblokir; `BANDING=putar` untuk video
  yang berputar sendiri). Bandingkan hanya angka yang diukur dalam keadaan
  yang sama: PSS dipengaruhi proses lain yang sedang hidup.
- `scripts/uji-cepat.sh`: uji asap binary rilis setelah WebKitGTK diperbarui
  atau sebelum merilis.

## Sebelum mengirim Pull Request

- `dotnet build` tanpa peringatan, `dotnet test Uji` lulus.
- Perubahan yang menyangkut memori disertai angka ukur.
- Teks baru untuk pemakai ada dalam dua bahasa.
