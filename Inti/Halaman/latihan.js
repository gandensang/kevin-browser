// Tebak langkah (kevin://catur?latihan=…) sebagai obrolan dengan pelatih AI.
// Papan hanya gambar posisi soal. Skrip ini:
//  1. mencari posisi penting untuk sisi siswa dengan Stockfish (lihat
//     penting()), lalu memintanya jadi soal di obrolan;
//  2. menganalisis posisi soal selagi siswa menulis jawaban;
//  3. mengirim jawaban ke pelatih (HalamanCatur/PelatihCatur), dan kalau
//     pelatih meminta Stockfish menguji langkah dan variasi siswa,
//     mengerjakannya lalu mengirim hasilnya kembali.
// Stockfish adalah Web Worker di halaman perantara kevin-mesin:// (iframe
// tersembunyi), karena WebKit tidak menjalankan Worker di halaman skema
// lokal kevin://.
'use strict';
(() => {
  const data = JSON.parse(document.getElementById('data-latihan').textContent);
  const T = data.teks;
  const teks = (kunci, ...isi) => T[kunci].replace(/\{(\d)\}/g, (_, n) => isi[n]);
  const el = id => document.getElementById(id);
  const papan = new PapanCatur(el('papan'));
  papan.balik = !data.putih;
  const obrolan = el('obrolan'), kotak = el('pesan'), tKirim = el('tombol-kirim');
  const tBerikutnya = el('berikutnya'), tSimpan = el('simpan');

  // Sesi ikut di alamat (#sesi=…), supaya tab yang ditidurkan lalu
  // dibangunkan melanjutkan obrolan ini: halaman yang dimuat ulang dengan
  // #sesi pindah ke alamat bersesi, dan HalamanCatur menampilkan obrolannya
  // lagi. Di skema lokal kevin://, history.replaceState hanya boleh
  // mengganti bagian # (alamat lengkap ditolak, terlihat 7 Okt 2026).
  const sesiLama = /^#sesi=([0-9A-F]{16})$/.exec(location.hash);
  if (sesiLama && sesiLama[1] !== data.sesi && !location.search.includes('sesi=')) {
    location.replace(data.alamat.replace(/sesi=[0-9A-F]+/, 'sesi=' + sesiLama[1]));
    return;
  }
  history.replaceState(null, '', '#sesi=' + data.sesi);

  let sibuk = false;          // menunggu pelatih
  let analisisAwal = null;    // Promise analisis posisi soal sekarang
  let berikutnya = null;      // Promise ply soal berikutnya, atau null
  let mesinGagal = false;

  // ---------- mesin ----------

  // Kalau mesin gagal, pesannya membawa kode supaya pemakai bisa
  // melaporkannya: WASM, WORKER, MUAT (dari perantara, lihat
  // HalamanCatur.Perantara), WAKTU-n (belum siap sesudah 30 detik; n = tahap
  // terakhir: 0 perantara tidak termuat, 1 mesin diam sama sekali, 2 mesin
  // bersuara tetapi tidak menjawab "uci", 3 tidak menjawab "isready"), dan JS
  // (galat lain di skrip ini).
  const mesin = (() => {
    const bingkai = document.createElement('iframe');
    let siap, tolakSiap, sudahSiap = false, selesai = null, tolakCari = null, mati = null, info = [];
    let tahap = 0, barisTerakhir = '';
    const janjiSiap = new Promise((ok, tolak) => { siap = ok; tolakSiap = tolak; });
    let antre = Promise.resolve();
    // Isinya hanya perintah UCI; perantara sendiri hanya menerima dari kevin://catur.
    const kirim = s => bingkai.contentWindow.postMessage(s, '*');

    // Mesin tidak bisa dipakai lagi: pencarian yang sedang menunggu dan yang
    // berikutnya gagal dengan galat berkode, dan pesannya tampil sekali.
    function rusak(kode, rincian) {
      if (mati)
        return;
      mati = Object.assign(new Error(kode), { kode, rincian });
      tolakSiap(mati);
      if (tolakCari) {
        const tolak = tolakCari;
        selesai = tolakCari = null;
        tolak(mati);
      }
      gagal(mati);
    }
    setTimeout(() => {
      if (!sudahSiap)
        rusak('WAKTU-' + tahap, barisTerakhir);
    }, 30000);

    // "info depth 14 … multipv 2 score cp -35 … pv e7e5 g1f3 …"
    function bacaInfo(s) {
      const b = s.split(' '), skor = b.indexOf('score'), pv = b.indexOf('pv');
      if (skor < 0 || pv < 0 || b[skor + 3] === 'lowerbound' || b[skor + 3] === 'upperbound')
        return null;
      const k = b.indexOf('multipv'), n = +b[skor + 2], mat = b[skor + 1] === 'mate';
      return {
        k: k < 0 ? 1 : +b[k + 1],
        cp: mat ? (n > 0 ? 100000 - n : -100000 - n) : n,
        mat: mat ? n : null,
        pv: b.slice(pv + 1),
      };
    }

    function baris(s) {
      tahap = Math.max(tahap, 2);
      if (s === 'uciok') {
        tahap = 3;
        kirim('isready');
      } else if (s === 'readyok') {
        sudahSiap = true;
        siap();
      } else if (s.startsWith('info ')) {
        const i = bacaInfo(s);
        if (i)
          info[i.k - 1] = i;
      } else if (s.startsWith('bestmove') && selesai) {
        const ok = selesai;
        selesai = tolakCari = null;
        ok(info.filter(Boolean));
      } else
        barisTerakhir = s.slice(0, 120);
    }

    addEventListener('message', e => {
      if (e.origin !== 'kevin-mesin://mesin' || e.source !== bingkai.contentWindow || !e.data)
        return;
      if (e.data.siap) {
        tahap = Math.max(tahap, 1);
        kirim('uci');
      } else if (e.data.galat)
        rusak(String(e.data.galat), e.data.rincian);
      else if (typeof e.data.mesin === 'string')
        baris(e.data.mesin);
    });
    bingkai.hidden = true;
    bingkai.src = data.perantara;
    document.body.appendChild(bingkai);

    // Satu pencarian sekaligus; yang berikutnya menunggu di antrean.
    function cari(fen, jumlah, kedalaman, waktu, langkah) {
      const kerja = antre.then(() => janjiSiap).then(() => new Promise((ok, tolak) => {
        if (mati) {
          tolak(mati);
          return;
        }
        info = [];
        selesai = ok;
        tolakCari = tolak;
        kirim('setoption name MultiPV value ' + jumlah);
        kirim('position fen ' + fen);
        kirim('go depth ' + kedalaman + ' movetime ' + waktu + (langkah ? ' searchmoves ' + langkah.join(' ') : ''));
      }));
      antre = kerja.catch(() => {});
      return kerja;
    }

    return {
      siap: janjiSiap,
      // `jumlah` langkah terbaik (0 = tidak perlu), ditambah nilai langkah
      // `langkahCari` yang belum ada di antaranya. Hasil: [{uci, cp, mat,
      // pv, bebas}] dari sudut yang melangkah; bebas = hasil MultiPV biasa.
      async analisis(fen, jumlah, langkahCari, kedalaman, waktu) {
        const hasil = [];
        if (jumlah > 0)
          for (const i of await cari(fen, jumlah, kedalaman, waktu))
            hasil.push({ uci: i.pv[0], cp: i.cp, mat: i.mat, pv: i.pv.join(' '), bebas: true });
        const kurang = (langkahCari || []).filter(u => !hasil.some(h => h.uci === u));
        if (kurang.length)
          for (const i of await cari(fen, kurang.length, kedalaman, waktu, kurang))
            hasil.push({ uci: i.pv[0], cp: i.cp, mat: i.mat, pv: i.pv.join(' '), bebas: false });
        return hasil;
      },
    };
  })();

  // ---------- posisi penting ----------

  const fenSebelum = ply => ply === 0 ? data.fen0 : data.langkah[ply - 1].fen;
  const giliranSiswa = ply => (fenSebelum(ply).split(' ')[1] === 'w') === data.putih;
  const nomor = ply => parseInt(data.langkah[ply].no, 10);

  // Peluang menang dari nilai mesin, rumus Lichess (sama dengan KEVIN).
  const peluang = cp => 50 + 50 * (2 / (1 + Math.exp(-0.00368208 * Math.max(-1000, Math.min(1000, cp)))) - 1);

  // Posisi penting: ada satu langkah yang jauh lebih baik dari yang lain
  // (taktik, satu-satunya langkah bagus, menghukum kesalahan lawan), ada
  // skakmat dalam paling banyak 3 langkah, atau langkah di partai sendiri
  // sebuah kesalahan. Di sepuluh langkah pertama ambangnya lebih tinggi:
  // langkah pembukaan biasanya hafalan, jadi hanya kesalahan besar yang
  // ditanyakan. Langkah tanpa pilihan nyata (data "lewat", dihitung
  // HalamanCatur) tidak dianalisis sama sekali. Skakmat perlu aturan sendiri:
  // di posisi yang sudah menang besar, peluang menang langkah lain juga
  // hampir 100%, jadi selisihnya kecil (terlihat 7 Okt 2026: 19... Qxg2# tidak
  // ditanyakan).
  function penting(ply, hasil) {
    const bebas = hasil.filter(h => h.bebas).sort((a, b) => b.cp - a.cp);
    if (!bebas.length)
      return false;
    if (bebas[0].mat !== null && bebas[0].mat > 0 && bebas[0].mat <= 3)
      return true;
    const terbaik = Math.max(...hasil.map(h => peluang(h.cp)));
    const partai = hasil.find(h => h.uci === data.langkah[ply].uci);
    const hilang = partai ? terbaik - peluang(partai.cp) : 0;
    const jarak = bebas.length > 1 ? peluang(bebas[0].cp) - peluang(bebas[1].cp) : 0;
    return nomor(ply) <= 10 ? hilang >= 20 || jarak >= 25 : hilang >= 10 || jarak >= 15;
  }

  // Ply soal berikutnya mulai dari ply `dari`; null kalau partainya habis.
  async function cariSoal(dari, tampil) {
    for (let ply = dari; ply < data.langkah.length; ply++) {
      if (!giliranSiswa(ply) || data.langkah[ply].lewat)
        continue;
      if (tampil)
        status(teks('mencari', nomor(ply)));
      // 600 ms: dengan 400 ms, posisi yang nilainya dekat ambang kadang
      // terpilih, kadang tidak (terlihat 7 Okt 2026 di partai yang sama).
      if (penting(ply, await mesin.analisis(fenSebelum(ply), 2, [data.langkah[ply].uci], 12, 600)))
        return ply;
    }
    return null;
  }

  // ---------- obrolan ----------

  let gelembungStatus = null;

  function status(isi) {
    if (!gelembungStatus) {
      gelembungStatus = document.createElement('div');
      gelembungStatus.className = 'baris-ai sedang';
      gelembungStatus.innerHTML = '<span class="avatar" aria-hidden="true">♞</span>'
        + '<div class="gelembung ai"><p><span></span><span class="titik" aria-hidden="true"><i></i><i></i><i></i></span></p></div>';
    }
    gelembungStatus.querySelector('p > span').textContent = isi;
    obrolan.appendChild(gelembungStatus);
    gulir();
  }

  function hapusStatus() {
    if (gelembungStatus)
      gelembungStatus.remove();
  }

  // HTML dari HalamanCatur (sudah di-escape di sana).
  function tambah(html) {
    obrolan.insertAdjacentHTML('beforeend', html);
    gulir();
  }

  function info(isi, kelas) {
    const p = document.createElement('p');
    p.className = 'info' + (kelas ? ' ' + kelas : '');
    p.textContent = isi;
    obrolan.appendChild(p);
    gulir();
  }

  const gulir = () => { obrolan.scrollTop = obrolan.scrollHeight; };

  // Kotak tulis sengaja tidak difokus otomatis: kursor yang berkedip
  // menggambar ulang jendela terus-menerus (terukur 7 Okt 2026, build Debug,
  // Xephyr/llvmpipe: 11% satu inti selama kotaknya fokus, 0% tanpa fokus).
  function bolehMenulis(boleh) {
    kotak.disabled = tKirim.disabled = !boleh;
  }

  function kirimData(aksi, isian) {
    return fetch(data.alamat + '&' + aksi, { method: 'POST', body: new URLSearchParams(isian) })
      .then(r => r.json())
      .catch(() => ({ galat: T.putus }));
  }

  // Semua pekerjaan Stockfish yang diminta pelatih, berurutan. Jawaban yang
  // panjang dan bercabang bisa puluhan posisi, jadi kemajuannya ditampilkan.
  async function kerjakan(tugas) {
    const hasil = [];
    for (const x of tugas) {
      status(teks('menguji', hasil.length + 1, tugas.length));
      hasil.push(await mesin.analisis(x.fen, x.n, x.cari, x.d, x.ms));
    }
    return hasil;
  }

  function gambarSoal(ply) {
    const sorot = {};
    if (ply > 0) {
      const u = data.langkah[ply - 1].uci;
      sorot[PapanCatur.indeks(u.slice(0, 2))] = sorot[PapanCatur.indeks(u.slice(2, 4))] = 'terakhir';
    }
    papan.gambar(PapanCatur.isi(fenSebelum(ply)), sorot);
  }

  // Analisis soal sekarang kalau belum pernah terkirim (soal ditinggalkan
  // tanpa pesan): langkah partainya tetap dinilai untuk pembanding skor.
  async function awalBelumTerkirim() {
    return data.soal >= 0 && data.perluAwal
      ? JSON.stringify(await (analisisAwal || Promise.resolve([])).catch(() => []))
      : '';
  }

  // Soal di posisi sebelum ply itu: gelembungnya dari HalamanCatur, lalu
  // Stockfish menganalisisnya selagi siswa menulis.
  async function tanyakan(ply) {
    const r = await kirimData('soal', { ply, awal: await awalBelumTerkirim() });
    hapusStatus();
    if (r.galat) {
      info(r.galat, 'galat');
      return;
    }
    tambah(r.html);
    data.soal = ply;
    data.perluAwal = true;
    gambarSoal(ply);
    mulaiAnalisis(ply);
    bolehMenulis(true);
  }

  // Analisis posisi soal, lalu mencari soal berikutnya di latar supaya
  // "Posisi berikutnya" langsung siap. Pekerjaan pelatih tetap didahulukan:
  // tiap langkah pencarian hanya 0,4 detik, lalu antre di belakangnya.
  function mulaiAnalisis(ply) {
    analisisAwal = mesin.analisis(fenSebelum(ply), 3, [data.langkah[ply].uci], 18, 3000);
    analisisAwal.catch(gagal);
    cariBerikutnya(analisisAwal, ply);
  }

  function cariBerikutnya(sesudah, ply) {
    berikutnya = sesudah.then(() => cariSoal(ply + 1, false));
    berikutnya.catch(() => {});
    tBerikutnya.disabled = false;
  }

  async function lanjutKeBerikutnya() {
    if (sibuk || mesinGagal)
      return;
    tBerikutnya.disabled = true;
    bolehMenulis(false);
    status(teks('mencari', data.soal >= 0 ? nomor(data.soal) : 1));
    const ply = await (berikutnya || cariSoal(data.soal + 1, true)).catch(() => null);
    berikutnya = null;
    if (ply === null) {
      await akhiri();
      return;
    }
    await tanyakan(ply);
  }

  // Partai habis: skor dan kesimpulan pelatih, sekali per latihan (skornya
  // dihitung sejak soal pertama, tetapi baru ditunjukkan di sini), lalu
  // HalamanCatur menyimpan hasilnya sendiri ke catatan partai.
  async function akhiri() {
    sibuk = true;
    status(T.menilai);
    const r = await kirimData('selesai', { awal: await awalBelumTerkirim() });
    hapusStatus();
    if (r.html)
      tambah(r.html);
    else
      info(T.selesai);
    if (r.galat)
      info(r.galat, 'galat');
    sibuk = false;
    bolehMenulis(data.soal >= 0);
    tBerikutnya.disabled = mesinGagal;
  }

  async function kirim() {
    const pesan = kotak.value.trim();
    if (!pesan || sibuk || data.soal < 0)
      return;
    sibuk = true;
    bolehMenulis(false);
    tBerikutnya.disabled = true;
    let awal = '';
    if (data.perluAwal) {
      status(T.menganalisis);
      awal = JSON.stringify(await (analisisAwal || Promise.resolve([])).catch(() => []));
    }
    status(T.membaca);
    const r = await kirimData('kirim', { pesan, awal });
    if (r.siswa) {
      hapusStatus();
      tambah(r.siswa);
      kotak.value = '';
      data.perluAwal = false;
    }
    const akhir = await selesaikan(r);
    sibuk = false;
    bolehMenulis(true);
    tBerikutnya.disabled = mesinGagal;
    // Siswa bilang "lanjut": pelatih menjalankan tombol Posisi berikutnya.
    if (akhir.berikutnya)
      lanjutKeBerikutnya();
  }

  // Selama pelatih meminta Stockfish, kerjakan lalu kirim hasilnya.
  async function selesaikan(r) {
    while (r.tugas) {
      const hasil = await kerjakan(r.tugas).catch(() => []);
      status(T.membaca);
      r = await kirimData('lanjut', { hasil: JSON.stringify(hasil) });
    }
    hapusStatus();
    if (r.ai)
      tambah(r.ai);
    if (r.galat)
      info(r.galat, 'galat');
    if (r.dinilai)
      tSimpan.disabled = false;
    return r;
  }

  // Galat mesin (berkode, lihat mesin di atas) atau galat skrip lain (JS).
  function gagal(e) {
    if (mesinGagal)
      return;
    mesinGagal = true;
    hapusStatus();
    const rincian = e && e.kode ? e.rincian : e && e.message;
    info(teks('mesinGagal', e && e.kode || 'JS') + (rincian ? ' (' + rincian + ')' : ''), 'galat');
    tBerikutnya.disabled = true;
  }

  // ---------- kendali ----------

  // Enter membuat baris baru (jawaban boleh panjang); yang mengirim hanya
  // tombol Kirim (keputusan pemakai 7 Okt 2026).
  el('kirim').addEventListener('submit', e => {
    e.preventDefault();
    kirim();
  });
  tBerikutnya.addEventListener('click', lanjutKeBerikutnya);
  mesin.siap.catch(gagal);

  // Mulai: lanjutkan soal yang sedang berjalan (tab yang dibangunkan lagi),
  // atau cari soal pertama.
  gulir();
  if (data.soal >= 0) {
    gambarSoal(data.soal);
    if (data.perluAwal)
      mulaiAnalisis(data.soal);
    else
      cariBerikutnya(mesin.siap, data.soal);
    if (data.tugas) {
      sibuk = true;
      selesaikan({ tugas: data.tugas }).then(akhir => {
        sibuk = false;
        bolehMenulis(true);
        if (akhir.berikutnya)
          lanjutKeBerikutnya();
      });
    } else
      bolehMenulis(true);
  } else {
    papan.gambar(PapanCatur.isi(data.fen0));
    status(T.memuat);
    cariSoal(0, true).then(ply => {
      if (ply === null) {
        hapusStatus();
        info(T.selesai);
      } else
        tanyakan(ply);
    }).catch(gagal);
  }
})();
