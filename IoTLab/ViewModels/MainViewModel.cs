using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using IoTLab.Models;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using Microsoft.Toolkit.Uwp.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Devices.Geolocation;
using Windows.Devices.Gpio;
using Windows.Devices.Spi;
using Windows.UI;
using Windows.UI.Xaml.Media;

namespace IoTLab.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private const Int32 SPI_CHIP_SELECT_LINE = 0;       /* Line 0 maps to physical pin number 24 on the Rpi2, Line 1 maps to physical pin number 26 on the Rpi2        */
        private SpiDevice SpiADC;

        private readonly byte StartByte = 0x01;
        private readonly byte Channel0 = 0x80; /* 10000000 channel 0  128 */
        private readonly byte Channel1 = 0x90; /* 10010000 channel 1  144 */

        private static SolidColorBrush redBrush = new SolidColorBrush(Colors.Red);
        private static SolidColorBrush grayBrush = new SolidColorBrush(Colors.Gray);
        private static SolidColorBrush blueBrush = new SolidColorBrush(Colors.Blue);

        private const int LED_CONTROL = 18;
        private const int LED_ALERT = 23;

        private GpioPin ledControl;
        private GpioPin ledAlert;

        private static GpioPinValue ledControlValue = GpioPinValue.Low;
        private GpioPinValue ledAlertValue = GpioPinValue.Low;

        private Timer _readTimer;  // To query the sensors every x minutes

        public ObservableCollection<Reading> Readings { set; get; }

        public RelayCommand AlertCommand { get; set; }

        public RelayCommand ControlCommand { get; set; }

        public MainViewModel()
        {
            GalaSoft.MvvmLight.Threading.DispatcherHelper.Initialize();

            AlertCommand = new RelayCommand(ToggleAlert);

            ControlCommand = new RelayCommand(ToggleControl);

            InitAll();
        }

        private async void InitAll()
        {
            Readings = new ObservableCollection<Reading>();

            try
            {
                await InitGPIO();    /* Initialize the SPI bus for communicating with the ADC      */
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                return;
            }

            try
            {
                await InitSPI();    /* Initialize the SPI bus for communicating with the ADC      */
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                return;
            }

            StartMeasurements();
        }

        private async Task InitSPI()
        {
            SpiConnectionSettings spiSettings = new SpiConnectionSettings(SPI_CHIP_SELECT_LINE); //we are using line 0
            spiSettings.ClockFrequency = 500000;   /* 0.5MHz clock rate                                        */
            spiSettings.Mode = SpiMode.Mode0;      /* The ADC expects idle-low clock polarity so we use Mode0  */

            //Get the default SPI bus
            SpiController controller = await SpiController.GetDefaultAsync();
            SpiADC = controller.GetDevice(spiSettings);
        }

        private async Task InitGPIO()
        {
            GpioController gpio = GpioController.GetDefault();

            ledControl = gpio.OpenPin(LED_CONTROL);
            ledAlert = gpio.OpenPin(LED_ALERT);

            // Initialize LED to the OFF state by first writing a Low value
            ledControl.Write(ledControlValue);
            ledControl.SetDriveMode(GpioPinDriveMode.Output);

            ledAlert.Write(ledAlertValue);
            ledAlert.SetDriveMode(GpioPinDriveMode.Output);
        }

        private void StartMeasurements()
        {
            DateTime nextRead = DateTime.Now;

            int h = nextRead.Hour;
            int m = nextRead.Minute + 1;

            nextRead = nextRead.Date;
            nextRead = nextRead.AddHours(h);
            nextRead = nextRead.AddMinutes(m);

            Debug.WriteLine("Readings start at {0}", nextRead);

            _readTimer = new Timer(async (e) =>
            {
                await ReadSensorAsync();
            }, null, (int)nextRead.Subtract(DateTime.Now).TotalMilliseconds, (int)TimeSpan.FromMinutes(1).TotalMilliseconds);
        }

        private async Task ReadSensorAsync()
        {
            GalaSoft.MvvmLight.Threading.DispatcherHelper.CheckBeginInvokeOnUI(async () =>
            {
                DateTime ReadingTime = DateTime.Now;

                //Get PhotoResistor
                byte[] readBuffer = new byte[3]; /* Buffer to hold read data*/
                byte[] writeBuffer = new byte[3] { StartByte, Channel0, 0x00 };

                SpiADC.TransferFullDuplex(writeBuffer, readBuffer); /* Read data from the ADC                           */
                Light = convertToInt(readBuffer);                /* Convert the returned bytes into an integer value */

                //Get Temperature
                readBuffer = new byte[3]; /* Buffer to hold read data*/
                writeBuffer = new byte[3] { StartByte, Channel1, 0x00 };

                SpiADC.TransferFullDuplex(writeBuffer, readBuffer); /* Read data from the ADC                           */
                int adcTemp = convertToInt(readBuffer);                /* Convert the returned bytes into an integer value */
                                                                       // millivolts = value * (volts in millivolts / ADC steps)
                double millivolts = adcTemp * (3300.0 / 1024.0);
                Temperature = (millivolts - 500) / 10.0; //given equation from sensor documentation

                if (Temperature > AlertTemperature)
                {
                    string m = string.Format("Temperature {0} exceeds Alert Temperature {1}", Temperature, AlertTemperature);
                    Alert alertMsg = new Alert { AlertMsg = m };

                    //turn on alert LED, only way to clear it is IoT Central Command)
                    ledAlertValue = GpioPinValue.High;
                    ledAlert.Write(ledAlertValue);
                    Alert = redBrush;
                }

                Reading reading = new Reading
                {
                    ReadingDateTime = ReadingTime,
                    Temperature = Temperature,
                    Light = Light,
                    Control = ledControlValue == GpioPinValue.High ? true : false
                };

                //limit the amount of data we are charting
                while (Readings.Count > 60)
                {
                    Readings.RemoveAt(0);
                }

                Readings.Add(reading);
            });
        }

        /* Convert the raw ADC bytes to an integer for MCP3008 */

        private int convertToInt(byte[] data)
        {
            int result = 0;
            //bit bashing is inevitable when you play at this level
            //Note we tossed data[0] as nothing in there we need
            result = data[1] & 0x03;
            result <<= 8;
            result += data[2];

            return result;
        }

        public void ToggleAlert()
        {
            if (ledAlertValue == GpioPinValue.High)
            {
                ledAlertValue = GpioPinValue.Low;
                Alert = grayBrush;
            }
            else
            {
                ledAlertValue = GpioPinValue.High;
                Alert = redBrush;
            }
            ledAlert.Write(ledAlertValue);
        }

        public void ClearAlert()
        {
            ledAlertValue = GpioPinValue.Low;
            ledAlert.Write(ledAlertValue);
            Alert = grayBrush;
        }

        public void ToggleControl()
        {
            if (ledControlValue == GpioPinValue.High)
            {
                ledControlValue = GpioPinValue.Low;
                Control = grayBrush;
            }
            else
            {
                ledControlValue = GpioPinValue.High;
                Control = blueBrush;
            }
            ledControl.Write(ledControlValue);
        }

        public void SetControl(bool status)
        {
            if (status)
            {
                ledControlValue = GpioPinValue.High;
                Control = blueBrush;
            }
            else
            {
                ledControlValue = GpioPinValue.Low;
                Control = grayBrush;
            }
            ledControl.Write(ledControlValue);
        }

        private double _light;

        public double Light
        {
            get
            {
                return _light;
            }
            set
            {
                Set(ref _light, value);
            }
        }

        private double _temperature;

        public double Temperature
        {
            get
            {
                return _temperature;
            }
            set
            {
                Set(ref _temperature, value);
            }
        }

        private double _alertTemperature = 20;

        public double AlertTemperature
        {
            get
            {
                return _alertTemperature;
            }
            set
            {
                Set(ref _alertTemperature, value);
            }
        }

        private SolidColorBrush _alert = grayBrush;

        public SolidColorBrush Alert
        {
            get
            {
                return _alert;
            }
            set
            {
                Set(ref _alert, value);
            }
        }

        private SolidColorBrush _control = grayBrush;

        public SolidColorBrush Control
        {
            get
            {
                return _control;
            }
            set
            {
                Set(ref _control, value);
            }
        }
    }
}
