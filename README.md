# Transparent Folder (Cam Klasör)

![Downloads](https://img.shields.io/github/downloads/kaganpmk/transparent-folder/total?style=flat-square&color=a9f1fe&labelColor=0d0f14)
![Release](https://img.shields.io/github/v/release/kaganpmk/transparent-folder?style=flat-square&color=a9f1fe&labelColor=0d0f14)
![License](https://img.shields.io/github/license/kaganpmk/transparent-folder?style=flat-square&color=a9f1fe&labelColor=0d0f14)

[English](#english) · [Türkçe](#türkçe) · [Code Signing & Privacy Policy](#code-signing--privacy-policy)

iOS/Android-style translucent dark-glass desktop folders for Windows 11. Keep your desktop tidy by grouping games, apps, shortcuts, and files inside expandable glass containers—without cluttering your screen.

---

## English

### Quick Installation (No Coding or .NET Required)

1. Choose one of the two packages below (or browse all versions on the [Releases](../../releases) page):
   - **[Download Standalone (`63.37 MB`)](https://github.com/kaganpmk/transparent-folder/releases/download/v1.0.0/TransparentFolder-v1.0.0-win-x64-standalone.zip) — Download this if `.NET 8` is not installed on your PC or if you're not sure.**
   - **[Download Lite (`0.47 MB`)](https://github.com/kaganpmk/transparent-folder/releases/download/v1.0.0/TransparentFolder-v1.0.0-win-x64-lite.zip) — Download this smaller package if you already have [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) installed.**
2. Extract the ZIP archive into a permanent folder you want to keep (for example, `Documents\TransparentFolder`).
   - Each ZIP contains **only 2 files**: `TransparentFolder.exe` and `Uninstall.bat` (all UI/web assets are embedded directly inside the `.exe`).
3. Double-click **`TransparentFolder.exe`** — that's it!
   - **Standalone** requires no `.NET` installation (self-contained `win-x64`). **Lite** uses your system's `.NET 8 Desktop Runtime`.
   - Microsoft Edge WebView2 Runtime comes pre-installed on Windows 11.
   - Runs quietly in the background (no taskbar clutter or system tray icon) and starts automatically with Windows.
4. Right-click any empty space on your Desktop → **New → Transparent Folder** (or **Cam Klasör** on Turkish Windows) to create as many folders as you need.

> **SmartScreen Note:** On first launch, if Windows SmartScreen appears, choose **More info → Run anyway** (**Ek bilgi → Yine de çalıştır**). This warning will disappear once open-source code signing onboarding is completed.

### Features

- **2×2 / 3×3 Mini Preview & Custom Icon:** Collapsed folders show a 2×2 (default) or 3×3 live grid of real Windows app icons, or any custom icon/image you choose.
- **Hover & 2-Stage Pin Mode:**
  - **Hover** over a folder to peek inside (configurable hover delay).
  - **1st click (Pinned):** Stays open until you click outside.
  - **2nd click (Desktop-Locked Pin):** Locks the folder directly onto the desktop (`HWND_BOTTOM`, protected from `Win+D`)—stays open even when clicking elsewhere, without covering other app windows.
  - **3rd click:** Closes the folder.
- **Seamless Drag & Drop (In & Out):**
  - Drag files or shortcuts from your desktop or Explorer into a folder (collapsed or expanded) to move them into the app's vault and clear them from the desktop immediately.
  - Drag items out of an expanded folder back onto the desktop right where you drop them, or reorder items inside the folder.
- **Drag to Recycle Bin:** Drag a folder or an item inside it to the Recycle Bin to delete safely (`FOF_ALLOWUNDO`).
- **Native Windows Right-Click Menu:** Right-clicking any item inside a folder opens the real Windows Explorer context menu (`Open`, `Run as administrator`, `Properties`, etc.).
- **File Explorer Sidebar Integration:** Access all your folders with their real names directly from the **Transparent Folders** (**Cam Klasörler**) node in the File Explorer navigation pane—hidden from the desktop wallpaper.
- **Nameless Folders:** Leave the folder title empty to hide the label underneath the collapsed icon.
- **Automatic Language Detection (EN / TR):** Automatically matches your Windows display language (English or Turkish).
- **Per-Folder & Global Settings:**
  - Per-folder accent color, `2×2` / `3×3` preview toggle, custom icon picker, Open in Explorer, and Delete (restore items to desktop or delete all).
  - Global animation duration and hover delay sliders, plus instant in-folder search when a folder has more than 8 items.

### Uninstall

To remove startup registration, the right-click **New → Transparent Folder** menu entry, and stop any running instance:

1. Simply double-click **`Uninstall.bat`** inside your `TransparentFolder` folder (or alternatively run `"C:\path\to\TransparentFolder\TransparentFolder.exe" --uninstall`).
2. Delete the `TransparentFolder` folder.
   - *(Optional)* Your folder data and vault files are stored in `%LOCALAPPDATA%\TransparentFolder\`.

### Build from Source

> *For developers who want to modify and compile the code themselves — regular users only need to download and run the ZIP above.*

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows:

```powershell
.\scripts\build-release.ps1
```

This publishes both single-file `win-x64` packages (`release\TransparentFolder-win-x64-standalone.zip` and `release\TransparentFolder-win-x64-lite.zip`; pass `-Version v1.0.0` to include a version tag).

---

## Türkçe

Windows 11 masaüstü için iOS/Android tarzı yarı saydam koyu cam masaüstü klasörleri. Oyunlarını, uygulamalarını, kısayollarını ve dosyalarını açılır cam klasörler içinde düzenli tut.

### Kolay Kurulum (Kodlama veya .NET Kurulumu Gerekmez)

1. İhtiyacına göre aşağıdaki iki paketten birini seç (veya tüm sürümler için [Releases](../../releases) sayfasına göz at):
   - **[Standalone İndir (`63.37 MB`)](https://github.com/kaganpmk/transparent-folder/releases/download/v1.0.0/TransparentFolder-v1.0.0-win-x64-standalone.zip) — Bilgisayarında `.NET 8` kurulu değilse veya emin değilsen bunu indir.**
   - **[Lite İndir (`0.47 MB`)](https://github.com/kaganpmk/transparent-folder/releases/download/v1.0.0/TransparentFolder-v1.0.0-win-x64-lite.zip) — Bilgisayarında [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) zaten kuruluysa boşuna fazla indirme, bunu indir.**
2. ZIP dosyasını silmeyeceğin kalıcı bir klasöre çıkar (örneğin `Belgeler\TransparentFolder`).
   - Her iki ZIP de **sadece 2 dosya** içerir: `TransparentFolder.exe` ve `Uninstall.bat` (arayüz/web dosyaları doğrudan `.exe` içine gömülüdür).
3. **`TransparentFolder.exe`** dosyasına çift tıkla — kurulum bitti!
   - **Standalone** sürümünde `.NET` kurulumu gerekmez (self-contained `win-x64`). **Lite** sürümü sistemdeki `.NET 8 Desktop Runtime`'ı kullanır.
   - Windows 11'de WebView2 çalışma zamanı zaten hazırdır.
   - Görev çubuğunu ve sistem tepsisini doldurmadan arka planda sessizce çalışır ve Windows açılışında otomatik başlar.
4. Masaüstünde boş bir yere **Sağ Tık → Yeni → Cam Klasör** (İngilizce Windows'ta **Transparent Folder**) diyerek istediğin kadar klasör oluşturabilirsin.

> **SmartScreen Notu:** İlk açılışta Windows SmartScreen mavi ekranı çıkarsa **Ek bilgi → Yine de çalıştır** (*More info → Run anyway*) seçin; açık kaynak kod imzalama başvurusu tamamlandığında bu uyarı kalkacaktır.

### Özellikler

- **2×2 / 3×3 Önizleme veya Özel İkon:** Kapalı klasörler gerçek Windows uygulama ikonlarından oluşan `2×2` (varsayılan) veya `3×3` mini ızgara gösterir; dilersen klasöre özel ikon da seçebilirsin.
- **Hover ve 2 Aşamalı Raptiye (Pin) Modu:**
  - Klasörün üzerine gelerek (**Hover**) içeriği hızlıca görüntüle.
  - **1. tık (Sabit):** Dışarı tıklayana kadar açık kalır.
  - **2. tık (Masaüstüne Kilitli):** Klasör doğrudan masaüstüne kilitlenir (`Win+D` korumalı); ekranın neresine tıklarsan tıkla kapanmaz ve diğer pencerelerin üstüne çıkmaz.
  - **3. tık:** Klasörü kapatır.
- **Masaüstünden Taşıma & Dışarı Sürükleyip Bırakma:**
  - Masaüstündeki veya Explorer'daki dosya/kısayolları kapalı ya da açık klasörün üstüne sürükle; masaüstünden anında klasöre taşınır.
  - Klasör içindeki öğeleri sürükleyerek sırasını değiştir veya klasör dışına, masaüstünde istediğin konuma geri bırak.
- **Çöp Kutusuna Sürükleyip Silme:** Klasörü veya içindeki bir öğeyi Geri Dönüşüm Kutusu'na sürükleyerek güvenle silebilirsin.
- **Gerçek Windows Sağ Tık Menüsü:** Klasör içindeki öğelere sağ tıkladığında gerçek Windows sağ tık menüsü (`Aç`, `Yönetici olarak çalıştır`, `Özellikler` vb.) açılır.
- **Dosya Gezgini Sol Menü Erişimi:** Dosya Gezgini sol menüsündeki **Cam Klasörler** (**Transparent Folders**) girişi üzerinden tüm klasörlere gerçek adlarıyla doğrudan erişebilirsin (masaüstünde ek simge göstermez).
- **İsimsiz Klasör:** Klasör adını boş bıraktığında kapalı klasör altındaki isim etiketi otomatik gizlenir.
- **Otomatik TR / EN Dil Desteği:** Windows sistem diline göre otomatik olarak Türkçe veya İngilizce arayüzle açılır.
- **Klasör & Genel Ayarlar:**
  - Klasöre özel aksan rengi, `2×2` / `3×3` önizleme seçimi, özel ikon, Explorer'da açma ve silme (öğeleri masaüstüne geri çıkararak veya tamamen silerek).
  - Tüm klasörler için **Animasyon Süresi** ve **Hover Gecikmesi** kaydırıcıları; 8'den fazla öğe olduğunda otomatik açılan klasör içi arama kutusu.

### Kaldırma (Uninstall)

Başlangıç kaydını, sağ tık **Yeni → Cam Klasör** menü girişini kaldırmak ve çalışan uygulamayı durdurmak için:

1. Klasör içindeki **`Uninstall.bat`** dosyasına çift tıkla ve ardından `TransparentFolder` klasörünü sil. *(Alternatif olarak terminalden `"C:\klasor\yolu\TransparentFolder\TransparentFolder.exe" --uninstall` komutunu da çalıştırabilirsin.)*
   - *(İsteğe bağlı)* Klasör verilerin ve taşınan dosyaların `%LOCALAPPDATA%\TransparentFolder\` konumunda tutulur.

### Kaynaktan Derleme

> *Yalnızca kodu değiştirip kendisi derlemek isteyen geliştiriciler içindir — normal kullanıcıların yukarıdaki ZIP dosyasını indirip çalıştırması yeterlidir.*

Windows üzerinde [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) gerektirir:

```powershell
.\scripts\build-release.ps1
```

Bu komut her iki tek-dosya `win-x64` paketini (`release\TransparentFolder-win-x64-standalone.zip` ve `release\TransparentFolder-win-x64-lite.zip`) oluşturur (sürüm etiketi eklemek için `-Version v1.0.0` parametresini kullanabilirsin).

---

## Code Signing & Privacy Policy

Free code signing provided by [SignPath.io](https://signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

- **Privacy Policy:** Transparent Folder does not collect, store, or transmit any user data, telemetry, or analytics to external servers; all data is stored strictly locally on the device (`%LocalAppData%\TransparentFolder`). *(Uygulama hiçbir kullanıcı verisi, telemetri veya analitik toplamaz ve harici sunuculara göndermez; tüm veriler yalnızca yerel cihazda `%LocalAppData%\TransparentFolder` konumunda saklanır.)*
- **Build Policy:** Official releases are built via GitHub Actions directly from the source code in this public repository. *(Resmi sürümler bu public repodaki kaynak koddan GitHub Actions ile üretilir.)*
- **Team / Maintainer:** Alper Kağan Pamuk (`@kaganpmk`)

---

## License

[MIT License](LICENSE) — Copyright © 2026 Alper Kağan Pamuk
