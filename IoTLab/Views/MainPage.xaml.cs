using System;

using IoTLab.ViewModels;

using Windows.UI.Xaml.Controls;

namespace IoTLab.Views
{
    public sealed partial class MainPage : Page
    {
        private MainViewModel ViewModel
        {
            get { return ViewModelLocator.Current.MainViewModel; }
        }

        public MainPage()
        {
            InitializeComponent();
        }
    }
}
