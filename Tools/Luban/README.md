# Pinned Luban tool

`v4.10.2/Luban.7z` is the retained official Windows release archive downloaded from `https://github.com/focus-creative-games/luban/releases/download/v4.10.2/Luban.7z`. `v4.10.2/Luban/` is its extracted executable directory.

The executable reports product version `4.10.2+332018b42be100dfc3e2bc77b7647e79851bb861`. `Configs/Luban/verify-tool.bat` checks this product version and the SHA-256 values in `v4.10.2/SHA256SUMS.txt` before validation or generation. This makes a local replacement or silent upgrade fail closed.

Luban and the vendored runtime sources are distributed under the MIT License; see `v4.10.2/LICENSE.txt`.
