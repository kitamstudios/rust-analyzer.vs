[marketplace]: https://marketplace.visualstudio.com/items?itemName=kitamstudios.RustAnalyzer
[vsixgallery]: http://vsixgallery.com/extension/KS.RustAnalyzer.86e60933-e1fd-4db1-992e-79303efc192b/
[repo]: https://github.com/kitamstudios/rust-analyzer

# rust-analyzer - Rust language support for Visual Studio

[![CDP](https://github.com/kitamstudios/rust-analyzer/actions/workflows/cdp.yml/badge.svg)](https://github.com/kitamstudios/rust-analyzer/actions/workflows/cdp.yml)  [![License: CC BY-NC-SA 4.0](https://img.shields.io/badge/License-CC%20BY--NC--SA%204.0-lightgrey.svg)](https://creativecommons.org/licenses/by-nc-sa/4.0/)

Download this extension from the [Visual Studio Marketplace][marketplace] or get the [CI build][vsixgallery].


Support: [![](https://dcbadge.vercel.app/api/server/JyK55EsACr)](https://discord.gg/JyK55EsACr)

## Principles

- 100% UI and behavior parity with 'Open Folder' experience for a C# solution with multiple projects.
- Enhance with Rust community best practices e.g. fmt, clippy.
- Killer features e.g. copilot integration.

## Demo

### MVP1 - Build & debug

<img src="http://i.imgur.com/qvqSHDp.gif" width="605" height="405" />

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
