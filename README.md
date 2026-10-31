# FileSort (فايل سورت)

مستكشف ملفات وتصنيف وتنظيم لـ Windows + تطبيق Android.
- applicationId: `com.filesort.organizer`
- الإصدار الحالي: انظر `version.props`

## الحالة
- المرحلة 1: مستكشف الملفات (قراءة فقط).
- المرحلة 2: قاعدة بيانات SQLite ومحرك التصنيف اليدوي (تصنيفات، أهمية، ملاحظات، مكتبة الملفات المصنفة). لا يوجد نقل أو حذف أو إعادة تسمية للملفات حتى الآن.

بيانات المستخدم: `%LOCALAPPDATA%\FileSort\filesort.db`، والنسخ الاحتياطية في `%LOCALAPPDATA%\FileSort\backups`.

## البنية
- `src/FileSort.Core` — منطق الملفات (net10.0)
- `src/FileSort.Data` — قاعدة البيانات (SQLite، ترحيلات، نسخ احتياطي)
- `src/FileSort.Classification` — محرك التصنيف اليدوي
- `src/FileSort.Desktop` — واجهة WPF (net10.0-windows)
- `tests/FileSort.Core.Tests` — اختبارات xUnit
- `version.props` — ملف الإصدار المركزي
- `docs/workflows` — نسخ ملفات GitHub Actions

## البناء من الهاتف
GitHub ← Actions ← Windows Build ← Run workflow. الناتج: FileSort-Windows-Portable-vX.Y.Z.zip في Artifacts.
