// Pemutar partai kevin://catur. Tanpa pustaka dan tanpa animasi: setiap
// pindah langkah, 64 kotak papan diberi kelas baru dari FEN langkah itu
// (lihat catur.css). Data partai disiapkan HalamanCatur di
// <script id="data-partai">: FEN awal, lalu FEN, asal, dan tujuan tiap
// langkah, ditambah nilai mesin dan komentar kalau ada.
'use strict';
(() => {
  const data = JSON.parse(document.getElementById('data-partai').textContent);
  const papan = document.getElementById('papan');
  const kolom = document.querySelector('.kolom-papan');
  const bilah = document.querySelector('.bilah-nilai');
  const komentar = document.getElementById('komentar');
  const analisis = document.getElementById('analisis');
  const daftar = document.getElementById('langkah');
  const tombol = [...daftar.querySelectorAll('[data-ply]')];
  const akhir = data.langkah.length;
  const adaNilai = data.langkah.some(l => 'nilai' in l || 'mat' in l);
  const koma = document.documentElement.lang === 'id';
  let balik = data.balik;
  // Mulai dari #langkah di alamat (muat ulang), atau dari posisi akhir seperti Lichess.
  let ply = /^#\d+$/.test(location.hash) ? Math.min(+location.hash.slice(1), akhir) : akhir;

  const petak = [];
  for (let i = 0; i < 64; i++)
    papan.appendChild(petak[i] = document.createElement('div'));
  bilah.hidden = !adaNilai;

  const indeks = nama => (nama.charCodeAt(1) - 49) * 8 + nama.charCodeAt(0) - 97;

  // Nilai dari sudut putih → bagian putih bilah (rumus peluang menang Lichess).
  function persenPutih(l) {
    if ('mat' in l)
      return l.mat > 0 ? 100 : 0;
    const cp = Math.max(-1000, Math.min(1000, l.nilai * 100));
    return 50 + 50 * (2 / (1 + Math.exp(-0.00368208 * cp)) - 1);
  }

  function teksNilai(l) {
    if ('mat' in l)
      return '#' + l.mat;
    const teks = (l.nilai > 0 ? '+' : '') + l.nilai.toFixed(1);
    return koma ? teks.replace('.', ',') : teks;
  }

  // Gulir daftar langkah saja, bukan halamannya.
  function gulir(el) {
    const k = daftar.getBoundingClientRect(), e = el.getBoundingClientRect();
    if (e.top < k.top)
      daftar.scrollTop -= k.top - e.top + 6;
    else if (e.bottom > k.bottom)
      daftar.scrollTop += e.bottom - k.bottom + 6;
  }

  function gambar() {
    const l = ply === 0 ? null : data.langkah[ply - 1];
    const fen = l ? l.fen : data.fen0;
    const isi = [];
    fen.split(' ')[0].split('/').forEach((baris, r) => {
      let f = 0;
      for (const c of baris) {
        if (c >= '1' && c <= '8')
          f += +c;
        else
          isi[(7 - r) * 8 + f++] = c;
      }
    });
    const dari = l ? indeks(l.dari) : -1, ke = l ? indeks(l.ke) : -1;
    for (let t = 0; t < 64; t++) {
      // t: urutan di layar, baris atas dulu.
      const r = balik ? t >> 3 : 7 - (t >> 3);
      const f = balik ? 7 - (t & 7) : t & 7;
      const i = r * 8 + f, c = isi[i], el = petak[t];
      el.className = 'petak ' + ((r + f) % 2 ? 'terang' : 'gelap')
        + (c ? ' b-' + (c < 'a' ? 'w' : 'b') + c.toLowerCase() : '')
        + (i === dari || i === ke ? ' terakhir' : '');
      if (t >> 3 === 7)
        el.dataset.kolom = 'abcdefgh'[f];
      else
        delete el.dataset.kolom;
      if ((t & 7) === 0)
        el.dataset.baris = r + 1;
      else
        delete el.dataset.baris;
    }
    kolom.classList.toggle('terbalik', balik);
    tombol.forEach(b => b.classList.toggle('sekarang', +b.dataset.ply === ply));
    if (ply > 0)
      gulir(tombol[ply - 1]);
    else
      daftar.scrollTop = 0;
    if (adaNilai) {
      const dinilai = l && ('nilai' in l || 'mat' in l);
      bilah.firstElementChild.style.height = (dinilai ? persenPutih(l) : 50) + '%';
      bilah.title = dinilai ? teksNilai(l) : '';
    }
    komentar.hidden = !(l && l.komentar);
    komentar.textContent = l && l.komentar ? l.komentar : '';
    analisis.href = 'https://lichess.org/analysis/' + fen.replace(/ /g, '_') + (balik ? '?color=black' : '');
    history.replaceState(null, '', '#' + ply);
  }

  function pindah(n) {
    ply = Math.max(0, Math.min(akhir, n));
    gambar();
  }

  document.querySelector('.kendali').addEventListener('click', e => {
    const b = e.target.closest('[data-aksi]');
    if (!b)
      return;
    if (b.dataset.aksi === 'balik') {
      balik = !balik;
      gambar();
      return;
    }
    pindah({ awal: 0, mundur: ply - 1, maju: ply + 1, akhir: akhir }[b.dataset.aksi]);
  });
  daftar.addEventListener('click', e => {
    const b = e.target.closest('[data-ply]');
    if (b)
      pindah(+b.dataset.ply);
  });
  document.addEventListener('keydown', e => {
    if (e.altKey || e.ctrlKey || e.metaKey || e.target.closest('input, textarea, select'))
      return;
    const tujuan = { ArrowLeft: ply - 1, ArrowRight: ply + 1, Home: 0, End: akhir }[e.key];
    if (tujuan === undefined)
      return;
    e.preventDefault();
    pindah(tujuan);
  });
  gambar();
})();
