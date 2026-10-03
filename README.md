# Kevin Browser

**A web browser for the laptops the modern web left behind.**

Kevin needed a laptop. His family didn't have the money for one.

So they bought a very cheap, old second-hand laptop with little RAM, and put
Linux on it to keep it light. Linux ran light. The browsers didn't: most
browsers today are hungry for memory, and that laptop had little to spare.

So Kevin, then in his first year of senior high school, came up with an idea:
build his own browser.

**This is that browser.** Kevin Browser opens the modern web (YouTube, Google
Docs, WhatsApp Web, news sites) on a laptop like Kevin's. It puts tabs to
sleep, blocks ads and trackers, refuses to let videos play on their own, and
ships as a single 5.3 MB file for Linux.

    kevin-browser                       # opens the home page
    kevin-browser detik.com             # opens an address
    kevin-browser "how to bake a cake"  # searches Google, just like the address bar

## In the family's own words

> Kevin Browser is our family project. Kevin is my son's name. When this
> browser was made, in 2026, he was in his first year of senior high school.
>
> Kevin needed a laptop, but we didn't have the money. In the end we bought a
> very cheap, old second-hand laptop, and installed Linux on it to keep it
> light. Linux was indeed light. The problem was the browser: most browsers
> today are hungry for memory.
>
> That is where Kevin came up with the idea of making his own browser. And so
> Kevin Browser was born: a browser that can still open the modern web, but
> is made as light as possible for laptops like Kevin's.
>
> If you also use an old laptop, this browser is for you too.

## The one rule

Kevin Browser doesn't chase features. It chases one thing: **the web should
work on the laptop you already have, even if it's old and slow.**

Kevin isn't the only one. All around the world there are families in the
same situation: a second-hand laptop, little RAM, an old processor, maybe an
old spinning hard drive. They don't need a cleverer browser. They need one
that works.

So every change is judged by a single question: *does it make Kevin Browser
work better on a weak laptop?* A clever feature that costs memory is a step
backwards. A boring fix that saves memory or CPU is a step forward. We would
rather leave a feature out than make the browser heavier.

## This is where you come in

Kevin Browser is small, young, and open source. It won't change the whole
web. But it can change what an old laptop is able to do, and in many homes an
old laptop is the only laptop there is.

It doesn't need to be perfect. It needs people who believe an old laptop
still deserves a good web.

You don't have to be a WebKit expert to help:

- **Try it on an old laptop** and tell us what's slow or broken. Include your
  specs (`inxi -Fxz`).
- **Measure the sites you use** and share the numbers. Every memory claim in
  this project is measured, never guessed.
- **Bring an idea.** The top menu bar is deliberately kept almost empty:
  space reserved for whatever will make Kevin Browser truly its own. The
  first is Learn, a notebook for school that now has a study assistant.
  Chess is next. Yours could be the third, as long as a weak laptop can
  carry it.
- **Fix a bug, translate a page, or improve the docs.**

Open an issue to talk it through, in English or Indonesian. When you're ready
to write code, [CONTRIBUTING.md](CONTRIBUTING.md) explains how the project is
built, its habits, and the WebKitGTK pitfalls we've already fallen into, so
you don't have to.

## Roadmap

**First, and always: run well on weak laptops.**

- **Real old hardware.** So far Kevin Browser has mostly been measured on a
  single developer laptop. It needs testing on 2 GB machines, old processors,
  old graphics cards, and spinning hard drives, and fixes for whatever breaks
  there. This is the most valuable help right now.
- **Less memory per page.** On heavy sites, WebKit's page processes are still
  bigger than we'd like.
- **Less CPU.** Video, heavy JavaScript, and even idle pages cost processor
  time that an old laptop doesn't have.
- **A Windows version**, because many old laptops run Windows. The browser's
  logic already lives in a separate library (`Inti/`) so it can be reused
  with WebView2.

**Later, and only if they stay light:**

- **Chess.** Chess is our children's hobby, so something chess-related will
  come, in a form a weak laptop can carry.
- **A study assistant.** The first steps are already here: Learn
  (`kevin://belajar`), a notebook of plain Markdown files that can turn
  study material into notes and answer questions from them, with your own
  DeepSeek API key. For homework it is told to help step by step instead of
  handing over answers. More will follow, as long as it costs the laptop
  next to nothing.

## What makes it light

- **Sleeping tabs.** Only 2–4 tabs stay awake at once, depending on the
  laptop's RAM (2 on a 2 GB laptop). The tab you haven't looked at the longest
  goes to sleep: its page is thrown out of memory, while its address and
  back/forward history are kept. Background tabs left alone for 5 minutes
  sleep too, and when the laptop is down to its last 15% of RAM, every
  background tab sleeps right away. WhatsApp Web, tabs playing sound, tabs
  using the microphone or camera, and tabs with unsent form input stay awake.
- **Background tabs aren't loaded** until you open them, and an empty new tab
  has no web process at all.
- **Ads and trackers are blocked** with EasyList, EasyPrivacy, and ABPindo
  (ads on Indonesian sites). On news sites, the ads take more memory than the
  news itself (see the numbers below). Tracking parameters in addresses
  (`utm_…`, `fbclid`, `gclid`, …) are removed too.
- **Videos don't play by themselves**, YouTube included (WhatsApp Web is the
  exception, so GIFs still move). On one detik.com video page, that saves
  about 820 MB of memory and about 60% of a CPU core.
- **YouTube's mobile version**, which uses half the memory of the desktop
  version. Music keeps playing when you switch tabs, and the desktop version
  is still one setting away.
- **Hardware video decoding** (H.264 through VA-API) when the graphics card
  supports it, and videos capped at 720p.
- **A memory limit per WebKit process**, and a JavaScript heap that is cleaned
  up more often.
- **Two process leaks in WebKitGTK 2.52 are patched.** Without these patches,
  every site you visit leaves its process behind in memory.
- **Ahead-of-time compiled (NativeAOT)**: no JIT in the interface process,
  which makes it about 20 MB smaller.

It also reopens the tabs you had open the next time you start it (even after
a sudden shutdown), brings back a closed tab with Ctrl+Shift+T, shows
WhatsApp Web notifications, and speaks Indonesian or English.

## Numbers

Measured on the developer's laptop (Intel HD 4600, 8 GB RAM): the memory of
every browser process (PSS) plus graphics memory, 25 seconds after opening the
page in a fresh profile, averaged over two runs of `scripts/ukur-halaman.sh`.

| Page | Ads blocked (default) | No blocker |
|---|---|---|
| Home page, just opened | 313 MB | 310 MB |
| google.com | 408 MB | 400 MB |
| detik.com | 497 MB | 1150 MB |
| kompas.com | 578 MB | 899 MB |
| tribunnews.com | 433 MB | 674 MB |
| YouTube watch page (mobile version) | 537 MB | 542 MB |

The desktop version of YouTube, measured the same way: 1067 MB.

We're honest about the price, too: video calls don't work yet (the WebKitGTK
shipped by Ubuntu and Mint is built without WebRTC), sleeping tabs have to
reload when you open them, and videos need a click. The full list lives on the
*Pros & cons* page inside the browser.

## Install

On Linux Mint 22, Ubuntu 24.04, or later (64-bit):

    sudo wget -qO /usr/share/keyrings/kevin-browser.gpg https://gandensang.github.io/kevin-browser-apt/kevin-browser.gpg
    sudo wget -qO /etc/apt/sources.list.d/kevin-browser.sources https://gandensang.github.io/kevin-browser-apt/kevin-browser.sources
    sudo apt update
    sudo apt install kevin-browser

apt also installs WebKitGTK 6.0, GTK4, and the video codecs if they're
missing. New versions arrive through the Update Manager. To remove it:
`sudo apt purge kevin-browser`.

The packages are served from
[gandensang/kevin-browser-apt](https://github.com/gandensang/kevin-browser-apt)
with signed indexes, and built from the code in this repository.

**Just one file.** You can also copy the single program file
(`Linux/bin/Release/net10.0/linux-x64/publish/kevin-browser`, from
`dotnet publish Linux -c Release`) to any 64-bit Linux with glibc 2.34 or
newer and WebKitGTK 6.0 (`sudo apt install libwebkitgtk-6.0-4`). Flash drives
formatted FAT32/exFAT drop the execute permission, so run
`chmod +x kevin-browser` after copying.

## Build from source

You need the .NET 10 SDK and, to run it, GTK4 and WebKitGTK 6.0.

    dotnet build
    dotnet test Uji
    dotnet run --project Linux -- https://example.com
    dotnet publish Linux -c Release     # the single NativeAOT file

It's written in C# with GTK4 and WebKitGTK 6.0 (WebKit, the engine Safari also
uses), through the [GirCore](https://github.com/gircore/gir.core) bindings.
The code speaks Indonesian (names, comments, commit messages), because it
started as a family project in Indonesia. Don't let that stop you; see
[CONTRIBUTING.md](CONTRIBUTING.md).

## A quick tour

The home page (`kevin://beranda`) has a Google search box and your bookmarks.
The top menu holds just Home, Learn, Settings, and a spot for what's coming
next. Learn (`kevin://belajar`) is a notebook for school: notes are plain
Markdown files in `~/kevin-catatan`, one folder per subject, which you can
read, search, and write there or open in any text editor. With your own
DeepSeek API key, it can also turn study material (a PDF, a text file, or
pasted text) into notes, with the estimated cost shown before anything is
sent, and answer questions from your notes, naming the notes it used. The
pages about
the browser itself (Story, Pros & Cons, Guide, Plans, About Us) sit at the
bottom of every page, and all of them work offline.

| Keys | Action |
|---|---|
| Ctrl+L | address bar |
| Ctrl+T / Ctrl+W | new tab / close tab |
| Ctrl+Shift+T | reopen the tab you just closed |
| Ctrl+Tab / Ctrl+Shift+Tab | next / previous tab |
| Alt+Left / Alt+Right | back / forward |
| Ctrl+H | history |
| Ctrl+F | find on page |
| Ctrl+D / Ctrl+Shift+O | bookmark / manage bookmarks |
| Ctrl+Shift+Delete | clear browsing data |
| Middle-click a link | open it in a background tab |

Your data (history, bookmarks, cookies, open tabs, settings) stays in
`~/.local/share/kevin-browser/` and can be cleared from `kevin://pengaturan`.
Your notes stay in `~/kevin-catatan/`.
Kevin Browser itself sends nothing to any server: apart from the sites you
open, it only downloads the block lists once a week, and, if you set up the
AI assistant, sends DeepSeek the material you choose, your questions, and
the notes the AI reads to answer them.

## License

[MIT](LICENSE). Use it, change it, share it, even build your own browser on
top of it.

---

*Made for one old laptop. Open to everyone.*
