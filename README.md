# FileOrbit | فايل أوربت

أداة ملفات محلية لويندوز. حدّد ملفات في مستكشف الملفات، ثم اضغط زر الفأرة
الأيمن مطولاً أو اسحب مع Shift لتظهر قائمة دائرية. حرّك المؤشر نحو العملية
وأفلت الزر لتنفيذها. الإعدادات تسمح بتشغيل أو إطفاء كل طريقة، وتغيير اللغة،
المدة، ترتيب الصور، شاشة مراجعة النص والصور، والتشغيل مع ويندوز.

## التشغيل

1. فك ضغط حزمة Windows في مجلد ثابت.
2. شغّل `FileOrbit.exe`. ستظهر أيقونته بجانب الساعة.
3. افتح الإعدادات من الأيقونة. لتجربة أداة من دون الإيماءات، اختر
   **اختيار ملفات** من الأيقونة.
4. احفظ ملفاتك الأصلية؛ النتائج تُنشأ بجانبها باسم جديد ولا تُستبدل.

يدعم Windows 11. بني على .NET Framework 4.8، وقد يعمل على Windows 10، لكن
هذا الإصدار اختُبر على Windows 11 فقط. لا يحتاج اتصالاً بالإنترنت أثناء
الاستخدام. تُشغّل مكتبات التحويل في عملية منفصلة عند الحاجة؛ لا تبقى محمّلة
في عملية الأيقونة أثناء الخمول.

## العمليات الحالية

- PDF: تحويل الصفحات إلى PNG/JPG داخل مجلد؛ استخراج النص إلى TXT/DOCX مع
  OCR عربي وإنجليزي للصفحات المصورة؛ تقسيم أو دمج عدة PDF؛ ضغط بنيوي.
- صور: تحويل الصيغة، إنشاء PDF واحد من صور متعددة بترتيب الاسم أو وقت الالتقاط،
  قص وتدوير وتغيير الحجم والسطوع والتباين وإضافة نص وخلفية وإزالة الخلفية.
- TXT/DOCX: تحويل إلى PDF، مع عرض صحيح للنص العربي في PDF.
- صوت وفيديو: تحويل الصيغة، ضغط، واستخراج MP3 من الفيديو.
- ZIP/7z/TAR/TAR.GZ: إنشاء وفك الأرشيف. يمكن جمع ملفات مختلطة في أرشيف واحد.

OCR قد يخطئ في النصوص المصورة، وتحويل PDF إلى DOCX يستخرج النص ليصبح قابلاً
للتحرير، لكنه لا يحافظ على تخطيط كل صفحة. ضغط PDF البنيوي لا يضمن تقليل
الحجم إذا كانت الصور داخله مضغوطة مسبقاً. بعض الصيغ غير المدعومة ستُعرض لها
خيارات محدودة أو رسالة واضحة.

## البناء من المصدر

المصدر: `FileOrbit.cs` لعملية الأيقونة الخفيفة، و`worker.py` للتحويل والواجهات.
ثبت Python 3.12 و .NET Framework 4.8 على Windows، ثم:

```powershell
python -m pip install -r requirements.txt
.\build.ps1 -Python python
```

ضع الملفات الخارجية تحت `assets/` قبل البناء:

- `assets/tesseract/tesseract.exe` مع DLLs اللازمة و
  `assets/tesseract/tessdata/{ara,eng}.traineddata`
- `assets/ffmpeg/ffmpeg.exe`
- `assets/rembg/models/u2netp/u2netp.onnx`

روابط الملفات وأرقام التحقق موجودة في `ASSETS.md`؛ تراخيصها في `THIRD_PARTY.md`. السكربت
ينشئ `dist/FileOrbit.Worker/` ويضيف ملفات البرنامج والرخصة. لن ينزّل أي
ملف أثناء تشغيل التطبيق.

## الترخيص

كود FileOrbit مرخص تحت AGPL-3.0-only؛ راجع `LICENSE`. المكونات المضمنة لها
تراخيصها الخاصة في `THIRD_PARTY.md`. عند توزيع نسخة مبنية، انشر معها المصدر
المقابل وتراخيص المكونات.

---

FileOrbit is an offline Windows tray app with a contextual radial menu.
Select files in Explorer, hold the right button or start a Shift-drag, hover an
action, and release. Use the tray menu to open Settings or choose files
manually. Results are saved next to the originals without overwriting them.

Windows 11 is the tested target. The app uses .NET Framework 4.8 and may run
on Windows 10, but that target has not been verified. The tray process stays
small while conversion libraries load only for a job. See the Arabic sections
above for feature and build details. FileOrbit source is AGPL-3.0-only; see
`LICENSE` and `THIRD_PARTY.md` before redistribution.
