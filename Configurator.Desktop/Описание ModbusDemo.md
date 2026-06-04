ModbusDemo — отдельный демо-экран, который показывает простые привязки UI к Modbus TCP: toggle пишет/читает Coil, textbox пишет Holding Register, картинка показывается по значению регистра. Важно: демо использует отдельную секцию настроек ModbusDemo и отдельный стек сервисов, поэтому не мешает основному экрану Modbus TCP.

**Где Экран**  
Экран подключён во вкладке Modbus Demo в WorkspaceView.axaml (line 38):

xml

`<modbusDemo:ModbusDemoView DataContext="{Binding ModbusDemo}"/>`

WorkspaceViewModel получает ModbusDemoViewModel через DI и кладёт его в свойство ModbusDemo: WorkspaceViewModel.cs (line 18).

Сам UI находится в ModbusDemoView.axaml (line 1). Основные биндинги:

- Start Server → StartServerCommand
- Start Client → StartClientCommand
- Stop → StopCommand
- Настройки → OpenSettingsCommand
- toggle IsChecked → DemoButton
- textbox Text → DemoInputText
- Save → SaveInputCommand
- картинка IsVisible → IsDemoImageVisible

**ViewModel**  
Главный класс экрана: ModbusDemoViewModel (line 21).

В конструктор передаются:

csharp

`IModbusDemoTcpService modbusTcpService IModbusDemoOptionsProvider optionsProvider IDialogService dialogService IAppConfigService appConfigService`

Внутри конструктор:

- берёт настройки: _optionsProvider.CurrentValue.Clone()
- создаёт команды:
    - StartServerCommand вызывает _modbusTcpService.StartServerAsync(BuildOptions())
    - StartClientCommand вызывает _modbusTcpService.StartClientAsync(BuildOptions())
    - StopCommand вызывает _modbusTcpService.StopAsync()
    - SaveInputCommand вызывает SaveInputAsync()
- подписывается на состояние: _modbusTcpService.StateChanged += OnStateChanged
- подписывается на data map:
    - Subscribe("DemoButton", ApplyDataValue)
    - Subscribe("DemoInput", ApplyDataValue)
    - Subscribe("DemoImageVisible", ApplyDataValue)

**Настройки**  
Настройки лежат в appsettings.json (line 78), секция ModbusDemo.

Ключевые значения по умолчанию:

- клиент: Host = 127.0.0.1, Port = 1502, UnitId = 1
- сервер: BindAddress = 127.0.0.1, Port = 1502, UnitId = 1
- polling: PollIntervalMs = 500
- карта данных:
    - DemoButton: Coil, address 0, Bool, ReadWrite
    - DemoInput: HoldingRegister, address 0, UInt16, ReadWrite
    - DemoImageVisible: HoldingRegister, address 0, UInt16, Read

Класс настроек: ModbusOptions, внутри него Client, Server, DataMap, WriteConfirmationTimeoutMs.

**DI И Отдельный Demo-Стек**  
Регистрация идёт в DependencyInjection.cs (line 25), метод:

csharp

`AddModbusInfrastructure(IServiceCollection services, IConfiguration configuration)`

Он регистрирует обычную секцию Modbus и named-секцию ModbusDemo:

csharp

`services.Configure<ModbusOptions>(configuration.GetSection(ModbusOptions.SectionName)); services.Configure<ModbusOptions>( ModbusOptions.DemoSectionName, configuration.GetSection(ModbusOptions.DemoSectionName));`

Для демо вызывается CreateDemoTcpService(IServiceProvider serviceProvider). Там вручную создаётся отдельный набор:

csharp

`NamedOptionsMonitor<ModbusOptions>(..., "ModbusDemo") ModbusClientService ModbusServerService ModbusRuntimeService ModbusTcpService ModbusDemoTcpService`

Это важно: ModbusDemoTcpService не переиспользует основной IModbusTcpService.

**Facade**  
ModbusDemoTcpService — тонкий делегирующий фасад: ModbusDemoTcpService.cs (line 12).

Он реализует IModbusDemoTcpService, а тот наследуется от IModbusTcpService. Методы просто прокидываются во внутренний facade:

csharp

`StartClientAsync(ModbusOptions? options = null, CancellationToken ct = default) StartServerAsync(ModbusOptions? options = null, CancellationToken ct = default) StopAsync(CancellationToken ct = default) GetAsync<T>(string name, CancellationToken ct = default) SetAsync<T>(string name, T value, CancellationToken ct = default) Subscribe(string name, Action<ModbusDataValue> onChanged)`

Реальная логика фасада находится в ModbusTcpService.

**Запуск Сервера**  
Когда пользователь жмёт Start Server:

1. UI вызывает StartServerCommand.
2. ModbusDemoViewModel вызывает:

csharp

`RunCommandAsync(() => _modbusTcpService.StartServerAsync(BuildOptions()))`

3. BuildOptions() возвращает _currentOptions.Clone().
4. ModbusDemoTcpService.StartServerAsync(options, ct) делегирует в ModbusTcpService.StartServerAsync(options, ct).
5. ModbusTcpService вызывает приватный StartRoleAsync(ModbusRunMode.Server, options, ct).
6. Внутри:
    - валидируется DataMap: _dataMapValidator.Validate(nextOptions, ModbusRunMode.Server)
    - останавливается клиент: _runtimeService.StopClientAsync(ct)
    - запускается сервер: _runtimeService.StartServerAsync(_currentOptions, ct)
7. ModbusRuntimeService.StartServerAsync(options, cancellationToken) вызывает:

csharp

`_serverService.StartAsync(CurrentOptions.Server, cancellationToken)`

8. ModbusServerService.StartAsync(ModbusEndpointOptions options, CancellationToken cancellationToken) создаёт ModbusServer, DataStore, запускает TCP-сервер через:

csharp

`_server.StartTcpServer(_options.Port, (byte)_options.UnitId) _server.Start()`

**Запуск Клиента**  
Когда пользователь жмёт Start Client, цепочка похожая:

csharp

`_modbusTcpService.StartClientAsync(BuildOptions())`

Дальше:

- ModbusTcpService.StartClientAsync(options, ct)
- StartRoleAsync(ModbusRunMode.Client, options, ct)
- _runtimeService.StopServerAsync(ct)
- _runtimeService.StartClientAsync(_currentOptions, ct)
- ModbusRuntimeService.StartClientAsync(...)
- _clientService.StartAsync(CurrentOptions.Client, cancellationToken)

ModbusClientService.StartAsync(ModbusEndpointOptions options, CancellationToken cancellationToken) нормализует настройки и запускает фоновой цикл ConnectAndPollAsync.

В ConnectCoreAsync создаётся Modbus TCP master:

csharp

`var tcpClient = new TcpClientRx(_options.Host, _options.Port); _master = ModbusIpMaster.CreateIp(tcpClient); _reader = new ModbusClientDataReader(_master, _options.UnitId);`

**Чтение**  
Клиент в цикле вызывает ReadAndPublishAsync(cancellationToken).

Если включены coils:

csharp

`reader.ReadCoilsAsync(_options.CoilStartAddress, _options.CoilCount)`

Если включены holding registers:

csharp

`reader.ReadHoldingRegistersAsync( _options.HoldingRegisterStartAddress, _options.RegisterCount)`

ModbusClientDataReader превращает это в вызовы ModbusIpMaster:

csharp

`ReadCoilsAsync(slaveAddress: _unitId, startAddress, numberOfPoints) ReadHoldingRegistersAsync(slaveAddress: _unitId, startAddress, numberOfPoints)`

После чтения создаётся ModbusSnapshot с Coils, HoldingRegisters, DecodedRegisters, Timestamp. Snapshot уходит вверх через событие SnapshotChanged.

**Как UI Получает Значения**  
ModbusRuntimeService принимает snapshot от клиента или сервера в OnSnapshotChanged(...).

ModbusTcpService подписан на runtime и получает snapshot в OnRuntimeSnapshotChanged(...), затем вызывает ApplySnapshot(ModbusSnapshot snapshot).

В ApplySnapshot фасад проходит по DataMap и декодирует точки:

csharp

`TryDecodePoint(point, snapshot, out var dataValue, out var failure)`

Для DemoButton берётся snapshot.Coils[0].

Для DemoInput и DemoImageVisible берётся snapshot.HoldingRegisters[0].

Потом вызывается:

csharp

`PublishDataValue(dataValue)`

А уже подписка из ModbusDemoViewModel вызывает:

csharp

`ApplyDataValue(ModbusDataValue value)`

Там:

- DemoButton обновляет toggle
- DemoInput обновляет textbox
- DemoImageVisible делает IsDemoImageVisible = value == 1

**Запись Toggle**  
Когда пользователь меняет toggle:

1. Срабатывает setter DemoButton.
2. Если это действие пользователя, вызывается:

csharp

`WriteDemoButtonAsync(value)`

3. Он вызывает:

csharp

`_modbusTcpService.SetAsync("DemoButton", value)`

4. ModbusTcpService.SetAsync<T>(string name, T value, CancellationToken ct = default) ищет точку DemoButton в DataMap.
5. TryBuildWritePayload(...) превращает bool в coilValue.
6. Если запущен клиент, запись идёт через:

csharp

`_clientService.WriteCoilAsync(point.Address, coilValue, ct)`

7. Если запущен только сервер, запись идёт в локальную карту сервера:

csharp

`_serverService.SetCoilAsync(point.Address, coilValue, ct)`

8. Если точка readable, фасад ждёт подтверждения через WaitForConfirmationAsync(...).

**Запись TextBox**  
Когда пользователь вводит число и жмёт Save:

1. SaveInputCommand вызывает SaveInputAsync().
2. SaveInputAsync парсит DemoInputText в ushort.
3. Если успешно, вызывает:

csharp

`_modbusTcpService.SetAsync("DemoInput", value)`

4. ModbusTcpService.SetAsync находит DemoInput в DataMap.
5. Так как это HoldingRegister и UInt16, TryBuildWritePayload делает:

csharp

`registers = [Convert.ToUInt16(value)]`

6. Если пишет через клиент:

csharp

`_clientService.WriteRegisterAsync(point.Address, registers[0], ct)`

7. Если пишет прямо в demo-сервер:

csharp

`_serverService.SetRegisterAsync(point.Address, registers[0], ct)`

**Настройки**  
Кнопка Настройки вызывает OpenSettingsAsync():

csharp

`var options = _appConfigService.GetSection<ModbusOptions>(ModbusOptions.DemoSectionName); var savedOptions = await _dialogService.EditModbusSettingsAsync( "Настройки Modbus TCP Demo", ModbusOptions.DemoSectionName, options);`

Диалог создаёт ModbusSettingsDialogViewModel. При сохранении он собирает новый ModbusOptions, валидирует его и вызывает:

csharp

`_appConfigService.SaveSectionAsync(SectionName, options, ct)`

Для demo SectionName равен "ModbusDemo".

**Остановка**  
Кнопка Stop вызывает:

csharp

`_modbusTcpService.StopAsync()`

Дальше:

- ModbusDemoTcpService.StopAsync(ct)
- ModbusTcpService.StopAsync(ct)
- _runtimeService.StopAsync(ct)
- ModbusRuntimeService.StopAsync(...)
- _clientService.StopAsync(...)
- _serverService.StopAsync(...)

При закрытии приложения App.StopModbusRuntimeAsync() дополнительно останавливает и основной runtime, и demo facade. Это в App.axaml.cs (line 67).

**Коротко**  
ModbusDemoView только отображает кнопки и поля.  
ModbusDemoViewModel переводит действия пользователя в команды IModbusDemoTcpService.  
ModbusDemoTcpService отделяет demo от основного Modbus.  
ModbusTcpService знает про именованные точки DemoButton, DemoInput, DemoImageVisible.  
ModbusRuntimeService координирует клиент и сервер.  
ModbusClientService реально подключается по TCP и читает/пишет через ModbusIpMaster.  
ModbusServerService поднимает встроенный Modbus TCP сервер и хранит локальную карту coils/registers.