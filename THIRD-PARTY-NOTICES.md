# Third-Party Notices — miPDFconvert

miPDFconvert — Copyright (C) 2026 Wolfgang Mitterbucher (mitterbucher.com).

miPDFconvert is distributed under the **Apache License, Version 2.0** (see [`LICENSE`](LICENSE)
and [`NOTICE`](NOTICE)). It uses the third-party components listed below; each remains under
its own license.

> **Note:** This file documents license obligations to the best of our knowledge. It is not
> legal advice.

---

## 1. miPDFlib and miPDFlib.PostScript 1.5.2

PDF library and PostScript interpreter that convert the print jobs to PDF.

- Copyright 2026 Wolfgang Mitterbucher
- Source: <https://github.com/Mibuw/miPDFlib>
- **License: Apache License, Version 2.0.** Its NOTICE (including the notices of the
  material it contains) is reproduced in [`NOTICE`](NOTICE).

## 2. .NET libraries used by miPDFlib

`System.Security.Cryptography.Pkcs`, `System.Formats.Asn1` and `Microsoft.Bcl.Cryptography`
(from the NuGet package System.Security.Cryptography.Pkcs 10.0.14).

- Copyright (c) .NET Foundation and Contributors
- Source: <https://github.com/dotnet/runtime>
- **License: MIT**

```
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

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

## 3. Microsoft PostScript class driver (not distributed)

The printer uses the Microsoft PostScript class driver (`pscript5.dll`, `ps5ui.dll`,
`pscript.hlp`, `pscript.ntf`) that is part of Windows. These files are **not** included in
miPDFconvert: `PrinterSetup.exe` takes them from the driver store of the Windows installation
it runs on. The PostScript printer description (`printer/miPDFconvert.ppd`) is part of
miPDFconvert.

---

## Summary

| Component | Version | License |
|-----------|---------|---------|
| miPDFlib, miPDFlib.PostScript | 1.5.2 | Apache-2.0 |
| System.Security.Cryptography.Pkcs, System.Formats.Asn1, Microsoft.Bcl.Cryptography | 10.0.14 | MIT |
| Microsoft PostScript class driver | (Windows) | not distributed |

**Earlier versions:** miPDFconvert up to version 1.1.1 was licensed under the GNU AGPL v3 and
contained code derived from mfilemon (GPL-2.0-or-later) and clawPDF/PDFCreator (AGPL-3.0),
the Ghostscript PPD file and — in 1.1.x — the Ghostscript engine. Since version 1.2.0 none of
these components is part of miPDFconvert: the print monitor, the printer setup and the PPD
were newly written, and the PDF conversion uses miPDFlib. The earlier versions remain
available under their original licenses in the Git history.
