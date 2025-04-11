using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSP.IO;
using FinePrint;

/*
Source code copyrighgt 2019, by Michael Billard (Angel-125)
License: GNU General Public License Version 3
License URL: http://www.gnu.org/licenses/
If you want to use this code, give me a shout on the KSP forums! :)
Wild Blue Industries is trademarked by Michael Billard and may be used for non-commercial purposes. All other rights reserved.
Note that Wild Blue Industries is a ficticious entity 
created for entertainment purposes. It is in no way meant to represent a real entity.
Any similarity to a real entity is purely coincidental.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/
namespace WBIResources
{
    [KSPScenario(ScenarioCreationOptions.AddToAllGames, GameScenes.SPACECENTER, GameScenes.EDITOR, GameScenes.FLIGHT, GameScenes.TRACKSTATION)]
    public class WBIGoldStrikeScenario : ScenarioModule
    {
        #region Constants
        public const double kMaxProspectSearchDistance = 0.1f; //km
        public const string kLodeIcon = "WildBlueIndustries/WBIResources/Icons/LodeIcon";

        public const string kGoldStrikeDataNode = "GOLDSTRIKE";
        public const string kGoldStrikeSettingsNode = "GOLDSTRIKE_SETTINGS";
        public const string kGoldStrikeLodeLimit = "GOLDSTRIKE_LODE_LIMIT";
        public const string kGoldStrikeLodeNode = "GoldStrikeLode";
        public const string kAsteroidsSearched = "AsteroidsSearched";
        public const string kAsteroidNodeName = "ProspectedAsteroid";
        private const string kName = "name";
        #endregion

        #region Housekeeping
        public static WBIGoldStrikeScenario Instance;

        //Goldstrike compatibility
        public static Dictionary<string, ConfigNode[]> goldStrikeNodes = null;

        //Gold strike
        public Dictionary<string, List<GoldStrikeData>> goldStrikeResources = new Dictionary<string, List<GoldStrikeData>>();
        public Dictionary<string, Dictionary<string, GoldStrikeLode>> goldStrikeLodes = new Dictionary<string, Dictionary<string, GoldStrikeLode>>();
        public List<String> prospectedAsteroids = new List<string>();
        public static bool debugMode = false;
        public static bool debugProspectAlwaysSuccessful = false;
        public static bool showLodeIcons = false;
        public static bool noLodeLimits = false;
        public Dictionary<string, int> lodeCountPerBiomePlanet = new Dictionary<string, int>();
        public Dictionary<string, int> lodeLimits = new Dictionary<string, int>();
        public int defaultLodesPerPlanetAndBiome = 20;
        public static float defaultMinProspectDistance = 3;

        protected void debugLog(string message)
        {
            if (debugMode)
                Debug.Log("[WBIGoldStrikeScenario] - " + message);
        }
        #endregion

        #region Overrides
        public override void OnAwake()
        {
            base.OnAwake();
            Instance = this;
            GameEvents.OnGameSettingsApplied.Add(onGameSettingsApplied);
            debugMode = GoldStrikeSettings.DebugModeEnabled;
            showLodeIcons = GoldStrikeSettings.LodeIconsVisible;
            noLodeLimits = GoldStrikeSettings.NoLodeLimits;
        }

        public void OnDestroy()
        {
            GameEvents.OnGameSettingsApplied.Remove(onGameSettingsApplied);
        }

        public void onGameSettingsApplied()
        {
            debugMode = GoldStrikeSettings.DebugModeEnabled;
            showLodeIcons = GoldStrikeSettings.LodeIconsVisible;
            noLodeLimits = GoldStrikeSettings.NoLodeLimits;

            if (showLodeIcons == false)
                removeWaypoints();
            else
                addWaypoints();
        }

        public override void OnLoad(ConfigNode node)
        {
            ConfigNode[] goldStrikeDataNodes = GameDatabase.Instance.GetConfigNodes(kGoldStrikeDataNode);
            ConfigNode[] goldStrikeLodeNodes = node.GetNodes(kGoldStrikeLodeNode);
            ConfigNode[] asteroidsSearched = node.GetNodes(kAsteroidsSearched);
            ConfigNode[] settingsNodes = GameDatabase.Instance.GetConfigNodes(kGoldStrikeSettingsNode);

            // Load settings
            if (settingsNodes.Length > 0)
            {
                loadSettings(settingsNodes[0]);
            }

            // Load lode limit nodes
            loadLodeLimitNodes();

            debugLog("OnLoad: there are " + goldStrikeDataNodes.Length + " GOLDSTRIKE items to load.");
            List<GoldStrikeData> strikeDataList;
            foreach (ConfigNode goldStrikeDataNode in goldStrikeDataNodes)
            {
                GoldStrikeData strikeData = new GoldStrikeData();
                strikeData.Load(goldStrikeDataNode);
                if (string.IsNullOrEmpty(strikeData.resourceName) == false)
                {
                    if (!goldStrikeResources.ContainsKey(strikeData.resourceName))
                    {
                        strikeDataList = new List<GoldStrikeData>();
                        goldStrikeResources.Add(strikeData.resourceName, strikeDataList);
                    }
                    strikeDataList = goldStrikeResources[strikeData.resourceName];
                    strikeDataList.Add(strikeData);
                }
            }

            //Load our data
            loadGoldStrikeLodes(goldStrikeLodeNodes);
            loadAsteroidNodes(asteroidsSearched);

            if (showLodeIcons == false)
                removeWaypoints();
        }

        protected void loadSettings(ConfigNode node)
        {
            if (node.HasValue("defaultLodesPerPlanetAndBiome"))
            {
                int.TryParse(node.GetValue("defaultLodesPerPlanetAndBiome"), out defaultLodesPerPlanetAndBiome);
            }

            if (node.HasValue("defaultMinProspectDistance"))
                float.TryParse(node.GetValue("defaultMinProspectDistance"), out defaultMinProspectDistance);
        }

        protected void loadLodeLimitNodes()
        {
            ConfigNode[] lodeLimitNodes = GameDatabase.Instance.GetConfigNodes(kGoldStrikeLodeLimit);
            ConfigNode lodeLimitNode;
            int planetID;
            string planetBiomeKey;
            string biomeName;
            string planetName;
            int maxNodes;

            Dictionary<string, int> planetIDs = new Dictionary<string, int>();
            int count = FlightGlobals.Bodies.Count;
            for (int index = 0; index < count; index++)
            {
                planetIDs.Add(FlightGlobals.Bodies[index].name, FlightGlobals.Bodies[index].flightGlobalsIndex);
            }

            for (int index = 0; index < lodeLimitNodes.Length; index++)
            {
                lodeLimitNode = lodeLimitNodes[index];

                if (lodeLimitNode.HasValue("planetName"))
                {
                    planetName = lodeLimitNode.GetValue("planetName");
                    if (planetIDs.ContainsKey(planetName) == false)
                        continue;

                    if (lodeLimitNode.HasValue("biomeName"))
                        biomeName = lodeLimitNode.GetValue("biomeName");
                    else
                        biomeName = string.Empty;

                    if (lodeLimitNode.HasValue("maxNodes"))
                        int.TryParse(lodeLimitNode.GetValue("maxNodes"), out maxNodes);
                    else
                        maxNodes = defaultLodesPerPlanetAndBiome;

                    planetBiomeKey = planetIDs[planetName].ToString() + biomeName;

                    if (lodeLimits.ContainsKey(planetBiomeKey) == false)
                        lodeLimits.Add(planetBiomeKey, 0);
                    lodeLimits[planetBiomeKey] = maxNodes;
                }
            }
        }

        protected void loadAsteroidNodes(ConfigNode[] asteroidsSearched)
        {
            debugLog("OnLoad: there are " + asteroidsSearched.Length + " asteroids that have been prospected.");
            foreach (ConfigNode asteroidNode in asteroidsSearched)
                prospectedAsteroids.Add(asteroidNode.GetValue(kName));
        }

        protected void loadGoldStrikeLodes(ConfigNode[] goldStrikeLodeNodes)
        {
            debugLog("OnLoad: there are " + goldStrikeLodeNodes.Length + " GoldStrikeLode items to load.");
            foreach (ConfigNode goldStrikeLodeNode in goldStrikeLodeNodes)
            {
                GoldStrikeLode lode = new GoldStrikeLode();
                Dictionary<string, GoldStrikeLode> lodeMap = null;
                string planetBiomeKey, lodeKey;

                lode.Load(goldStrikeLodeNode);
                planetBiomeKey = lode.planetID.ToString() + lode.biome;

                if (goldStrikeLodes.ContainsKey(planetBiomeKey) == false)
                {
                    lodeMap = new Dictionary<string, GoldStrikeLode>();
                    goldStrikeLodes.Add(planetBiomeKey, lodeMap);
                }
                lodeMap = goldStrikeLodes[planetBiomeKey];

                //Add the new lode
                lodeKey = lode.longitude.ToString() + lode.latitude.ToString() + lode.resourceName;
                lodeMap.Add(lodeKey, lode);

                // Also count how many lodes we have per planet and biome
                if (lodeCountPerBiomePlanet.ContainsKey(planetBiomeKey) == false)
                    lodeCountPerBiomePlanet.Add(planetBiomeKey, 0);
                if (lodeCountPerBiomePlanet.ContainsKey(lode.planetID.ToString()) == false)
                    lodeCountPerBiomePlanet.Add(lode.planetID.ToString(), 0);

                lodeCountPerBiomePlanet[planetBiomeKey] = lodeCountPerBiomePlanet[planetBiomeKey] + 1;
                lodeCountPerBiomePlanet[lode.planetID.ToString()] = lodeCountPerBiomePlanet[lode.planetID.ToString()] + 1;
            }
        }

        protected void removeWaypoints()
        {
            Waypoint waypoint;
            Guid guid;
            foreach (Dictionary<string, GoldStrikeLode> lodeMap in goldStrikeLodes.Values)
            {
                foreach (GoldStrikeLode lode in lodeMap.Values)
                {
                    guid = new Guid(lode.navigationID);
                    waypoint = WaypointManager.FindWaypoint(guid);
                    if (waypoint != null)
                        WaypointManager.RemoveWaypoint(waypoint);
                }
            }
        }

        protected void addWaypoints()
        {
            Waypoint waypoint;
            Guid guid;
            foreach (Dictionary<string, GoldStrikeLode> lodeMap in goldStrikeLodes.Values)
            {
                foreach (GoldStrikeLode lode in lodeMap.Values)
                {
                    guid = new Guid(lode.navigationID);
                    waypoint = WaypointManager.FindWaypoint(guid);
                    if (waypoint == null)
                    {
                        setWaypoint(lode.resourceName, lode, false);
                    }
                }
            }
        }

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);

            debugLog("OnSave: there are " + goldStrikeLodes.Values.Count + " GoldStrikeLode items to save.");
            foreach (Dictionary<string, GoldStrikeLode> lodeMap in goldStrikeLodes.Values)
            {
                foreach (GoldStrikeLode lode in lodeMap.Values)
                {
                    ConfigNode lodeNode = lode.Save();
                    node.AddNode(lodeNode);
                }
            }

            debugLog("OnSave: there are " + prospectedAsteroids.Count + " prospected asteroids to save.");
            foreach (string asteroidName in prospectedAsteroids)
            {
                ConfigNode asteroidNode = new ConfigNode(kAsteroidNodeName);
                asteroidNode.AddValue(kName, asteroidName);
                node.AddNode(asteroidNode);
            }
        }
        #endregion

        #region GoldStrike
        public void ClearProspects()
        {
            removeWaypoints();
            goldStrikeLodes.Clear();
            prospectedAsteroids.Clear();
            lodeCountPerBiomePlanet.Clear();
        }

        public bool IsLodeWaypoint(string navigationID)
        {
            Dictionary<string, GoldStrikeLode>[] lodeMaps = null;
            Dictionary<string, GoldStrikeLode> lodeMap = null;
            GoldStrikeLode[] lodes = null;
            GoldStrikeLode lode = null;

            lodeMaps = goldStrikeLodes.Values.ToArray();
            for (int index = 0; index < lodeMaps.Length; index++)
            {
                lodeMap = lodeMaps[index];
                lodes = lodeMap.Values.ToArray();
                for (int lodeIndex = 0; lodeIndex < lodes.Length; lodeIndex++)
                {
                    lode = lodes[lodeIndex];
                    if (string.IsNullOrEmpty(lode.navigationID))
                        continue;
                    if (lode.navigationID == navigationID)
                        return true;
                }
            }

            return false;
        }

        public void UpdateDrillLodes(ModuleAsteroid asteroid)
        {

            //Find all loaded vessels that have drills, and tell them to update their lode data.
            //If we prospected an asteroid then just inform the asteroid drills.
            //Otherwise just inform the ground drills.
            if (asteroid == null)
            {
                debugLog("Updating ground drills");
                List<WBIGoldStrikeDrill> groundDrills = null;
                foreach (Vessel loadedVessel in FlightGlobals.VesselsLoaded)
                {
                    groundDrills = loadedVessel.FindPartModulesImplementing<WBIGoldStrikeDrill>();
                    if (groundDrills != null)
                    {
                        foreach (WBIGoldStrikeDrill groundDrill in groundDrills)
                            groundDrill.UpdateLode();
                    }
                }
            }

            else
            {
                debugLog("Updating asteroid drills");
                List<WBIGoldStrikeAsteroidDrill> asteroidDrills = null;
                foreach (Vessel loadedVessel in FlightGlobals.VesselsLoaded)
                {
                    asteroidDrills = loadedVessel.FindPartModulesImplementing<WBIGoldStrikeAsteroidDrill>();
                    if (asteroidDrills != null)
                    {
                        debugLog("Found " + asteroidDrills.Count + " asteroid drills");
                        foreach (WBIGoldStrikeAsteroidDrill asteroidDrill in asteroidDrills)
                            asteroidDrill.UpdateLode();
                    }
                }
            }
        }

        public double GetDistanceFromLastLocation(Vessel vessel)
        {
            double distance = 0;

            //Pull last prospect location from the vessel module.
            GoldStrikeVesselModule vesselModule = null;
            foreach (VesselModule module in vessel.vesselModules)
            {
                if (module is GoldStrikeVesselModule)
                {
                    vesselModule = (GoldStrikeVesselModule)module;
                    break;
                }
            }
            if (vesselModule == null)
                return double.MaxValue;

            //If we've never set a prospect location then we're automatically ok.
            if (!vesselModule.HasLastProspectLocation())
                return double.MaxValue;

            //Calculate the distance
            distance = Utils.HaversineDistance(vesselModule.lastProspectLongitude, vesselModule.lastProspectLatitude,
                vessel.longitude, vessel.latitude, vessel.mainBody);

            return distance;
        }

        public bool IsAsteroidProspected(ModuleAsteroid asteroid)
        {
            if (asteroid == null)
                return false;

            if (this.prospectedAsteroids.Contains(asteroid.AsteroidName))
                return true;

            return false;
        }

        public bool VesselSituationValid(Vessel vessel)
        {
            if (vessel.situation == Vessel.Situations.LANDED || vessel.situation == Vessel.Situations.SPLASHED || vessel.situation == Vessel.Situations.PRELAUNCH)
                return true;
            else
                return false;
        }

        public bool lodeLimitReached(string biomeName, int planetID)
        {
            // If the game settings disables lode limits then we'll never reach lode limits.
            if (noLodeLimits)
                return false;

            // Get the current total lodes for the planet and biome
            string planetBiomeKey = planetID.ToString() + biomeName;
            if (lodeCountPerBiomePlanet.ContainsKey(planetBiomeKey) == false)
            {
                lodeCountPerBiomePlanet.Add(planetBiomeKey, 0);
            }
            int totalLodes = lodeCountPerBiomePlanet[planetBiomeKey];

            // Check for custom configs indicating how many lodes are allowed for a particular planet and biome.
            if (lodeLimits.ContainsKey(planetBiomeKey))
                return totalLodes >= lodeLimits[planetBiomeKey];

            // Check for custom configs for each of the biomes on the planet
            planetBiomeKey = planetID.ToString() + "Each";
            if (lodeLimits.ContainsKey(planetBiomeKey))
                return totalLodes >= lodeLimits[planetBiomeKey];

            // Check for custome configs specifying total number of lodes allowed for the entire planet
            planetBiomeKey = planetID.ToString();
            if (lodeCountPerBiomePlanet.ContainsKey(planetBiomeKey) == false)
            {
                lodeCountPerBiomePlanet.Add(planetBiomeKey, 0);
            }
            int totalLodesEntirePlanet = lodeCountPerBiomePlanet[planetBiomeKey];
            if (lodeLimits.ContainsKey(planetBiomeKey))
                return totalLodesEntirePlanet >= lodeLimits[planetBiomeKey];

            // No config found. Check default number of lodes allowed per planet-biome.
            return totalLodes >= defaultLodesPerPlanetAndBiome;
        }

        public ProspectSituations GetProspectSituation(Vessel vessel, out GoldStrikeLode lode, out List<GoldStrikeData> prospectResources)
        {
            ModuleAsteroid asteroid = null;
            CBAttributeMapSO.MapAttribute biome = null;
            string biomeName = string.Empty;
            int planetID = int.MaxValue;
            bool vesselSituationIsValid = false;
            double longitude = 0f;
            double latitude = 0f;
            double altitude = 0f;

            //Initialize the list of resources to prospect.
            prospectResources = null;

            //If we're landed then we're ok to check prospect situation.
            if (VesselSituationValid(vessel))
            {
                biome = Utils.GetCurrentBiome(vessel);
                biomeName = biome.name;
                planetID = vessel.mainBody.flightGlobalsIndex;
                longitude = vessel.longitude;
                latitude = vessel.latitude;
                altitude = vessel.altitude;
                vesselSituationIsValid = true;
                debugLog("Vessel is landed or prelaunch");
            }

            //If we have an asteroid, then we're ok to check prospect situation.
            asteroid = vessel.FindPartModuleImplementing<ModuleAsteroid>();
            if (asteroid != null)
            {
                biomeName = asteroid.AsteroidName;
                vesselSituationIsValid = true;
                debugLog("Vessel has an asteroid");
            }

            // Check if we've reached our lode limits
            if (lodeLimitReached(biomeName, planetID))
            {
                lode = null;
                return ProspectSituations.LodeLimitsReached;
            }

            //Is there a lode in the area?
            if (asteroid != null)
                lode = FindNearestLode(asteroid);
            else
                lode = FindNearestLode(planetID, biomeName, vessel.longitude, vessel.latitude, vessel.altitude);
            if (lode != null)
                return ProspectSituations.LodeAlreadyExists;

            //If the flight situation is bad then we're done.
            if (vesselSituationIsValid == false)
                return ProspectSituations.InvalidVesselSituation;

            //Is the prospect site an asteroid, and has it been prospected?
            if (asteroid != null)
            {
                if (prospectedAsteroids.Contains(asteroid.AsteroidName))
                    return ProspectSituations.AsteroidProspected;

                else //Record the fact that we prospected the asteroid.
                    prospectedAsteroids.Add(asteroid.AsteroidName);
            }

            //Do we have resources that can be prospected?
            else
            {
                double travelDistance = GetDistanceFromLastLocation(vessel);
                List<GoldStrikeData> resources = new List<GoldStrikeData>();
                GoldStrikeData strikeData;
                List<GoldStrikeData> strikeDataList;
                int count = goldStrikeResources.Keys.Count;
                string[] keys = goldStrikeResources.Keys.ToArray();
                string resourceName;
                int strikeDataCount;

                for (int index = 0; index < count; index++)
                {
                    resourceName = keys[index];
                    strikeDataList = goldStrikeResources[resourceName];
                    strikeDataCount = strikeDataList.Count;

                    for (int strikeDataIndex = 0; strikeDataIndex < strikeDataCount; strikeDataIndex++)
                    {
                        strikeData = strikeDataList[strikeDataIndex];
                        if (strikeData.MatchesProspectCriteria(vessel.situation, vessel.mainBody.name, biomeName, altitude, travelDistance))
                            resources.Add(strikeData);
                    }
                }

                //If we have no resources that meet the criteria then we're done.
                if (resources.Count == 0)
                    return ProspectSituations.NoResourcesToProspect;

                //Record the possible prospect resources.
                else
                    prospectResources = resources;
            }

            return ProspectSituations.Valid;
        }

        public GoldStrikeLode FindNearestLode(ModuleAsteroid asteroid)
        {
            int planetID;
            string biomeName;
            GoldStrikeUtils.GetBiomeAndPlanet(out biomeName, out planetID, null, asteroid);

            string planetBiomeKey = planetID.ToString() + biomeName;
            Dictionary<string, GoldStrikeLode> lodeMap = null;
            debugLog("planetBiomeKey: " + planetBiomeKey);

            //Get the lode map. If there is none then we're done.
            if (goldStrikeLodes.ContainsKey(planetBiomeKey) == false)
            {
                debugLog("goldStrikeLodes has no lodeMap for key: " + planetBiomeKey);
                return null;
            }
            lodeMap = goldStrikeLodes[planetBiomeKey];
            debugLog("lodeMap has " + lodeMap.Count + " entries");
            if (debugMode)
            {
                foreach (string key in lodeMap.Keys)
                {
                    debugLog("Key: " + key + "\r\n lode: " + lodeMap[key].ToString());
                }
            }

            //Asteroids only have one lode
            if (lodeMap.ContainsKey(asteroid.AsteroidName) == false)
            {
                debugLog("No GoldStrikeLode found in lodeMap for " + asteroid.AsteroidName);
                return null;
            }

            return lodeMap[asteroid.AsteroidName];
        }

        public GoldStrikeLode FindNearestLode(int planetID, string biome, double longitude, double latitude, double altitude, double searchDistance = kMaxProspectSearchDistance)
        {
            string planetBiomeKey = planetID.ToString() + biome;
            Dictionary<string, GoldStrikeLode> lodeMap = null;

            //Get the lode map. If there is none then we're done.
            if (goldStrikeLodes.ContainsKey(planetBiomeKey) == false)
            {
                debugLog("no lodeMap found for key: " + planetBiomeKey);
                return null;
            }
            lodeMap = goldStrikeLodes[planetBiomeKey];

            //Now iterate through the dictionary to find the nearest lode.
            //We have a minimum cutoff distance
            GoldStrikeLode[] lodes = lodeMap.Values.ToArray<GoldStrikeLode>();
            GoldStrikeLode lode, closestProspect = null;
            double distance, prevDistance;
            debugLog("lodes length: " + lodes.Length);
            prevDistance = double.MaxValue;
            for (int index = 0; index < lodes.Length; index++)
            {
                lode = lodes[index];
                debugLog("checking lode: " + lode.ToString());

                distance = Utils.HaversineDistance(longitude, latitude, lode.longitude, lode.latitude, FlightGlobals.Bodies[lode.planetID]);
                debugLog("distance between current location and lode location: " + distance);
                if ((distance <= searchDistance) && (distance < prevDistance))
                {
                    debugLog("new closest lode: " + lode);
                    closestProspect = lode;
                    prevDistance = distance;
                }
            }

            if (closestProspect != null)
            {
                debugLog("closest lode found");
                return closestProspect;
            }
            else
            {
                debugLog("closest lode not found");
                return null;
            }
        }

        public GoldStrikeLode AddLode(ModuleAsteroid asteroid, string resourceName, float abundance)
        {
            int planetID;
            string biomeName;
            GoldStrikeUtils.GetBiomeAndPlanet(out biomeName, out planetID, null, asteroid);
            GoldStrikeLode lode = new GoldStrikeLode();
            Dictionary<string, GoldStrikeLode> lodeMap = null;
            string planetBiomeKey = planetID.ToString() + biomeName;

            //Setup the new lode
            lode.resourceName = resourceName;
            lode.longitude = 0;
            lode.latitude = 0;
            lode.biome = biomeName;
            lode.abundance = abundance;
            lode.planetID = planetID;

            //Get the lode map
            if (goldStrikeLodes.ContainsKey(planetBiomeKey) == false)
            {
                lodeMap = new Dictionary<string, GoldStrikeLode>();
                goldStrikeLodes.Add(planetBiomeKey, lodeMap);
                debugLog("Added new goldStrikeLode with planetBiomeKey: " + planetBiomeKey);
            }
            lodeMap = goldStrikeLodes[planetBiomeKey];

            //Add the new lode
            lodeMap.Add(asteroid.AsteroidName, lode);
            goldStrikeLodes[planetBiomeKey] = lodeMap;
            debugLog("Added new lode: " + lode.ToString());

            // Update the counts
            if (lodeCountPerBiomePlanet.ContainsKey(planetBiomeKey) == false)
                lodeCountPerBiomePlanet.Add(planetBiomeKey, 0);
            if (lodeCountPerBiomePlanet.ContainsKey(lode.planetID.ToString()) == false)
                lodeCountPerBiomePlanet.Add(lode.planetID.ToString(), 0);

            lodeCountPerBiomePlanet[planetBiomeKey] = lodeCountPerBiomePlanet[planetBiomeKey] + 1;
            lodeCountPerBiomePlanet[lode.planetID.ToString()] = lodeCountPerBiomePlanet[lode.planetID.ToString()] + 1;

            //Save the game
            GamePersistence.SaveGame("quicksave", HighLogic.SaveFolder, SaveMode.BACKUP);

            return lode;
        }

        public GoldStrikeLode AddLode(int planetID, string biome, double longitude, double lattitude, double altitude, string resourceName, double amountRemaining)
        {
            GoldStrikeLode lode = new GoldStrikeLode();
            Dictionary<string, GoldStrikeLode> lodeMap = null;
            string planetBiomeKey = planetID.ToString() + biome;
            string lodeKey = longitude.ToString() + lattitude.ToString() + resourceName;

            //Setup the new lode
            lode.resourceName = resourceName;
            lode.longitude = longitude;
            lode.latitude = lattitude;
            lode.biome = biome;
            lode.amountRemaining = amountRemaining;
            lode.planetID = planetID;

            //Get the lode map
            if (goldStrikeLodes.ContainsKey(planetBiomeKey) == false)
            {
                lodeMap = new Dictionary<string, GoldStrikeLode>();
                goldStrikeLodes.Add(planetBiomeKey, lodeMap);
            }
            lodeMap = goldStrikeLodes[planetBiomeKey];

            //Add the new lode
            lodeMap.Add(lodeKey, lode);
            goldStrikeLodes[planetBiomeKey] = lodeMap;
            debugLog("Added new lode: " + lode.ToString());

            //Save the game
            GamePersistence.SaveGame("quicksave", HighLogic.SaveFolder, SaveMode.BACKUP);

            return lode;
        }

        public string setWaypoint(string resourceName, GoldStrikeLode lode, bool saveGame = true)
        {
            debugLog("Trying to set waypoint");
            string location = string.Format("Lon: {0:f2} Lat: {1:f2} Alt: {2:f2}", lode.longitude, lode.latitude, lode.altitude);

            List<CelestialBody> bodies = FlightGlobals.fetch.bodies;
            int count = bodies.Count;
            string planetName = "Unknown";
            for (int index = 0; index < count; index++)
            {
                if (bodies[index].flightGlobalsIndex == lode.planetID)
                {
                    planetName = bodies[index].name;
                    break;
                }
            }

            Waypoint waypoint = new Waypoint();
            waypoint.name = resourceName + " Lode";
            waypoint.isExplored = true;
            waypoint.isNavigatable = true;
            waypoint.isOnSurface = true;
            waypoint.celestialName = planetName;
            waypoint.longitude = lode.longitude;
            waypoint.latitude = lode.latitude;
            waypoint.altitude = lode.altitude;
            waypoint.seed = UnityEngine.Random.Range(0, int.MaxValue);
            waypoint.navigationId = Guid.NewGuid();

            //Add the waypoint to the custom waypoint scenario
            ScenarioCustomWaypoints.AddWaypoint(waypoint);

            //Our icon is not correct, do a quick remove, reset the icon, and add
            WaypointManager.RemoveWaypoint(waypoint);
            waypoint.id = kLodeIcon;
            waypoint.nodeCaption1 = location;
            WaypointManager.AddWaypoint(waypoint);

            //Record the waypoint info
            lode.navigationID = waypoint.navigationId.ToString();

            //Save the game
            if (saveGame)
                GamePersistence.SaveGame("quicksave", HighLogic.SaveFolder, SaveMode.BACKUP);

            //Done
            return waypoint.navigationId.ToString();
        }
        #endregion
    }
}
