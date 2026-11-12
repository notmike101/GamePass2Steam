# Third-party notices

GamePass2Steam is MIT licensed; see LICENSE. It is not affiliated with Microsoft, Xbox or Valve. Product names belong to their owners.

The portable release bundles the Microsoft .NET runtime and Windows Desktop runtime. Their license and third-party notices are included in `licenses/`, retrieved from the .NET 10 upstream repositories. See https://github.com/dotnet/runtime/blob/main/LICENSE.TXT and https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT. No external NuGet libraries are used by the product.

UWPHook was reviewed as a reference, not vendored. Acknowledgement and its upstream license follow:

MIT License

Copyright (c) 2016 Brian Lima

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
# Optional artwork service

Optional artwork is retrieved using the documented SteamGridDB API with a user-supplied session key. No SteamGridDB client library, SGDBoop source, API credentials or downloaded artwork is bundled. Downloaded artwork is not licensed under this repository's MIT license; source URLs and author attribution are recorded alongside each downloaded image. See `docs/wiki/Artwork.md` for usage and redistribution considerations.
