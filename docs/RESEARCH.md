# AI Altyazı Çevirmeni — Araştırma, Plan ve Claude Code Promptu

Nov 18, 2026 · @Enes Karamanoğlu

## Netleşen kararlar (18 Kasım 2026)

Bu kararlar aşağıdaki bölümlerde ve 7. bölümdeki promptta işlendi:

- **Konuşma dili şu an Türkçe.** Hat: Türkçe ses → **Türkçe transkript + LLM ile düzeltme** (çeviri yok) → düzenleme → **İngilizce çeviri**. Başka dilde video gelirse diye "Türkçeye çevir" adımı kodda kalır ama ikinci planda.
- **Windows: NVIDIA GPU var** → yerel Whisper, CUDA hızlandırması ile varsayılan motor olur. Türkçe için `large-v3` (turbo modeli İngilizce dışı dillerde biraz daha fazla isabet kaybediyor), hız gerekirse `large-v3-turbo`.
- **Mac: Apple Silicon** → yalnızca `osx-arm64` paketi, Whisper için CoreML. Intel Mac desteği plandan çıkarıldı.
- **Repo public** → MIT lisans, API anahtarı sızıntısına karşı ekstra önlem (gitignore + GitHub secret scanning). Public repoda GitHub Actions macOS runner'ları ücretsiz, bu Mac derlemesi için avantaj.
- **Uygulama adını Claude Code belirleyecek** (dokümandaki "SubForge" sadece örnek ad).
- **Sağlayıcı: Gemini.** Bulut ASR için **Gemini 3.5 Transcribe** (kelime zaman damgası veriyor), düzeltme ve İngilizce çeviri için güncel **Gemini Flash** (kalite modunda Pro). Model listesi API'den çekilip ayarlardan seçilir. ElevenLabs / Claude / OpenAI arayüz hazır kalır, gerektiğinde eklenir.
- **Gemini'nin genel sohbet modellerinden zaman damgası istenmez:** uzun seste zaman kayması yapıyorlar. Zamanlama ya Whisper'dan ya da Gemini 3.5 Transcribe'ın kelime damgalarından gelir; Flash/Pro sadece metne dokunur.

## 1. Özet ve teknoloji kararı

**Ne yapıyoruz:** MP4 yüklenen, konuşmayı sesten tanıyıp zaman damgalı altyazıya çeviren, bunu Türkçeye (sonra İngilizceye) yapay zekâ ile tutarlı biçimde çeviren, altyazıyı videonun önizlemesinde canlı gösteren, stil verilebilen, SRT/ASS olarak dışa aktarılabilen (Premiere Pro'ya aktarmak için) ve istenirse videoya gömülü (burn-in) MP4 üreten bir masaüstü uygulaması. Windows'ta geliştirilecek, macOS'ta GitHub'dan çekilip test edilecek.

**Karar: C# / .NET 10 + Avalonia UI (MVVM).** Gerekçeler:

- **Senin uzmanlığın:** zaten C# backend geliştiriyorsun; Rust (Tauri) ya da Python (PyQt) öğrenme maliyeti yok, mimariyi (katmanlar, NuGet paketleri, DI) bildiğin gibi kurarsın.
- **Kanıtlanmış emsal:** Subtitle Edit 5 tam olarak bu yığına geçti; C#, .NET 10 ve Avalonia ile Windows/macOS/Linux'ta tek kod tabanından çalışıyor, video için libmpv ve FFmpeg kullanıyor. Yani bu tür bir altyazı editörünün bu teknolojiyle yapılabildiği sahada kanıtlı.
- **Tek kod tabanı, gerçek native uygulama:** self-contained publish ile kullanıcıda .NET kurulumu gerekmez; win-x64 ve osx-arm64 (+ istenirse osx-x64) için ayrı paket çıkar.
- **Ekosistem hazır:** Whisper.net (yerel konuşma tanıma, CUDA / CoreML), Microsoft.Extensions.AI (tek arayüzle Claude, OpenAI, Gemini, Ollama), FFmpeg sarmalayıcıları, libmpv/LibVLC bağları.

**Değerlendirilen alternatifler:**

| Seçenek | Artı | Eksi | Sonuç |
| --- | --- | --- | --- |
| **.NET 10 + Avalonia** | Senin dilin, SE5 emsali, native performans | Video gömme (libmpv) biraz el emeği ister | **Seçildi** |
| Tauri 2 + React/TS | Modern web UI, küçük paket | Rust + TS iki dil; video önizleme ile ASS stilini birebir eşlemek zor | Yedek |
| Electron | Çok hızlı UI geliştirme | Ağır (150 MB+), video/altyazı yine FFmpeg'e muhtaç | Hayır |
| Python + PySide6 | Whisper/ML kütüphaneleri zengin (VideoCaptioner böyle) | Paketleme ve macOS dağıtımı sancılı, senin ana dilin değil | Hayır |
| .NET MAUI | Microsoft resmi | macOS'ta Mac Catalyst, masaüstü olgunluğu Avalonia'nın gerisinde | Hayır |

**Kritik tasarım fikri — WYSIWYG altyazı:** Önizleme oynatıcısı (libmpv) ve gömülü çıktı (FFmpeg `subtitles`/`ass` filtresi) **ikisi de libass** ile ASS dosyasını render eder. Uygulama içi tek doğru kaynak bir ASS stil modeli olursa, önizlemede ne görüyorsan render edilen MP4'te de birebir onu alırsın. SRT ise stil taşımadığı için yalnızca metin + zaman olarak (Premiere için) dışa aktarılır.

## 2. Araştırma: benzer projeler ve alınacak dersler

| Proje | Ne yapıyor | Bizim için ders |
| --- | --- | --- |
| **Subtitle Edit 5** (C#, Avalonia, MIT) | 300+ format, dalga formu, Whisper ile otomatik transkripsiyon, çeviri, OCR; v5.0 ile ilk kez native Windows/macOS/Linux | Mimari referans: libmpv + FFmpeg'i uygulamaya gömme, macOS'ta self-contained .app, Whisper.cpp motor seçenekleri (CPU, cuBLAS, Vulkan). MIT lisanslı olduğu için kod okunup fikir alınabilir. Format okuma/yazma için kendi `libse` kütüphanesi incelenmeli. |
| **VideoCaptioner** (Python, GPL-3) | Uçtan uca: ASR → LLM ile cümle bölme → LLM düzeltme → çeviri → videoya gömme; kelime düzeyi zaman damgası + VAD; ASS stil şablonları | Hattın (pipeline) doğru sırası bu. Özellikle **LLM ile yeniden segmentasyon** ve "reflective" (çevir-eleştir-düzelt) çeviri modu kalite için önemli. GPL olduğu için kod kopyalanmaz, sadece yaklaşım alınır. |
| **LLM-Subtrans / PySubtrans** (Python) | SRT'yi sahne/batch'lere bölüp LLM'e çevirtiyor; OpenAI, Gemini, Claude, DeepSeek destekli | Tutarlılığın anahtarı: altyazıyı **sahne + küçük batch** halinde, önceki çevirilerden özet/bağlam taşıyarak çevirmek; satır sayısı eşleşmesini doğrulamak. |
| **TransLora, subtitle-translator, chatgpt-subtitle-translator** | Batch'li LLM çevirisi, bağlam satırları, blok düzeyinde doğrulama, paralellik | Varsayılanlar: batch 10–30 satır, önceki 2–5 satır salt-okunur bağlam, ID bazlı birebir eşleme; tutmazsa yeniden dene. |
| **Aegisub** | Klasik ASS stil/zamanlama editörü | Stil editörü ve zamanlama UX'i için ilham (stil yöneticisi, karaoke). |
| **Buzz / WhisperX** | Yerel Whisper arayüzü / kelime hizalama | Yerel mod ve kelime hizalama ihtiyacı için referans. |

**Bu projelerden çıkan ortak sonuç:** "ses → metin → çeviri" tek adımda yapılınca tutarsızlık çıkıyor. Kaliteyi belirleyen üç şey var: (1) kelime düzeyinde zaman damgası, (2) okunabilir altyazı satırlarına akıllı bölme, (3) bağlamlı, sözlüklü, doğrulamalı batch çeviri. Planın geri kalanı bu üç şey etrafında kuruldu.

## 3. Araştırma: konuşma tanıma (ASR) motorları

Altyazı için ASR'den beklenen tek şey doğru metin değil; **kelime düzeyinde zaman damgası** şart, çünkü satırları biz böleceğiz. Bu yüzden motorlar bu kritere göre elendi.

| Motor | Kelime zaman damgası | Dil | Maliyet / çalışma | Durum |
| --- | --- | --- | --- | --- |
| **ElevenLabs Scribe v2** (bulut) | Var, varsayılan | 90+ dil, konuşmacı ayrımı (diarization), anahtar terim ipucu | Ses saati başına \~0,20–0,22 $; 3 GB / 10 saate kadar dosya | **Şimdilik yok; ileride eklenebilecek üçüncü motor (bulutta varsayılan artık Gemini 3.5 Transcribe).** ElevenLabs fiyat sayfasında daha da isabetli Scribe v3 de görünüyor; model ID'si ayardan seçilebilir olmalı. |
| **Whisper.net** (yerel, whisper.cpp) | Var (token/kelime düzeyi) | 99 dil | Ücretsiz, internetsiz. Windows'ta NVIDIA CUDA / Vulkan, macOS'ta CoreML (Apple Silicon) hızlandırma; CPU yedek | **Varsayılan yerel motor.** Model: `ggml-large-v3-turbo` (hızlı) veya `large-v3` (daha isabetli). Silero VAD ile sessizlik/halüsinasyon azaltılır. |
| OpenAI `gpt-transcribe` | **Yok** | Çok dilli | \~0,0045 $/dk | Altyazı için uygun değil. OpenAI'ın zaman damgası veren `whisper-1` ve `gpt-4o-transcribe` ailesi **26 Şubat 2027'de kapanıyor**; bu yüzden OpenAI ASR'ye bağımlı olunmamalı. |
| Deepgram Nova-3, AssemblyAI, Soniox | Var | Çok dilli | Ucuz, hızlı | İleride eklenebilecek opsiyonel motorlar (arayüz hazır olsun yeter). |

**Seçim (güncellendi):** `ITranscriptionEngine` arayüzü + iki varsayılan motor: **Whisper.net yerel** (Windows'ta NVIDIA CUDA, Mac'te CoreML; ücretsiz, Türkçe için `large-v3`) ve **Gemini 3.5 Transcribe** (bulut, kelime zaman damgalı, konuşmacı ayrımlı; Gemini anahtarın zaten var). M3'te aynı Türkçe videoda ikisi karşılaştırılıp varsayılan seçilir. ElevenLabs Scribe daha sonra eklenebilecek üçüncü motor. Kaynak dil varsayılan olarak Türkçe'ye sabitlenir (otomatik algılama açılabilir).

**ASR sonrası işleme (kalitenin asıl olduğu yer):**

1. FFmpeg ile sesi ayıkla: 16 kHz, mono, WAV (Whisper için) / sıkıştırılmış (bulut için boyut düşürmek amacıyla).
2. Ham kelime listesini (metin, başlangıç, bitiş, konuşmacı, güven) proje dosyasına **cache'le** — tekrar çevirmek için yeniden ücret ödeme.
3. **Segmentasyon:** kelimeleri okunabilir altyazı satırlarına böl. Kurallar (ayarlanabilir): satır başı maks. 42 karakter, maks. 2 satır, süre 0,8–7 sn, okuma hızı \~17 karakter/sn, noktalama ve duraklamalarda (>400 ms) böl, konuşmacı değişince böl, dilbilgisel olarak kötü yerlerde (sıfat-isim arası gibi) bölme.
4. Opsiyonel **LLM düzeltme** geçişi: ASR hatalarını (özel isim, terim) düzelt, noktalama ekle — zamanlamaya dokunmadan.

## 4. Tutarlı çeviri stratejisi

"Oldukça tutarlı" çeviri için satır satır değil, **bağlamlı ve doğrulamalı bir hat** kuruyoruz:

1. **Bağlam geçişi (1 çağrı):** Tüm transkript LLM'e verilir; çıktı JSON: konu özeti, ton/hitap (sen/siz), konuşmacılar, özel isimler, terimler ve önerilen karşılıklar. Bu, düzenlenebilir bir **proje sözlüğü (glossary)** olarak UI'da gösterilir; kullanıcının onayladığı terimler kilitlenir.
2. **Batch çeviri:** 20–40 satırlık batch'ler; her istekte sistem promptu + sözlük + özet + önceki batch'in son 3–5 çevrilmiş satırı (salt-okunur bağlam) + sonraki 2 satırın kaynağı. Girdi/çıktı **ID anahtarlı JSON** (structured output), böylece satır kayması imkansız hale gelir.
3. **Doğrulama:** ID kümesi birebir aynı mı, boş satır var mı, satır uzunluğu ve okuma hızı limitleri aşılıyor mu, sözlük terimleri doğru kullanılmış mı. Başarısız batch hata mesajıyla birlikte yeniden denenir (maks. 2), yine olmazsa işaretlenir.
4. **Opsiyonel inceleme geçişi ("reflect"):** ikinci bir çağrı çeviriyi akıcılık/doğruluk açısından eleştirip düzeltir. Kalite modu açıkken çalışır.
5. **Altyazıya uygun kısaltma:** çeviri okuma hızını aşıyorsa LLM'den anlamı koruyarak kısaltması istenir (altyazı çevirisi birebir çeviri değildir).

**Türkçe → İngilizce adımı:** Kaynak olarak senin **düzenlediğin Türkçe iz** esas alınır (otorite sensin), ama orijinal konuşma dili metni de referans olarak prompta eklenir; böylece pivot çeviride anlam kaybolmaz. Zamanlamalar Türkçe izden kopyalanır, İngilizce iz ayrı bir track olarak tutulur.

**Sağlayıcılar:** `Microsoft.Extensions.AI` üzerinden tek `IChatClient` arayüzü; Anthropic (Claude), OpenAI, Google Gemini ve yerel Ollama. Model adları koda gömülmez, ayarlardan seçilir (varsayılan: güncel Gemini Flash, kalite modunda Gemini Pro; Claude, OpenAI ve Ollama arayüz üzerinden sonra eklenir). API anahtarları işletim sisteminin güvenli deposunda saklanır (Windows Credential Manager / macOS Keychain), asla düz dosyada veya repoda.

**Türkçeye özgü tuzaklar:** büyük/küçük harf dönüşümleri `CultureInfo("tr-TR")` ile yapılmalı (i/İ, ı/I); SRT UTF-8 yazılmalı (Premiere uyumu için BOM opsiyonu); seçilen fontun ğ, ş, ı, İ glyph'lerini içerdiği kontrol edilmeli.

## 5. Mimari, solution yapısı ve stil seçenekleri

Çalışma adı: **SubForge** (istediğin adı koyarsın; prompttaki adı değiştirmen yeterli).

**Solution yapısı** (katmanlı, UI'dan bağımsız çekirdek — monolit-modüler ayrımı alışkın olduğun şekilde):

```
SubForge.sln
├─ src/
│  ├─ SubForge.Core            # Domain: Project, Track, Cue, Word, SubtitleStyle; SRT/VTT/ASS okuma-yazım; segmentasyon; QA kuralları
│  ├─ SubForge.Media           # FFmpeg/ffprobe sarmalayıcı: ses ayıklama, burn-in, soft-sub mux, ilerleme okuma
│  ├─ SubForge.Transcription   # ITranscriptionEngine + ElevenLabsScribeEngine + WhisperNetEngine
│  ├─ SubForge.Translation     # ITranslator, LLM hattı (bağlam, batch, doğrulama, reflect), sözlük
│  ├─ SubForge.Infrastructure  # Ayarlar, güvenli anahtar deposu (Win Credential Manager / macOS Keychain), loglama, model indirme
│  └─ SubForge.App             # Avalonia UI (MVVM: CommunityToolkit.Mvvm), video oynatıcı kontrolü, DI kompozisyonu
├─ tests/
│  ├─ SubForge.Core.Tests      # xUnit: format round-trip, segmentasyon, zaman hesapları
│  ├─ SubForge.Translation.Tests  # sahte IChatClient ile hat testleri
│  └─ SubForge.Media.Tests     # kısa örnek videoyla entegrasyon testleri
├─ docs/  (PLAN.md, ARCHITECTURE.md, TESTING-macos.md, adr/)
├─ .github/workflows/  (ci.yml, release.yml)
├─ CLAUDE.md, README.md, CHANGELOG.md, Directory.Packages.props, global.json
```

**Veri modeli (özet):** `Project` (video yolu, meta, iz listesi, stil, sözlük, ASR cache) → `Track` (dil, tür: Source / Translation, cue listesi) → `Cue` (id, start, end, text, speaker, durum bayrakları) → `Word` (ASR'den gelen kelime + zaman). Proje dosyası: `.subforge` uzantılı JSON; video yanında saklanır, ASR sonucu içinde cache'lenir.

**Video önizleme:** öncelik **libmpv** (Subtitle Edit 5'in de kullandığı, ASS'yi libass ile render eden oynatıcı). Altyazı değişince geçici bir ASS dosyası yazılıp mpv'ye `sub-reload` ile yüklenir → anlık, birebir önizleme. Yedek plan: LibVLCSharp.Avalonia. Hangisinin Avalonia'ya en sorunsuz gömüldüğü ilk milestone'da kısa bir **spike** ile kararlaştırılır ve ADR olarak yazılır.

**FFmpeg:** Windows'ta uygulama ile birlikte dağıtılan `ffmpeg.exe`/`ffprobe.exe` (ilk çalışmada indirilebilir), macOS'ta .app içine gömülü ya da Homebrew yolu. Process olarak çalıştırılır, `-progress pipe:1` ile ilerleme çubuğu beslenir.

**Altyazı özelleştirme seçenekleri (hepsi ASS stiline eşlenir):**

- Font ailesi (sistem fontları listesi + özel font dosyası ekleme), boyut, kalınlık, italik, altı çizili, harf aralığı, satır aralığı
- Metin rengi, kontur (outline) rengi ve kalınlığı, gölge rengi/mesafesi, saydamlık
- Arka plan kutusu (opak kutu), kutu rengi/opaklığı/iç boşluğu
- Konum: 9 noktalı hizalama, dikey/yatay kenar boşluğu, maksimum satır genişliği, güvenli alan kılavuzları
- Büyük harf modu (tr-TR kültürüyle), maks. satır sayısı, otomatik satır kırma
- **Hazır şablonlar:** YouTube klasik, Netflix tarzı, Shorts/Reels (9:16, büyük kalın ortalı), Belgesel; kendi şablonunu kaydet/paylaş (JSON)
- **Kelime kelime vurgulama (karaoke)**: Shorts için aktif kelimeyi renklendiren animasyonlu stil (ASS `\k` etiketleri; kelime zaman damgaları zaten elimizde)
- **İki dilli mod:** TR üstte, EN altta (farklı boyut/renk) tek videoda
- Fade in/out, cue bazlı stil geçersiz kılma (örn. vurgulanacak bir satır)

**Dışa aktarma:** SRT (Premiere Pro için; her iz ayrı dosya), VTT, ASS (stilli), gömülü MP4 (H.264, CRF 18–20, ses kopyalanır, hızlı preset varsayılan), soft-sub MP4 (TR + EN izleri açılıp kapanabilir).

**Editör:** cue tablosu (zaman, süre, metin, CPS uyarısı), oynatıcıyla senkron seçim, satıra çift tıklayınca oraya atlama, zamanlama kaydırma (±100 ms kısayolla), böl/birleştir, bul/değiştir, geri al/yinele, dalga formu (ses görselleştirme) üzerinde sürükleyerek zamanlama.

## 6. Yol haritası: milestone ve release planı

Her milestone bir **GitHub Milestone + feature branch + PR + tag/release** olur. Her milestone içindeki her küçük adım ayrı commit'tir (Conventional Commits: `feat:`, `fix:`, `chore:`, `docs:`, `test:`). Tag atıldığında `release.yml` Windows ve macOS paketlerini üretip GitHub Release'e ekler; sen Mac'te o release'i (ya da branch'i) çekip `docs/TESTING-macos.md` listesindeki adımları test edersin.

| Sürüm | Milestone | Kapsam | Kabul kriteri |
| --- | --- | --- | --- |
| v0.1.0 | M0 – İskelet | Repo, solution, boş Avalonia penceresi, DI, loglama, CI (windows-latest + macos-latest build+test), README, CLAUDE.md, CHANGELOG | CI iki platformda yeşil; uygulama iki OS'ta açılıyor |
| v0.2.0 | M1 – Video | MP4 açma (dosya seçici + sürükle-bırak), ffprobe meta, oynatıcı spike + karar (ADR), oynat/duraklat/ileri-geri/zaman çubuğu | Video iki OS'ta sesli oynuyor, seek çalışıyor |
| v0.3.0 | M2 – Altyazı çekirdeği | Domain modeli, SRT/VTT/ASS okuma-yazma, cue tablosu, oynatıcıda canlı altyazı, SRT içe/dışa aktar, proje kaydet/aç | Elle yazılan SRT yüklenip düzenlenip geri kaydediliyor; round-trip testleri geçiyor |
| v0.4.0 | M3 – Transkripsiyon | Ses ayıklama, Whisper.net (CUDA/CoreML) ve Gemini 3.5 Transcribe motorları, LLM düzeltme geçişi, model indirici, kelime damgaları → segmentasyon, ilerleme + iptal, ASR cache | 5 dk'lık videodan okunabilir kaynak dil altyazısı çıkıyor |
| v0.5.0 | M4 – LLM hattı (TR düzeltme + çeviri altyapısı) | LLM sağlayıcı soyutlaması, ayarlar + güvenli anahtar deposu, bağlam geçişi, sözlük UI, batch çeviri + doğrulama + reflect | TR izi oluşuyor, satır sayısı eşleşiyor, sözlük terimleri tutarlı |
| v0.6.0 | M5 – Stil | Stil editörü, şablonlar, canlı önizleme, karaoke, iki dilli mod | Önizlemede görülen stil ASS'ye birebir yansıyor |
| v0.7.0 | M6 – Dışa aktarma | Burn-in MP4 render kuyruğu (ilerleme, iptal), soft-sub mux, SRT/ASS/VTT export, Premiere notları | Render edilen MP4 ile önizleme aynı; SRT Premiere'e sorunsuz giriyor |
| v0.8.0 | M7 – İngilizce | TR → EN çeviri (orijinal referanslı), çoklu iz yönetimi, iz seçici | EN izi TR zamanlamasıyla oluşuyor |
| v0.9.0 | M8 – Editör cilası | Dalga formu, böl/birleştir, kısayollar, undo/redo, QA paneli (CPS, uzunluk, çakışma), bul/değiştir, son açılanlar | 10 dk'lık video takılmadan rahatça düzenlenebiliyor |
| v1.0.0 | M9 – Paketleme | Windows kurulum + macOS .app/.dmg (arm64), otomatik güncelleme (örn. Velopack), uygulama ikonu, hata raporlama, dokümantasyon | Temiz makinede kurulup uçtan uca çalışıyor |

**Her milestone sonunda Claude Code'un yapacağı ritüel:** testleri çalıştır → CHANGELOG'u güncelle → `docs/TESTING-macos.md`'ye o sürümün Mac test adımlarını ekle → PR aç → (senin onayınla) merge → tag → GitHub Release.

## 7. Claude Code'a yapıştırılacak ana prompt

Kullanım: Windows'ta boş bir klasör aç, `gh auth login` yapmış ol, .NET 10 SDK ve `ffmpeg` (winget ile) kurulu olsun, `claude` komutunu başlat ve aşağıdaki bloğu olduğu gibi yapıştır. Claude Code bunu `docs/PLAN.md` ve `CLAUDE.md` olarak repoya da kaydedecek; sonraki oturumlarda sadece "M3'e devam et" demen yeterli olur.

```text
Sen kıdemli bir .NET masaüstü geliştiricisisin. Benimle birlikte Windows ve
macOS'ta çalışan, yapay zekâ destekli bir video altyazı uygulaması geliştireceksin.
Ben Windows'ta (NVIDIA GPU'lu) geliştiriyorum; Apple Silicon bir Mac'te repodan
çekip elle test edeceğim. Deneyimli bir C# backend geliştiricisiyim; UI tarafında
daha az deneyimliyim.

Uygulamaya adı sen koy: kısa, akılda kalıcı, GitHub'da ve NuGet'te bariz bir
çakışması olmayan bir ad seç, M0'da gerekçesiyle bildir ve kullan. Aşağıda
APPNAME yazan her yere o ad gelecek.

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
src/APPNAME.Core, src/APPNAME.Media, src/APPNAME.Transcription,
src/APPNAME.Llm (düzeltme + çeviri hattı), src/APPNAME.Infrastructure, src/APPNAME.App;
tests/APPNAME.Core.Tests, tests/APPNAME.Llm.Tests, tests/APPNAME.Media.Tests;
docs/ (PLAN.md, ARCHITECTURE.md, TESTING-macos.md, adr/); .github/workflows/
(ci.yml, release.yml). Çekirdek kütüphaneler UI'a bağımlı olmaz.
Veri modeli: Project -> Track (dil, Source/Corrected/Translation) -> Cue (id, start,
end, text, speaker, flags) -> Word. Proje dosyası kendi uzantılı JSON.

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
1. İlk iş: uygulama adını seç; bu promptı (APPNAME yerine adı yazarak)
   docs/PLAN.md olarak kaydet; proje kurallarını ve komutları (build, test, run)
   CLAUDE.md'ye yaz. gh ile PUBLIC repo oluştur, milestone'ları ve her milestone
   için issue'ları aç.
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

Şimdi M0 ile başla: uygulama adı önerini ve M0 planını göster.
```

## 8. Riskler, maliyetler ve açık sorular

**Riskler ve önlemler**

- **Avalonia'ya video gömme en zor parça.** libmpv'nin render API'si ile OpenGL yüzeyine çizmek ya da native pencere tutamacı vermek platforma göre farklı. Önlem: M1'de spike, çalışmazsa LibVLCSharp; ikisi de sorun çıkarsa önizlemeyi FFmpeg ile çıkarılan kareler + Avalonia üstünde altyazı katmanı ile yapan basit bir yedek mod.
- **macOS dağıtımı:** imzasız uygulamayı Gatekeeper engeller. Kendi kullanımın için `xattr -dr com.apple.quarantine SubForge.app` yeterli; başkalarına dağıtacaksan Apple Developer hesabı (yıllık ücret) ile imzalama + notarization gerekir.
- **Fontlar platformlar arası farklı.** Windows'taki bir font Mac'te olmayabilir; önlem: proje içine font dosyası ekleme ve render'da `fontsdir` kullanma.
- **Lisanslar:** FFmpeg ve mpv'nin GPL derlemeleri kişisel kullanım için sorun değil; uygulamayı ticari/kapalı kaynak dağıtacaksan LGPL derlemeleri seçilmeli. VideoCaptioner GPL olduğu için kodu kopyalanmamalı; Subtitle Edit MIT.
- **Sağlayıcı değişiklikleri:** OpenAI'ın transkripsiyon modellerini kapatması gibi gelişmeler olabiliyor; bu yüzden motorlar ve model adları arayüz + ayar arkasında.
- **Whisper yerel hızı:** Intel Mac'te veya GPU'suz Windows'ta large modeller yavaş; turbo model ve VAD varsayılan olmalı.

**Tahmini maliyet (bulut modunda, 10 dakikalık video):** ASR yerel Whisper (CUDA) ile ücretsiz, Gemini 3.5 Transcribe seçilirse Google'ın güncel fiyatına göre küçük bir ücret; LLM çevirisi birkaç bin token olduğu için model seçimine göre birkaç sent mertebesinde; reflect modu yaklaşık iki katı. Yerel Whisper + Ollama ile tamamen ücretsiz de çalışabilir (kalite düşer).

**Başlamadan netleştirmen iyi olacak sorular** (Claude Code da M0'da soracak):

1. ~~Konuşma dili?~~ → Türkçe.
2. ~~Mac çipi?~~ → Apple Silicon.
3. ~~NVIDIA GPU?~~ → Var.
4. ~~Repo / ad?~~ → Public; adı Claude Code seçecek.
5. ~~API hesapları?~~ → Gemini var; diğerleri gerekirse.

Kalan tek açık nokta: M3 sonunda Türkçe videoda Whisper large-v3 ile Gemini 3.5 Transcribe'ın karşılaştırmasına bakıp varsayılan motoru onaylaman.

**Kaynaklar**

- [Subtitle Edit (GitHub)](https://github.com/SubtitleEdit/subtitleedit) · [Subtitle Edit 5 dokümantasyonu](https://niksedk.github.io/subtitleedit-avalonia/overview.html)
- [VideoCaptioner](https://github.com/WEIFENG2333/VideoCaptioner) · [PySubtrans / LLM-Subtrans](https://pypi.org/project/pysubtrans/)
- [Whisper.net (NuGet)](https://www.nuget.org/packages/Whisper.net)
- [ElevenLabs API fiyatları](https://elevenlabs.io/pricing/api)
- [OpenAI Speech-to-text rehberi ve kapanma tarihleri](https://developers.openai.com/api/docs/guides/speech-to-text)
- [LibVLCSharp.Avalonia (NuGet)](https://packages.nuget.org/packages/LibVLCSharp.Avalonia)
