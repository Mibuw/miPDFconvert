# Third-Party Notices — miPDFconvert

miPDFconvert — Copyright (C) 2026 Wolfgang Mitterbucher (mitterbucher.com).

miPDFconvert is distributed under the **GNU Affero General Public License, version 3**
(see [`LICENSE`](LICENSE)).

It incorporates and/or depends on the third-party components listed below. Each
component remains under its own license; the terms of those licenses continue to
apply to the respective component. Where a component is copyleft (GPL/AGPL), the
project as a whole is licensed under the AGPL v3 to remain compatible.

Full license texts referenced here are provided in the [`licenses/`](licenses/)
folder and inline below.

> **Note:** This file documents license obligations to the best of our knowledge.
> It is not legal advice. Verify obligations for your specific distribution
> with qualified counsel.

---

## 1. miPortMon — Printer Port Monitor (C++)

The `miPortMon/` component is a derivative work of **mfilemon**.

- **mfilemon** — "print to file with automatic filename assignment"
  Copyright (C) 2007–2015 Monti Lorenzo
  Source: <https://sourceforge.net/projects/mfilemon/>

**License: GNU General Public License, version 2, or (at your option) any later
version (GPL-2.0-or-later).** Full text: [`licenses/GPL-2.0.txt`](licenses/GPL-2.0.txt).

The original license headers are retained in every source file under `miPortMon/`.
Because "or any later version" is permitted, this component is used here under
GPL v3, which is compatible with the project's AGPL v3 license.

---

## 2. clawPDF / PDFCreator (SetupHelper & architecture)

The `miPDFSetupHelper/` component (printer/driver installation, spooler and
related utilities) is a derivative work of **clawPDF**, which is itself based on
**PDFCreator**. miPDFconvert also follows the overall architecture of clawPDF, an
open-source virtual PDF printer for Windows.

- **clawPDF** — Copyright (C) 2019 Andrew Hess // clawSoft
  Source: <https://github.com/clawsoftware/clawPDF>
- **PDFCreator** — Copyright (C) pdfforge GmbH
  Source: <https://github.com/pdfforge/PDFCreator>
- **License: GNU Affero General Public License, version 3 (AGPL-3.0).**

Following the upstream clawPDF convention, the individual `.cs` files under
`miPDFSetupHelper/` carry no per-file license header; the AGPL v3 is conveyed
through the root [`LICENSE`](LICENSE) file and this document. The copyright of
the original authors is also recorded in
`miPDFSetupHelper/Properties/AssemblyInfo.cs`. This is the primary reason
miPDFconvert as a whole is distributed under AGPL v3.

---

## 3. Ghostscript (native engine)

The PDF/PostScript conversion relies on the **Ghostscript** engine, which is
invoked at runtime as a **separate process** (`gswin64c.exe` / `gswin32c.exe`).
miPDFconvert does not link against it in-process.

The installer **bundles Ghostscript 10.08.0** in `<install dir>\gs`: the
**unmodified** binaries `gswin64c.exe`/`gsdll64.dll` (or `gswin32c.exe`/`gsdll32.dll`)
extracted from the official Artifex Windows installers, together with
Ghostscript's license text (`gs\COPYING`) and a notice on where to obtain the
source (`gs\README.txt`). The exact version and SHA256 hashes are pinned in
`build.ps1`.

- Copyright (C) Artifex Software, Inc.
- Home: <https://www.ghostscript.com/>
- **License: GNU Affero General Public License, version 3 (AGPL-3.0)** — used
  here under the AGPL (Artifex also offers a commercial license).
- **Corresponding source:** the complete source of the bundled version
  (`ghostscript-10.08.0.tar.xz`) is attached to the corresponding miPDFconvert
  [GitHub release](https://github.com/Mibuw/miPDFconvert/releases) and is also
  available from Artifex at
  <https://github.com/ArtifexSoftware/ghostpdl-downloads/releases/tag/gs10080>.

"Ghostscript" is a trademark of Artifex Software, Inc. miPDFconvert is not
affiliated with or endorsed by Artifex.

If a newer Ghostscript is installed on the system, the bundled copy is still
used; a system installation is only used as a fallback when the bundled copy is
missing.

---

## 3a. Microsoft Visual C++ Runtime (app-local, for Ghostscript)

`vcruntime140.dll`, `vcruntime140_1.dll` (x64 only) and `msvcp140.dll` are
placed next to the bundled Ghostscript so it runs without a system-wide Visual
C++ Redistributable of matching bitness.

- Copyright (C) Microsoft Corporation
- **License:** redistributable files of Microsoft Visual Studio ("Distributable
  Code", taken from the Visual Studio `VC\Redist` folder), distributed under the
  Visual Studio license terms.

---

## 4. Apache log4net 3.3.2

- Copyright © The Apache Software Foundation
- Home: <https://logging.apache.org/log4net/>
- **License: Apache License, Version 2.0.** Full text:
  [`licenses/Apache-2.0.txt`](licenses/Apache-2.0.txt).

---

## Summary

| Component | Version | License | Copyleft |
|-----------|---------|---------|----------|
| miPortMon (mfilemon) | — | GPL-2.0-or-later | yes |
| miPDFSetupHelper (clawPDF / PDFCreator) | — | AGPL-3.0 | yes |
| Ghostscript (engine, bundled, external process) | 10.08.0 | AGPL-3.0 | yes |
| Microsoft Visual C++ Runtime (app-local) | 14.x | VS redistributable | no |
| log4net | 3.3.2 | Apache-2.0 | no |

Because the project combines GPL-2.0-or-later and AGPL-3.0 components, the
combined work **miPDFconvert** is distributed under **AGPL-3.0**, and its
complete corresponding source code must be made available to recipients
(including users interacting with it over a network, per AGPL §13).
