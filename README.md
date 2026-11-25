# DisplayRest

DisplayRest отключает дисплеи Windows по горячей клавише или по таймеру бездействия. [Скачать DisplayRest.exe для Windows x64](https://github.com/Quake-Journey/DisplayRest/releases/latest).

- Назначь одну клавишу или сочетание. Повторное нажатие включает дисплеи.
- Выбери, могут ли мышь и клавиатура будить дисплеи. Горячая клавиша действует при любой комбинации этих параметров.
- Задай время отключения дисплеев от сети и от батареи. «Применить» записывает значения в активную схему питания Windows.
- Настрой закрытие, запуск в трее и автозапуск. Кнопка сворачивания переносит программу в трей. По умолчанию крестик тоже закрывает только окно, а запуск в трее и автозапуск выключены. Команда «Закрыть DisplayRest» доступна в меню трея и на панели задач.
- При повторном ручном запуске DisplayRest открывает текущее окно и показывает предупреждение.
- Включи «Обновлять автоматически», чтобы получать свежие стабильные версии из GitHub Releases. Проверка выполняется в трее, максимум раз в сутки за сеанс в обычном режиме. При совпадении размера и SHA-256 DisplayRest заменяет EXE и возвращается в трей. Папка с EXE должна быть доступна для записи.
- Выбери русский, английский или язык интерфейса Windows. У элементов управления есть всплывающие подсказки.

При назначении горячей клавиши DisplayRest проверяет глобальную регистрацию Windows. Если Windows сообщает о конфликте, текущее назначение сохраняется. Отдельные приложения могут обрабатывать клавиши напрямую. Для `F12` и сочетаний только из модификаторов DisplayRest показывает, что проверка недоступна. [Документация RegisterHotKey](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-registerhotkey).

## Сборка / Build

Понадобятся Windows x64, .NET SDK 10 и инструменты Visual Studio 2022 с компонентом «Разработка классических приложений на C++». Готовый Native AOT EXE запускается без установки.

Requires Windows x64, the .NET 10 SDK, and Visual Studio 2022 Build Tools with the Desktop development with C++ workload. The published Native AOT EXE runs without installation.

```powershell
dotnet publish -c Release -o release
```

Номер версии берётся из `DisplayRest.csproj`.

## English

DisplayRest turns Windows displays off with a hotkey or after an idle timeout. Press the same hotkey again to wake them. Configure mouse and keyboard wake separately and edit the active Windows display timeout for AC and battery power. English and Russian are available, with the Windows display language used by default. [Download DisplayRest.exe](https://github.com/Quake-Journey/DisplayRest/releases/latest).
