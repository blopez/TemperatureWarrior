using System;

namespace TemperatureWarriorCode
{
    
    class TemperatureController
    {


        bool isWorking = false;
        double outputUpperbound;
        double outputLowerbound;
        long sampleTimeInMilliseconds;
        double setpoint;


        public TemperatureController(double outputUpperbound, double outputLowerbound, long sampleTimeInMilliseconds)
        {
            
            this.outputUpperbound = outputUpperbound;
            this.outputLowerbound = outputLowerbound;
            this.sampleTimeInMilliseconds = sampleTimeInMilliseconds;
        }

        

        void SetWorkingMode(bool workingMode)
        {
            
            isWorking = workingMode;
        }

        public void Start()
        {
            SetWorkingMode(true);
        }

        public void Stop()
        {
            SetWorkingMode(false);
        }

        public void SetSetpoint(double setpoint)
        {
            this.setpoint = setpoint;
        }

        
    }
}