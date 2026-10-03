# Contributing to Kevin Browser

Thank you for stopping by. Kevin Browser is made for old laptops with little
RAM, and every bit of help counts: a report from another laptop, a
measurement, a fix, a translation, or a new feature.

## Getting started

1. For anything bigger than a small fix, open an issue first, so we can agree
   on the direction before you spend your time.
2. Fork, create a branch, and send a pull request.
3. Issues and pull requests are welcome in English or Indonesian.

Where help is most wanted:

- **Testing on old laptops** (2–4 GB of RAM, old graphics cards, spinning
  hard drives): what is slow or broken, together with the specs
  (`inxi -Fxz`).
- **Reducing the memory of WebKit's page processes**, which is still much
  larger than we'd like on heavy sites.
- **Features from the roadmap** in the README: chess, an assistant inside the
  browser, and a Windows version.
- **Translations and writing**: the built-in pages exist in Indonesian and
  English.

## The code speaks Indonesian

Kevin Browser started as a family project in Indonesia, so class names,
methods, variables, comments, and commit messages are in Indonesian
(`Tab.Tidurkan()` puts a tab to sleep, `Bangunkan()` wakes it up). Please
keep new code consistent with the code around it. If Indonesian isn't your
language, write it in English anyway and say so in the pull request; we'll
help with the naming during review. A short glossary:

| Indonesian | English |
|---|---|
| tab tidur, tidurkan, bangunkan | sleeping tab, put to sleep, wake up |
| halaman, beranda, pengaturan | page, home page, settings |
| penyaring, pemblokir | filter, blocker |
| riwayat, bookmark | history, bookmarks |
| jendela, tombol, kotak alamat | window, button, address bar |
| setelan, preferensi, bahasa | settings (computed), preferences, language |
| uji, ukur, terukur | test, measure, measured |
| mesin, proses, pemulung | engine, process, reaper (kills leaked processes) |

## Building

You need the .NET 10 SDK and, to run it, GTK4 and WebKitGTK 6.0
(`sudo apt install libwebkitgtk-6.0-4`). It is developed on Linux Mint 22.3
(GTK 4.14, WebKitGTK 2.52).

    dotnet build                                     # all projects
    dotnet run --project Linux -- https://example.com
    dotnet test Uji                                  # tests for Inti/
    dotnet test Uji --filter "FullyQualifiedName~UjiAlamat"
    dotnet publish Linux -c Release                  # single NativeAOT file
    scripts/pasang.sh                                # publish + install to ~/.local/bin and the menu

Debug builds use a separate profile (`~/.local/share/kevin-browser-debug`)
and don't run as a single instance, so they can run next to an installed
copy. Release builds are single-instance: starting a second one just hands
the address to the window that's already open.

## How the code is organized

- **`Inti/`** ("core"): logic with no GTK or WebKit, so it can be reused for
  a Windows version and tested without a window. Address bar parsing
  (`Alamat`), tab sleeping rules (`TidurTab`), RAM-based settings
  (`Setelan`), the built-in `kevin://` pages (`HalamanBawaan` and the HTML in
  `Inti/Halaman/`, English in `Inti/Halaman/en/`), history, bookmarks, tab
  restore, the Adblock list converter (`KonverterAdblock`), the tracking
  parameter stripper, the interface language (`Teks`, `Preferensi`), and
  per-site rules (`IzinMedia`, `IzinNotifikasi`, `PutarOtomatis`).
- **`Linux/`**: the application. `Program` prepares the environment, `Mesin`
  ("engine") creates one WebContext and NetworkSession shared by all tabs
  (every global memory-saving setting lives there), `Jendela` ("window") is
  the toolbar and the tab strip, and `Tab` holds one WebView that can be put
  to sleep and woken up. `Penyaring` runs the blocker, `Pemulung` cleans up
  leaked WebKit processes, and `Sinyal` and `Asli` talk to C directly.
- **`Uji/`** ("tests"): xUnit tests for `Inti/`.
- **`scripts/`**: .deb packaging, APT release, memory measurement, and
  window-testing tools.

## House rules

- **User-facing text comes in two languages.** In code:
  `t["Simpan", "Save"]` (see `Inti/Bahasa.cs`). Fixed pages have an English
  version in `Inti/Halaman/en/` with the same file name. The
  `HalamanBahasaInggrisLengkap` test catches Indonesian words left behind on
  English pages.
- **Every claim about memory or CPU is measured**, and the result is written
  in a comment next to the code ("Terukur: …", "measured: …"). Several
  guesses in this project turned out wrong once tried. The numbers in the
  README, `beranda.html`, and `kelebihan.html` (and their English versions)
  must match.
- **Warnings are errors** (`TreatWarningsAsErrors`).
- **Public text doesn't compare Kevin Browser with other branded browsers.**
  Compare against our own numbers. Stating facts is fine, e.g. "WebKit, the
  engine Safari also uses".
- **The Story page is the family's story in their own words.** Don't add
  details they didn't tell.

## Pitfalls we've already hit

Each of these cost hours. Read them before touching `Linux/`.

- **Don't use GirCore events** (`web.OnLoadChanged`, `button.OnClicked`, …).
  Every signal emission leaks a reference to the sender, so the WebView is
  never destroyed and its web process never exits. Use
  `Sinyal.Sambung(object, "signal-name", …)` with a delegate that matches the
  signal's C signature exactly. Pointers passed to a signal handler are only
  valid while the handler runs.
- **Boxed GirCore objects we own** (`WebViewSessionState`, `GObject.Value`,
  …) must be disposed on the main thread.
- **A GirCore wrapper whose pointer you only borrowed can be garbage
  collected** in AOT builds, freeing the native object with it. Keep the
  wrapper alive (`using`, a field) for as long as the pointer is in use.
- **Environment variables for GTK/WebKit must be set with
  `GLib.Functions.Setenv`.** `Environment.SetEnvironmentVariable` reaches
  neither C code nor WebKit's child processes.
- **`SetCacheModel` must be called before the WebContext is created.** The
  other way around, every site you leave keeps its process alive.
- **WebKitGTK 2.52 with the sandbox leaks "prewarm" processes**, one more per
  site you navigate to. `Pemulung` kills them. When WebKit is updated, check
  whether this is still needed (`scripts/uji-cepat.sh`).
- **Fractional JSC options are parsed with the process locale.** With a
  decimal comma, `1.5` becomes `1`. Use integer options, and always look at a
  screenshot.
- **`decide-policy` is also emitted for iframes**, and the API can't tell them
  apart from the main frame.
- **WebKit's content blocker**: a single unsupported regex (`|`, `{n,m}`,
  backreferences, non-ASCII characters) fails the whole list; the limit is
  150,000 rules per filter; `redirect` and `modify-headers` actions are never
  executed.
- **The WebKitGTK shipped by Ubuntu and Mint is built without WebRTC**, so
  video calls are impossible whatever the permissions.
- **`NetworkSession` doesn't store cookies on disk** without
  `SetPersistentStorage`.

## Testing

- `dotnet test Uji` covers the logic in `Inti/`. The GTK/WebKit side has no
  automated tests; run the application.
- `KEVIN_BROWSER_CATAT=1` logs tabs waking and sleeping, permissions, and
  killed processes to stderr.
- `KEVIN_BROWSER_UJI_JS='…'` (Debug builds only) runs JavaScript in the first
  page that finishes loading.
- `scripts/klik.py` sends real X11 clicks and keystrokes to the newest
  kevin-browser window, and `scripts/tangkap.py` takes a screenshot of it.
  `klik.py` refuses to send anything if that window isn't focused. To keep
  other windows out of harm's way, run it in a nested X display:

      Xephyr :9 -screen 1366x768 -ac &
      env -u I3SOCK DISPLAY=:9 i3 &   # or any window manager; klik.py and tangkap.py need wmctrl
      DISPLAY=:9 dotnet run --project Linux

  (`env -u I3SOCK`: if your own desktop also runs i3, the second i3 would
  otherwise try to use your i3's socket.)

- Measuring memory: `scripts/ukur-ram.sh [PID]` (PSS and graphics memory of
  every browser process) and `scripts/ukur-halaman.sh URL` (one page in a
  fresh profile, with and without the blocker; `BANDING=putar` compares
  videos that play by themselves). Only compare numbers measured under the
  same conditions: PSS depends on which other processes are running.
- `scripts/uji-cepat.sh`: a smoke test of the release binary, after a
  WebKitGTK update or before a release.

## Before you send a pull request

- `dotnet build` has no warnings and `dotnet test Uji` passes.
- Changes that affect memory come with measurements.
- New user-facing text is in both languages.
