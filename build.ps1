param([string]$Python = "python")

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$compiler = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path -LiteralPath $compiler)) { throw "The .NET Framework 4.x C# compiler was not found." }

& $compiler /nologo /target:winexe /out:"$root\FileOrbit.build.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll "$root\FileOrbit.cs"
if ($LASTEXITCODE -ne 0) { throw "C# launcher build failed." }

$vendor = Join-Path (Split-Path -Parent $root) "vendor"
$paths = @()
if (Test-Path -LiteralPath $vendor) { $paths = @("--paths", $vendor) }
& $Python -m PyInstaller --noconfirm --clean --windowed --onedir --name FileOrbit.Worker @paths `
    --collect-all pymupdf `
    --collect-all py7zr `
    --exclude-module matplotlib --exclude-module torch --exclude-module pandas `
    --exclude-module openpyxl --exclude-module imageio_ffmpeg `
    --exclude-module rembg --exclude-module skimage --exclude-module scipy `
    --exclude-module numba --exclude-module llvmlite --exclude-module pymatting `
    --exclude-module onnxruntime.tools --exclude-module onnxruntime.quantization `
    --exclude-module onnxruntime.transformers `
    --distpath "$root\dist" --workpath "$root\pyi-build" --specpath "$root" "$root\worker.py"
if ($LASTEXITCODE -ne 0) { throw "Python worker build failed." }

$package = Join-Path $root "dist\FileOrbit.Worker"
Copy-Item -LiteralPath (Join-Path $root "FileOrbit.build.exe") -Destination (Join-Path $package "FileOrbit.exe")
Copy-Item -LiteralPath (Join-Path $root "LICENSE") -Destination $package
Copy-Item -LiteralPath (Join-Path $root "README.md") -Destination $package
Copy-Item -LiteralPath (Join-Path $root "ASSETS.md") -Destination $package
Copy-Item -LiteralPath (Join-Path $root "THIRD_PARTY.md") -Destination $package
if (Test-Path -LiteralPath (Join-Path $root "licenses")) {
    Copy-Item -LiteralPath (Join-Path $root "licenses") -Destination $package -Recurse -Force
}
if (Test-Path -LiteralPath (Join-Path $root "assets")) {
    $assets = Join-Path $package "assets"
    New-Item -ItemType Directory -Force -Path $assets | Out-Null
    foreach ($name in @("ffmpeg", "rembg")) {
        $source = Join-Path (Join-Path $root "assets") $name
        if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $assets -Recurse -Force }
    }
    $tesseract = Join-Path (Join-Path $root "assets") "tesseract"
    if (Test-Path -LiteralPath (Join-Path $tesseract "tesseract.exe")) {
        $target = Join-Path $assets "tesseract"
        New-Item -ItemType Directory -Force -Path (Join-Path $target "tessdata") | Out-Null
        Copy-Item -LiteralPath (Join-Path $tesseract "tesseract.exe") -Destination $target
        Get-ChildItem -LiteralPath $tesseract -Filter "*.dll" -File | Copy-Item -Destination $target
        foreach ($language in @("ara", "eng")) {
            Copy-Item -LiteralPath (Join-Path $tesseract "tessdata\$language.traineddata") -Destination (Join-Path $target "tessdata")
        }
        if (Test-Path -LiteralPath (Join-Path $tesseract "doc\LICENSE")) {
            Copy-Item -LiteralPath (Join-Path $tesseract "doc\LICENSE") -Destination (Join-Path $target "TESSERACT-LICENSE")
        }
    }
}
Write-Host "Built: $package"
