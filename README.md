# MeadowApp - Control y Monitoreo de Temperatura

## Descripción
Este proyecto implementa una aplicación en C# para dispositivos Meadow que permite el monitoreo y control de la temperatura utilizando un sensor analógico TMP36. Además, se conecta a una red WiFi y proporciona una interfaz basada en WebSockets para recibir comandos y enviar notificaciones de temperatura en tiempo real.

## Funcionalidades Principales

### 1. **Inicialización y Configuración**
   - `Run()`: Método principal que inicia la aplicación, configura los sensores y establece el modo inicial del sistema.
   - `SensorSetup()`: Configura el sensor de temperatura y define la función de callback para manejar las actualizaciones de temperatura.

### 2. **Gestión de Conexión a la Red y Servidor Web**
   - `LaunchNetworkAndWebserver()`: Conecta el dispositivo a una red WiFi y lanza un servidor WebSocket para la comunicación con clientes externos.
   - `ConnectionFinishedHandler()`: Maneja la finalización de la conexión, activando el proceso de apagado si se pierde la conexión.

### 3. **Manejo del Sensor de Temperatura**
   - `TemperatureUpdateHandler()`: Se activa cuando el sensor actualiza su medición, almacena la nueva temperatura y ejecuta el controlador de temperatura.

### 4. **Control de la Temperatura**
   - `TemperatureControllerHandler()`: Evalúa la temperatura actual y ajusta el actuador si el sistema está en modo de operación.

### 5. **Recepción y Manejo de Comandos**
   - `MessageHandler()`: Procesa los mensajes recibidos a través del servidor WebSocket y gestiona los comandos de configuración, inicio y apagado del sistema.
   - `Shutdown()`: Detiene la ejecución del sistema según el motivo de cancelación recibido.

### 6. **Ejecución de una Ronda de Control de Temperatura**
   - `StartRound()`: Ejecuta una ronda de control basada en los parámetros de temperatura establecidos, registrando tiempos en rango y fuera de rango, y notificando al cliente.


## Instalación y Uso
1. Conectar el dispositivo Meadow.
2. Configurar los datos de la red WiFi en `Secrets.WIFI_NAME` y `Secrets.WIFI_PASSWORD`.
3. Compilar y desplegar la aplicación en el dispositivo.
4. Abrir el archivo TemperatureWarriorCode/Client/www/index.html
5. Si el despliegue ha sido correcto, por terminal debe aparecer una ip:port que deberemos copiar en IP del Servidor (Tened en cuenta que es necesario que vuestra máquina y la Meadow estén conectadas a la misma red WiFi) 

![](imgs/connection.png)

6. Si el mensaje es correcto , se verá en la interfaz cómo la conexión ha sido exitosa. 


## Configuración Sensor TMP36 en ProtoBoard

Si no ha habido ningún problema, se puede observar la temperatura descomentando la línea 164 del fichero [MeadowApp.cs](TemperatureWarriorCode/MeadowApp.cs). 

El siguiente paso es conectar el sensor. En la siguiente imagen se observa la configuración.

![](imgs/sensor_setup.png)

La imagen muestra la conexión de un sensor de temperatura TMP36 en una protoboard, con cables que llevan alimentación, tierra y señal hacia una placa Meadow. Aquí está el análisis del setup:

1. Pin de alimentación (VCC, 5V): Cable rojo conectado a la línea de +5V en la protoboard.
2. Pin de tierra (GND): Cable amarillo conectado a la línea de GND en la protoboard.
3. Pin de salida de señal (A02): Cable verde que lleva la señal analógica del sensor hacia la placa Meadow.



