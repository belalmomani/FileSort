# FileSort (فايل سورت)

مستكشف ملفات وتصنيف وتنظيم لـ Windows + تطبيق Android.
- applicationId: `com.filesort.organizer`
- الإصدار الحالي: انظر `version.props`

## الحالة
المرحلة 1 (Windows File Explorer Foundation): مستكشف للقراءة فقط. لا يوجد نقل أو حذف أو إعادة تسمية.

## البنية
- `src/FileSort.Core` — منطق الملفات (net10.0)
- `src/FileSort.Desktop` — واجهة WPF (net10.0-windows)
- `tests/FileSort.Core.Tests` — اختبارات xUnit
- `version.props` — ملف الإصدار المركزي
- `docs/workflows` — نسخ ملفات GitHub Actions

## البناء من الهاتف
GitHub ← Actions ← Windows Build ← Run workflow. الناتج: FileSort-Windows-Portable-vX.Y.Z.zip في Artifacts.
