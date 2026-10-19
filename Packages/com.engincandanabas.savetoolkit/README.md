# Save Toolkit

Mobil (iOS/Android) ve PC/Steam oyunları için ScriptableObject ile yapılandırılan, düşük allocation'lı kayıt sistemi.

- **Profiller:** Editor, Mobile, Desktop ve mağaza (Steam/Epic/itch…) için ayrı `SaveProfile` asset'leri tanımlanır. Aktif profil çalışma anında otomatik seçilir.
- **Format:** JSON (Newtonsoft) yerleşik gelir. MessagePack opsiyonel bir modüldür ve projede MessagePack varsa kendiliğinden açılır.
- **Güvenlik:** GZip sıkıştırma ve AES-256 + HMAC-SHA256 ile kurcalama tespiti.
- **Dayanıklılık:**
  - Atomik yazma.
  - Dönüşümlü backup'lar. Ana dosya `File.Replace` ile bak1'e taşınır, kopyalama yapılmaz.
  - Bozuk kayıtta otomatik olarak yedekten kurtarma.
- **Sürüm ve slotlar:** `DataVersion` ile migration ve çoklu kayıt slotları.
- **Async ve autosave:** async kayıt/yükleme; pause, focus kaybı, çıkış ve belirli aralıklarla autosave.
- **Performans:** Pipeline'ın her aşaması `ArrayPool` tabanlı pooled buffer'larla çalışır. Senkron yolda closure yoktur. Debug log kapalıyken log string'i üretilmez.

## Kurulum

Package Manager → **+** → **Add package from git URL**:

```
https://github.com/engincandanabas/Unity-Save-Toolkit.git?path=Packages/com.engincandanabas.savetoolkit#v0.2.0
```

Bağımlılık olarak yalnızca `com.unity.nuget.newtonsoft-json` paketi gelir ve otomatik kurulur. Minimum Unity sürümü 2022.3'tür.

Diğer kurulum yolları (dosyalar GitHub release'inde ya da `python Tools/build_packages.py` ile `Builds/` altında üretilir):
- **UPM tarball:** `com.engincandanabas.savetoolkit-0.2.0.tgz` → Package Manager → **Add package from tarball**. Newtonsoft bağımlılığı otomatik kurulur.
- **.unitypackage:** `SaveToolkit-0.2.0.unitypackage` → **Assets → Import Package**. Dosyalar `Assets/SaveToolkit` altına kopyalanır. Bu yöntem bağımlılık taşımadığı için `com.unity.nuget.newtonsoft-json` paketini kendiniz kurmalısınız. Paket Manager tarafından yönetilmediği için güncellemeleri elle yapmanız gerekir.

**MessagePack (opsiyonel):** MessagePack'i [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity) ile kurun (`MessagePack`). Save Toolkit `SAVETOOLKIT_MESSAGEPACK` define'ını kendisi ekler ve `SaveToolkit.MessagePack` modülü derlenir. Bu davranışı `Tools/Save Toolkit/MessagePack Module` menüsünden kapatabilir veya elle yönetebilirsiniz.

### İlk adımlar
1. `Tools/Save Toolkit/Create Default Settings` menüsü `Assets/SaveToolkit/Settings/Resources/SaveSettings.asset` ve üç profil (Editor, Mobile, Steam) oluşturur. Hiçbir asset oluşturmasanız da sistem çalışır: `SaveController` yerleşik bir varsayılan profille (JSON dosyalar + backup) açılır ve bunu bir uyarıyla bildirir.
2. Mobile profilinde şifreleme açıksa **`EncryptionPassphrase` ve `EncryptionSalt` değerlerini oyununuza özel değerlerle değiştirin.** Oyun yayınlandıktan sonra bu değerleri değiştirmeyin; değişirse mevcut şifreli kayıtlar açılamaz.
3. Örnekler için: Package Manager → Save Toolkit → **Samples → Basic → Import**.

## Kullanım

```csharp
using SaveToolkit;

SaveController.Instance.Save("player_progress", progress, out string error);
SaveController.Instance.TryLoad("player_progress", out PlayerProgress loaded, out error);

SaveResult result = await SaveController.Instance.SaveAsync("player_progress", progress);
SaveResult<PlayerProgress> load = await SaveController.Instance.LoadAsync<PlayerProgress>("player_progress");
if (load.NotFound) { /* yeni oyun */ }

SaveController.Instance.SetActiveSlot(1, out error);
SaveController.Instance.TryGetSlotInfo(1, out SaveSlotInfo info, out error);
```

`TryLoad` başarılı olduğunda `error` normalde `null` olur. Veri bir yedekten kurtarıldıysa `error` alanında bilgi notu döner. Uygulama kapanırken `SaveController.Instance` `null` döner; bu, kapanış sırasında yeni bir obje yaratılmasını önler.

### Autosave ve ISaveable

```csharp
public sealed class PlayerHealth : SaveableBehaviour<PlayerHealth.State>
{
    public sealed class State { public int Hp; }   // MessagePack + IL2CPP için: [MessagePackObject(true)]

    private int hp = 100;

    protected override State CaptureState() => new State { Hp = hp };
    protected override void RestoreState(State state) => hp = state.Hp;
}
```

Inspector'dan bir `saveKey` verin. Bileşen etkin olduğu sürece `SaveAll` / `LoadAll` çağrılarına ve autosave'e dahil olur. `ISaveable` olmayan veriler için `SaveController.AutosaveRequested` event'ini dinleyin.

| Tetikleyici | Profil alanı | Not |
|---|---|---|
| Uygulama arka plana alındığında | `AutosaveOnPause` | Senkron çalışır; mobil işletim sistemi uygulamayı her an kapatabilir |
| Odak kaybı | `AutosaveOnFocusLost` | Pause ile art arda gelirse tek kayıt yapılır |
| Çıkış | `AutosaveOnQuit` | Desktop |
| Belirli aralık | `AutosaveIntervalSeconds` | Async çalışır |

### Migration

```csharp
SaveController.Instance.RegisterMigration(new SaveMigration("player_progress", 1, token =>
{
    token["Coins"] = token["Gold"];
    ((JObject)token).Remove("Gold");
    return token;
}));
```

Profildeki `DataVersion`'ı artırın ve önceki sürümden bir migration kaydedin. Migration'lar ilk yüklemeden önce kaydedilmelidir. `SaveMigration.AnyKey` (`"*"`) tüm anahtarlara uygulanır.

## Profil seçimi

Profil şu sırayla belirlenir:
1. `OverrideProfile` atanmışsa her platformda o kullanılır.
2. Editor'da `EditorProfile` kullanılır.
3. `StoreProfiles` listesinde define'ı aktif olan **ilk** giriş kullanılır.
4. Platform profili kullanılır: Android ve iOS için `MobileProfile`, Windows, macOS ve Linux build'leri için `DesktopProfile`.
5. Hiçbiri yoksa `FallbackProfile` kullanılır.

**Mağaza profilleri** scripting define'larla eşlenir (`STEAM → Profile_Steam`, `EPIC → Profile_Epic`). Define'lar yalnızca derleme zamanında var olduğu için bir build hook'u, build öncesinde aktif define'ı `SaveSettings`'e yazar ve build bittikten sonra temizler. `BuildPlayerOptions.extraScriptingDefines` ile verilen define'lar bu hook tarafından görülmez; mağaza define'larını Player Settings'te tanımlayın. `SaveSettings` inspector'ındaki **Resolved Profiles** tablosu, her platform ve mağaza için hangi profilin kullanılacağını gösterir.

## Performans notları

### Unity 6000.3 Editor (Mono), Windows, backup açık, `FlushToDisk` açık
Ölçümler 200 iterasyonun ortalamasıdır; GC değerleri `GC Allocated In Frame` sayacından alınmıştır.

| Senaryo | Kayıt (alloc) | Kayıt (süre) | Yükleme (alloc) | Yükleme (süre) | Dosya |
|---|---:|---:|---:|---:|---:|
| JSON, küçük DTO | 14.2 KB | 3.6 ms | 6.3 KB | 0.40 ms | 83 B |
| JSON, 10 KB veri | 29.2 KB | 6.2 ms | 35.9 KB | 2.4 ms | 7.6 KB |
| MessagePack, 10 KB veri | 13.6 KB | 4.9 ms | 18.6 KB | 1.7 ms | 4.9 KB |
| MessagePack + GZip + AES, 10 KB | 19.2 KB | 10.1 ms | 44.0 KB | 1.2 ms | 1.2 KB |

Pipeline'ın kendi maliyeti küçüktür: JSON serialize ~0.4 KB, envelope ~0.1 KB, buffer ~0.03 KB. Allocation'ın büyük bölümü Mono'nun dosya API'lerinden gelir:

| Çağrı | Ayrılan | Süre |
|---|---:|---:|
| `FileStream` açma | ~4.6 KB | ~0.5 ms |
| `File.Replace` | ~3.3 KB | ~2 ms |
| `File.Move` | ~2.2 KB | — |
| `Directory.CreateDirectory` | ~2.4 KB | — |
| `File.Exists` | ~1 KB | — |

`FileSaveStorage` bu yüzden bilinen dosyaları ve oluşturulmuş klasörleri önbelleğe alır. Bunlar, kayıt dosyası olmadan kaçınılamayan maliyetlerdir.

### .NET 8 (pipeline karşılaştırması, v0.1 → v0.2)

| Senaryo | v0.1 kayıt | v0.2 kayıt | v0.1 yükleme | v0.2 yükleme |
|---|---:|---:|---:|---:|
| JSON, küçük DTO | 17.5 KB | 1.4 KB | 6.3 KB | 0.55 KB |
| MessagePack, 10 KB veri | 29.6 KB | 1.0 KB | 28.6 KB | 13.5 KB |

### Öneriler
- **Sık kayıt yapılan veriler:** MessagePack kullanın. Kayıtları tek anahtarda birleştirin (`SaveAll` her `ISaveable` için ayrı dosya yazar).
- **`BackupCount = 1`:** Rotasyondaki ek `File.Move`'u ortadan kaldırır (~2 KB ve ~1 ms).
- **`FlushToDisk`:** Kapatıldığında fsync atlanır (Windows'ta ~0.7 ms). Elektrik kesintisi riskini kabul ediyorsanız kapatın.
- **PlayerPrefs:** Yalnızca string tuttuğu için her işlemde Base64 string'i ayırır. Mümkünse dosya storage'ı kullanın.
- **Büyük kayıtlar:** `ArrayPool` ~1 MB üzerindeki buffer'ları havuzlamaz. Çok büyük kayıtları birden fazla anahtara bölün.

## Platform notları

### Mobil (IL2CPP)
- MessagePack ile kaydedilen her tipte `[MessagePackObject]` olmalıdır. Private setter kullanılıyorsa tip `partial` da olmalıdır. Editor bu durumda bir kez uyarı verir.
- Paket assembly'leri `[assembly: Preserve]` ile stripping'e karşı korunur. Kendi kayıt tiplerinizi `[Preserve]` ile işaretleyin ya da `Assets` altındaki bir `link.xml`'e ekleyin.

### Steam Cloud (Auto-Cloud)
Kayıtlar `Application.persistentDataPath/<SaveDirectory>/slot_N/*.sav` altına yazılır.

| Root | Subdirectory | Pattern | OS |
|---|---|---|---|
| `WinAppDataLocalLow` | `<Company>/<Product>/Saves` | `*.sav*` (recursive) | Windows |
| `MacAppSupport` | `unity.<Company>.<Product>/Saves` | `*.sav*` (recursive) | macOS |
| `LinuxXdgDataHome` | `unity3d/<Company>/<Product>/Saves` | `*.sav*` (recursive) | Linux |

Şifreleme anahtarı cihazdan bağımsız olduğu için kayıtlar başka bir makinede de açılır. Bulut storage eklemek için `ISaveStorage` arayüzünü implement edip `new SaveManager(options, storage)` ile kullanın.

## Genişletme
- **Özel format:** `SaveSerializerRegistry.Register(format, options => new MySerializer())`. `ISaveSerializer`, sonucu `IBufferWriter<byte>`'a yazar ve `ReadOnlyMemory<byte>`'tan okur.
- **Özel storage:** `ISaveStorage` arayüzünü implement edin. `Write`, header ve payload'u ayrı span'lar olarak alır ve isteğe bağlı bir backup yolu kabul eder.

## Dosya formatı
Her kayıt 32 byte'lık bir başlıkla başlar: `STK2` magic, format, sıkıştırma, şifreleme bayrağı, `DataVersion`, payload uzunluğu ve SHA-256 checksum. Payload başlığın ardından gelir. Okuma tarafı ayarları profilden değil başlıktan alır; bu yüzden profil değişse bile eski kayıtlar okunur. Bozuk kayıtlar silinmez, incelenebilmeleri için `.corrupt` uzantısıyla kenara alınır.

## Testler
Paket testlerini görmek için projenin `Packages/manifest.json` dosyasına şunu ekleyin:

```json
"testables": ["com.engincandanabas.savetoolkit"]
```

Ardından `Window → General → Test Runner → EditMode` üzerinden çalıştırın. MessagePack testleri yalnızca modül aktifken görünür.
