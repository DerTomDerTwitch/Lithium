using MelonLoader.Utils;
using Newtonsoft.Json;

namespace Lithium.Modules
{
    public abstract class ModuleConfiguration
    {
        private static readonly string ConfigFolder = Path.Combine(MelonEnvironment.UserDataDirectory, "Lithium");

        [JsonIgnore] public abstract string Name { get; }

        [JsonProperty(Order = -500)]
        public bool Enabled;

        public string GetConfigFile() => Path.Combine(ConfigFolder, $"{Name}.json");

        public virtual void Validate() { }

        public void SaveConfiguration()
        {
            if (!Directory.Exists(ConfigFolder))
            {
                Directory.CreateDirectory(ConfigFolder);
            }

            string configFilePath = GetConfigFile();
            string json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(configFilePath, json);
        }

        public void LoadConfiguration()
        {
            string configFilePath = GetConfigFile();

            if (File.Exists(configFilePath))
            {
                string json = File.ReadAllText(configFilePath);
                JsonConvert.PopulateObject(json, this);

                SaveConfiguration();
            }
            else
            {
                SaveConfiguration();
            }
        }
    }
}
