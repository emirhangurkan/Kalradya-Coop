# Bannerlord sefer masası

Bu proje Mount & Blade II: Bannerlord Coop 0.1.6'ya özeldir. Ana aksiyonlar: seferi açmak, arkadaşına katılmak, katılım bilgilerini kopyalamak ve kayıtlı dünyayı sonraki hosta devretmek. Koyu deri/taş yüzeyler, bronz kenarlar, parşömen rengi yazılar ve Georgia başlıklarla oyunun dönem hissi korunur. Eylem metinlerinde Segoe UI ve adreslerde Consolas okunabilirlik sağlar.

## Varlıklar

- `Assets/calradia-campaign.png`: özel üretilmiş sefer manzarası, EXE içine gömülü.
- `Assets/Kalradya.ico`: özgün geometrik kalkan/çapraz kılıç simgesi; `BuildIcon.ps1` ile tekrar üretilebilir. Aynı motif Theme.xaml'de ölçeklenebilir çizim olarak bulunur.
- Resmi bir TaleWorlds uygulaması veya resmi oyun görseli olduğu iddia edilmez; ekranda topluluk başlatıcısı olarak tanımlanır.

## Görsel üretimi

Yerleşik **imagegen** aracı kullanıldı; CLI/API anahtarı kullanılmadı. Üretim sonucu değiştirilmeden proje içine kopyalandı.

Kullanılan istem:

> Use case: stylized-concept. Asset type: background illustration for a Windows companion launcher dedicated to Mount & Blade II: Bannerlord co-op. Generate a cinematic, richly painted medieval Calradian campaign scene, original fan companion artwork rather than a screenshot. Wide 16:9 landscape, 1920x1080 if possible. Right third: two mounted warriors in lamellar armor and cloaks viewed from behind, red and faded gold pennants, overlooking a distant stone hill fortress and a misty valley with a small camp. Center: winding road, layered mountains and distant campfires. Left half: low-detail dark atmospheric hillside, ample calm negative space for overlaid interface text. Early morning muted bronze light from the upper right, charcoal and dark olive shadows, weathered leather and steel mood, restrained ochre highlights, realistic proportions, sophisticated oil-painted game concept art. Main subjects mainly in upper and right portions; lower half gets progressively dark and quiet for UI overlays. No text, no UI, no letters, no logos, no watermarks, no modern objects, no magic or monsters. Detailed and beautiful but subdued, strong silhouette readability.

## Uygulama

Görsel üst bölümde görünür, metinlerin altında koyu katmanlarla sönümlenir. Dekorasyon fare etkileşimi almaz. Durumlar hem metin hem renk ile anlatılır. Ana aksiyon bronz, yardımcı aksiyonlar sade; devre dışı düğmeler açıkça soluktur. Kısa pencerede dikey kaydırma vardır. İlk kurulum, davet ve oturum ayarları aynı paleti kullanır.
