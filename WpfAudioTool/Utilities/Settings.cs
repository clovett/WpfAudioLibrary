﻿using Microsoft.Storage;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace WpfAudioTool.Utilities
{
    public enum AppTheme
    {
        Light,
        Dark
    }

    public class Settings : INotifyPropertyChanged
    {
        const string SettingsFileName = "settings.xml";
        string fileName;
        Point windowLocation;
        Size windowSize;
        string deviceName;
        AppTheme theme = AppTheme.Dark;

        static Settings _instance;

        public Settings()
        {
            _instance = this;
        }

        public static string SettingsFolder
        {
            get
            {
                string appSetttingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"LovettSoftware\WpfAudioTool");
                Directory.CreateDirectory(appSetttingsPath);
                return appSetttingsPath;
            }
        }

        public static Settings Instance
        {
            get
            {
                if (_instance == null)
                {
                    return new Settings();
                }
                return _instance;
            }
        }

        public Point WindowLocation
        {
            get { return this.windowLocation; }
            set
            {
                if (this.windowLocation != value)
                {
                    this.windowLocation = value;
                    OnPropertyChanged("WindowLocation");
                }
            }
        }

        public Size WindowSize
        {
            get { return this.windowSize; }
            set {
                if (this.windowSize != value)
                {
                    this.windowSize = value;
                    OnPropertyChanged("WindowSize");
                }
            }
        }

        public AppTheme Theme
        {
            get { return this.theme; }
            set { this.theme = value; }
        }

        public string LastFile
        {
            get
            {
                return this.fileName;
            }
            set
            {
                if (this.fileName != value)
                {
                    this.fileName = value;
                    OnPropertyChanged("LastFile");
                }
            }
        }

        public string DeviceName
        {
            get { return this.deviceName; }
            set {
                if (this.deviceName != value)
                {
                    this.deviceName = value;
                    OnPropertyChanged("DeviceName");
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name)
        {
            if (PropertyChanged != null)
            {
                UiDispatcher.RunOnUIThread(() =>
                {
                    PropertyChanged(this, new PropertyChangedEventArgs(name));
                });
            }
        }

        public static async Task<Settings> LoadAsync()
        {
            var store = new IsolatedStorage<Settings>();
            Settings result = null;
            try
            {
                Debug.WriteLine("Loading settings from : " + SettingsFolder);
                result = await store.LoadFromFileAsync(SettingsFolder, SettingsFileName);
            }
            catch
            {
            }
            if (result == null)
            {
                result = new Settings();
                await result.SaveAsync();
            }
            return result;
        }

        bool saving;

        public async Task SaveAsync()
        {
            var store = new IsolatedStorage<Settings>();
            if (!saving)
            {
                saving = true;
                try
                {
                    Debug.WriteLine("Saving settings to : " + SettingsFolder);
                    await store.SaveToFileAsync(SettingsFolder, SettingsFileName, this);
                }
                finally
                {
                    saving = false;
                }
            }
        }

    }


}
