# Book of Hours mods

Код-моды для Book of Hours (Unity 2022.3, Mono) на BepInEx 5 + Harmony, с горячей
перезагрузкой через ScriptEngine. Общение с пользователем на русском.

**Перед работой прочитай [DEVLOG.md](DEVLOG.md)**: там история, принятые решения и
найденные грабли.

## Где что
- Игра: `D:\games\steamapps\common\Book of Hours` (подключена к workspace).
  Основная логика игры — `bh_Data\Managed\SecretHistories.Main.dll`.
- Декомпилированный код игры: `decompiled/SecretHistories.Main/` (git-ignored).
  Ищи по нему, прежде чем гадать о поведении игры. Если его нет, запусти `tools/setup.ps1`.
- Моды: `src/<ModName>/`. Общие ссылки, target и деплой — в `Directory.Build.props`.
- Окружение ставит `tools/setup.ps1` (.NET SDK 8, ilspycmd, BepInEx, ScriptEngine, конфиги).

## Сборка и проверка
- `dotnet build src\<Mod>\<Mod>.csproj`: DLL копируется в `BepInEx\scripts`, игра
  перезагружает её через ~3 с, если окно игры в фокусе, или по F6.
- `dotnet` может не быть в PATH текущей оболочки: `C:\Program Files\dotnet\dotnet.exe`.
- Логи: `BepInEx\LogOutput.log`, полный лог Unity с исключениями —
  `%USERPROFILE%\AppData\LocalLow\Weather Factory\Book of Hours\Player.log`.
- Каждый плагин пишет в `Awake` строку `<Name> <Version> loaded`. По ней видно,
  загрузился ли он.

## Правила
- Каждый мод — `BaseUnityPlugin`, GUID `shirofaii.boh.<modname>`. Всё, что он создаёт
  в сцене, убирать в `OnDestroy`: ScriptEngine уничтожает плагин при перезагрузке.
- Клавиатура и мышь — только через новый Input System (`Keyboard.current`, `Mouse.current`).
- Приватные члены игры — через `AccessTools` (Harmony), поля кешировать в static readonly.
- После заметной работы (новый мод или фича, важное решение, найденные грабли)
  **добавляй запись в начало DEVLOG.md**: что, зачем, как устроено в игре, что отвергли.
