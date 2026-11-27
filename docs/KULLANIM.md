# LingoCue kullanım kılavuzu

LingoCue bir videonun konuşmasını yazıya döker, yazıyı düzeltir, başka dillere çevirir ve
altyazıyı istediğiniz görünümle videoya gömer ya da ayrı dosya olarak verir.

- [Kurulum](#kurulum)
- [İlk açılış](#ilk-açılış)
- [Adım adım: videodan altyazıya](#adım-adım-videodan-altyazıya)
- [Altyazıyı düzenlemek](#altyazıyı-düzenlemek)
- [Stil ve arka plan kutusu](#stil-ve-arka-plan-kutusu)
- [Dışa aktarma](#dışa-aktarma)
- [Yapay zekâ motorları ve maliyet](#yapay-zekâ-motorları-ve-maliyet)
- [Ayarlar](#ayarlar)
- [Güncellemeler](#güncellemeler)
- [Sorun giderme](#sorun-giderme)

## Kurulum

İndirmeler GitHub'daki [sürümler sayfasındadır](https://github.com/eneskaramanoglu/lingocue/releases/latest).

### Windows (64 bit)

1. `LingoCue-win-Setup.exe` dosyasını indirip çalıştırın. Yönetici izni istemez; uygulama
   kullanıcı klasörünüze (`%LocalAppData%\LingoCue`) kurulur. Masaüstüne ve Başlat menüsüne kısayol eklenir.
2. Windows "Windows bilgisayarınızı korudu" uyarısı gösterebilir. Uygulama ücretli bir kod imzalama
   sertifikasıyla imzalı olmadığı için bu uyarı normaldir: **Ek bilgi → Yine de çalıştır**.
3. Kurmadan denemek isterseniz `LingoCue-win-Portable.zip` dosyasını açıp `LingoCue.exe`'yi çalıştırabilirsiniz
   (bu sürüm kendini güncellemez, yeni sürümü haber verir).

Kaldırmak için: **Ayarlar → Uygulamalar → LingoCue → Kaldır**. Ayarlarınız ve indirdiğiniz modeller
(`%AppData%\LingoCue`) silinmez.

### macOS (Apple Silicon: M1, M2, M3, M4)

1. `LingoCue-<sürüm>-osx-arm64.dmg` dosyasını açın ve **LingoCue**'yu **Applications** klasörüne sürükleyin.
2. Uygulama Apple tarafından onaylanmış (notarize) değildir. İlk açılışta macOS "hasarlı" ya da
   "açılamıyor" diyebilir. Terminal'de bir kez şunu çalıştırın:

   ```sh
   xattr -dr com.apple.quarantine /Applications/LingoCue.app
   ```

3. Sonra LingoCue'yu normal şekilde açabilirsiniz.

### FFmpeg (iki sistemde de gerekli)

LingoCue sesi ve videoyu FFmpeg ile işler; ayrıca kurulması gerekir:

- **Windows:** `winget install Gyan.FFmpeg`
- **macOS:** `brew install ffmpeg-full` (sade `ffmpeg` paketinde altyazıyı videoya gömmek için gereken libass yoktur)

Kurduktan sonra LingoCue'yu yeniden başlatın. Karşılama ekranındaki **Hazırlık** listesinde
"FFmpeg · bulundu" görmelisiniz.

## İlk açılış

Karşılama ekranının sağındaki **Hazırlık** listesi neyin hazır olduğunu gösterir:

| Satır | Anlamı |
| --- | --- |
| FFmpeg | Video açmak ve dışa aktarmak için gerekli. |
| Gemini anahtarı | Bulutta düzeltme, çeviri ve Gemini ile transkripsiyon için. Yerel motor kullanacaksanız gerekmez. |
| Whisper modeli | Yerel transkripsiyon modeli; ilk transkripsiyonda bir kez iner (~1 GB). |
| Donanım | Ekran kartınız ve bellek; bu bilgisayar için önerilen ayarlar. |
| Yerel model (Ollama) | Bilgisayarınızda çalışan dil modeli (isteğe bağlı). |

**Önerilen ayarlar:** **Ayarlar → Tercihler → Genel → Önerilen ayarları uygula**, ekran kartınıza uygun
Whisper modelini ve doğruluk ayarını seçer.

**Gemini anahtarı:** [Google AI Studio](https://aistudio.google.com/apikey)'dan bir anahtar alın ve
**Tercihler → API anahtarları**'na yapıştırın. Anahtar Windows Kimlik Bilgisi Yöneticisi'nde ya da
macOS Anahtar Zinciri'nde saklanır; proje dosyalarına yazılmaz.

## Adım adım: videodan altyazıya

Üst çubuktaki üç adım işin sırasını gösterir: **Transkribe → Düzelt → Çevir**.

1. **Video açın.** Videoyu pencereye sürükleyin ya da **Video aç** (Ctrl+O). Sol tarafta oynatıcı ve
   zaman çizelgesi açılır.
2. **Transkribe (Ctrl+T).** Konuşmayı yazıya döker.
   - **Whisper (yerel):** ücretsizdir, video bilgisayardan çıkmaz. Ekran kartı varsa onu kullanır.
   - **Gemini Transcribe (bulut):** gürültülü ve çok konuşmacılı videolarda daha iyidir; dakikası yaklaşık 0,005 $.
   - Sonuçta **Kaynak** izi oluşur. Ses tanımanın emin olmadığı satırlar **!** ile işaretlenir; bunları dinleyip kontrol edin.
   - Böyle bir satırda emin olunmayan kelimeler zaman çizelgesinde turuncu çizgiyle gösterilir. Düzenleme kutusunun altındaki kelime düğmeleri, o kelimeyi biraz öncesinden çalar.
   - **Birden çok dil konuşulan videolar:** Konuşma dili olarak **Karışık (birden çok dil)** seçin ve videoda
     konuşulan dilleri işaretleyin (en çok 3, ör. Türkçe, İngilizce, Korece). Her konuşma bölümünün dili bu
     diller arasından algılanır ve kendi dilinde yazılır; satırlar TR, EN, KO gibi dil etiketi alır. İlk
     kullanımda küçük bir dil algılama modeli (60 MB) bir kez iner. Aynı cümlenin içindeki dil geçişleri
     ayrılmaz.
3. **Düzelt.** Yapay zekâ yazım, noktalama ve özel isimleri düzeltir, satırları okunur uzunlukta tutar.
   Sonuç ayrı bir **Düzeltilmiş** izdir; orijinal korunur. Çok değişen satırlar **!** ile işaretlenir.
4. **Çevir.** Bir ya da birkaç hedef dil seçin (ör. İngilizce ve Korece). Her dil ayrı bir iz olur.
5. **Dışa aktar (Ctrl+E).** Altyazıyı videoya gömün ya da SRT/VTT/ASS dosyası olarak kaydedin.

İpuçları:

- **Sözlük:** Özel isimleri (kişi, yer, marka) projenin sözlüğüne ekleyin. Hem Whisper hem düzeltme
  bunları sözlükteki gibi yazar. Kilitli terimler hiç değiştirilmez.
- Bir düzeltme ya da çeviri yarıda kalırsa **Altyazı → Başarısız satırları yeniden dene** yalnızca
  bitmeyen satırları gönderir; bitmiş satırlar için tekrar ödeme yapmazsınız.
- Projeyi **Kaydet** (Ctrl+S) ile `.lingocue` dosyası olarak saklayın. Video dosyası projeye kopyalanmaz;
  yerini değiştirirseniz proje açılırken sorar.

## Altyazıyı düzenlemek

- **Satırlar** sekmesinde bir satır seçin; alttaki kutuda metni, başlangıcı ve bitişi düzenleyin.
- **Zaman çizelgesinde** satır bloklarını sürükleyerek kaydırın, kenarlarından tutarak uzatın ya da kısaltın.
  Ses dalgası konuşmanın nerede başladığını gösterir.
- **Böl (Ctrl+B)** ve **Birleştir (Ctrl+M)**; **Bul ve değiştir (Ctrl+F)**; **Geri al (Ctrl+Z)**.
- **Kontrol** sekmesi okuma hızı, satır uzunluğu ve çakışma sorunlarını listeler; tıklayınca o satıra gider.
- **Karışık dilli izlerde** satırların yanında dil etiketi görünür ve listenin üstündeki kutuyla tek bir dili
  süzebilirsiniz. Düzenleme kutusunun altındaki **Dil** düğmeleri seçili satırın dilini değiştirir
  (Ctrl+Shift+L sıradaki dile geçer). Okuma hızı ve büyük harf her satırda o satırın diline göre uygulanır.
  Düzeltme her satırı kendi dilinde tutar; çeviri, zaten hedef dilde olan satırlara dokunmaz. Dışa aktarırken
  **Karışık dilli izi dile göre ayrı dosyalara böl** ile her dil ayrı bir dosya (ör. `film.en.srt`) olur.
- Tüm kısayollar: **Yardım → Klavye kısayolları**.

Okuma kuralları (satır başına karakter, saniyedeki karakter) **Tercihler → Altyazı kuralları**'ndadır.
Korece için Netflix kuralları (satırda 16 karakter) otomatik uygulanır.

## Stil ve arka plan kutusu

**Stil** sekmesi altyazının görünümünü değiştirir; önizleme anında güncellenir ve dışa aktarılan videoyla
birebir aynıdır.

- **Şablonlar:** Her şablon bir kart olarak görünür.
  - Kartın küçük resmi, seçili satırın videonuzdaki görünümüdür.
  - Karta tıklayınca şablon uygulanır; Ctrl+Z ile geri alırsınız. Projenin şu anki görünümüne uyan kart mavi çerçeveyle işaretlenir.
  - Kendi stilinizi **Şablon olarak kaydet** ile tüm projelerde kullanabilirsiniz.
- **Yazı:** font, boyut, kalın/italik, harf aralığı, BÜYÜK HARF (Türkçe kurallarıyla i → İ).
  Bilgisayarda olmayan ya da Türkçe/Korece harfi eksik fontlar için uyarı çıkar; **Font dosyası ekle**
  fontu projenin `fonts` klasörüne kopyalar.
- **Arka plan kutusu:**
  - **Blok kutusu:** tüm satırların arkasında tek dikdörtgen; yazının konturu kalır.
  - **Satır kutusu:** her satırın arkasında ayrı kutu.
  - **Tam genişlik şerit:** videonun bir ucundan öbür ucuna bant.
  - Kutu rengi ve saydamlığı (renk seçicinin **A** değeri), yatay/dikey iç boşluk, çerçeve kalınlığı ve rengi.
  - **Köşe yuvarlaklığı** (blok kutusu): yazının genişliği font dosyasından ölçülerek yuvarlak köşeli kutu çizilir. Ölçülemeyen ya da otomatik kaydırılacak kadar uzun satırlarda köşeli kutu kullanılır.
- **Konum:** dokuz hizalama ve kenar boşlukları. **Güvenli alan** kılavuzları önizlemede gösterilir.
- **Karaoke:** kelimeler konuşuldukça renklenir (Shorts tarzı). Kelime zamanı olan izlerde çalışır.
- **İki dilli mod:** ikinci bir izi (ör. İngilizce çeviri) ana altyazının altında gösterir.

## Dışa aktarma

**Dışa aktar** (Ctrl+E):

- **Videoya göm (burn-in):** Altyazı görüntünün parçası olur; her oynatıcıda aynı görünür.
  Kodlayıcı olarak ekran kartı (NVIDIA, AMD, Intel ya da Apple) seçilirse çok daha hızlıdır.
- **Ayrı altyazı izi (MKV/MP4):** Video yeniden kodlanmaz; altyazı açılıp kapatılabilir.
  MKV stilleri korur, MP4 yalnızca düz metni.
- **Yalnızca altyazı dosyası:** SRT (en yaygın), WebVTT (web) ya da ASS (stil, karaoke ve kutular dahil).

Dışa aktarma işleri arka planda sürer; ilerlemeyi sağ alttaki **İşler**'den izleyebilirsiniz.

> Arka plan kutuları ve şerit libass ile çizilir. Videoya gömüldüğünde her yerde aynı görünür;
> ayrı ASS dosyası olarak verildiğinde libass kullanmayan oynatıcılar onları farklı gösterebilir.

## Yapay zekâ motorları ve maliyet

| | Ne için | Maliyet | Gizlilik |
| --- | --- | --- | --- |
| Whisper (yerel) | Transkripsiyon | Ücretsiz | Video bilgisayardan çıkmaz |
| Gemini Transcribe | Transkripsiyon | ~0,005 $/dk | Ses Google'a gönderilir |
| Gemini (Flash/Pro) | Düzeltme, çeviri | Kısa bir video için genelde 1 sentin altında | Metin Google'a gönderilir |
| Ollama (yerel) | Düzeltme, çeviri | Ücretsiz | Metin bilgisayardan çıkmaz |

- Her işlemden önce tahmini maliyet gösterilir.
- **Ücretsiz Gemini anahtarı** kullanıyorsanız **Tercihler → Dil modeli → Ücretsiz katman**'ı açın.
  İstekler sınıra göre aralıklı gönderilir ve zaman aşımı yaşanmaz. Ücretsiz katmanda Google gönderilen
  içeriği ürünlerini geliştirmek için kullanabilir; gizli videolar için ücretli anahtar kullanın.
- **Yerel model (Ollama):** [ollama.com](https://ollama.com)'dan kurun (Windows: `winget install Ollama.Ollama`,
  macOS: `brew install ollama`), bir model indirin ve **Tercihler → Dil modeli**'nde seçin.
  Hangi boyutun ekran kartınıza sığdığını **Tercihler → Genel → Bu bilgisayar** söyler:
  4 GB kartta ~4B, 8 GB'ta ~8B, 12 GB'ta ~12B, 16 GB ve üstünde ~20B ve üzeri modeller.

## Ayarlar

**Tercihler** (dişli simgesi):

- **Genel:** transkripsiyon motoru, Whisper modeli, konuşma dili, **Whisper doğruluğu**
  (Otomatik / En iyi / Hızlı), sözlük ipuçları, güncelleme denetimi.
  - *En iyi* (beam search) daha isabetlidir ama yaklaşık iki kat yavaştır; *Otomatik* ekran kartı varsa açar.
  - *large-v3 (tam)* modeli 6 GB ve üstü ekran kartları içindir (3,1 GB indirme).
  - **Neural Engine hızlandırması** (yalnızca Apple Silicon): Whisper'ın bir bölümü Neural Engine'de çalışır. İlk kullanımda ~1,2 GB indirilir; ilk çalıştırma birkaç dakika sürebilir.
- **Dil modeli:** varsayılan motor (Gemini ya da Ollama), modeller, ücretsiz katman, paket boyutu.
- **Altyazı kuralları:** satır uzunluğu, satır sayısı, süreler, okuma hızı.
- **API anahtarları:** Gemini anahtarı.

## Güncellemeler

- LingoCue açılışta, günde en fazla bir kez yeni sürüm olup olmadığına bakar. GitHub'a yalnızca
  en son sürümün numarası sorulur; videolarınız, projeleriniz ya da anahtarlarınız gönderilmez.
  Kapatmak için: **Tercihler → Genel → Güncellemeler**.
- **Windows (Setup.exe ile kurulu):** güncelleme arka planda iner; üstteki şeritte
  **Yeniden başlat ve güncelle**'ye basın. Kaydedilmemiş işiniz varsa önce sorulur.
- **macOS ve taşınabilir zip:** şerit yeni sürümü haber verir; **Sürüm sayfasını aç** ile yeni `.dmg`'yi
  indirip eskisinin üzerine kurun.
- Elle denetlemek için: **Yardım → Güncellemeleri denetle**.

## Sorun giderme

| Belirti | Çözüm |
| --- | --- |
| "FFmpeg bulunamadı" | FFmpeg'i kurun (yukarıya bakın) ve LingoCue'yu yeniden başlatın. |
| Altyazı videoya gömülmüyor (macOS) | `brew install ffmpeg-full` kurun; sade `ffmpeg` paketinde libass yok. |
| Transkripsiyon çok yavaş | Durum çubuğu kullanılan cihazı gösterir. "CPU" yazıyorsa *large-v3-turbo* modelini seçin. NVIDIA kartlarda CUDA Toolkit 13 kurmak hızlandırır. |
| "Model yüklenemedi" | Ekran kartı belleği yetmiyor olabilir: daha küçük modeli seçin, sürücüyü güncelleyin. |
| Sessiz bölümlerde uydurma yazı | Bu satırlar **!** ile işaretlenir; silin ya da düzeltin. |
| Gemini "429" ya da zaman aşımı | **Ücretsiz katman** ayarını açın; yarıda kalan satırlar için **Başarısız satırları yeniden dene**. |
| Ollama'ya bağlanılamıyor | Ollama'nın çalıştığından emin olun (`ollama serve`) ve **Tercihler → Dil modeli → Bağlan ve listele**. |
| Korece harfler kutu (□) görünüyor | Stil sekmesindeki uyarıdaki Korece fontu seçin ya da Noto Sans KR'yi projeye ekleyin. |
| macOS "uygulama hasarlı" diyor | Kurulumdaki `xattr` komutunu çalıştırın. |

**Hata bildirmek:** **Yardım → Tanılama paketi oluştur** masaüstüne bir zip kaydeder (özet, son log'lar,
ayarlar; ev klasörü yolu gizlenir, anahtarlar yoktur). Sonra **Yardım → Hata bildir** ile GitHub'da yeni bir
kayıt açın, ne yaptığınızı yazın ve zip'i ekleyin. Log'lar: Windows `%AppData%\LingoCue\logs`,
macOS `~/Library/Logs/LingoCue`.
