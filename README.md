# Transparent Folder (Cam Klasör)

[English](#english) · [Türkçe](#türkçe)

iOS/Android-style translucent dark-glass desktop folders for Windows 11. Keep your desktop tidy by grouping games, apps, shortcuts, and files inside expandable glass containers—without cluttering your screen.

---

## English

### Quick Installation (No Coding or .NET Required)

1. Go to the [**Releases**](../../releases) page and download **`TransparentFolder-v1.0.0-win-x64.zip`**.
2. Extract the ZIP archive into a permanent folder you want to keep (for example, `Documents\TransparentFolder`).
3. Double-click **`TransparentFolder.exe`** — that's it!
   - No `.NET` runtime installation is required (self-contained `win-x64` build).
   - Microsoft Edge WebView2 Runtime comes pre-installed on Windows 11.
   - Runs quietly in the background (no taskbar clutter or system tray icon) and starts automatically with Windows.
4. Right-click any empty space on your Desktop → **New → Transparent Folder** (or **Cam Klasör** on Turkish Windows) to create as many folders as you need.

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
- **Nameless Folders:** Leave the folder title empty to hide the label underneath the collapsed icon.
- **Automatic Language Detection (EN / TR):** Automatically matches your Windows display language (English or Turkish).
- **Per-Folder & Global Settings:**
  - Per-folder accent color, `2×2` / `3×3` preview toggle, custom icon picker, Open in Explorer, and Delete (restore items to desktop or delete all).
  - Global animation duration and hover delay sliders, plus instant in-folder search when a folder has more than 8 items.

### Uninstall

To remove startup registration, the right-click **New → Transparent Folder** menu entry, and stop any running instance:

1. Press `Win + R` (or open a terminal in the folder where you extracted the app) and run:
   ```powershell
   "C:\path\to\TransparentFolder\TransparentFolder.exe" --uninstall
   ```
2. Delete the `TransparentFolder` folder.
   - *(Optional)* Your folder data and vault files are stored in `%LOCALAPPDATA%\TransparentFolder\`.

### Build from Source

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows:

```powershell
.\scripts\build-release.ps1
```

This publishes a self-contained `win-x64` build to `dist\` and creates `release\TransparentFolder-win-x64.zip`.

---

## Türkçe

Windows 11 masaüstü için iOS/Android tarzı yarı saydam koyu cam masaüstü klasörleri. Oyunlarını, uygulamalarını, kısayollarını ve dosyalarını açılır cam klasörler içinde düzenli tut.

### Kolay Kurulum (Kodlama veya .NET Kurulumu Gerekmez)

1. Sağdaki [**Releases**](../../releases) bölümünden **`TransparentFolder-v1.0.0-win-x64.zip`** dosyasını indir.
2. ZIP dosyasını silmeyeceğin kalıcı bir klasöre çıkar (örneğin `Belgeler\TransparentFolder`).
3. **`TransparentFolder.exe`** dosyasına çift tıkla — kurulum bitti!
   - `.NET` kurulumu gerekmez (self-contained `win-x64` paketi).
   - Windows 11'de WebView2 çalışma zamanı zaten hazırdır.
   - Görev çubuğunu ve sistem tepsisini doldurmadan arka planda sessizce çalışır ve Windows açılışında otomatik başlar.
4. Masaüstünde boş bir yere **Sağ Tık → Yeni → Cam Klasör** (İngilizce Windows'ta **Transparent Folder**) diyerek istediğin kadar klasör oluşturabilirsin.

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
- **İsimsiz Klasör:** Klasör adını boş bıraktığında kapalı klasör altındaki isim etiketi otomatik gizlenir.
- **Otomatik TR / EN Dil Desteği:** Windows sistem diline göre otomatik olarak Türkçe veya İngilizce arayüzle açılır.
- **Klasör & Genel Ayarlar:**
  - Klasöre özel aksan rengi, `2×2` / `3×3` önizleme seçimi, özel ikon, Explorer'da açma ve silme (öğeleri masaüstüne geri çıkararak veya tamamen silerek).
  - Tüm klasörler için **Animasyon Süresi** ve **Hover Gecikmesi** kaydırıcıları; 8'den fazla öğe olduğunda otomatik açılan klasör içi arama kutusu.

### Kaldırma (Uninstall)

Başlangıç kaydını, sağ tık **Yeni → Cam Klasör** menü girişini kaldırmak ve çalışan uygulamayı durdurmak için:

1. Uygulamanın bulunduğu klasörde terminali aç (veya `Win + R` ile çalıştır) ve şu komutu yaz:
   ```powershell
   "C:\klasor\yolu\TransparentFolder\TransparentFolder.exe" --uninstall
   ```
2. Ardından `TransparentFolder` klasörünü silebilirsin.
   - *(İsteğe bağlı)* Klasör verilerin ve taşınan dosyaların `%LOCALAPPDATA%\TransparentFolder\` konumunda tutulur.

### Kaynaktan Derleme

Windows üzerinde [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) gerektirir:

```powershell
.\scripts\build-release.ps1
```

Bu komut self-contained `win-x64` çıktısını `dist\` klasörüne derler ve `release\TransparentFolder-win-x64.zip` paketini oluşturur.

---

## License

[MIT License](LICENSE) — Copyright © 2026 Alper Kağan Pamuk
