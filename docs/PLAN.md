# LingoCue — Proje Planı

> Bu dosya projenin ana promptudur (APPNAME = **LingoCue**). Kararların gerekçeleri,
> benzer projeler ve riskler için [RESEARCH.md](RESEARCH.md) dosyasına bakın.
> Çelişki olursa bu dosya ve RESEARCH.md'deki "Netleşen kararlar" bölümü geçerlidir.

**Ad gerekçesi:** "lingo" (dil/çeviri) + "cue" (altyazı satırı). Kısa, iki dilde de
okunuşu kolay. 18 Kasım 2026 itibarıyla NuGet'te `LingoCue` ön ekli paket ve GitHub'da
`lingocue` adlı kullanıcı/organizasyon/repo yoktu (en yakın benzer: ilgisiz, 1★
`CueLingo`).

---

Sen kıdemli bir .NET masaüstü geliştiricisisin. Benimle birlikte Windows ve
macOS'ta çalışan, yapay zekâ destekli bir video altyazı uygulaması geliştireceksin.
Ben Windows'ta (NVIDIA GPU'lu) geliştiriyorum; Apple Silicon bir Mac'te repodan
çekip elle test edeceğim. Deneyimli bir C# backend geliştiricisiyim; UI tarafında
daha az deneyimliyim.

Uygulamanın adı: **LingoCue**.

## Ürün
Videolarımda konuşma dili şu an Türkçe. Akış:
1. Kullanıcı bir MP4 (ayrıca MOV/MKV) yükler (dosya seçici veya sürükle-bırak).
2. Uygulama sesi ayıklar, Türkçe konuşmayı kelime düzeyinde zaman damgasıyla
   tanır (kaynak dil varsayılan tr, otomatik algılama açılabilir).
3. Kelimeleri okunabilir altyazı satırlarına böler (segmentasyon).
4. LLM düzeltme geçişi: zamanlamaya dokunmadan ASR hatalarını, özel isimleri,
   noktalamayı ve yazımı düzeltir -> Türkçe altyazı izi.
5. Videonun önizlemesinde altyazı canlı ve stiliyle görünür; satırlar tabloda
   düzenlenebilir, zamanlama ayarlanabilir.
6. Altyazı SRT (Premiere Pro'ya aktarmak için), VTT ve ASS olarak ayrı alınabilir;
   ayrıca videoya gömülü (burn-in) MP4 ve soft-sub MP4 üretilebilir.
7. Düzenlenmiş Türkçe iz LLM ile İngilizceye tutarlı biçimde çevrilir (ayrı iz,
   aynı zamanlama).
8. Mimari dilden bağımsız olsun: ileride başka dilde konuşulan bir video gelirse
   aynı hat "kaynak dil -> Türkçe -> İngilizce" olarak çalışabilmeli.
9. Zengin stil seçenekleri: font, boyut, kalınlık, italik, renkler, kontur, gölge,
   arka plan kutusu, konum/hizalama, kenar boşlukları, hazır şablonlar (YouTube,
   Netflix tarzı, Shorts 9:16), kelime kelime vurgulama (karaoke), iki dilli mod
   (TR üstte, EN altta).

## Teknoloji kararları (değiştirmeden önce bana sor)
- C# / .NET 10, Avalonia UI (son kararlı sürüm), MVVM için CommunityToolkit.Mvvm,
  Microsoft.Extensions.DependencyInjection/Logging/Configuration, Serilog.
- Central Package Management (Directory.Packages.props), nullable enable,
  TreatWarningsAsErrors, .editorconfig, xUnit + FluentAssertions (veya Shouldly).
- Hedef platformlar: win-x64 ve osx-arm64 (Intel Mac yok).
- Harici bir API'yi veya NuGet paketini kullanmadan önce güncel dokümantasyonunu
  kontrol et; eğitim verindeki API şekilleri ve model adları eskimiş olabilir.
- Video önizleme: öncelik libmpv (ASS'yi libass ile render eder, Subtitle Edit 5
  de bunu kullanıyor). Yedek: LibVLCSharp.Avalonia. M1'de kısa bir spike yap,
  sonucu docs/adr/0001-video-player.md olarak yaz, sonra karar ver.
- Önizleme = çıktı ilkesi: uygulama içi tek doğru kaynak ASS stil modelidir.
  Önizlemede geçici ASS dosyası mpv'ye yüklenir; burn-in FFmpeg'in ass/subtitles
  filtresiyle aynı ASS'den yapılır. Fontlar için fontsdir kullan.
- Medya: FFmpeg/ffprobe harici process olarak (-progress pipe:1 ile ilerleme).
  Windows'ta ffmpeg yolunu bul veya ilk çalışmada indir; macOS'ta .app içine
  gömülü veya Homebrew yolu. H.264 kodlamada varsayılan -preset veryfast, CRF 18-20,
  ses kopyalanır. Windows'ta NVENC (h264_nvenc) hızlı render seçeneği olsun.
- ASR: ITranscriptionEngine arayüzü ve iki motor:
  a) WhisperNetEngine (Whisper.net, yerel): Windows'ta Whisper.net.Runtime.Cuda
     (CUDA bulunamazsa Vulkan, sonra CPU), macOS'ta CoreML. Türkçe için varsayılan
     model ggml-large-v3, hız seçeneği large-v3-turbo. Silero VAD. Model ilk
     kullanımda indirilir, ilerleme gösterilir. CUDA çalışmıyorsa kullanıcıya ne
     kurması gerektiğini net söyle.
  b) GeminiTranscribeEngine (bulut): Gemini 3.5 Transcribe, kelime zaman damgalı
     çıktı + konuşmacı ayrımı. Uzun videolarda parçalara böl, zamanları birleştir.
  Genel Gemini sohbet modellerinden (Flash/Pro) asla zaman damgası isteme; uzun
  seste kayma yapıyorlar. Zamanlama sadece ASR motorundan gelir.
  ElevenLabs Scribe ileride eklenecek üçüncü motor (şimdilik yok).
  M3 sonunda aynı Türkçe videoda iki motoru karşılaştıran kısa bir rapor üret
  (süre, maliyet, örnek fark satırları) ve varsayılanı bana sor.
  ASR sonucu (kelimeler) proje dosyasında cache'lenir.
- Segmentasyon kuralları (ayarlanabilir): satır başı maks 42 karakter, maks 2 satır,
  cue süresi 0.8-7 sn, okuma hızı hedefi ~17 CPS, noktalama ve >400 ms duraklamada
  böl, konuşmacı değişiminde böl, Türkçe'de sıfat-isim / isim-tamlaması ortasında
  bölme.
- LLM: varsayılan sağlayıcı Google Gemini (anahtarım var). Resmi Google Gen AI
  .NET SDK'sını veya REST API'yi kullan; Microsoft.Extensions.AI IChatClient
  arayüzüne adapte et ki Claude, OpenAI ve Ollama sonradan eklenebilsin. Model
  adları koda gömülmez: Gemini model listesini API'den çek, ayarlardan seçtir
  (varsayılan: en güncel kararlı Flash; kalite modu: Pro).
- LLM hattı (düzeltme ve çeviri aynı iskeleti kullanır):
  1) Bağlam geçişi: tüm transkriptten JSON (özet, ton, hitap, konuşmacılar,
     özel isimler, terim önerileri) -> düzenlenebilir proje sözlüğü.
  2) 20-40 satırlık batch'ler, ID anahtarlı JSON giriş/çıkış (structured output),
     önceki batch'in son 3-5 sonucu ve sonraki 2 kaynak satır bağlam olarak.
  3) Doğrulama: ID kümesi eşleşmesi, boş satır, uzunluk/CPS, sözlük uyumu;
     hatalı batch hata açıklamasıyla en fazla 2 kez yeniden denenir, sonra
     işaretlenir.
  4) Opsiyonel "reflect" kalite geçişi ve CPS aşımında anlamı koruyan kısaltma.
  5) TR->EN: kaynak, benim düzenlediğim Türkçe izdir; zamanlama ondan kopyalanır.
  Paralel batch sayısı ayarlanabilir; iptal ve ilerleme desteği; token/maliyet
  tahmini göster; rate limit hatalarında exponential backoff.
- Güvenlik (repo PUBLIC): API anahtarları Windows Credential Manager / macOS
  Keychain'de saklanır. Hiçbir anahtar, .env, örnek gerçek video veya kişisel veri
  repoya commit edilmez; .gitignore bunu garanti etsin, commit öncesi diff'te
  anahtar ara. Testlerde gerçek API anahtarı sadece ortam değişkeninden okunur.
- Türkçe: harf dönüşümleri CultureInfo("tr-TR") ile; dosyalar UTF-8 (SRT için BOM
  opsiyonu); font glyph kontrolü (ğ ş ı İ).
- Paketleme (M9): self-contained publish win-x64 ve osx-arm64, Windows kurulum +
  macOS .app/.dmg, otomatik güncelleme (Velopack değerlendir). Mac imzasız olacağı
  için README'ye quarantine kaldırma adımını yaz.

## Solution yapısı
src/LingoCue.Core, src/LingoCue.Media, src/LingoCue.Transcription,
src/LingoCue.Llm (düzeltme + çeviri hattı), src/LingoCue.Infrastructure, src/LingoCue.App;
tests/LingoCue.Core.Tests, tests/LingoCue.Llm.Tests, tests/LingoCue.Media.Tests;
docs/ (PLAN.md, ARCHITECTURE.md, TESTING-macos.md, adr/); .github/workflows/
(ci.yml, release.yml). Çekirdek kütüphaneler UI'a bağımlı olmaz.
Veri modeli: Project -> Track (dil, Source/Corrected/Translation) -> Cue (id, start,
end, text, speaker, flags) -> Word. Proje dosyası kendi uzantılı JSON (`.lingocue`).

## Yol haritası (her biri bir GitHub Milestone ve release)
v0.1.0 M0 İskelet: public repo, solution, boş pencere, DI/log, CI (windows-latest +
       macos-latest build+test), README, CLAUDE.md, CHANGELOG, LICENSE (MIT).
v0.2.0 M1 Video: açma, ffprobe, oynatıcı spike + ADR, oynat/duraklat/seek.
v0.3.0 M2 Altyazı çekirdeği: model, SRT/VTT/ASS okuma-yazma (round-trip testli),
       cue tablosu, canlı önizleme, proje kaydet/aç.
v0.4.0 M3 Transkripsiyon: ses ayıklama, Whisper.net (CUDA/CoreML) + Gemini 3.5
       Transcribe, segmentasyon, ilerleme/iptal, cache, motor karşılaştırma raporu.
v0.5.0 M4 LLM hattı: Gemini sağlayıcısı, ayarlar + anahtar deposu, sözlük UI,
       Türkçe düzeltme geçişi (çeviri altyapısı da hazır).
v0.6.0 M5 Stil: stil editörü, şablonlar, karaoke, iki dilli mod.
v0.7.0 M6 Dışa aktarma: burn-in render kuyruğu (x264 + NVENC), soft-sub, SRT/VTT/ASS.
v0.8.0 M7 İngilizce: TR->EN çeviri, çoklu iz yönetimi.
v0.9.0 M8 Editör cilası: dalga formu, böl/birleştir, kısayollar, undo/redo,
       QA paneli, bul/değiştir.
v1.0.0 M9 Paketleme ve dağıtım.

## Çalışma kuralları
1. İlk iş: uygulama adını seç; bu promptı docs/PLAN.md olarak kaydet; proje
   kurallarını ve komutları (build, test, run) CLAUDE.md'ye yaz. gh ile PUBLIC repo
   oluştur, milestone'ları ve her milestone için issue'ları aç.
2. Her milestone için önce kısa bir uygulama planı (adımlar + riskler) yaz ve onayımı
   bekle. Sonra feature/mX-... branch'inde çalış.
3. Her küçük, çalışan adım = bir commit (Conventional Commits). Commit öncesi
   dotnet build ve dotnet test yeşil olmalı. Kocaman tek commit yok.
4. Platforma özgü kodu (yol, keychain, ffmpeg, GPU runtime) arayüzlerin arkasına
   koy; OperatingSystem.IsWindows()/IsMacOS() kontrollerini tek yerde topla.
   Dosya yollarında Path.Combine kullan, macOS'un büyük/küçük harf duyarlı
   olabileceğine dikkat et.
5. Milestone sonunda: CHANGELOG güncelle, docs/TESTING-macos.md'ye o sürüm için
   adım adım Mac test listesi ekle, PR aç, benim onayımla merge et, tag at
   (vX.Y.Z), release.yml ile Windows ve macOS (arm64) paketlerini GitHub
   Release'e yükle.
6. Bir karar belirsizse veya geri alınması zorsa dur ve sor; küçük detaylarda makul
   varsayılanı seç, PR açıklamasında belirt.
7. Testler: Core için birim testleri (format round-trip, segmentasyon, zaman
   aritmeti), Llm için sahte IChatClient ile hat testleri, Media için kısa örnek
   videoyla entegrasyon testi (örnek video testte FFmpeg ile üretilsin, repoya büyük
   dosya koyma). Gerçek API çağrısı yapan testler ortam değişkeni yoksa atlansın.
8. UI: koyu/açık tema, sade ve profesyonel; ana ekran = solda video + altında zaman
   çizelgesi/dalga formu, sağda cue tablosu, üstte araç çubuğu (Transkribe Et,
   Düzelt, Çevir, Stil, Dışa Aktar), uzun işler için alt durum çubuğunda ilerleme +
   iptal. Uzun işler UI thread'ini asla kilitlemez (async + CancellationToken).
