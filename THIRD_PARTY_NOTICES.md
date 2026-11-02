# Third-party components and notices

Applies to **Taco Stuffer 1.0.7**. Taco Stuffer's own terms remain in [LICENSE.txt](LICENSE.txt).

| Component | Version | Use | License |
| --- | --- | --- | --- |
| AvalonEdit | 6.3.1.120 | WPF XML/Lua/document editor | MIT |
| Markdig | 0.42.0 | Markdown preview generation | BSD-2-Clause |
| Microsoft .NET / Windows Desktop | .NET 10; exact patch depends on publish | Application/runtime framework | Applicable Microsoft binary terms and bundled component notices |

Package versions were checked against `TacoStuffer/TacoStuffer.csproj` and restored package metadata. These two direct packages have no additional NuGet dependencies for the framework assets selected by this application. Self-contained runtime components are a separate inventory.

[Full notice text](THIRD_PARTY_NOTICES.txt) includes the AvalonEdit and Markdig licenses. The same notice is embedded from `TacoStuffer/THIRD-PARTY-NOTICES.txt` and available through About → Third-party notices, preserving single-EXE use. Keep the root and embedded copies synchronized.

## Runtime publishing

For each self-contained release, retain the exact runtime versions and their required license/third-party notices from the restored publish packages. A framework-dependent build inventory does not fully describe a self-contained release. See [release guidance](docs/RELEASING.md).

## Other content

Windows supplies system interfaces; no arbitrary Windows system DLL redistribution is authorized here. Guild Wars 2 and related marks belong to their owners. Imported packs, textures, scripts and other content retain their original permissions. Include additional notices when adding third-party components or bundled assets.

## Sources

- [AvalonEdit package](https://www.nuget.org/packages/AvalonEdit/6.3.1.120) and [upstream license](https://github.com/icsharpcode/AvalonEdit/blob/v6.3.1/LICENSE)
- [Markdig package](https://www.nuget.org/packages/Markdig/0.42.0) and [upstream license](https://github.com/xoofx/markdig/blob/0.42.0/license.txt)
- [Microsoft .NET licensing](https://github.com/dotnet/core/blob/main/license-information.md) and [Windows binary terms information](https://github.com/dotnet/core/blob/main/license-information-windows.md)

## AvalonEdit license

```text
MIT License

Copyright (c) AvalonEdit Contributors

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

```

## Markdig license

```text
Copyright (c) 2018-2019, Alexandre Mutel
All rights reserved.

Redistribution and use in source and binary forms, with or without modification
, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this 
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice, 
   this list of conditions and the following disclaimer in the documentation 
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND 
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED 
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE 
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL 
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR 
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER 
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE 
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```
