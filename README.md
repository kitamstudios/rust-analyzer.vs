[marketplace]: https://marketplace.visualstudio.com/items?itemName=kitamstudios.RustAnalyzer
[vsixgallery]: http://vsixgallery.com/extension/KS.RustAnalyzer.3a91e56b-fb28-4d85-b572-ec964abf8e31/
[repo]: https://github.com/kitamstudios/rust-analyzer.vs

# rust-analyzer.vs - Rust language support for Visual Studio 2022

> [!IMPORTANT]
> I wrote this extension for my pet Rust projects. Features / fixes implemented as & when I need them. That is the entire scope of the project. But made it free & OSS just in case anyone finds it useful.
> Of late have been getting 'requests' from rather entitled set of users that do not have the spirit of giving back (e.g. [this](https://github.com/kitamstudios/rust-analyzer.vs/issues/56), [this](https://github.com/user-attachments/assets/185a78cb-d04d-46b7-b68d-745802258597) and [this](https://github.com/user-attachments/assets/cc7e2c7f-64f2-4c1a-98d1-4b50c53d34c4)). If you have similar attitude, don't use this extension.
>
> You can contribute to the project with working code **OR** pay me to build your fav features **OR** do not use this extension. Abusive, unpleasant or derogatory attitude will get you banned from this project.

[![Discord](https://img.shields.io/discord/1060697970426773584?color=5965F2&label=ask%20for%20help)](https://discord.gg/JyK55EsACr) [![CDP](https://github.com/kitamstudios/rust-analyzer.vs/actions/workflows/cdp.yml/badge.svg)](https://github.com/kitamstudios/rust-analyzer.vs/actions/workflows/cdp.yml) [![License: CC BY-NC-SA 4.0](https://img.shields.io/badge/License-CC%20BY--NC--SA%204.0-lightgrey.svg?label=license)](https://creativecommons.org/licenses/by-nc-sa/4.0/) [![Release](https://img.shields.io/github/release/kitamstudios/rust-analyzer.vs.svg?label=release)](https://github.com/kitamstudios/rust-analyzer.vs/releases) [![Visual Studio Marketplace Downloads](https://img.shields.io/visual-studio-marketplace/i/kitamstudios.RustAnalyzer?color=A0A22A)](https://marketplace.visualstudio.com/items?itemName=kitamstudios.RustAnalyzer) [![Visual Studio Marketplace Rating](https://img.shields.io/visual-studio-marketplace/r/kitamstudios.RustAnalyzer?color=C0442E)](https://marketplace.visualstudio.com/items?itemName=kitamstudios.RustAnalyzer&ssr=false\#review-details) [![Repo stars](https://img.shields.io/github/stars/kitamstudios/rust-analyzer.vs?label=repo%20stars&style=flat)](https://github.com/kitamstudios/rust-analyzer.vs/stargazers)


Download this extension from the [Visual Studio Marketplace][marketplace] or get the [CI build][vsixgallery].

**For a superior Rust development experience install the [Rust Development Pack](https://marketplace.visualstudio.com/items?itemName=kitamstudios.RustDevelopmentPack) (includes this extension along with few other useful extensions).**

## Principles

1. Drive developer towards Rust community best practices.
1. UI and behavior parity with 'Open Folder' experience for a C# solution with multiple projects (unless it contradicts #1).
1. Enhance with Rust community tools sets e.g. fmt, clippy, examples, docs. etc.
1. Killer features e.g. ChatGPT integration.

## Features

- [Workspaces & super workspaces both fully supported (super workspace is a folder with multiple cargo workspaces).](https://youtu.be/pE1Vr2zVCbg?t=80)
- [Intellisense / Auto-complete / Goto definition / Code actions / Find references etc. all features from Rust language server.](https://youtu.be/pE1Vr2zVCbg?t=210)
- [Build, Clean (errors in Error list with details in output window).](https://youtu.be/pE1Vr2zVCbg?t=242)
- [Debug & Run without debugging.](https://youtu.be/pE1Vr2zVCbg?t=1424)
- [Run, debug & manage unit tests from test explorer.](https://youtu.be/pE1Vr2zVCbg?t=379)
- [Examples support (run & debug).](https://youtu.be/pE1Vr2zVCbg?t=1311)
- [Set additional properties for build / debug / run (e.g. command line arguments).](https://youtu.be/pE1Vr2zVCbg?t=1469)
- [Clippy / Fmt integration.](https://youtu.be/pE1Vr2zVCbg?t=1197)
- [Comment (Ctrl+K Ctrl+C) & Uncomment (Ctrl+K Ctrl+U)](https://youtu.be/pE1Vr2zVCbg?t=762)
- Infrastructure features
  - [Extension Logs for debugging in Output | rust-analyzer.vs pane.](https://youtu.be/pE1Vr2zVCbg?t=129)
  - [Test integration requires changes currently in rust nightly toolchain. RFC is in progress to promote the changes to stable. Refer: https://](https://youtu.be/pE1Vr2zVCbg?t=379)
  - [Tools | Options | rust-analyzer.vs](https://youtu.be/pE1Vr2zVCbg?t=1225)

> These features continue to get tested with top Rust OSS projects like [cargo](https://github.com/rust-lang/cargo), [ruffle](https://github.com/ruffle-rs/ruffle), [iced](https://github.com/iced-rs/iced), [geo](https://github.com/georust/geo), [ruff](https://github.com/charliermarsh/ruff), [reqwest](https://github.com/seanmonstar/reqwest), [wasmtime](https://github.com/bytecodealliance/wasmtime). There are bugs! 😉

### Upcoming

- Cross compilation (wasm, linux, etc.) / build & run on WSL2 / leverage docker.
- Test experience enhancements (document and benchmark tests).

### Probably never

- Basic project templates.
- ChatGPT integration.
- Folder enhancements (icons, context menus).
- crates.io integration.
- cargo management.

## How can I help?

If you enjoy using the extension, please give it a ★★★★★ rating on the [Visual Studio Marketplace][marketplace].

Should you encounter bugs or if you have feature requests, head on over to the [GitHub repo][repo] to open an issue if one doesn't already exist.

Pull requests are also very welcome, since I can't always get around to fixing all bugs myself.

## Common links

- [Open Folder extensibility](https://learn.microsoft.com/en-us/visualstudio/extensibility/open-folder?view=vs-2022) is pretty much the only documentation apart from the sample code folks have written (see [Acknowledgements](#Acknowledgements)).

## Acknowledgements

- [VS-RustAnalyzer](https://github.com/cchharris/VS-RustAnalyzer) for the inspiration and show how to write an 'Open Folder' extension.
- [nodejstools](https://github.com/microsoft/nodejstools/) for demonstrating good practices and utilities for writing extensions.
- [madskristensen](https://github.com/madskristensen) for being an immense store of extensions authoring tips, tricks & techniques.
- [develop-vsextension-with-github-actions](https://cezarypiatek.github.io/post/develop-vsextension-with-github-actions/) for the workflow scripts.
- [vsixcookbook](https://www.vsixcookbook.com/publish/checklist.html) for the guidance and best practices.
