# Security / Güvenlik

## What the app does / Program ne yapar

- Reads **only** touchpad input (HID Digitizer / Touch Pad) through the Windows Raw Input API. No keyboard reading, no hooks.
  Windows Raw Input API ile **yalnızca** touchpad girişini okur. Klavye okumaz, hook kurmaz.
- Makes **no** network connections and collects no data.
  İnternete **bağlanmaz**, veri toplamaz.
- Runs without administrator rights. The only registry write is the optional "Start with Windows" entry (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`).
  Yönetici izni gerektirmez. Kayıt defterine yazdığı tek şey isteğe bağlı "Windows ile başlat" kaydıdır.
- Release builds are produced by GitHub Actions from this repository (see `.github/workflows/build.yml`).
  Sürümler GitHub Actions ile bu depodan derlenir.

## Verifying a download / İndirilen dosyayı doğrulama

Compare the file hash with the one in the release notes:
İndirdiğiniz dosyanın özetini sürüm notlarındakiyle karşılaştırın:

```powershell
Get-FileHash .\TrackPadForWindows-win-x64.zip -Algorithm SHA256
```

## Reporting a vulnerability / Güvenlik açığı bildirme

Please open a private security advisory from the repository's **Security** tab.
Lütfen deponun **Security** sekmesinden gizli bir güvenlik bildirimi açın.
