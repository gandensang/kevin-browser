// Papan catur halaman kevin://catur, dipakai catur.js (pemutar partai) dan
// latihan.js (tebak langkah). Tanpa pustaka dan tanpa animasi: 64 kotak div
// yang diberi kelas dari isi papan (lihat catur.css).
'use strict';
window.PapanCatur = class {
  constructor(el) {
    this.el = el;
    this.balik = false;
    this.petak = [];
    for (let t = 0; t < 64; t++)
      el.appendChild(this.petak[t] = document.createElement('div'));
  }

  // "e4" → 28 (a1 = 0, h8 = 63).
  static indeks(nama) {
    return (nama.charCodeAt(1) - 49) * 8 + nama.charCodeAt(0) - 97;
  }

  static nama(i) {
    return 'abcdefgh'[i & 7] + ((i >> 3) + 1);
  }

  // Isi papan dari FEN: larik 64, huruf bidak seperti FEN atau undefined.
  static isi(fen) {
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
    return isi;
  }

  // Isi papan sesudah langkah UCI, hanya untuk ditampilkan (sah tidaknya
  // sudah diperiksa di HalamanCatur): rokade, en passant, dan promosi.
  static jalankan(isi, uci) {
    const b = isi.slice();
    const d = PapanCatur.indeks(uci.slice(0, 2)), k = PapanCatur.indeks(uci.slice(2, 4)), c = b[d];
    if ((c === 'P' || c === 'p') && (d & 7) !== (k & 7) && !b[k])
      b[k + (c === 'P' ? -8 : 8)] = undefined;
    if ((c === 'K' || c === 'k') && Math.abs((k & 7) - (d & 7)) === 2) {
      const asal = (k & 7) === 6 ? k + 1 : k - 2, tujuan = (k & 7) === 6 ? k - 1 : k + 1;
      b[tujuan] = b[asal];
      b[asal] = undefined;
    }
    b[k] = uci.length > 4 ? (c === 'P' ? uci[4].toUpperCase() : uci[4]) : c;
    b[d] = undefined;
    return b;
  }

  // sorot: { indeks kotak: 'kelas tambahan' }.
  gambar(isi, sorot = {}) {
    for (let t = 0; t < 64; t++) {
      // t: urutan di layar, baris atas dulu.
      const i = this.kotakLayar(t), r = i >> 3, f = i & 7, c = isi[i], el = this.petak[t];
      el.className = 'petak ' + ((r + f) % 2 ? 'terang' : 'gelap')
        + (c ? ' b-' + (c < 'a' ? 'w' : 'b') + c.toLowerCase() : '')
        + (sorot[i] ? ' ' + sorot[i] : '');
      if (t >> 3 === 7)
        el.dataset.kolom = 'abcdefgh'[f];
      else
        delete el.dataset.kolom;
      if ((t & 7) === 0)
        el.dataset.baris = r + 1;
      else
        delete el.dataset.baris;
    }
  }

  // Indeks kotak untuk urutan layar t.
  kotakLayar(t) {
    const r = this.balik ? t >> 3 : 7 - (t >> 3);
    const f = this.balik ? 7 - (t & 7) : t & 7;
    return r * 8 + f;
  }

  // Indeks kotak yang diklik, atau -1.
  kotakDari(target) {
    const t = this.petak.indexOf(target);
    return t < 0 ? -1 : this.kotakLayar(t);
  }
};
