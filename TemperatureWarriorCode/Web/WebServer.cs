// C#
using System;
using System.IO;
using System.Threading.Tasks;

using System.Net;
using System.Net.Sockets;

using System.Text;
using System.Text.RegularExpressions;

// Meadow
using Meadow;

namespace TemperatureWarriorCode.Web
{

    // Servidor de WebSocket
    // NOTA: Este código expande en la implementación de https://developer.mozilla.org/en-US/docs/Web/API/WebSockets_API/Writing_WebSocket_server
    // NOTA: No se usa el soporte a WebSocket que brinda C# en el módulo Http porque al parecer no está soportado por netstandard2.1 (o por el subconjunto que posiblemente use Meadow).
    //             Usar ese módulo puede compilar pero las peticiones nunca son detectadas como WebSocket (Request.IsWebSocketRequest siempre es falso).
    class WebSocketServer
    {
        public IPAddress serverIp;
        public int serverPort;

        public delegate void MessageReceivedHandler(WebSocketServer webServer, NetworkStream connection, Message message);
        public event MessageReceivedHandler MessageReceived = delegate { };
        protected void OnMessageReceived(NetworkStream connection, Message message) => MessageReceived.Invoke(this, connection, message);

        public delegate void ConnectionFinishedHandler(WebSocketServer webServer, NetworkStream connection);
        public event ConnectionFinishedHandler ConnectionFinished = delegate { };
        protected void OnConnectionFinished(NetworkStream connection) => ConnectionFinished.Invoke(this, connection);

        public WebSocketServer(string ip_, int port_)
        {
            serverIp = IPAddress.Parse(ip_);
            serverPort = port_;
        }

        public WebSocketServer(IPAddress ip_, int port_)
        {
            serverIp = ip_;
            serverPort = port_;
        }

        public async Task Start()
        {
            var server = new TcpListener(serverIp, serverPort);
            server.Start();
            Resolver.Log.Info($"[WebServer] Escuchando en {serverIp}:{serverPort}");
            while (true)
            {
                var client = await server.AcceptTcpClientAsync();
                // Manejamos cada conexión en su propio task a manera
                // de poder manejar varias conexiones concurrentemente
                _ = Task.Run(async () => await HandleConnection(client));
            }
        }

        private async Task HandleConnection(TcpClient client)
        {
            Resolver.Log.Info("[WebServer] ### Init: HandleConnection() ###");

            bool isConnectionLost = false;
            // Este NetworkStream se pasa por parámetro a la mayoría de los métodos
            // Decidí no hacer una variable miembro del WebsocketServer que la almacene
            // para facilitar el manejo concurrente de más de una conexión.
            NetworkStream connection = client.GetStream();
            byte[] packet = new byte[512];
            int readBytes = 0;
            while (readBytes < 3)
            { // esperar por "GET" para iniciar handshake
                var rb = connection.Read(new ArraySegment<byte>(packet, readBytes, packet.Length - readBytes));
                if (rb <= 0)
                {
                    isConnectionLost = true;
                    break;
                }
                readBytes += rb;
            }
            if (isConnectionLost)
                return;

            string stringifiedPacket = Encoding.UTF8.GetString(packet);
            if (Regex.IsMatch(stringifiedPacket, "^GET", RegexOptions.IgnoreCase))
            {
                await HandleHandshake(connection, stringifiedPacket);
            }
            else
            {
                Resolver.Log.Info("[WebServer] Handshake WebSocket inválido. GET no encontrado");
                return;
            }

            while (true)
            {
                Resolver.Log.Info("[WebServer] Esperando mensaje...");
                // El header más pequeño de un paquete es de 2 bytes (2 bytes header básico + 0 payload)
                Array.Clear(packet, 0, packet.Length);
                readBytes = 0;
                while (readBytes < 2)
                {
                    var rb = connection.Read(new ArraySegment<byte>(packet, readBytes, packet.Length - readBytes));
                    if (rb <= 0)
                    {
                        isConnectionLost = true;
                        break;
                    }
                    readBytes += rb;
                }
                if (isConnectionLost)
                    break;

                var payload = PacketToPayload(packet);
                if (payload is null)
                { // Mensaje no soportado, romper conexión
                    Resolver.Log.Info("[WebServer] Mensaje no soportado por el servidor");
                    break;
                }

                if (payload.Length == 0)
                { // keep-alive
                    Resolver.Log.Info("[WebServer] Mensaje de longitud 0. Considerado keep-alive");
                    continue;
                }

                // Tenemos un mensaje como la gente (a no ser que recibamos algo que no sea reconocido como mensaje websocket, en tal caso explota el mundo)
                Message message;
                if (!Message.TryParse(payload, out message))
                {
                    Resolver.Log.Info($"[WebServer] Mensaje en mal formato: '{payload}'");
                    await SendMessage(connection, "{\"type\": \"Bad Format\"}");
                    continue; // Mensaje en formato inválido. Ignorar (quizás deberíamos fallar)
                }

                OnMessageReceived(connection, message);
            }
            OnConnectionFinished(connection);
            connection.Close();
            Resolver.Log.Info("### Fin: HandleConnection() ###");
        }

        private Task HandleHandshake(NetworkStream connection, string stringifiedPacket)
        {
            // 1. Obtain the value of the "Sec-WebSocket-Key" request header without any leading or trailing whitespace
            // 2. Concatenate it with "258EAFA5-E914-47DA-95CA-C5AB0DC85B11" (a special GUID specified by RFC 6455)
            // 3. Compute SHA-1 and Base64 hash of the new value
            // 4. Write the hash back as the value of "Sec-WebSocket-Accept" response header in an HTTP response
            string swk = Regex.Match(stringifiedPacket, "Sec-WebSocket-Key: (.*)").Groups[1].Value.Trim();
            string swka = swk + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
            byte[] swkaSha1 = System.Security.Cryptography.SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(swka));
            string swkaSha1Base64 = Convert.ToBase64String(swkaSha1);

            // HTTP/1.1 defines the sequence CR LF as the end-of-line marker
            byte[] response = Encoding.UTF8.GetBytes(
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Connection: Upgrade\r\n" +
                "Upgrade: websocket\r\n" +
                "Sec-WebSocket-Accept: " + swkaSha1Base64 + "\r\n\r\n");

            return connection.WriteAsync(response, 0, response.Length);
        }

        private string? PacketToPayload(byte[] packet)
        {
            bool fin = (packet[0] & 0b10000000) != 0;
            bool mask = (packet[1] & 0b10000000) != 0; // must be true, "All messages from the client to the server have this bit set"
            int opcode = packet[0] & 0b00001111; // expecting 1 - text message

            // Si alguna de las siguientes cosas no se cumple, fallar
            // - Solamente esperamos recibir texto (opcode 1).
            // - No manejamos chunks de texto en distintos paquetes por lo que fin debe estar seteado.
            // - Mask siempre debe estar seteado en mensajes del cliente.
            //if (opcode != 1 || !fin || !mask)
            //    return null;

            ulong offset = 2; // offset mínimo del mensaje (según msglen puede ser mayor, ver switch debajo)
            ulong msglen = packet[1] & (ulong)0b01111111;
            switch (msglen)
            {
                case 126:
                    {
                        // La longitud del mensaje está en los próximos 2 bytes
                        // bytes tomados de mayor índice en packet a menor índice en packet porque vienen en orden de red
                        // (Big-Endian) y el BitConverter los espera en Little-Endian - porque aparentemente el Cortex M7 
                        // de la Meadow está configurado para little-endian
                        msglen = BitConverter.ToUInt16([packet[3], packet[2]], 0);
                        offset = 4;
                        break;
                    }
                case 127:
                    {
                        // La longitud del mensaje está en los próximos 8 bytes
                        msglen = BitConverter.ToUInt64([packet[9], packet[8], packet[7], packet[6], packet[5], packet[4], packet[3], packet[2]], 0);
                        offset = 10;
                        break;
                    }
                case 0:
                    {
                        // Podría ser un keep-alive, lo dejamos pasar
                        return "";
                    }
                default:
                    {
                        break;
                    }
            }

            byte[] decoded = new byte[msglen];
            byte[] masks = [packet[offset], packet[offset + 1], packet[offset + 2], packet[offset + 3]];
            offset += 4;

            for (ulong i = 0; i < msglen; ++i)
                decoded[i] = (byte)(packet[offset + i] ^ masks[i % 4]);

            return Encoding.UTF8.GetString(decoded);
        }

        public Task SendMessage(NetworkStream connection, string message)
        {
            var body = Encoding.UTF8.GetBytes(message);
            if (body.Length > 255)
                throw new InvalidDataException("Longitud de mensaje no soportado (> 255)");

            byte[] header = [0b10000010, (byte)body.Length];
            byte[] response = [.. header, .. body];
            return connection.WriteAsync(response, 0, response.Length);
        }
    }
}
