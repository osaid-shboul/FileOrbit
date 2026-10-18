# Bundled offline assets

The binary release includes these files. The source archive omits large third-party
binaries; put them in the paths below before running `build.ps1`.

| Path | Source | SHA-256 of bundled file |
| --- | --- | --- |
| `assets/ffmpeg/ffmpeg.exe` | `imageio-ffmpeg` 0.6.0 Windows wheel, containing FFmpeg 7.1 gyan.dev essentials GPL build | `2CE797A0F88D7F067180338FB227F7B1928EA727BD9A4D7A1D022F7C52AF71A3` |
| `assets/tesseract/tesseract.exe` and its DLLs | [UB Mannheim Tesseract 5.5.3 Windows setup](https://github.com/tesseract-ocr/tesseract/releases/download/5.5.3/tesseract-ocr-w64-setup-5.5.3.20260724.exe) | `C66F0F12ED76F6AA455DAC97684BBC86756D6A732380BEE09122454CFDA3F420` |
| `assets/tesseract/tessdata/ara.traineddata` | [tessdata_fast Arabic](https://github.com/tesseract-ocr/tessdata_fast/raw/refs/heads/main/ara.traineddata) | `E3206D3DC87FD50C24A0FB9F01838615911D25168F4E64415244B67D2BB3E729` |
| `assets/tesseract/tessdata/eng.traineddata` | [tessdata_fast English](https://github.com/tesseract-ocr/tessdata_fast/raw/refs/heads/main/eng.traineddata) | `7D4322BD2A7749724879683FC3912CB542F19906C83BCC1A52132556427170B2` |
| `assets/rembg/models/u2netp/u2netp.onnx` | [rembg U2NetP model release](https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2netp.onnx) | `309C8469258DDA742793DCE0EBEA8E6DD393174F89934733ECC8B14C76F4DDD8` |

The Tesseract runtime bundle copies `tesseract.exe`, its DLLs, the two traineddata
files, and its license notice. The FFmpeg build reports GPLv3-or-later using `-L`.
Keep its `GPL-3.0.txt` notice and provide the matching FFmpeg source when
redistributing the binary.
