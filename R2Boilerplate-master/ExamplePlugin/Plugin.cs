using BepInEx;
using BepInEx.Logging;
using R2API;

namespace CookBook
{
    [BepInDependency(LanguageAPI.PluginGUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(ItemAPI.PluginGUID)]

    // This attribute is required, and lists metadata for your plugin.
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]

    public class CookBook : BaseUnityPlugin
    {
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "rainorshine";
        public const string PluginName = "CookBook";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;
        private CraftPlanner _planner; // crafting planner

        public void Awake()
        {
            Log = Logger;
            Log.LogInfo("CookBook: Awake()");

            RecipeProvider.Init(Log); // Parse all chef recipe rules

            // TODO: initialize settings ui and pull maxdepth from it
            // also allow maxdepth to be changed dynamically

            _planner = new CraftPlanner(RecipeProvider.Recipes, maxDepth: 3);
            ChefStateController.Init(Log, _planner); // Initialize chef/state logic
        }
    }
}
