# Noty for Windows — план переноса на WPF

Порт macOS-приложения [aimen08/noty](https://github.com/aimen08/noty) (Swift + SwiftUI + AppKit)
на Windows: **.NET 8 / WPF, C#**. Цель — то же приложение и та же эстетика:
колода стикеров, живущая у края экрана, без окна и без кнопки на панели задач.

## 1. Что переносим

Три состояния одной колоды:

| Состояние | Что видно | Триггер |
|---|---|---|
| **Rest** | Пилюля 12 px у края экрана — по цветному штриху на заметку | простой |
| **Fan** | Вкладки «черепицей» разъезжаются вниз с шагом 45 мс, каждая со своим цветом и подписью, повёрнутой на бок | курсор вошёл в пилюлю |
| **Expanded** | Заметка выезжает из колоды в полный размер, на уровне своей вкладки | клик по вкладке |

Плюс: задачи-чекбоксы `☐/☑` прямо в тексте, инлайновый Markdown, архив,
окно «Все заметки» с поиском, экспорт/импорт, перетаскивание вкладок,
закрепление заметки, шифрование тел заметок, глобальные горячие клавиши,
поддержка нескольких мониторов, автозапуск.

## 2. Соответствие технологий

| macOS | Windows / WPF |
|---|---|
| `NSPanel` `.borderless` `.nonactivatingPanel` | `Window` `WindowStyle=None`, `AllowsTransparency`, `Topmost`, `WS_EX_NOACTIVATE \| WS_EX_TOOLWINDOW` |
| `.statusBar` level (поверх full-screen) | `HWND_TOPMOST` + опционально `SetWindowBand`/повышенный z-order; в WPF — `Topmost` + периодический `SetWindowPos` |
| `NSTrackingArea` mouseEntered/Exited | `WM_MOUSEMOVE`/`WM_MOUSELEAVE` через `TrackMouseEvent`, плюс поллинг курсора таймером (как в оригинале) |
| `NSEvent.addGlobalMonitorForEvents` (клик мимо) | `SetWindowsHookEx(WH_MOUSE_LL)` |
| Carbon `RegisterEventHotKey` | `RegisterHotKey` (user32) — тоже без прав администратора |
| `NSScreen` + `CGDirectDisplayID` | `System.Windows.Forms.Screen` + `MonitorFromPoint`, обработка `WM_DISPLAYCHANGE` |
| SwiftUI анимации/переходы | WPF Storyboard + `RenderTransform`, `BeginStoryboard` с `BeginTime` для лесенки 45 мс |
| `NSTextView` + `NSLayoutManager` | `RichTextBox` + `FlowDocument`, перерисовка стилей по plain-text источнику |
| CryptoKit AES-GCM | `System.Security.Cryptography.AesGcm` |
| ключ-файл `0600` | ключ-файл + DPAPI (`ProtectedData`, `CurrentUser`) — на Windows это правильный аналог Keychain |
| SQLite C API | `Microsoft.Data.Sqlite` |
| `UserDefaults` | JSON-файл `settings.json` в `%APPDATA%\Noty\` |
| `SMAppService` (launch at login) | ключ реестра `HKCU\...\Run` |
| `NSStatusItem` / меню по правому клику | `NotifyIcon` (Windows Forms) + `ContextMenu` WPF на пилюле |
| Sparkle (автообновление) | **не переносим** на первом этапе |

## 3. Структура проекта

```
Noty.sln
src/Noty/
  Noty.csproj              net8.0-windows, WPF + WinForms (ради Screen и NotifyIcon)
  App.xaml(.cs)            точка входа, single-instance, поднятие колод
  Core/
    Paths.cs               %APPDATA%\Noty\
    Crypto.cs              AES-GCM + DPAPI-защищённый ключ
    Palette.cs             восемь цветов (paper / dash / ink) — те же hex
    Ink.cs                 набор шрифтов для тела заметки
    Note.cs                модель + производный заголовок + счётчик задач
    TaskSyntax.cs          ☐/☑ ↔ markdown `- [ ]`
    Fmt.cs                 форматирование дат
    Store.cs               SQLite: схема, load, upsert, delete, миграции
    NoteStore.cs           единственный источник правды, ObservableObject
    Settings.cs            настройки в JSON + автозапуск
  Interop/
    Win32.cs               P/Invoke
    HotKeys.cs             глобальные RegisterHotKey
    MouseHook.cs           WH_MOUSE_LL — клик мимо закрывает заметку
    Screens.cs             мониторы и их перечисление
  Deck/
    DeckGeom.cs            вся геометрия и раскладка «черепицы»
    DeckState.cs           rest / fan / expanded
    DeckViewModel.cs       состояние одной колоды
    DeckWindow.xaml(.cs)   окно-колода: пилюля, веер, заметка
    DeckController.cs      конечный автомат, таймеры простоя, монитор клавиш
    DeckManager.cs         по колоде на монитор, пересборка при смене конфигурации
    Controls/
      PillControl          штрихи в состоянии покоя
      VerticalTab          вкладка с повёрнутой подписью
      ChipTab, MoreTab, PlusButton, EmptyTab
  Editor/
    NoteEditor.xaml(.cs)   шапка, поиск, тело, подвал с цветами
    NoteTextBox.cs         RichTextBox: клик по чекбоксу, Enter продолжает список
    Styler.cs              задачи + инлайновый Markdown
  Windows/
    LibraryWindow          «Все заметки» / «Архив» с поиском
    SettingsWindow         клавиши, шрифт, размер, ширина кромки, Markdown
    UndoToastWindow        десять секунд на отмену удаления
  Services/
    Transfer.cs            экспорт md / txt / один файл / .stickies, импорт
    TrayIcon.cs            иконка в трее и её меню
  Themes/Styles.xaml       кисти, тени, стили кнопок
```

## 4. Этапы

1. **Каркас и данные** — проект, `Paths`, `Crypto`, `Palette`, `Note`, `TaskSyntax`,
   `Store`, `NoteStore`, `Settings`. Проверка: база создаётся, welcome-заметка на месте.
2. **Колода** — окно без рамки и без активации, пилюля, веер с черепицей и
   лесенкой 45 мс, автомат состояний, таймеры простоя, несколько мониторов.
3. **Редактор** — `RichTextBox`, чекбоксы, Markdown, автосохранение через 250 мс,
   поиск по заметке, палитра в подвале, шапка с закреплением.
4. **Окружение** — трей и контекстное меню, глобальные клавиши, «Все заметки»,
   «Архив», окно настроек, тост отмены удаления.
5. **Экспорт/импорт** — `.md`, `.txt`, один документ, `.stickies` (тот же JSON,
   что и у оригинала, — архивы совместимы между платформами).
6. **Сборка** — `dotnet publish -r win-x64` в один self-contained exe, иконка,
   опционально MSIX/Inno Setup.

## 5. Заведомые отличия от оригинала

- **Нет Sparkle** — обновлений в приложении нет, и сети приложение не трогает вовсе.
- **Ключ шифрования** защищён DPAPI под учётной записью пользователя, а не правами
  файла: на Windows это ближе к Keychain, чем `0600`.
- **Скрытие markdown-разметки.** В оригинале маркеры схлопываются в ноль ширины
  через `NSLayoutManager`; в WPF аналога нет, поэтому маркеры приглушаются
  (как и описано в README оригинала), а не прячутся.
- **Поверх полноэкранных приложений** — `Topmost` перекрывает обычные окна, но не
  игры в эксклюзивном полноэкранном режиме; это ограничение самой Windows.
- **Горячие клавиши** — Win-раскладка: `Alt+Ctrl+N` / `Alt+Ctrl+A` / `Alt+Ctrl+L`
  вместо `⌥⌘N` и т.д., `Ctrl+Backspace` вместо `⌘⌫`.
