# Сторонние компоненты

P90 Nexus 0.2.0 включает собственные P90.API, P90.Core, P90.GameAdapter, P90.Loader и тестовый P90.Samples.ServerDiagnostics. BepInEx и его зависимости поставлены из upstream-архива без изменения DLL; настройки BepInEx подготовлены для этой сборки игры.

Источники комплекта:

- [BepInEx Unity IL2CPP Windows x64 6.0.0-be.788](https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788%2B5b766a3.zip). [Исходники соответствующего коммита](https://github.com/BepInEx/BepInEx/tree/5b766a3b7f6c164d4798924a93f3acf4db769d06). Текст лицензии: `Licenses/BepInEx-LICENSE.txt`.
- [UnityDoorstop 4.5.0](https://github.com/NeighTools/UnityDoorstop/tree/v4.5.0), компонент `winhttp.dll` из комплекта BepInEx. Текст лицензии: `Licenses/Doorstop-LICENSE.txt`.
- [.NET runtime 6.0.7](https://github.com/dotnet/runtime/tree/v6.0.7), каталог `dotnet` из комплекта BepInEx. Лицензия и уведомления: `Licenses/dotnet-LICENSE.txt`, `Licenses/dotnet-THIRD-PARTY-NOTICES.txt`.
- [Unity base libraries 2022.3.16](https://unity.bepinex.dev/libraries/2022.3.16.zip), локальный архив для генератора interop. Unity и SCP Project 90 принадлежат своим правообладателям; комплект не включает саму игру.

В upstream BepInEx также входят Cpp2IL, LibCpp2IL, Il2CppInterop, Harmony, MonoMod, Mono.Cecil, AsmResolver, AssetRipper, Iced и другие зависимости. Их версии и состав определяются указанным upstream-архивом; список поставляемых файлов с SHA-256 есть в `payload-sha256.json`. Исходники и лицензии зависимостей доступны через ссылки и зависимости соответствующего проекта BepInEx. Этот список не заменяет условия лицензий правообладателей и не предоставляет дополнительных прав на игру или Unity.

