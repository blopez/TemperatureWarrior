// C#
using System;
using System.IO;

using System.Linq;

using System.Text.Json;
using System.Text.Json.Serialization;

// DOSS
using NETDuinoWar;

namespace TemperatureWarriorCode.Web
{
    // Estructura de datos utilizada en la ejecución del comando
    public struct Command
    {
        public TemperatureRange[] temperatureRanges;
        public int refreshInMilliseconds;
        public bool isTest;
    }

    // Etructura de datos recibida del cliente web
    // mensaje := start | command | shutdown
    // start := '{'  "type" ':' "Start" '}'
    // shutdown := '{'  "type" ':' "Shutdown" '}'
    // command := '{'  "type" ':' "Command", "data" ':' '{' "isTest": bool, "refreshInMilliseconds" ':' int, "ranges" ':' ranges '}' '}'
    // ranges := '[' element | ranges_tail
    // ranges_tail := ',' element ranges_tail | ']'
    // element := '{' "tempMin" ':' double, "tempMax" ':' double, "roundTime" ':' int '}'
    struct Message
    {
        public MessageType type { get; set; }
        // NOTA: `data` e `isTest` solo disponibles cuando type == MessageType.SendCommand (tipo suma de pobre, parte 1)
        public CommandRequest? data { get; set; }
        public bool? isTest;

        // (tipo suma de pobre, parte 2)
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public enum MessageType
        {
            Command,
            Start,
            Shutdown,
        }

        public struct TemperatureRangeRequest
        {
            // Usamos esto en lugar de TemperatureRange porque para poder ser serializado necesitamos la presencia de los getters y setters
            // pero la versión ofrecida por la librería de NetDuinoWar no lo hace (por eso la conversión a TemperatureRange es trivial).
            public double tempMin { get; set; }
            public double tempMax { get; set; }
            public int roundTime { get; set; }
            public bool IsValid() => roundTime <= 0  // tiempo positivo
                                                || tempMin < Config.TemperatureLowerbound.Celsius || tempMax > Config.TemperatureUpperbound.Celsius // en rango
                                                || tempMin <= tempMax;
            public TemperatureRange ToTemperatureRange() => new TemperatureRange(tempMin, tempMax, roundTime * 1000);
            public override string ToString() => $"{{ tempMin: {tempMin}, tempMax: {tempMax}, roundTime: {roundTime} }}";
        }

        // Etructura de datos recibida del cliente web (anidada en Message)
        public struct CommandRequest
        {
            public TemperatureRangeRequest[] ranges { get; set; }
            public int refreshInMilliseconds { get; set; }
            public string pass { get; set; }
            public bool isTest { get; set; }

            // En .net8.0 podríamos usar `required` para verificar esto, pero
            // Esta placa parece usar .netstandard2.1 así que tenemos que crear
            // esta función por separado para validar.
            // TODO: Mejorar esto. Idealmente el objeto no podría ser siquiera creado si no es válido
            public readonly bool IsValid() => !(ranges.Any(x => !x.IsValid()) || refreshInMilliseconds <= 0);
            public override string ToString() => $"{{ ranges: [ {string.Join(", ", ranges.Select(x => x.ToString()))} ] , refreshInMilliseconds: {refreshInMilliseconds}, isTest: {isTest} }}";
            public Command ToCommand()
            {
                if (!IsValid())
                    throw new InvalidDataException("No se puede convertir a Command un CommandRequest inválido");
                return new Command
                {
                    temperatureRanges = ranges.Select(x => x.ToTemperatureRange()).ToArray(),
                    refreshInMilliseconds = refreshInMilliseconds,
                    isTest = isTest
                };
            }
        };

        static public Message Parse(string message)
        {
            // Notar que esto parsearía correctamente Type != MessageType.Command donde data != null. 
            // Creo que eso no importaría porque datos de más (dentro del tamaño límite del paquete)
            // no son un problema en nuestro caso (se ignoran), datos de menos sí
            Message m = JsonSerializer.Deserialize<Message>(message);
            if (m.type == MessageType.Command && (m.data is null || !m.data.Value.IsValid()))
                throw new FormatException($"'{message}' no es un formato de mensaje válido");
            return m;
        }
        static public bool TryParse(string message, out Message returned)
        {
            try
            {
                returned = Parse(message);
                return true;
            }
            catch (Exception)
            {
                returned = default;
                return false;
            }
        }

        public override string ToString()
        {
            if (MessageType.Command != type)
                return $"{{ type: {type} }}";
            if (data is null)
                throw new InvalidDataException("Message.parsedCommand no puede ser nulo si Message.Type == Message.MessageType.Start");
            return $"{{ type: {type}, data: {data} }}";
        }
    }
}