#!/usr/bin/env python3
"""Tangkapan layar jendela kevin-browser (X11) ke PNG, untuk memeriksa hasil
gambar tanpa melihat layar — mis. halaman yang tampil hitam.

    scripts/tangkap.py hasil.png          # kevin-browser yang paling baru
    PID=12345 scripts/tangkap.py hasil.png

Juga mencetak kecerahan rata-rata area di bawah toolbar (0 = hitam total).
Butuh python3-gi dengan GdkX11 3.0 dan wmctrl (ada di Mint 22.3).
"""
import os
import subprocess
import sys

import gi
gi.require_version('Gdk', '3.0')
gi.require_version('GdkX11', '3.0')
from gi.repository import Gdk, GdkX11  # noqa: E402


def jendela():
    pid = os.environ.get('PID') or subprocess.run(
        ['pgrep', '-xn', 'kevin-browser'], capture_output=True, text=True).stdout.strip()
    for baris in subprocess.run(['wmctrl', '-lp'], capture_output=True, text=True).stdout.splitlines():
        kolom = baris.split()
        if len(kolom) > 2 and kolom[2] == pid:
            return int(kolom[0], 16)
    sys.exit('jendela kevin-browser tidak ditemukan')


def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    win = GdkX11.X11Window.foreign_new_for_display(GdkX11.X11Display.get_default(), jendela())
    pb = Gdk.pixbuf_get_from_window(win, 0, 0, win.get_width(), win.get_height())
    pb.savev(sys.argv[1], 'png', [], [])

    w, h, n, rs = pb.get_width(), pb.get_height(), pb.get_n_channels(), pb.get_rowstride()
    px = pb.get_pixels()
    nilai = [px[y * rs + x * n] + px[y * rs + x * n + 1] + px[y * rs + x * n + 2]
             for y in range(60, h, 7) for x in range(0, w, 7)]
    print(f'{sys.argv[1]}: {w}x{h}, kecerahan konten {sum(nilai) / len(nilai) / 3:.1f}')


if __name__ == '__main__':
    main()
