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

        /// <summary>
        /// Override to sanity-check loaded values and log warnings for nonsensical input (e.g. negative
        /// multipliers, a min above its max). Runs after the JSON has been merged onto this object, so it
        /// sees the user's actual values. Clamp/correct in place — the defaults make a no-op override fine.
        /// </summary>
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

                // Re-save the merged config so fields added in a newer mod version are populated into
                // the file with their defaults. PopulateObject above keeps the user's existing values;
                // re-serializing only fills in the missing keys (and drops any obsolete ones).
                SaveConfiguration();
            }
            else
            {
                SaveConfiguration();
            }
        }
    }
}
