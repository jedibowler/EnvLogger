using System;
using System.IO;
using System.Net;
using Gadgeteer.Modules.Seeed;
using Gadgeteer.Networking;
using Microsoft.SPOT;
using Microsoft.SPOT.Time;
using GT = Gadgeteer;
using GTM = Gadgeteer.Modules;

namespace EnvLogger
{
    public partial class Program
    {
        private static object LockObject = new object();

        private static bool timeSynchronized = false;
        WebEvent GetReading;
        WebEvent GetLog;
        WebEvent ClearLog;

        DateTime LastReading;

        double temp = 0;
        double rhum = 0;
        string fileName = @"\data.log";

        GT.StorageDevice storage;
        string rootDir;

        // This method is run when the mainboard is powered up or reset.   
        void ProgramStarted()
        {
            Debug.Print("Program Started");
            
            sdCard.MountSDCard();
            storage = sdCard.GetStorageDevice();
            rootDir = sdCard.GetStorageDevice().RootDirectory;

            ethernet_J11D.NetworkUp += new GTM.Module.NetworkModule.NetworkEventHandler(ethernet_J11D_NetworkUp);
            ethernet_J11D.NetworkDown += new GTM.Module.NetworkModule.NetworkEventHandler(ethernet_J11D_NetworkDown);
            ethernet_J11D.UseDHCP();

            TimeService.SystemTimeChanged += new SystemTimeChangedEventHandler(TimeService_SystemTimeChanged);
            TimeService.TimeSyncFailed += new TimeSyncFailedEventHandler(TimeService_TimeSyncFailed);

            temperatureHumidity.MeasurementComplete += new TemperatureHumidity.MeasurementCompleteEventHandler(temperatureHumidity_MeasurementComplete);
            
            GT.Timer timer = new GT.Timer(30000); 
            timer.Tick += new GT.Timer.TickEventHandler(timer_Tick);
            timer.Start();
        }

        void GetReading_WebEventReceived(string path, WebServer.HttpMethod method, Responder responder)
        {
            responder.Respond(GetNewReadings());
        }

        void timer_Tick(GT.Timer timer)
        {
            string msg = GetNewReadings();

            if (VerifySDCard())
            {
                lock (LockObject)
                {
                    using (StreamWriter sw = new StreamWriter(rootDir + fileName, true))
                    {
                        sw.WriteLine(msg);
                    }
                }
            }
        }

        private string GetNewReadings()
        {
            temperatureHumidity.RequestMeasurement();
            string msg = temp.ToString("F") + ",";
            msg += rhum.ToString("F") + ",";
            msg += LastReading.ToString();
            return msg;
        }

        void temperatureHumidity_MeasurementComplete(TemperatureHumidity sender, double temperature, double relativeHumidity)
        {
            LastReading = DateTime.Now;
            temp = temperature;
            rhum = relativeHumidity;
        }

        void TimeService_TimeSyncFailed(object sender, TimeSyncFailedEventArgs e)
        {
            Debug.Print("Error synchronizing system time with NTP server: " + e.ErrorCode);
        }

        void TimeService_SystemTimeChanged(object sender, SystemTimeChangedEventArgs e)
        {
            Debug.Print("Network time received.");
            if (!timeSynchronized)
                timeSynchronized = true;
        }

        void ethernet_J11D_NetworkDown(GTM.Module.NetworkModule sender, GTM.Module.NetworkModule.NetworkState state)
        {
            Debug.Print("Network down!");
        }

        void ethernet_J11D_NetworkUp(GTM.Module.NetworkModule sender, GTM.Module.NetworkModule.NetworkState state)
        {
            Debug.Print("Network up!");
            Debug.Print("IP Address: " + ethernet_J11D.NetworkSettings.IPAddress.ToString());
 
            // Configure TimeService settings.
            TimeServiceSettings settings = new TimeServiceSettings();
            settings.ForceSyncAtWakeUp = true;
            settings.RefreshTime = 1800;    // in seconds.

            IPAddress[] address = Dns.GetHostEntry("ntp.nasa.gov").AddressList;
            if (address != null && address.Length > 0)
                settings.PrimaryServer = address[0].GetAddressBytes();
 
            address = Dns.GetHostEntry("pool.ntp.org").AddressList;
            if (address != null && address.Length > 0)
                settings.AlternateServer = address[0].GetAddressBytes();
 
            TimeService.Settings = settings;
            TimeService.Start();

            WebServer.StartLocalServer(ethernet_J11D.NetworkSettings.IPAddress.ToString(), 80);
            GetReading = WebServer.SetupWebEvent("getreading");
            GetReading.WebEventReceived += new WebEvent.ReceivedWebEventHandler(GetReading_WebEventReceived);

            GetLog = WebServer.SetupWebEvent("getlog");
            GetLog.WebEventReceived += new WebEvent.ReceivedWebEventHandler(GetLog_WebEventReceived);

            ClearLog = WebServer.SetupWebEvent("clearlog");
            ClearLog.WebEventReceived += new WebEvent.ReceivedWebEventHandler(ClearLog_WebEventReceived);

            temperatureHumidity.RequestMeasurement();
        }

        void ClearLog_WebEventReceived(string path, WebServer.HttpMethod method, Responder responder)
        {
            if (VerifySDCard())
            {
                lock (LockObject)
                {
                    using (StreamWriter sw = new StreamWriter(rootDir + fileName))
                    {
                        sw.WriteLine(string.Empty);
                    }
                }
            }
            responder.Respond("Log cleared.");
        }

        void GetLog_WebEventReceived(string path, WebServer.HttpMethod method, Responder responder)
        {
            if (VerifySDCard())
            {
                using (StreamReader sr = new StreamReader(rootDir + fileName))
                {
                    string text = sr.ReadToEnd();
                    responder.Respond(text);
                }
            }
            else
                responder.Respond("No SD card detected.");
        }
        
        bool VerifySDCard()
        {
            if (!sdCard.IsCardInserted || !sdCard.IsCardMounted)
            {
                System.Threading.Thread.Sleep(2000);
                return false;
            }

            return true;
        }
    }
}
