# Kalradya Co-op

**Bannerlord'u arkadaşlarınla birlikte oynamak için daha kolay bir başlangıç.**

Kalradya Co-op; sunucuyu hazırlamana, arkadaşlarına bağlantı bilgilerini göndermene ve kayıtlarını yönetmene yardımcı olan bir Windows programıdır. Bir gün sen, başka bir gün arkadaşın sunucuyu açabilir.

Bu bir topluluk projesidir; TaleWorlds, Bannerlord Coop veya Tailscale'in resmi uygulaması değildir. Oyunun ve Coop modunun yerini almaz.

## Neler gerekli?

- Windows 10 veya 11, 64 bit.
- Her oyuncuda satın alınmış ve yüklenmiş **Mount & Blade II: Bannerlord 1.4.8** ve **Coop 0.1.6**. Program Steam ve Epic kurulumlarını bulmaya çalışır. Oyun ve mod sürümleriniz aynı olmalı.
- Her oyuncu için bir Tailscale hesabı ve internet bağlantısı.
- Oyunu açacak kişi için yaklaşık **4,4 GB'lık** sunucu paketi indirmesi ve kurulum için boş disk alanı.

Steam ve Epic kurulumları desteklenir; iki farklı mağaza arasında bağlantı ayrıca uçtan uca doğrulanmamıştır.

## 1. İlk kurulum

1. GitHub sayfasındaki **Releases / Sürümler** bölümünde yayımlanan Kalradya Co-op ZIP dosyasını indirip çıkar. İçindeki programı aç.
2. Oyunu açacaksan **Ben açacağım**, katılacaksan **Arkadaşıma katılacağım** seç. **Diğer işlemler → İlk kurulum → Eksikleri hazırla** yolunu izle.
3. Program oyunu bulamazsa oyun klasörünü seç. İstenen Windows izinlerini inceleyip onayla ve Tailscale'e kendi hesabınla giriş yap.
4. Sunucuyu ilk kez kuruyorsan paket indirildikten sonra kullanım koşullarını oku ve kabul et; ardından gerekirse **Eksikleri hazırla** düğmesine tekrar bas.

Kurulum Coop'u etkinleştirir; War Sails, Birth and Death ve FastMode'u kapatır. Önceki başlatıcı ayarları yedeklenir.

## 2. Oyunu sen açacaksan

1. **Ben açacağım** seç. İstersen **Diğer işlemler → Şifre ve ayarlar** bölümünden oyun şifreni belirle.
2. **Oyunu başlat** düğmesine bas. Sunucu hazırlandığında oyun açılır; oyun başlatıcısı görünürse **Play** seç.
3. Oyunda **Join Co-op Sandbox → Direct** ekranına gir. Kalradya Co-op'ta **Oyunda sen bunu kullan** yazan adresi ve oyun şifresini gir.
4. Programdaki **IP ve şifreyi birlikte kopyala** düğmesine basıp mesajı arkadaşlarına gönder.

Arkadaşların bu bilgisayara Tailscale üzerinden ilk kez katılıyorsa **Tailscale daveti ekle → Tailscale paylaşımını aç** yolunu kullan. Açılan sayfada kendi bilgisayarın için **Share → Copy invite link** seç; birkaç arkadaşın kullanacaksa **Reusable link** seçeneğini aç. Bağlantıyı programa yapıştırıp kaydet ve mesajı tekrar kopyala. Her arkadaş daveti, Tailscale'de kullandığı kendi hesabıyla kabul etmeli. [Tailscale paylaşım rehberi](https://tailscale.com/docs/features/sharing)

## 3. Arkadaşının oyununa katılacaksan

1. Onun gönderdiği mesajı kopyala. Programda **Arkadaşıma katılacağım → 1. Mesajı buraya yapıştır** seç.
2. İlk kez katılıyorsan görünen **Tailscale davetini kabul et** düğmesini kullan ve kendi hesabınla daveti kabul et.
3. **2. Oyunu aç** düğmesine bas. Oyunda **Join Co-op Sandbox → Direct** ekranına arkadaşının adresini ve şifresini gir, sonra **Join** seç.

## 4. Kayıt ve yeni oyun

- **Yeni oyun başlat** ayrı bir sefer açar; eski seferini **Kayıt seç** ile bulabilirsin.
- **Zorluk ve oyun ayarları** sunucu kapalıyken değiştirilebilir. Seçim sonraki açılışta herkes için geçerlidir. Gerçekçi seçeneğinde oyuncu kahramanları savaşta ölebilir.
- Sunucu otomatik kayıt alır. Bitirdiğinizde **Kaydet ve kapat** kullan. Penceredeki **X**, sunucu çalışıyorsa programı arka plana gizler.
- Sunucuyu açan bilgisayar kapanırsa diğer oyuncular devam edemez.

**Başka biri devam edecekse:** Eski ev sahibi **Diğer işlemler → Kaydı arkadaşıma gönder** ile bir `.kalradya` dosyası oluşturup gönderir. Yeni ev sahibi **Diğer işlemler → Arkadaşımdan gelen kaydı aç** ile dosyayı alır ve oyunu başlatır. Herkes onun yeni IP ve şifresini kullanır; gerekirse yeni Tailscale davetini kabul eder. Zorluk ayarını yeni ev sahibi kendi programından seçer.

## Açık kaynak ve güven

Bu projenin kaynak kodunu inceleyebilir ve [MIT lisansı](LICENSE) koşullarıyla geliştirebilirsin. Bannerlord, Bannerlord Coop ve Tailscale kendi lisans ve koşullarına tabidir.

Program, indirdiği Coop paketlerinin bütünlüğünü ve Tailscale kurulum dosyasının dijital imzasını kontrol eder. Kurulum veya bağlantı sırasında Windows yönetici izni isteyebilir; bağlantıyı kurmak için Tailscale ağ profilini ve Windows'un **Özel ağ güvenlik duvarı** ayarlarını değiştirebilir. Bu profil değişikliği diğer özel ağları da etkileyebilir.

Oyun şifreleri ve katılım bilgileri bilgisayarda düz metin olarak saklanabilir; davet bağlantılarını yalnızca güvendiğin kişilerle paylaş. Bu sürümün EXE dosyası dijital olarak imzalı değildir ve bağımsız bir güvenlik denetiminden geçmemiştir. Açık kaynak olmak tek başına güvenlik garantisi vermez. [Geliştirme ve güvenlik notları](GELISTIRME.md)

## Bağlanamıyorsanız

Herkeste Tailscale'in bağlı olduğunu, arkadaşların doğru bilgisayarın davetini kabul ettiğini ve oyunu açan kişinin sunucusunun çalıştığını kontrol edin. Oyun ve Coop sürümlerini karşılaştırın. Gerekirse programdaki **Diğer işlemler → Bağlantıyı kontrol et** seçeneğini kullanın; bu işlem yönetici izni isteyebilir.
