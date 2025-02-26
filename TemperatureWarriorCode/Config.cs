using System;
using System.Net.Http.Headers;
using Meadow.Units;

namespace TemperatureWarriorCode {
    public static class Config {
        //WEB VARIABLES
        public const int Port = 2550;
        // PASSWORD COMUNICACIÓN
        public const string PASS = "pass";
        //START ROUND VARIABLES
        public static bool isWorking = false;
        public static readonly Temperature TemperatureUpperbound = new Temperature(30, Temperature.UnitType.Celsius);
        public static readonly Temperature TemperatureLowerbound = new Temperature(12, Temperature.UnitType.Celsius);
    }
}
