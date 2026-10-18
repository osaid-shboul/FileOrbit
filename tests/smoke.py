"""Offline integration checks for the conversion core."""
import os
import sys
import tempfile
from pathlib import Path

PROJECT = Path(__file__).resolve().parents[1]
VENDOR = PROJECT.parent / "vendor"
sys.path.insert(0, str(VENDOR))
sys.path.insert(0, str(PROJECT))

from PIL import Image, ImageDraw, ImageFont
from worker import (capture_time, create_archive, document_convert, extract_archive,
                    image_convert, ocr_image_bytes, pdf_from_images, pdf_text,
                    pdf_to_images, safe_member_path, sorted_images)


def main():
    with tempfile.TemporaryDirectory(dir=PROJECT.parent) as temp:
        root = Path(temp)
        image1 = root / "b.png"
        image2 = root / "a.png"
        img = Image.new("RGB", (640, 150), "white")
        font = ImageFont.truetype("arial.ttf", 50)
        ImageDraw.Draw(img).text((20, 40), "HELLO FILEORBIT 123", font=font, fill="black")
        img.save(image1)
        Image.new("RGB", (100, 100), "blue").save(image2)
        cfg = __import__("worker").load_settings()
        cfg["general"]["review_images"] = "0"
        pdf = pdf_from_images([image1, image2], root / "Combined.pdf", cfg)
        assert pdf.exists()
        import pymupdf as fitz
        with fitz.open(pdf) as doc:
            assert len(doc) == 2
        pages = pdf_to_images(pdf, "jpg")
        assert len(list(pages.glob("*.jpg"))) == 2
        assert image_convert(image1, "webp").exists()
        extracted = ocr_image_bytes(image1.read_bytes(), "eng")
        assert "FILEORBIT" in extracted.upper(), extracted
        text = pdf_text(pdf, force_ocr=True)
        assert "FILEORBIT" in text.upper(), text
        archive = create_archive([image1, image2], "7z")
        extracted_folder = extract_archive(archive)
        assert (extracted_folder / "a.png").exists()
        tar = create_archive([image1, image2], "tar.gz")
        assert (extract_archive(tar) / "b.png").exists()
        try:
            safe_member_path(root, "../escape.txt")
        except ValueError:
            pass
        else:
            raise AssertionError("Archive path traversal was accepted")
        (root / "note.txt").write_text("hello document", encoding="utf-8")
        assert document_convert(root / "note.txt", "docx").exists()
        (root / "arabic.txt").write_text("مرحبا بالعالم\nHello world", encoding="utf-8")
        arabic_pdf = document_convert(root / "arabic.txt", "pdf")
        with fitz.open(arabic_pdf) as doc:
            assert len(doc) == 1 and "Hello world" in doc[0].get_text()
    print("Smoke checks passed")


if __name__ == "__main__":
    main()
