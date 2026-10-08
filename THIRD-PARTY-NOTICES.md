# Third-party notices

Kevin Browser is MIT-licensed (see `LICENSE`). It also contains the work
below, under its own license.

## Stockfish (downloaded, not included)

Guess the move uses Stockfish 19, the WebAssembly build "lite single" of
stockfish.js (<https://github.com/nmrugg/stockfish.js>), licensed under the
GNU General Public License version 3. We build it ourselves without
WebAssembly SIMD, so it also runs on processors without AVX:
`scripts/bangun-stockfish.sh` applies `scripts/stockfish-tanpa-simd.patch` to
stockfish.js v19.0.0 and compiles it (rebuilding gives byte-identical
files). It is not part of Kevin Browser or its package: the browser downloads
the two files from the GitHub release
<https://github.com/gandensang/kevin-browser/releases/tag/stockfish-19-tanpa-simd>
only when the user presses the install button, checks their SHA-256
fingerprints, and keeps them as separate files in its data folder
(`~/.local/share/kevin-browser/catur/mesin/`). The same release carries the
complete corresponding source, `stockfish-19-lite-tanpa-simd-sumber.tar.gz`.

## Chess pieces

The chess piece images (`Inti/Halaman/bidak-*.svg`, used on `kevin://catur`)
are the "Cburnett" set by Colin M.L. Burnett, from Wikimedia Commons
(<https://commons.wikimedia.org/wiki/Category:SVG_chess_pieces>). The author
offers them under several licenses; Kevin Browser uses them under the BSD
license:

> Copyright © Cburnett. All rights reserved.
>
> Redistribution and use in source and binary forms, with or without
> modification, are permitted provided that the following conditions are met:
>
> 1. Redistributions of source code must retain the above copyright notice,
>    this list of conditions and the following disclaimer.
> 2. Redistributions in binary form must reproduce the above copyright
>    notice, this list of conditions and the following disclaimer in the
>    documentation and/or other materials provided with the distribution.
> 3. Neither the name of the author nor the names of its contributors may be
>    used to endorse or promote products derived from this software without
>    specific prior written permission.
>
> THIS SOFTWARE IS PROVIDED BY THE AUTHOR AND CONTRIBUTORS "AS IS" AND ANY
> EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
> WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
> DISCLAIMED. IN NO EVENT SHALL THE AUTHOR AND CONTRIBUTORS BE LIABLE FOR ANY
> DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
> (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
> LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
> ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
> (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF
> THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
