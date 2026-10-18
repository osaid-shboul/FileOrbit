# Third-party software and data

FileOrbit source code is licensed under AGPL-3.0-only. A release bundle also
contains these separately licensed projects. Their own notices and source are
available at the linked upstream repositories. Keep this file and the bundled
license texts when redistributing a binary.

| Component | Use | License / upstream |
| --- | --- | --- |
| PyMuPDF / MuPDF | PDF rendering, extraction, layout | AGPL-3.0 or commercial; https://github.com/pymupdf/PyMuPDF |
| Pillow | Images | MIT-CMU; https://github.com/python-pillow/Pillow |
| python-docx | DOCX | MIT; https://github.com/python-openxml/python-docx |
| py7zr | 7z archives | LGPL-2.1-or-later; https://github.com/miurahr/py7zr |
| FFmpeg 7.1 essentials build | Media conversion | GPL-3.0-or-later build with x264; https://www.gyan.dev/ffmpeg/builds/ and https://ffmpeg.org/download.html |
| Tesseract OCR | Offline OCR | Apache-2.0; https://github.com/tesseract-ocr/tesseract |
| tessdata_fast English and Arabic | OCR language data | Apache-2.0; https://github.com/tesseract-ocr/tessdata_fast |
| rembg | Reference for U2NetP preprocessing and model location; not bundled at runtime | MIT; https://github.com/danielgatis/rembg |
| u2netp model | Offline background model | Upstream model distributed by rembg; https://github.com/danielgatis/rembg/releases |
| ONNX Runtime | Background model inference | MIT; https://github.com/microsoft/onnxruntime |
| PyInstaller | Build tool and runtime bootloader | GPL-2.0-or-later with bootloader exception; https://github.com/pyinstaller/pyinstaller |

The FFmpeg binary reports GPL version 3 or later when run with `-L`. Its
source and build information must remain available alongside a binary release.
The Windows Tesseract package includes its own `doc/LICENSE` and upstream
notices. Python dependency notices are retained by the build where applicable.
