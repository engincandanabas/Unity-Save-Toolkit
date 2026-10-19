# Unity-Save-Toolkit

Mobil ve PC/Steam oyunları için ScriptableObject ile yapılandırılan, düşük allocation'lı kayıt sistemi. Unity paketi (UPM) olarak dağıtılır.

## Kurulum

Package Manager → **+** → **Add package from git URL**:

```
https://github.com/engincandanabas/Unity-Save-Toolkit.git?path=Packages/com.engincandanabas.savetoolkit#v0.2.0
```

Alternatif olarak `Builds/` altındaki `.tgz` dosyası Package Manager'da **Add package from tarball** ile, `.unitypackage` dosyası da **Assets → Import Package** ile kurulabilir. Paketleri üretmek için:

```bash
python Tools/build_packages.py
```

Dokümantasyon: [Packages/com.engincandanabas.savetoolkit/README.md](Packages/com.engincandanabas.savetoolkit/README.md)

## Bu repo

Bu repo, paketin geliştirme projesidir:

- `Packages/com.engincandanabas.savetoolkit/`: paketin kendisi (embedded package).
- `Assets/SaveToolkit/Settings/`: geliştirme projesinin `SaveSettings` asset'i ve profilleri.
- `Assets/Packages/`: NuGetForUnity ile kurulmuş MessagePack. MessagePack modülünü test etmek için kullanılır.
- `Assets/Scenes/Main.unity`: demo sahnesi. Önce Package Manager → Save Toolkit → **Samples → Basic → Import** yapılmalıdır.

Yeni sürüm yayınlarken `package.json` içindeki `version` alanını ve `CHANGELOG.md`'yi güncelleyin, ardından `vX.Y.Z` etiketini push edin.
