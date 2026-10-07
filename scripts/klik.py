#!/usr/bin/env python3
"""Menyetir jendela kevin-browser dengan input X11 sungguhan (XTest).

Klik dan ketikan dari sini dihitung gestur pemakai oleh WebKit — berbeda
dengan KEVIN_BROWSER_UJI_JS, yang tidak bisa membuka popup atau mengirim
klik tengah. Pointer mouse dikembalikan ke tempat semula di akhir.

    scripts/klik.py tombol:ctrl+l ketik:wikipedia.org tombol:Return tunggu:8
    scripts/klik.py klik:150:92 tunggu:5 klik:150:192:2

Perintah:
    klik:X:Y[:TOMBOL]   koordinat di dalam jendela; tombol 1 kiri, 2 tengah, 3 kanan
    tombol:MOD+KUNCI    mod: ctrl, shift, alt; kunci: nama keysym X (l, Return, Page_Up, …)
    ketik:TEKS          huruf, angka, dan . / - : ? & = _ # , + ( ) ! ; spasi
    tunggu:DETIK
Jendela yang disetir: kevin-browser yang paling baru (env PID menimpanya).
Sebelum setiap perintah, fokus keyboard harus ada di jendela itu; kalau
tidak, skrip berhenti. XTest mengirim input ke jendela mana pun yang
sedang fokus: jendela uji pernah muncul di workspace pemakai, di sebelah
WhatsApp di browser lain, dan ketikannya masuk ke sana.
Butuh libXtst dan wmctrl (ada di Mint 22.3).
"""
import ctypes as c
import os
import subprocess
import sys
import time

X = c.CDLL('libX11.so.6')
T = c.CDLL('libXtst.so.6')
X.XOpenDisplay.restype = c.c_void_p
X.XDefaultRootWindow.argtypes = [c.c_void_p]
X.XDefaultRootWindow.restype = c.c_ulong
X.XStringToKeysym.argtypes = [c.c_char_p]
X.XStringToKeysym.restype = c.c_ulong
X.XKeysymToKeycode.argtypes = [c.c_void_p, c.c_ulong]
X.XKeysymToKeycode.restype = c.c_ubyte
X.XTranslateCoordinates.argtypes = [c.c_void_p, c.c_ulong, c.c_ulong, c.c_int, c.c_int,
                                    c.POINTER(c.c_int), c.POINTER(c.c_int), c.POINTER(c.c_ulong)]
X.XQueryPointer.argtypes = [c.c_void_p, c.c_ulong] + [c.POINTER(c.c_ulong)] * 2 \
    + [c.POINTER(c.c_int)] * 4 + [c.POINTER(c.c_uint)]
X.XFlush.argtypes = [c.c_void_p]
X.XGetInputFocus.argtypes = [c.c_void_p, c.POINTER(c.c_ulong), c.POINTER(c.c_int)]
X.XQueryTree.argtypes = [c.c_void_p, c.c_ulong, c.POINTER(c.c_ulong), c.POINTER(c.c_ulong),
                         c.POINTER(c.c_void_p), c.POINTER(c.c_uint)]
X.XFree.argtypes = [c.c_void_p]
T.XTestFakeMotionEvent.argtypes = [c.c_void_p, c.c_int, c.c_int, c.c_int, c.c_ulong]
T.XTestFakeButtonEvent.argtypes = [c.c_void_p, c.c_uint, c.c_int, c.c_ulong]
T.XTestFakeKeyEvent.argtypes = [c.c_void_p, c.c_uint, c.c_int, c.c_ulong]

# Karakter → (keysym, perlu shift) untuk tata letak US.
KHUSUS = {'.': ('period', False), '/': ('slash', False), '-': ('minus', False),
          '=': ('equal', False), ' ': ('space', False), ':': ('colon', True),
          '?': ('question', True), '&': ('ampersand', True), '_': ('underscore', True),
          '#': ('numbersign', True), ',': ('comma', False), '+': ('equal', True),
          '(': ('9', True), ')': ('0', True), '!': ('1', True), ';': ('semicolon', False)}
MOD = {'ctrl': 'Control_L', 'shift': 'Shift_L', 'alt': 'Alt_L'}


def jendela():
    pid = os.environ.get('PID') or subprocess.run(
        ['pgrep', '-xn', 'kevin-browser'], capture_output=True, text=True).stdout.strip()
    for baris in subprocess.run(['wmctrl', '-lp'], capture_output=True, text=True).stdout.splitlines():
        kolom = baris.split()
        if len(kolom) > 2 and kolom[2] == pid:
            return int(kolom[0], 16)
    sys.exit('jendela kevin-browser tidak ditemukan')


def fokus_di(d, xid):
    """True kalau fokus keyboard ada di jendela xid atau salah satu anaknya."""
    w, balik = c.c_ulong(), c.c_int()
    X.XGetInputFocus(d, c.byref(w), c.byref(balik))
    w = w.value
    for _ in range(20):
        if w == xid:
            return True
        if w in (0, 1):   # None, PointerRoot
            return False
        akar, induk, anak, n = c.c_ulong(), c.c_ulong(), c.c_void_p(), c.c_uint()
        if not X.XQueryTree(d, w, c.byref(akar), c.byref(induk), c.byref(anak), c.byref(n)):
            return False
        if anak.value:
            X.XFree(anak)
        if induk.value in (0, akar.value):
            return False
        w = induk.value
    return False


def main():
    d = X.XOpenDisplay(None)
    akar = X.XDefaultRootWindow(d)
    xid = jendela()

    r = [c.c_ulong(), c.c_ulong()]
    p = [c.c_int() for _ in range(4)]
    m = c.c_uint()
    X.XQueryPointer(d, akar, c.byref(r[0]), c.byref(r[1]), *[c.byref(v) for v in p], c.byref(m))
    semula = (p[0].value, p[1].value)
    ox, oy, anak = c.c_int(), c.c_int(), c.c_ulong()
    X.XTranslateCoordinates(d, xid, akar, 0, 0, c.byref(ox), c.byref(oy), c.byref(anak))

    def kunci(nama, tekan):
        T.XTestFakeKeyEvent(d, X.XKeysymToKeycode(d, X.XStringToKeysym(nama.encode())), tekan, 0)

    def tekan(nama, mods=()):
        for mm in mods:
            kunci(MOD[mm], 1)
        kunci(nama, 1)
        kunci(nama, 0)
        for mm in reversed(mods):
            kunci(MOD[mm], 0)

    for perintah in sys.argv[1:]:
        jenis, _, arg = perintah.partition(':')
        if jenis != 'tunggu' and not fokus_di(d, xid):
            T.XTestFakeMotionEvent(d, -1, semula[0], semula[1], 0)
            X.XFlush(d)
            sys.exit(f'jendela kevin-browser tidak fokus; berhenti sebelum "{perintah}" '
                     'supaya input tidak masuk ke jendela lain')
        if jenis == 'klik':
            bagian = [int(x) for x in arg.split(':')]
            x, y, tombol = bagian[0], bagian[1], bagian[2] if len(bagian) > 2 else 1
            T.XTestFakeMotionEvent(d, -1, ox.value + x, oy.value + y, 0)
            X.XFlush(d)
            time.sleep(0.2)
            T.XTestFakeButtonEvent(d, tombol, 1, 0)
            T.XTestFakeButtonEvent(d, tombol, 0, 0)
        elif jenis == 'tombol':
            *mods, nama = arg.split('+')
            tekan(nama, mods)
        elif jenis == 'ketik':
            for ch in arg:
                if ch in KHUSUS:
                    nama, shift = KHUSUS[ch]
                    tekan(nama, ['shift'] if shift else [])
                else:
                    tekan(ch.lower(), ['shift'] if ch.isupper() else [])
        elif jenis == 'tunggu':
            X.XFlush(d)
            time.sleep(float(arg))
        else:
            sys.exit(f'perintah tidak dikenal: {perintah}')
        X.XFlush(d)
        time.sleep(0.3)

    T.XTestFakeMotionEvent(d, -1, semula[0], semula[1], 0)
    X.XFlush(d)


if __name__ == '__main__':
    main()
