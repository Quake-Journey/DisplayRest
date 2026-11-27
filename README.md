# PC Rest

PC Rest управляет дисплеями, системным сном и Bluetooth-геймпадами. Запускай `PCRest.exe`. Параметры DisplayRest подхватываются при запуске. [Релизы для Windows x64](https://github.com/Quake-Journey/DisplayRest/releases)

- **Дисплеи.** Горячая клавиша переключает экраны между отключением и пробуждением. Мышь и клавиатура настраиваются отдельно. Таймер от сети и от батареи записывается в активную схему питания Windows. Для HDR можно выбрать один дисплей или все поддерживаемые и назначить отдельную горячую клавишу.
- **Заставка.** Здесь можно включить заставку Windows и задать время бездействия в минутах или в формате ч:мм:сс. Для OLED кнопка «Включить сейчас» заливает все дисплеи чёрным и скрывает указатель. Вернуться можно повторным нажатием назначенного сочетания, клавишей Esc или щелчком мыши. Действие «Чёрный экран» доступно и в пресетах.
- **Компьютер.** Ручная кнопка переводит ПК в системный сон после подтверждения. Горячая клавиша действует сразу.
- **Геймпады.** Кнопка и горячая клавиша разрывают соединение с выбранным распознанным контроллером Bluetooth Classic или со всеми такими контроллерами. Сопряжение сохраняется. Поддержка зависит от режима соединения контроллера и драйвера Bluetooth. [Документация Bluetooth в Windows](https://learn.microsoft.com/en-us/windows/win32/api/bluetoothapis/nf-bluetoothapis-bluetoothfindfirstdevice).
- **Пресеты.** До 10 наборов с названием, описанием, действиями и необязательной горячей клавишей. Можно выбрать отключение дисплеев и геймпадов, изменение HDR, блокировку сеанса и системный сон. При выполнении сон идёт последним, а HDR и геймпады используют устройства, выбранные на своих вкладках. Сохранённые пресеты доступны в меню трея.
- **Настройки.** Закрытие окна в трей включено по умолчанию. Запуск в трее, автозапуск и автообновление из GitHub Releases включаются отдельно. Язык можно сменить между русским и английским или оставить системный. Команда «Закрыть PC Rest» доступна в меню трея и на панели задач. Кнопка в настройках запрашивает подтверждение выхода.

PC Rest проверяет конфликты со своими действиями, пресетами и глобальными регистрациями Windows. Для `F12` и сочетаний только из модификаторов Windows не предоставляет проверку регистрации. Отдельные приложения могут обрабатывать клавиши напрямую. [Документация RegisterHotKey](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-registerhotkey).

В «Настройках» кнопка «Проверить обновления» ищет стабильный релиз на GitHub и предлагает установить найденную версию. При включённом автообновлении выбери проверку раз в час, раз в 12 часов или раз в сутки (по умолчанию). PC Rest может проверить релиз с открытым окном и автоматически установит его в трее, когда дисплеи включены.

На любом действии или пресете нажми «Назначить», удержи нужные кнопки одного геймпада и отпусти их. Например, `D-pad ↓ + Start` (`Options` на DualShock и DualSense) можно назначить на «Отключить геймпады». В строке хранится одно назначение. Геймпад работает и в трее. PC Rest проверяет совпадения между своими назначениями, а игры могут одновременно получать те же кнопки. [Raw Input](https://learn.microsoft.com/en-us/windows/win32/inputdev/raw-input), [XInput](https://learn.microsoft.com/en-us/windows/win32/xinput/getting-started-with-xinput).

## Сборка / Build

Понадобятся Windows x64, .NET SDK 10 и инструменты Visual Studio 2022 с компонентом «Разработка классических приложений на C++». Готовый Native AOT EXE запускается без установки.

Requires Windows x64, the .NET 10 SDK, and Visual Studio 2022 Build Tools with the Desktop development with C++ workload. The published Native AOT EXE runs without installation.

```powershell
dotnet publish -c Release -o release
```

Номер версии берётся из `DisplayRest.csproj`.

## English

In **Settings**, **Check for updates** looks for a stable GitHub release and asks before installing it. Automatic checks can run every hour, every 12 hours, or daily (the default). Automatic installation runs in the tray with the displays on.

Open **Screen saver** to enable the Windows screen saver and set its idle timeout in minutes or h:mm:ss. **Black screen** covers all displays in black and hides the pointer. Activate it from the tab, a shortcut, or a preset. Press the shortcut again, press Esc, or click to return.

PC Rest controls display power and HDR, puts the PC into Windows sleep, and disconnects recognized Bluetooth Classic gamepads. Combine actions into up to 10 named presets with optional hotkeys or run them from the tray menu. It keeps your existing DisplayRest settings. English and Russian are available. [Windows x64 releases](https://github.com/Quake-Journey/DisplayRest/releases).

Any action or preset can use a keyboard shortcut or buttons from one gamepad. Select **Assign**, hold the buttons together, then release. For example, assign `D-pad ↓ + Start` (`Options` on PlayStation controllers) to **Disconnect gamepads**. Gamepad shortcuts work from the tray and are checked for duplicates within PC Rest. Other apps can still receive the same controller input.
