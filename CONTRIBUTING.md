# Contributing to Kevin Browser

Thank you for stopping by. Kevin Browser is made for old laptops with little
RAM, and every bit of help counts: a report from another laptop, a
measurement, a fix, a translation, or a new feature.

## The one rule

Kevin Browser exists for people whose only laptop is old and slow. So the
first question for any change is: **what does it cost on a weak laptop?**
More memory, more CPU, a slower start, more disk writes: each of these is a
regression, however nice the feature. Prefer the boring fix that saves 30 MB
over the clever feature that costs 30 MB, and measure both. If a feature
can't be made light, it doesn't ship.

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
- **Less CPU** for video, heavy JavaScript, and idle pages: old processors
  have none to spare.
- **A Windows version**, since many old laptops run Windows.
- **Features from the roadmap** (chess, the study assistant), but only in a
  form a weak laptop can carry.
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
| belajar, catatan, mapel (mata pelajaran) | learn, note, school subject |

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
- **`Asisten/`** ("assistant"): the Learn page (`kevin://belajar`), kept out
  of `Inti/` so the browser core stays small. `BukuCatatan` ("notebook")
  reads and writes the notes, plain Markdown files in `~/kevin-catatan`, one
  folder per subject, and never touches anything outside that folder.
  `Markah` turns a note into HTML, escaping everything: notes may contain
  text from websites. `HalamanBelajar` builds the pages; their icons are
  small inline SVGs drawn by hand in `Ikon`, so there is no icon font and no
  third-party license. Chess lives here too (`Asisten/Catur/`): `Papan` is
  our own chess rules (legal moves, SAN, FEN), checked against known perft
  counts in `UjiPapan`; `Pgn` reads games; `KoleksiPartai` fetches public
  games by username from Lichess and Chess.com (no login) and keeps them on
  disk; `HalamanCatur` builds `kevin://catur`. The chess pages are the only
  `kevin://` pages with JavaScript: the board (`papan.js`), the game viewer
  (`catur.js`), and guess the move (`latihan.js`), all in `Inti/Halaman/`;
  everything else there is plain forms. Guess the move is a chat with an AI
  coach (`PelatihCatur`): `latihan.js` picks the important positions with
  Stockfish (skipping moves with no real choice, `AlasanLewat`), and the
  coach reads the student's answer and calls one tool, `cek_variasi`. That
  tool runs in the page, not in C#: the AI round stops, the page runs the
  requested Stockfish searches, and posts the results back. The verdict
  (same, equal, better, worse than the game move) is computed in code; the
  AI only explains it. Every move in every line is checked, for both sides:
  a line that only works because the opponent plays a weaker reply is
  flagged, and so is any move that drops 10% winning chance or 1.5 pawns
  (winning chance alone saturates: in a lost position, giving away the queen
  barely changes it). A second tool, `lihat_posisi`, shows the AI the
  board, piece placement, and legal moves after any line, computed in C#.
  Language models can't follow a chess position in their head: without
  these tools the coach invented pieces and lines. So every position fact
  the coach may need is computed and handed to it: material, pawn structure,
  who attacks what, and mate-in-one threats (a null move, `Papan.Lewat`),
  also after each move of a tested line. Guards run before an answer is
  shown, and send it back to the AI, unseen, at most twice each: every
  piece move the student wrote must really have been reached by Stockfish
  (being named in a tool call isn't enough when the line stops at an earlier
  illegal move); a numbered move in the answer ("16... Bd6") must have
  appeared with that number and side in an evaluated line, not just be
  legal somewhere; and an answer cut off mid-word is asked for again. The
  coach always uses deepseek-v4-pro in DeepSeek's thinking mode
  (`PelatihCatur.ModelPelatih`), whatever model is chosen for questions in
  Learn, and this is deliberately not a setting: slower and pricier, but
  measured against real games it was the difference between misreading
  the student's line, giving in to a wrong objection, or inventing
  defences, and getting all of these right.
  `MesinCatur` ("chess engine") downloads Stockfish's WebAssembly build once,
  when the user presses Install, checks its SHA-256 fingerprints, and keeps
  it in the data folder; it is GPL-3.0 and never part of our package. It runs
  as a Web Worker inside a hidden iframe on a second scheme, `kevin-mesin://`
  (see the pitfalls below), and talks to the chess page with `postMessage`.
  The piece images are the BSD-licensed Cburnett set
  (`THIRD-PARTY-NOTICES.md`). `Penyerap`
  ("absorber") turns study material into notes with one DeepSeek call
  (`KlienAi`, your own key in `PengaturanAi`): code reads the file, checks
  `sumber.md` so nothing is processed twice, and writes the notes; the model
  only turns text into JSON notes and has no tools, so a document can't make
  it do anything. `Penanya` ("asker") teaches from the notes in a chat with a
  small tool loop: the model may call two read-only tools, `cari_catatan`
  (search) and `baca_catatan` (read one note), for at most four rounds, and
  the code runs them (`AlatCatatan`). There is no tool that writes, deletes,
  or opens anything. Its instructions (`PromptTanya`) ask for a tutor who
  explains one idea at a time and asks a question back, with a short example
  exchange: the models otherwise write articles. Each new message carries the
  chat so far, including the notes already read, so the model can respond to
  the student's answer without reading again, and DeepSeek's cache covers
  everything before the new message. HTTP goes through `IJaringan`, which `Linux/` implements
  with libsoup (already loaded by WebKit): .NET's `HttpClient` would add
  megabytes to the binary. No GTK or WebKit here either.
- **`Linux/`**: the application. `Program` prepares the environment, `Mesin`
  ("engine") creates one WebContext and NetworkSession shared by all tabs
  (every global memory-saving setting lives there), `Jendela` ("window") is
  the toolbar and the tab strip, and `Tab` holds one WebView that can be put
  to sleep and woken up. `Penyaring` runs the blocker, `Pemulung` cleans up
  leaked WebKit processes, and `Sinyal` and `Asli` talk to C directly.
- **`Uji/`** ("tests"): xUnit tests for `Inti/` and `Asisten/`.
- **`scripts/`**: .deb packaging, APT release, memory measurement, and
  window-testing tools.

## House rules

- **Nobody installs anything by hand.** Use whatever technology fits, but a
  user must never have to install Python, a library, or an engine
  separately to run the browser. Required dependencies go in the `.deb`
  package's `Depends`, so apt installs them. Optional, large pieces are
  downloaded from inside the browser with one click and set themselves up.

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
- **Lichess answers an HTML 404 page to API requests without an
  identifying User-Agent** (for example `/api/games/user/{name}`).
  `KoleksiPartai` sends `KevinBrowser/<version> (+repository URL)`; keep it.
- **Pages on a local scheme (`kevin://`) can't start Web Workers.** That is
  why the chess engine lives on `kevin-mesin://`, a second scheme that is
  secure and CORS-enabled but not local, which must also be listed in
  `Jendela.SkemaWeb` or its iframe never loads. Three more traps on the same
  path: `URISchemeResponse.SetHttpHeaders` overwrites `SetContentType`, so
  `Mesin` writes the `Content-Type` header itself (without it the Worker
  script silently doesn't run); `postMessage(…, 'kevin://catur')` is never
  delivered, so the relay posts to `'*'` and the chess page checks
  `e.origin`; and `fetch()` from a `kevin://` page to `kevin://` itself fails
  unless the response carries `Access-Control-Allow-Origin: kevin://catur`.
  Ordinary websites still can't read `kevin://` with that header.
- **On `kevin://` pages, `history.replaceState` with a full URL throws**,
  even for the same origin; only the `#fragment` may change. A script that
  calls it first stops right there.
- **While a POST form waits for its response, WebKit doesn't repaint the
  current page**, so script changes (a disabled button, a progress bar)
  never show. Long actions that need visible progress go through `fetch`
  (see the Stockfish install).
- **A focused text box with a blinking caret repaints the window
  continuously** (about 11% of a core with software rendering). Don't
  autofocus a text box on pages that stay open while the user reads.
- **A `kevin://` page that reloads itself** (`<meta http-equiv="refresh">`,
  as the chat page does every 2 seconds while the AI answers) pays for its
  layout on every reload. `position: sticky` alone took the chat page from
  about 5% to 8–9% of a core with software rendering, so its message box is
  sticky only when no answer is pending. Also, browsers ignore `autofocus`
  when the address has a `#fragment`; the chat page makes the message box
  itself the fragment target, which scrolls it into view and focuses it.

## Testing

- `dotnet test Uji` covers the logic in `Inti/` and `Asisten/`. The
  GTK/WebKit side has no automated tests; run the application.
- Debug builds keep their notes in their own folder
  (`~/.local/share/kevin-browser-debug-catatan`), so trying things out never
  touches real notes. `KEVIN_BROWSER_CATATAN=/some/folder` points any build
  at another notes folder.
- `KEVIN_BROWSER_CATAT=1` logs tabs waking and sleeping, permissions, and
  killed processes to stderr.
- `KEVIN_BROWSER_UJI_JS='…'` (Debug builds only) runs JavaScript in the first
  page that finishes loading.
- `KEVIN_BROWSER_UJI_AI=http://127.0.0.1:8765` (Debug builds only) sends the
  AI assistant's requests to `scripts/deepseek-tiruan.py`, a stand-in server
  on your own machine, so the Learn pages can be tried without a key or any
  cost. Any key is accepted. The unit tests use `JaringanPalsu` instead.
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
