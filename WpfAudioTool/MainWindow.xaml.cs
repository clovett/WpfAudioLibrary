using AudioLibrary;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using WpfAudioTool.Utilities;
using System.Diagnostics;

namespace WpfAudioTool
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        DelayedActions delayedActions = new DelayedActions();
        List<AudioDevice> devices;
        Settings settings;
        AudioDevice selectedDevice;
        AudioRecording recording;
        bool isRecording;
        string titlePattern;
        List<float> samples = new List<float>();


        public MainWindow()
        {
            InitializeComponent();
            this.titlePattern = this.Title;
            
            UiDispatcher.Initialize();

            RestoreSettings();

            this.SizeChanged += OnWindowSizeChanged;
            this.LocationChanged += OnWindowLocationChanged;
        }

        private void OnOpenFile(object sender, RoutedEventArgs e)
        {
            StopRecording();
            Microsoft.Win32.OpenFileDialog od = new Microsoft.Win32.OpenFileDialog();
            od.Filter = "WAV files (*.wav)|*.wav|m4a files (*.m4a)|*.m4a|mp3 files (*.mp3)|*.mp3|All files (*.*)|*.*";
            od.CheckFileExists = true;
            if (od.ShowDialog() == true)
            {
                OpenAudioFile(od.FileName);
            }
        }

        private void OpenAudioFile(string fileName)
        {
            this.isRecording = true;
            string wavFile = null;
            this.samples = new List<float>();
            if (System.IO.Path.GetExtension(fileName).ToLowerInvariant() == ".wav")
            {
                wavFile = fileName;
            }
            else
            {
                // then we'll load the audio and convert it to a wav file.
                wavFile = GetNextWavFileName();
            }
            
            try
            {
                this.recording = new AudioRecording(fileName, wavFile, (s, sample) =>
                {
                    // handle PCM data on background thread
                    UiDispatcher.RunOnUIThread(new Action(() =>
                    {
                        if (sample.Error != null)
                        {
                            ShowStatus(sample.Error);
                            StopRecording();
                        }
                        else
                        {
                            if (this.isRecording)
                            {
                                CollectSamples(sample);
                            }
                            if (sample.Closed)
                            {
                                StopRecording();
                            }
                            else
                            {
                                this.recording.ReadNextFrame();
                            }
                        }
                    }));
                });
            }
            catch (Exception ex)
            {
                ShowStatus(ex.Message);
            }
            SetButtonState();
        }

        private void OnClear(object sender, RoutedEventArgs e)
        {
            StopRecording();
            this.SoundChart.Clear();
        }

        private void OnSettings(object sender, RoutedEventArgs e)
        {
            XamlExtensions.Flyout(AppSettingsPanel);
        }

        private void OnWindowLocationChanged(object sender, EventArgs e)
        {
            delayedActions.StartDelayedAction("SaveWindowLocation", SavePosition, TimeSpan.FromMilliseconds(1000));
        }

        private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
        {
            delayedActions.StartDelayedAction("SaveWindowLocation", SavePosition, TimeSpan.FromMilliseconds(1000));
        }

        private async void RestoreSettings()
        {
            this.settings = await ((App)App.Current).LoadSettings();
            if (settings.WindowLocation.X != 0 && settings.WindowSize.Width != 0 && settings.WindowSize.Height != 0)
            {
                // make sure it is visible on the user's current screen configuration.
                var bounds = new System.Drawing.Rectangle(
                    XamlExtensions.ConvertFromDeviceIndependentPixels(settings.WindowLocation.X),
                    XamlExtensions.ConvertFromDeviceIndependentPixels(settings.WindowLocation.Y),
                    XamlExtensions.ConvertFromDeviceIndependentPixels(settings.WindowSize.Width),
                    XamlExtensions.ConvertFromDeviceIndependentPixels(settings.WindowSize.Height));
                var screen = System.Windows.Forms.Screen.FromRectangle(bounds);
                bounds.Intersect(screen.WorkingArea);

                this.Left = XamlExtensions.ConvertToDeviceIndependentPixels(bounds.X);
                this.Top = XamlExtensions.ConvertToDeviceIndependentPixels(bounds.Y);
                this.Width = XamlExtensions.ConvertToDeviceIndependentPixels(bounds.Width);
                this.Height = XamlExtensions.ConvertToDeviceIndependentPixels(bounds.Height);
            }
            this.Visibility = Visibility.Visible;
            OnSettingsLoaded();
        }

        void SavePosition()
        {
            var bounds = this.RestoreBounds;
            if (this.settings != null)
            {
                settings.WindowLocation = bounds.TopLeft;
                settings.WindowSize = bounds.Size;
            }
        }

        private void SetButtonState()
        {
            RecordButton.Visibility = this.isRecording ? Visibility.Collapsed : Visibility.Visible;
            StopButton.Visibility = this.isRecording ? Visibility.Visible : Visibility.Collapsed;
            PlayButton.Visibility = this.samples == null ? Visibility.Collapsed : Visibility.Visible;
        }

        private void OnRecord(object sender, RoutedEventArgs e)
        {
            this.samples = new List<float>();
            this.isRecording = true;
            string filename = GetNextWavFileName();
            try
            {
                this.recording = this.selectedDevice.StartRecording(filename, (s, sample) =>
                {
                    // handle PCM data on background thread
                    // 
                    UiDispatcher.RunOnUIThread(new Action(() =>
                    {
                        if (this.isRecording)
                        {
                            CollectSamples(sample);
                        }
                        if (sample.Error != null)
                        {
                            ShowStatus(sample.Error);
                        } 
                        else if (!sample.Closed)
                        {
                            // keep pumping no matter what so we don't get any buffering on next recording.
                            this.recording.ReadNextFrame();
                        }
                    }));
                });
            }
            catch (Exception ex)
            {
                ShowStatus(ex.Message);
            }
            SetButtonState();
        }

        public void CollectSamples(AudioSample sample)
        {
            if (this.recording != null && sample.Data != null)
            {
                float[] data = this.recording.ConvertToFloat(sample.Data);
                this.samples.AddRange(data);
            }
        }

        public string WavFileLocation
        {
            get
            {
                return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), @"AudioTool");
            }
        }

        private string GetNextWavFileName()
        {
            string wavFileLocation = WavFileLocation;
            System.IO.Directory.CreateDirectory(wavFileLocation);
            int index = 0;
            string filename = null;
            while (true)
            {
                filename = System.IO.Path.Combine(wavFileLocation, "Recording" + index + ".wav");
                if (!System.IO.File.Exists(filename))
                {
                    break;
                }
                index++;
            }
            return filename;
        }

        private void OnDeviceSelected(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0)
            {
                SelectDevice((AudioDevice)e.AddedItems[0]);
            }
        }

        private void OnListBoxMouseUp(object sender, MouseButtonEventArgs e)
        {
            AudioDeviceDropDown.ClosePopup();
        }

        private void OnSettingsLoaded()
        {
            this.devices = new List<AudioDevice>();
            try
            {
                AudioConfiguration config = new AudioConfiguration();
                this.devices = config.ListDevices();
            }
            catch (Exception ex)
            {
                ShowStatus(ex.Message);
            }
            SetButtonState();
            if (this.devices.Count > 0)
            {
                AudioDevice device = null;
                // select the last used microphone or the first one in the list.
                if (!string.IsNullOrEmpty(this.settings.DeviceName))
                {
                    device = (from i in this.devices where i.Name == this.settings.DeviceName select i).FirstOrDefault();
                }
                if (device == null)
                {
                    device = this.devices[0];
                }
                SelectDevice(device);
            }
            else
            {
                ShowStatus("No audio capture devices found");
            }
            if (this.settings.LastFile != null && System.IO.File.Exists(this.settings.LastFile))
            {
                OpenAudioFile(this.settings.LastFile);
            }
            this.Title = string.Format(this.titlePattern, this.settings.LastFile);
        }

        void SelectDevice(AudioDevice device)
        {
            this.selectedDevice = device;
            ShowStatus("Selected audio device: " + device.Name);

            settings.DeviceName = device.Name;
        }

        void ShowStatus(string msg)
        {
            StatusText.Text = msg;
        }
        
        private void OnDeviceListLoaded(object sender, RoutedEventArgs e)
        {
            ListBox deviceListBox = (ListBox)sender;
            deviceListBox.Items.Clear();
        
            foreach (var d in this.devices)
            {
                deviceListBox.Items.Add(d);
            }
            deviceListBox.SelectedItem = this.selectedDevice;
        }

        private void OnStop(object sender, RoutedEventArgs e)
        {
            StopRecording();
        }

        void StopRecording()
        { 
            this.isRecording = false;
            if (this.recording != null)
            {
                try
                {
                    this.recording.StopRecording();
                    this.settings.LastFile = this.recording.FileName;
                    this.Title = string.Format(this.titlePattern, this.settings.LastFile);
                }
                catch (Exception ex)
                {
                    ShowStatus(ex.Message);
                }
                SetButtonState();
                SoundChart.ShowSamples(this.samples);
                if (!this.recording.Loading)
                {
                    ShowStatus("saved audio to: " + this.recording.FileName);
                }
            }
        }

        private void OnPlay(object sender, RoutedEventArgs e)
        {
            //MediaElement me;
            if (this.recording != null)
            {
                MediaPlayer.LoadedBehavior = MediaState.Manual;
                MediaPlayer.Source = new Uri(this.recording.FileName);
                MediaPlayer.Play();
            }
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (this.isRecording)
            {
                this.recording.StopRecording();
            }
        }
    }
}


