// Meadow
using Meadow;
using Meadow.Foundation.Sensors.Temperature;
using Meadow.Devices;
using Meadow.Hardware;
using Meadow.Units;

// C#
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;

// DOSS
using TemperatureWarriorCode.Web;
using NETDuinoWar;

// RingBuffer.NET
using RingBuffer;

namespace TemperatureWarriorCode
{

    public class MeadowApp : App<F7FeatherV2>
    {

        // Sensor de temperatura
        AnalogTemperature sensor;
        TimeSpan sensorSampleTime = TimeSpan.FromSeconds(0.1);
        Temperature currentTemperature;

        

        TemperatureController temperatureController;
        bool temperatureHandlerRunning = false; // Evitar overlapping de handlers

        // Estado del actuador en un rango de temperatura
        
        double currentSetpoint;
        TemperatureRange currentRange;

        // Cancelación de ronda en curso
        CancellationTokenSource shutdownCancellationSource = new();
        enum CancellationReason
        {
            ShutdownCommand,
            TempTooHigh,
            ConnectionLost,
        }
        CancellationReason cancellationReason;

        // Estado inter-comando para la librería de registro de temperatura
        int totalOperationTimeInMilliseconds = 0;
        int totalTimeInRangeInMilliseconds = 0;
        int totalTimeOutOfRangeInMilliseconds = 0;

        // El comando a ejecutar
        Command? currentCommand;

        // Buffer de actualizaciones a enviar en la próxima notifiación al cliente
        RingBuffer<double> nextNotificationsBuffer = new(10);
        readonly long notificationPeriodInMilliseconds = 2000;

        // El modo de ejecución del sistema
        enum OpMode
        {
            Config, // Parámetros de ronda no configurados
            Prep, // Parámetros de ronda configurados, esperando comando de inicio de combate
            Combat, // Ejecutando ronda
        }
        OpMode currentMode = OpMode.Config;

        public override async Task Run()
        {
            Resolver.Log.Info("[MeadowApp] ### Init: Run() ###");

            // Configurar sensores
            SensorSetup();

            // Configurar modo inicial ('esperando configuración')
            currentMode = OpMode.Config;

            await LaunchNetworkAndWebserver();

            Resolver.Log.Info("[MeadowApp] ### Fin: Run() ###");
            return;
        }

        private void SensorSetup()
        {
            // TODO Inicializar sensores de actuadores

            temperatureController =
                new TemperatureController(outputUpperbound: 255.0, outputLowerbound: 0.0,
                                        sampleTimeInMilliseconds: sensorSampleTime.Milliseconds);

            // Configuración de Sensor de Temperatura
            sensor = new AnalogTemperature(analogPin: Device.Pins.A02, sensorType: AnalogTemperature.KnownSensorType.TMP36);
            
            sensor.Updated += TemperatureUpdateHandler;
            sensor.StartUpdating(sensorSampleTime);
        }

        private async Task LaunchNetworkAndWebserver()
        {
            // Configuración de Red
            var wifi = Device.NetworkAdapters.Primary<IWiFiNetworkAdapter>();
            if (wifi is null)
            {
                Resolver.Log.Info($"ERROR: No se pudo localizar la interfaz de red primaria");
                return;
            }

            Resolver.Log.Info("[MeadowApp] Connecting to WiFi ...");
            wifi.Connect(Secrets.WIFI_NAME, Secrets.WIFI_PASSWORD).Wait();
            //if (!wifi.IsConnected)
            //{
            //    Resolver.Log.Info($"ERROR: No se pudo establecer conexión a SSID: {Secrets.WIFI_NAME}");
            //    return;
            //}

            wifi.NetworkConnected += async (networkAdapter, networkConnectionEventArgs) =>
            {
                Resolver.Log.Info($"[MeadowApp] Connected to WiFi -> {networkAdapter.IpAddress}");

                // Lanzar Servidor de Comandos
                WebSocketServer webServer = new(wifi.IpAddress, Config.Port);
                if (webServer is null) {
                    Resolver.Log.Info("[MeadowApp] ERROR: Failed to create a WebSocketServer instance");
                    return;
                }
                webServer.MessageReceived += MessageHandler;
                webServer.ConnectionFinished += ConnectionFinishedHandler;
                await webServer.Start();
            };            
        }

        private void Shutdown(CancellationReason reason)
        {
            cancellationReason = reason;
            shutdownCancellationSource.Cancel();
            shutdownCancellationSource = new CancellationTokenSource();
        }

        private void ConnectionFinishedHandler(WebSocketServer webServer, NetworkStream connection)
        {
            Shutdown(CancellationReason.ConnectionLost);
        }



        private void TemperatureTooHighHandler()
        {
            Shutdown(CancellationReason.TempTooHigh);
        }


        private void TemperatureUpdateHandler(object sender, IChangeResult<Temperature> e)
        {
            currentTemperature = e.New;
            //Resolver.Log.Info($"[MeadowApp] DEBUG (Remove this console line): Current temperature={currentTemperature}");

            if (currentTemperature.Celsius < 0) {
                Random rnd = new Random();
                currentTemperature = new Temperature(rnd.Next(minValue: 20, maxValue: 21));
            }
            
            TemperatureControllerHandler();
        }

        private void TemperatureControllerHandler()
        {
            if (temperatureHandlerRunning)
                return;
            temperatureHandlerRunning = true; 

            var currTemp = currentTemperature; 

            // TODO Gestionar controlador de temperatura si estamos en modo combate

            temperatureHandlerRunning = false;
        }

        private async void MessageHandler(WebSocketServer webServer, NetworkStream connection, Message message)
        {
            switch (message.type)
            {
                case Message.MessageType.Command:
                    {
                        // No se puede cambiar el comando a la mitad de una ronda en curso
                        if (currentMode == OpMode.Combat)
                        {
                            Resolver.Log.Info("[MeadowApp] Esperar a la finalización de la ronda en curso");
                            await webServer.SendMessage(connection, "{\"type\": \"StateError\"}");
                            return;
                        }
                        if (!message.data.HasValue || !message.data.Value.IsValid())
                        {
                            Resolver.Log.Info("[MeadowApp] Comando inválido");
                            await webServer.SendMessage(connection, "{\"type\": \"Bad Format\"}");
                            return;
                        }

                        Resolver.Log.Info("[MeadowApp] Comando guardado");
                        currentCommand = message.data.Value.ToCommand();
                        currentMode = OpMode.Prep;
                        await webServer.SendMessage(connection, "{\"type\": \"ConfigOK\"}");
                        break;
                    }
                case Message.MessageType.Start:
                    {
                        if (!currentCommand.HasValue)
                        {
                            Resolver.Log.Info("[MeadowApp] Configurar comando antes de iniciar ejecución");
                            await webServer.SendMessage(connection, "{\"type\": \"StateError\"}");
                            return;
                        }
                        if (currentMode == OpMode.Combat)
                        {
                            Resolver.Log.Info("[MeadowApp] No se puede lanzar una ronda mientras otra está en curso");
                            await webServer.SendMessage(connection, "{\"type\": \"StateError\"}");
                            return;
                        }

                        Resolver.Log.Info("[MeadowApp] Iniciando ejecución de comando");
                        currentMode = OpMode.Combat;
                        await StartRound(webServer, connection);
                        currentCommand = null;
                        currentMode = OpMode.Config;
                        break;
                    }
                case Message.MessageType.Shutdown:
                    {
                        Resolver.Log.Info("[MeadowApp] Shutdown recibido");
                        Shutdown(CancellationReason.ShutdownCommand);
                        break;
                    }
                default:
                    {
                        await webServer.SendMessage(connection, "{\"type\": \"Bad Format\"}");
                        break;
                    }
            }
        }

        private string SerializeNextNotifications()
        {
            return JsonSerializer.Serialize(nextNotificationsBuffer,
                                                     new JsonSerializerOptions
                                                     {
                                                         Converters = { new RingBufferJsonConverter() },
                                                         WriteIndented = true
                                                     });

        }

        private Task NotifyClient(WebSocketServer webServer, NetworkStream connection)
        {
            return webServer.SendMessage(connection, $"{{ \"type\": \"N\", \"ns\": {SerializeNextNotifications()}}}");
        }

        private void RegisterTimeControllerTemperature(TimeController timeController)
        {
            var currTemp = currentTemperature.Celsius;
            timeController.RegisterTemperature(currTemp);
            try
            {
                if (!nextNotificationsBuffer.Enqueue(currTemp))
                    Resolver.Log.Info("[MeadowApp] Fallo en añadir a cola de notifiaciones");
            }
            catch
            {
                Resolver.Log.Info("[MeadowApp] Fallo en añadir a cola de notifiaciones");
            }
        }



        //TW Combat Round
        private async Task StartRound(WebSocketServer webServer, NetworkStream connection)
        {
            Resolver.Log.Info("[MeadowApp] ### Init: StartRound() ###");
            if (currentCommand is null)
            {
                throw new NullReferenceException("currentCommand no puede ser null al comenzar StartRound");
            }

            var cmd = currentCommand.Value;

            int totalRoundOperationTimeInMilliseconds = cmd.temperatureRanges.Aggregate(0, (acc, range) => acc + range.RangeTimeInMilliseconds);

            // Inicialización de librería de control
            TimeController timeController = new()
            {
                DEBUG_MODE = false
            };

            if (!timeController.Configure(cmd.temperatureRanges, totalRoundOperationTimeInMilliseconds, cmd.refreshInMilliseconds, out string error))
            {
                Resolver.Log.Info($"[MeadowApp] Error configurando controlador de tiempo >>> {error}");
                await webServer.SendMessage(connection, "{\"type\": \"TimeControllerConfigError\"}");
                return;
            }

            var shutdownCancellationToken = shutdownCancellationSource.Token;

            double getRangeSetpoint(TemperatureRange range) => range.MinTemp + (range.MaxTemp - range.MinTemp) * 0.5;

            if (!cmd.isTest)
            { 
                temperatureController.SetSetpoint(getRangeSetpoint(cmd.temperatureRanges.First()));
                temperatureController.Start();
            }

            //// Acomodar tamaño de ringbuffer y zero-out ringbuffer
            // Debemos ser capaces de ingresar ceil(notificationPeriodInMilliseconds / cmd.refreshInMilliseconds),
            // además multiplicamos este resultado por 3 para dar algo de "wiggle room".
            var newSize = 10 * (int)Math.Ceiling(notificationPeriodInMilliseconds / (double)cmd.refreshInMilliseconds);
            nextNotificationsBuffer.ResizeAndReset(newSize);

            //// Lanzar conteo en librería de control cada refreshInMilliseconds
            void registerTimeController(object _) => RegisterTimeControllerTemperature(timeController);
            timeController.StartOperation();
            Timer registerTimer = new(registerTimeController, null, 0, cmd.refreshInMilliseconds);

            // Enviar primera temperatura medida
            RegisterTimeControllerTemperature(timeController);

            //// Notificaciones al cliente
            Timer notificationTimer = new(async _ => await NotifyClient(webServer, connection), null, 0, notificationPeriodInMilliseconds);

            foreach (var range in cmd.temperatureRanges)
            { // modificar setpoint en cada iteración
                currentSetpoint = getRangeSetpoint(range);
                currentRange = range;
                temperatureController.SetSetpoint(currentSetpoint);
                Resolver.Log.Info($"Iniciando rango [{range.MinTemp} - {range.MaxTemp}]");
                try
                {
                    await Task.Delay(range.RangeTimeInMilliseconds, shutdownCancellationToken);
                }
                catch (TaskCanceledException)
                {
                    // En caso de cancelación por temperature alta, escapar de loop (notificación a cliente se maneja abajo)
                    break;
                }
            }

            // Apagar actuadores y desactivar timers/librería de registro de temp
            notificationTimer.Dispose();
            registerTimer.Dispose();

            // Cuando el tiempo de operación de la ronda es divisible por el tiempo de refresco, se pierde
            // la última medición
            if (totalRoundOperationTimeInMilliseconds / cmd.refreshInMilliseconds == 0)
                RegisterTimeControllerTemperature(timeController);

            if (!cmd.isTest)
            { // Apagar actuador en caso de no ser un test de sensor de temperatura
                temperatureController.Stop();
                Thread.Sleep(100);
                
            }

            if (shutdownCancellationToken.IsCancellationRequested)
            { // Notificar finalización por altas temperaturas
                switch (cancellationReason)
                {
                    case CancellationReason.TempTooHigh:
                        {
                            await webServer.SendMessage(connection, $"{{ \"type\": \"TempTooHigh\", \"message\": \"High Temperature Emergency Stop {currentTemperature}\" }}");
                            break;
                        }
                    case CancellationReason.ShutdownCommand:
                        {
                            await webServer.SendMessage(connection, $"{{ \"type\": \"ShutdownCommand\", \"message\": \"Shutdown Command Received\" }}");
                            break;
                        }
                    case CancellationReason.ConnectionLost:
                        {
                            break;
                        }
                }
            }
            else
            { // Calcular resultados
                if (!cmd.isTest)
                {
                    // Solamente actualizar estado inter-ronda si no se trata de un test de sensores
                    totalTimeInRangeInMilliseconds += timeController.TimeInRangeInMilliseconds;
                    totalTimeOutOfRangeInMilliseconds += timeController.TimeOutOfRangeInMilliseconds;
                    totalOperationTimeInMilliseconds += totalRoundOperationTimeInMilliseconds;
                    Resolver.Log.Info($"Global - Tiempo dentro del rango {totalTimeInRangeInMilliseconds} ms de {totalOperationTimeInMilliseconds}s");
                    Resolver.Log.Info($"Global - Tiempo fuera del rango {totalTimeOutOfRangeInMilliseconds} ms de {totalOperationTimeInMilliseconds}s");
                }

                Resolver.Log.Info($"Ronda - Tiempo dentro del rango {timeController.TimeInRangeInMilliseconds} ms de {totalRoundOperationTimeInMilliseconds} ms");
                Resolver.Log.Info($"Ronda - Tiempo fuera del rango {timeController.TimeOutOfRangeInMilliseconds} ms de {totalRoundOperationTimeInMilliseconds} ms");

                // Indicar finalización y enviar datos de refresco restantes en el buffer
                await webServer.SendMessage(connection, $"{{ \"type\": \"RoundFinished\", \"timeInRange\": {timeController.TimeInRangeInMilliseconds}, \"ns\": {SerializeNextNotifications()}}}");
            }

            timeController.FinishOperation();

            Resolver.Log.Info("[MeadowApp] ### Fin: StartRound() ###");
            return;
        }
    }



    // Serialización de ringbuffer de notificaciones
    public class RingBufferJsonConverter : JsonConverter<RingBuffer<double>>
    {
        public override RingBuffer<double> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotImplementedException("Deserialización no implementada");
        }

        public override void Write(Utf8JsonWriter writer, RingBuffer<double> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            while (value.Dequeue(out double item))
            {
                writer.WriteNumberValue(Math.Round(item, 2));
            }
            writer.WriteEndArray();
        }
    }

}
