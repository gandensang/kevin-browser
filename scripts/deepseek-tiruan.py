#!/usr/bin/env python3
"""Server tiruan DeepSeek untuk mencoba asisten AI di halaman Belajar tanpa
kunci API dan tanpa biaya. Hanya untuk build Debug:

    scripts/deepseek-tiruan.py [PORT] [TUNDA_DETIK] &
    KEVIN_BROWSER_UJI_AI=http://127.0.0.1:8765 dotnet run --project Linux -- kevin://belajar

Kunci API apa saja diterima (simpan dulu di kevin://belajar?ai, mis.
sk-tiruan-1234567890). Tanya-jawab: pesan pertama → cari_catatan(kata
terpanjang) → baca_catatan(hasil pertama) → jawaban pendek bergaya guru yang
diakhiri pertanyaan. Pesan berikutnya dijawab langsung kalau obrolannya sudah
membawa catatan yang dibaca. Pesan yang memuat "tabel" dijawab panjang dengan
tabel dan daftar, untuk melihat tata letaknya. Tebak langkah catur: langkah
(SAN) di jawaban siswa → cek_variasi(langkah pertama, semua langkahnya
sebagai satu variasi) → jawaban yang mengutip laporan Stockfish dan diakhiri
baris "Pelajaran:"; tanpa langkah, pelatih menanyakannya; "lanjut" →
posisi_berikutnya. Serap materi: dua catatan
contoh. Setiap jawaban ditunda TUNDA_DETIK (bawaan 1,5) supaya halaman
kemajuan dan tombol Batalkan bisa dicoba. Ringkasan tiap permintaan ditulis
ke stderr.
"""
import json
import os
import re
import sys
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 8765
TUNDA = float(sys.argv[2]) if len(sys.argv) > 2 else 1.5
PAKAI = {'prompt_tokens': 1200, 'completion_tokens': 60, 'prompt_cache_hit_tokens': 800, 'prompt_cache_miss_tokens': 400}

CATATAN_SERAP = json.dumps({'catatan': [
    {'nama': 'contoh-ringkasan', 'isi': '# Ringkasan contoh\n\nCatatan tiruan dari server uji. Lihat [[contoh-rincian]].\n\nDilewati: tidak ada.'},
    {'nama': 'contoh-rincian', 'isi': '# Rincian contoh\n\n- butir satu\n- butir dua\n\nDilewati: tidak ada.'},
]})


def panggil(id, nama, argumen):
    return {'role': 'assistant', 'content': None,
            'tool_calls': [{'id': id, 'type': 'function', 'function': {'name': nama, 'arguments': json.dumps(argumen)}}]}


def jawaban(nama, tanya):
    if 'tabel' in tanya.lower():
        return (f'Ini rangkuman panjang dari [[{nama}]]:\n\n- **Butir pertama** dari catatan itu.\n- **Butir kedua**.\n\n'
                '| Kolom | Isi |\n|---|---|\n| a | satu |\n| b | dua |\n\n'
                '### Langkah\n\n1. Langkah satu.\n2. Langkah dua.\n\nMau kita bahas yang mana dulu?')
    return (f'Bayangkan kamu sedang naik angkot yang tiba-tiba ngerem: badanmu terdorong ke depan. '
            f'Itulah gagasan utama di catatanmu [[{nama}]], yang disebut **istilah penting** (jawaban tiruan).\n\n'
            'Coba tebak: kalau angkotnya tiba-tiba ngegas, badanmu terdorong ke mana?')


def jawab_tanya(pesan, boleh_tool):
    akhir = pesan[-1]
    if not boleh_tool:
        return {'role': 'assistant', 'content': 'Jawaban sesudah batas putaran tool.'}
    if akhir['role'] == 'user':
        tanya = akhir['content'].split('\n')[-1]
        dibaca = [p['content'] for p in pesan if p['role'] == 'tool' and p['content'].startswith('[[')]
        if dibaca:
            nama = re.match(r'\[\[([^\]]+)\]\]', dibaca[-1]).group(1)
            if 'tabel' in tanya.lower():
                return {'role': 'assistant', 'content': jawaban(nama, tanya)}
            return {'role': 'assistant', 'content':
                    f'**Betul!** Badanmu "ingin" tetap diam, jadi seperti tertinggal (tanggapan tiruan atas "{tanya}").\n\n'
                    'Sekarang, mana yang lebih susah dihentikan: sepeda atau truk yang sama cepatnya?'}
        kata = re.findall(r'\w+', tanya)
        return panggil('call_1', 'cari_catatan', {'kata': max(kata, key=len) if kata else 'catatan'})
    if akhir['role'] == 'tool':
        tool = pesan[-2]['tool_calls'][0]['function']['name']
        if tool == 'cari_catatan':
            isi = akhir['content']
            hasil = json.loads(isi[isi.index('['):]) if '[' in isi else []
            if hasil:
                return panggil('call_2', 'baca_catatan', {'mapel': hasil[0]['mapel'], 'nama': hasil[0]['nama']})
            return {'role': 'assistant', 'content': 'Catatanmu belum membahas ini.\n\nPenjelasan umum (bukan dari catatanmu): …\n\nSudah pernah dengar istilah ini di kelas?'}
        nama = re.match(r'\[\[([^\]]+)\]\]', akhir['content']).group(1)
        tanya = next(p['content'] for p in reversed(pesan) if p['role'] == 'user').split('\n')[-1]
        return {'role': 'assistant', 'content': jawaban(nama, tanya)}
    return {'role': 'assistant', 'content': 'Halo.'}


SAN = re.compile(r'(?<![\w])(O-O(?:-O)?|[KQRBN]?[a-h]?[1-8]?x?[a-h][1-8](?:=[QRBN])?[+#]?)(?![\w])')


# Uji pohon variasi: TIRUAN_CEK=berkas.json berisi argumen cek_variasi yang
# dikirim untuk pesan siswa yang memuat kata "pohon" (seperti AI yang mengubah
# jawaban panjang jadi pohon variasi); TIRUAN_LAPORAN=berkas menyimpan laporan
# tool terakhir.
CEK = os.environ.get('TIRUAN_CEK')
LAPORAN = os.environ.get('TIRUAN_LAPORAN')


def jawab_catur(pesan, boleh_tool):
    akhir = pesan[-1]
    if akhir['role'] == 'tool' and LAPORAN:
        with open(LAPORAN, 'a') as f:
            f.write(akhir['content'] + '\n\n=====\n\n')
    if akhir['role'] == 'user' and akhir['content'].startswith('[Pesan otomatis'):
        laporan = next((p['content'] for p in reversed(pesan) if p['role'] == 'tool'), '')
        return {'role': 'assistant', 'content': f'Jawaban tiruan sesudah pesan otomatis. Laporan:\n\n{laporan}\n\nPelajaran: tiruan.'}
    if CEK and akhir['role'] == 'user' and 'pohon' in akhir['content'].split('Jawaban siswa:')[-1]:
        with open(CEK) as f:
            return panggil('call_pohon', 'cek_variasi', json.load(f))
    if akhir['role'] == 'tool' or not boleh_tool:
        laporan = akhir['content'] if akhir['role'] == 'tool' else ''
        return {'role': 'assistant', 'content':
                f'Jawaban tiruan dari pelatih. Yang dikatakan Stockfish:\n\n{laporan}\n\n'
                'Pelajaran: sebelum melangkah, periksa dulu ancaman lawan (pelajaran tiruan).'}
    teks = akhir['content'].split('Jawaban siswa:')[-1]
    if teks.strip().lower() in ('lanjut', 'next', 'ya', 'yes'):
        return {'role': 'assistant', 'content': 'Oke, kita lanjut. (tiruan)',
                'tool_calls': panggil('call_lanjut', 'posisi_berikutnya', {})['tool_calls']}
    langkah = SAN.findall(teks)
    if not langkah:
        return {'role': 'assistant', 'content': 'Menarik! Tapi langkah apa yang kamu pilih di posisi ini? (tiruan)'}
    return panggil('call_catur', 'cek_variasi', {'utama': langkah[0], 'variasi': [' '.join(langkah)] if len(langkah) > 1 else []})


class Penangan(BaseHTTPRequestHandler):
    def log_message(self, *a):
        pass

    def kirim(self, status, isi, jenis='application/json'):
        data = isi.encode() if isinstance(isi, str) else json.dumps(isi).encode()
        self.send_response(status)
        self.send_header('Content-Type', jenis)
        self.send_header('Content-Length', str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        if self.path == '/user/balance':
            self.kirim(200, {'is_available': True, 'balance_infos': [{'currency': 'USD', 'total_balance': '1.23'}]})
        else:
            self.kirim(404, {'error': {'message': 'not found'}})

    def do_POST(self):
        badan = json.loads(self.rfile.read(int(self.headers['Content-Length'])))
        pesan = badan['messages']
        print(time.strftime('%H:%M:%S'), badan['model'], badan.get('tool_choice', 'stream'),
              [(p['role'], (p.get('content') or '')[:40]) for p in pesan], file=sys.stderr, flush=True)
        time.sleep(TUNDA)
        if badan.get('stream'):
            potongan = {'choices': [{'index': 0, 'delta': {'content': CATATAN_SERAP}, 'finish_reason': None}]}
            akhir = {'choices': [{'index': 0, 'delta': {'content': ''}, 'finish_reason': 'stop'}], 'usage': PAKAI}
            self.kirim(200, f'data: {json.dumps(potongan)}\n\ndata: {json.dumps(akhir)}\n\ndata: [DONE]\n\n', 'text/event-stream')
            return
        catur = any(a['function']['name'] == 'cek_variasi' for a in badan.get('tools', []))
        hasil = (jawab_catur if catur else jawab_tanya)(pesan, badan.get('tool_choice') != 'none')
        self.kirim(200, {'object': 'chat.completion', 'model': badan['model'],
                         'choices': [{'index': 0, 'message': hasil, 'finish_reason': 'tool_calls' if 'tool_calls' in hasil else 'stop'}],
                         'usage': PAKAI})


ThreadingHTTPServer(('127.0.0.1', PORT), Penangan).serve_forever()
