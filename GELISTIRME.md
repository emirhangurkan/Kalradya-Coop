# Geliştirme notları

Kalradya Co-op, Windows için .NET 8 ile yazılmış bir WPF uygulamasıdır. Programın indirdiği Bannerlord Coop ve Tailscale ayrı projelerdir; oyun ve bu paketler kaynak kodun parçası değildir.

## Kaynaktan derleme

Windows'ta .NET 8 SDK yüklüyken proje klasöründe PowerShell açın:

```powershell
dotnet publish .\KalradyaCoop.csproj -c Release -r win-x64 --self-contained true -o .\publish
```

Çıktı `publish/KalradyaCoop.exe` dosyasıdır. Kullanıcıların EXE'yi çalıştırmak için ayrıca .NET kurması gerekmez.

## Güvenlik ve dosyalar

- `MachineSetup.cs`, Coop istemci ve sunucu arşivlerini sabit SHA-256 değerleriyle, Tailscale kurulum dosyasını Windows dijital imzasıyla kontrol eder. Bu kontrol, uygulamanın tamamının güvenli olduğuna dair garanti değildir.
- `NetworkService.cs`, gerektiğinde yönetici onayıyla Tailscale için UDP kuralı ekler veya günceller; Tailscale ağını Özel olarak ayarlar, mevcut `Tailscale-In` kurallarını devre dışı bırakabilir ve Özel ağ profilinde gelen kurallara izin verebilir. Özel profil değişikliği diğer özel ağları da etkileyebilir.
- Oyun şifresi ve son kullanılan katılım bilgileri yerel yapılandırma dosyalarında düz metin olarak tutulabilir. Günlük veya ayarları paylaşmadan önce gizli bilgileri kontrol edin.
- Yeni seferlerin ve zorluk ayarlarının dosyaları ayrı tutulur. Host, bir sonraki başlatmada o seferin ayarlarını kullanır. Devir paketi kayıtları taşır; yeni host zorluk ayarını kendisi seçer.
- Derleme çıktıları, yerel test raporları, kayıtlar ve kişisel ayarlar `.gitignore` ile dışarıda tutulur. Git'e yüklemeden önce yine de eklenecek dosyaları gözden geçirin.

## Kontrol

`--verify-campaigns`, kurulu sunucu ve oyun dosyaları bulunan bir bilgisayarda ayrı bir test dünyasıyla sefer ve kayıt işlemlerini sınar. Test gerçek sunucu işlemi başlatır; rapor yolu mutlak olmalıdır. İkinci fiziksel bilgisayarda veya Steam–Epic arasında uçtan uca bağlantıyı bu test tek başına doğrulamaz.

```powershell
.\publish\KalradyaCoop.exe --verify-campaigns C:\tam\yol\test-raporu.json
```

Coop sürüm bilgileri: [Bannerlord Coop 0.1.6](https://github.com/Bannerlord-Coop-Team/BannerlordCoop/releases/tag/v0.1.6). Tailscale paylaşımı: [Tailscale paylaşım rehberi](https://tailscale.com/docs/features/sharing).
