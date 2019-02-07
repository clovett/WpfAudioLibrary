using WpfAudioTool.Utilities;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace WpfAudioTool
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        Settings settings;
        DelayedActions saveActions = new DelayedActions();

        public async Task<Settings> LoadSettings()
        {
            if (this.settings == null)
            {
                this.settings = await Settings.LoadAsync();
                this.settings.PropertyChanged += Settings_PropertyChanged;
            }
            return this.settings;
        }

        private void Settings_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            saveActions.StartDelayedAction("Save Settings", new Action(async () =>
            {
                await settings.SaveAsync();
            }), TimeSpan.FromSeconds(1));
        }
    }
}
