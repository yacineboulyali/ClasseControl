## ⚠️ نسخة تجريبية — Version d'essai

**للتجربة فقط.** هذه النسخة لا تصل إلى الأجهزة عبر التحديث التلقائي، ولا تحل محل الإصدار الرسمي.
*Pour essai uniquement : elle n'est pas distribuée par la mise à jour automatique et ne remplace pas la version officielle.*

> الملفات **غير موقَّعة رقمياً** بعد. إذا كانت خاصية **Smart App Control** (Contrôle intelligent des applications) مفعّلة على الجهاز، فستمنع البرنامج. لذلك جرّبه في **Windows Sandbox** أو على جهاز تكون فيه هذه الخاصية متوقفة.
>
> *Fichiers pas encore signés : Smart App Control les bloque. Essayez dans Windows Sandbox ou sur un PC où il est désactivé.*

### الطريقة الموصى بها: Windows Sandbox (حاسوب Windows وهمي ومؤقت)

> تعمل على إصدارات Windows Pro وEntreprise وÉducation فقط.

1. **تفعيل Sandbox (مرة واحدة)**: في قائمة Démarrer اكتب « Activer ou désactiver des fonctionnalités Windows »، ثم ضع علامة على **Windows Sandbox** وأعد تشغيل الجهاز.
2. **فتح Sandbox**: افتح **Windows Sandbox** من قائمة Démarrer، ثم افتح Microsoft Edge داخله.
3. **التحميل**: حمّل منه **ClasseControle-portable.zip** من صفحة هذه النسخة، ثم فك الضغط (Extraire tout).
4. **تشغيل وضع التلميذ**: في مجلد البرنامج اكتب `cmd` في شريط العنوان ثم اضغط Entrée، ونفّذ:
   ```
   ClasseControle.exe --eleve
   ```
   إذا ظهرت رسالة SmartScreen: اضغط « Informations complémentaires »، ثم « Exécuter quand même ».
5. **تشغيل وضع الأستاذ في نفس الجهاز**:
   ```
   ClasseControle.exe --prof
   ```
   استعمل رمز القاعة نفسه في الوضعين (الافتراضي: `CLASSE`)، ثم اضغط « Rechercher les postes ». سيظهر الجهاز نفسه كتلميذ.
6. **ما يمكن تجربته**: القفل، الرسائل، رؤية الشاشة، الحصة، الاستطلاع، **تصفية المواقع** (Restrictions… › Sites interdits، ثم افتح youtube.com في Edge)، قطع الإنترنت…

عند إغلاق Sandbox يُحذف كل شيء تلقائياً.

### طريقة أخرى: حاسوب تجريبي أو آلة افتراضية
- **بالمثبِّت**: شغّل **ClasseControle-Setup.exe** واختر « Poste élève » أو « Poste du professeur ».
- **لتجربة أستاذ مع تلميذ**: استعمل جهازين في نفس الشبكة المحلية، أو وضعين على نفس الجهاز بالنسخة المحمولة كما في الأعلى.

### الملفات / Fichiers
| الملف | الاستعمال |
|---|---|
| **ClasseControle-portable.zip** | البرنامج بدون تثبيت (الأنسب للتجربة) |
| ClasseControle-Setup.exe | المثبِّت |
| ClasseControle.exe | البرنامج في ملف واحد |
| SHA256SUMS.txt | بصمات الملفات |

**عند ظهور مشكل**: افتح الملفين التاليين وأرسل محتواهما:
- `%LocalAppData%\Classe_controle\erreurs.log`
- `%ProgramData%\Classe_controle\maintenance.log`
