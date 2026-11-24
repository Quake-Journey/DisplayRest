# DisplayRest

DisplayRest отключает дисплеи Windows по горячей клавише или по таймеру бездействия. [Скачать DisplayRest.exe для Windows x64](https://github.com/Quake-Journey/DisplayRest/releases/latest).

- Назначь одну клавишу или сочетание. Повторное нажатие включает дисплеи.
- Выбери, могут ли мышь и клавиатура будить дисплеи. Горячая клавиша действует при любой комбинации этих параметров.
- Задай время отключения дисплеев от сети и от батареи. «Применить» записывает значения в активную схему питания Windows.
- Настрой закрытие в трей и автозапуск. По умолчанию окно скрывается в трей, автозапуск выключен. Команда «Закрыть DisplayRest» доступна в меню трея и на панели задач.
- Выбери русский, английский или язык интерфейса Windows. У элементов управления есть всплывающие подсказки.

При назначении горячей клавиши DisplayRest проверяет глобальную регистрацию Windows. Если Windows сообщает о конфликте, текущее назначение сохраняется. Отдельные приложения могут обрабатывать клавиши напрямую. Для `F12` и сочетаний только из модификаторов DisplayRest показывает, что проверка недоступна. [Документация RegisterHotKey](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-registerhotkey).

## Сборка / Build

Понадобятся Windows x64 и .NET SDK 10. Готовый EXE включает среду .NET и запускается без установки.

Requires Windows x64 and the .NET 10 SDK. The published EXE bundles .NET and runs without installation.

```powershell
dotnet publish -c Release -o release
```

Номер версии берётся из `DisplayRest.csproj`.

## English

DisplayRest turns Windows displays off with a hotkey or after an idle timeout. Press the same hotkey again to wake them. Configure mouse and keyboard wake separately and edit the active Windows display timeout for AC and battery power. English and Russian are available, with the Windows display language used by default. [Download DisplayRest.exe](https://github.com/Quake-Journey/DisplayRest/releases/latest).
