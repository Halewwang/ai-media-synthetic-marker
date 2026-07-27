# Third-party notices

The MIT License in this repository covers only the original EMKE AI Marker
source code. Third-party software remains subject to its own license terms.

## Production package

The EMKE AI Marker v2 Windows x64 package contains the following third-party
runtime components.

### .NET 10 self-contained runtime

- Copyright: Microsoft Corporation and contributors
- Website: https://dotnet.microsoft.com/
- Source: https://github.com/dotnet/runtime
- License: MIT and the additional notices supplied with the official runtime

The v2 ZIP includes the exact official .NET SDK 10.0.100 license files as:

```text
licenses/dotnet/LICENSE.txt
licenses/dotnet/ThirdPartyNotices.txt
```

The repository copies are retained byte-for-byte under
`packaging/licenses/dotnet/`.

### ExifTool 13.59

- Author: Phil Harvey
- Website: https://exiftool.org/
- Source: https://github.com/exiftool/exiftool
- License: the same terms as Perl itself (the Artistic License or the GNU GPL)

### ExifTool Windows package

- Package maintainer: Oliver Betz
- Information: https://oliverbetz.de/pages/Artikel/ExifTool-for-Windows
- Launcher: CC0, as stated in the bundled `readme_windows.txt`
- Strawberry Perl and bundled modules: see the package's
  `Licenses_Strawberry_Perl.zip`

The production package keeps `exiftool.exe` and `exiftool_files` together and
preserves the official Windows package's license materials, including:

```text
exiftool/README.txt
exiftool/exiftool_files/LICENSE
exiftool/exiftool_files/readme_windows.txt
exiftool/exiftool_files/Licenses_Strawberry_Perl.zip
```

## Legacy source only

The following components belong only to the archived v1.0.0 Python/Tkinter
source under `legacy/python/`. They are not production dependencies and are
not included in the v2 ZIP.

### CPython 3.14.6

- Copyright: Python Software Foundation and contributors
- Website: https://www.python.org/
- License: PSF License Agreement and the additional notices in the Python
  distribution

### Tcl/Tk 8.6

- Website: https://www.tcl.tk/
- License: Tcl/Tk license terms

The retained Tcl and Tk license sources are under
`legacy/python/packaging/licenses/`.

### PyInstaller 6.21.0

- Website: https://pyinstaller.org/
- Source: https://github.com/pyinstaller/pyinstaller
- License: GPL with the special exception applying to bundled applications

The historical PyInstaller spec and locked Python build requirements remain
under `legacy/python/` only as v1 behavior and build provenance. They are not
run by the v2 release workflow.

This notice is informational and does not replace the complete license files
distributed with each component.
