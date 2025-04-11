﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSP.IO;
using FinePrint;
using KSP.Localization;

/*
Source code copyrighgt 2017, by Michael Billard (Angel-125)
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
    public class WBIGoldStrike : PartModule, IOpsView
    {
        private const string kAnomalyBlacklist = "KSC;KSC2;IslandAirfield;Harvester Massif;Nye Island;Mesa South;Crater Rim;North Station One";
        private const string kAnomalyBlacklistNode = "ANOMALY_BLACKLIST";
        private const string statusReadyName = "Can prospect";
        private const string statusAlreadyProspectedName = "Already prospected";
        private const string statusInsufficientCrewName = "Insufficient crew";
        private const string statusNoAsteroidName = "Land or get asteroid";
        private const string statusNoResources = "No resources to prospect";
        private const string statusNAName = "N/A";
        private const string kLastProspectDist = "Last prospect dist: {0:f2}km";
        private const float kMessageDisplayTime = 10.0f;
        private const float kMotherlodeFactor = 0.05f;
        private const float kLabSkillBonus = 0.5f;
        private const float kAnomalyBonus = 50.0f;
        private const float kAsteroidBonus = 50.0f;
        private const float kMinAnomalyDistance = 0.2f;

        [KSPField()]
        public bool showGUI;

        //Minimum number of crew required by the part
        [KSPField()]
        public int minimumCrew = 0;

        //prospectChance: base chance to find a prospect. Some parts are better than others.
        [KSPField()]
        public float prospectChance = 10.0f;

        //Skill required to do some prospecting.
        [KSPField()]
        public string prospectSkill = "ScienceSkill";

        //prospectSkillBonus: multiplied by the EVA prospector's skill level. Default is 1.0.
        [KSPField()]
        public float prospectSkillBonus = 1.0f;

        [KSPField()]
        public string lodeStrikeSound = string.Empty;

        [KSPField(guiActive = true, guiName = "Prospecting")]
        public string status = "N/A";

        protected ModuleAsteroid asteroid = null;
        protected ModuleAsteroidInfo asteroidInfo = null;
        protected AudioSource jingle = null;
        protected string anomalyName = string.Empty;
        protected GoldStrikeVesselModule vesselModule = null;
        protected float anomalyBonus = 0;
        protected List<GoldStrikeData> prospectResources = null;
        protected double travelDistance;

        protected void debugLog(string message)
        {
            if (WBIGoldStrikeScenario.debugMode == true)
                Debug.Log("[WBIGoldStrike] - " + message);
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            Events["CheckGoldStrike"].guiActive = showGUI;
            Events["CheckGoldStrike"].guiActiveUncommand = showGUI;

            if (HighLogic.LoadedSceneIsFlight == false)
                return;

            //Vessel Module
            foreach (VesselModule module in this.part.vessel.vesselModules)
            {
                if (module is GoldStrikeVesselModule)
                {
                    vesselModule = (GoldStrikeVesselModule)module;
                    break;
                }
            }

            //Get asteroid (if any)
            asteroid = this.part.vessel.FindPartModuleImplementing<ModuleAsteroid>();
        }

        public override void OnUpdate()
        {
            base.OnUpdate();
            if (HighLogic.LoadedSceneIsFlight == false)
                return;

            //Check situation
            if (WBIGoldStrikeScenario.Instance.VesselSituationValid(this.part.vessel))
            {
                travelDistance = getDistanceFromLastLocation();
                if (travelDistance > 0.001f)
                    status = Localizer.Format(string.Format(kLastProspectDist, travelDistance));
                else
                    status = Localizer.Format(statusReadyName);
            }

            //Check asteroid status
            //Priority is to check for captured asteroids before checking to see if we've prospected a particular planetary biome.
            else if (asteroid != null)
            {
                if (WBIGoldStrikeScenario.Instance.IsAsteroidProspected(asteroid))
                {
                    status = Localizer.Format(statusAlreadyProspectedName);
                }

                //Ready to be prospected.
                else
                {
                    status = Localizer.Format(statusReadyName);
                }
            }

            //No asteroid
            else
            {
                status = Localizer.Format(statusNoAsteroidName);
            }
        }

        [KSPEvent(guiName = "Prospect for resources", unfocusedRange = 3.0f)]
        public void CheckGoldStrike()
        {
            string[] strikeResourceKeys = null;
            int resourceIndex;
            GoldStrikeData strikeData = null;
            WBIGoldStrikeScenario scenario = WBIGoldStrikeScenario.Instance;
            string resourceName = string.Empty;
            double resourceAmount = 0f;
            float analysisRoll = 0f;
            float successTargetNumber = prospectChance;
            string navigationID = string.Empty;
            GoldStrikeLode lode = null;
            string biomeName = string.Empty;
            int planetID = -1;

            //Do we have a valid situation?
            if (SituationIsValid() == false)
                return;

            //Update our location.
            asteroid = this.part.vessel.FindPartModuleImplementing<ModuleAsteroid>();
            GoldStrikeUtils.GetBiomeAndPlanet(out biomeName, out planetID, this.part.vessel, asteroid);
            vesselModule.UpdateLastProspectLocation();

            //Time to see if we find anything.
            //prospectChance: base chance to find a prospect. Some parts are better than others.
            //prospectBonus: Various situations contribute to the success of the attempt.
            successTargetNumber = 100 - (prospectChance + GetProspectBonus());
            debugLog("successTargetNumber: " + successTargetNumber);

            //Roll the chance and check it.
            analysisRoll = UnityEngine.Random.Range(1, 100);
            debugLog("analysisRoll: " + analysisRoll);

            //If we didn't succeed then we're done.
            if (analysisRoll < successTargetNumber && !WBIGoldStrikeScenario.debugProspectAlwaysSuccessful)
            {
                debugLog("Prospect failed; didn't roll high enough.");
                ScreenMessages.PostScreenMessage("Nothing of value here, try another location. ", kMessageDisplayTime, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            //Success! Get the resource name and strike data.
            //If we're near an anomaly then we need a roll table.
            if (anomalyBonus > 0)
            {
                SortedDictionary<int, GoldStrikeData> sortedResources = new SortedDictionary<int, GoldStrikeData>();
                int resourceCount = prospectResources.Count;

                //Add any resources that have an anomaly chance into the sorted resources
                for (int index = 0; index < resourceCount; index++)
                {
                    strikeData = prospectResources[index];
                    if (strikeData.anomalyChance > 0)
                        sortedResources.Add(strikeData.anomalyChance, strikeData);
                }

                //Roll a 1-100
                int pressenceRoll = UnityEngine.Random.Range(1, 100);

                //See if we have any strike data
                strikeData = null;
                foreach (KeyValuePair<int, GoldStrikeData> pair in sortedResources)
                {
                    if (pressenceRoll < pair.Key)
                    {
                        strikeData = pair.Value;
                        break;
                    }
                }

                //If we don't have any strike data then pick a random resource
                if (strikeData == null)
                {
                    resourceIndex = UnityEngine.Random.Range(0, resourceCount - 1);
                    strikeData = prospectResources[resourceIndex];
                    resourceName = strikeData.resourceName;
                }
            }

            else
            {
                resourceIndex = UnityEngine.Random.Range(0, prospectResources.Count - 1);
                strikeData = prospectResources[resourceIndex];
                resourceName = strikeData.resourceName;
                debugLog("prospectResources count: " + prospectResources.Count);
                debugLog("resourceIndex: " + resourceIndex);
            }
            debugLog("strikeData: " + strikeData.ToString());

            //Play the jingle
            playJingle();

            //Setup a planetary surface lode
            if (asteroid == null)
            {
                debugLog("Setting up surface lode");
                //Now, generate the resource amount to add to the map
                resourceAmount = UnityEngine.Random.Range(strikeData.minAmount, strikeData.maxAmount);
                debugLog("resourceAmount: " + resourceAmount);

                //If we hit the motherlode then factor that in.
                //The motherloade is 5% of the target number.
                if (analysisRoll <= (successTargetNumber * kMotherlodeFactor))
                {
                    resourceAmount *= strikeData.motherlodeMultiplier;
                    debugLog("resourceAmount after motherlode: " + resourceAmount);
                    ScreenMessages.PostScreenMessage(string.Format("Congratulations! You found a {0:s} motherlode with {1:f2} units available to mine!", resourceName, resourceAmount),
                        kMessageDisplayTime, ScreenMessageStyle.UPPER_CENTER);
                }

                else
                {
                    ScreenMessages.PostScreenMessage(string.Format("Congratulations! You found a {0:s} lode with {1:f2} units available to mine!", resourceName, resourceAmount),
                        kMessageDisplayTime, ScreenMessageStyle.UPPER_CENTER);
                }
                if (!string.IsNullOrEmpty(anomalyName))
                    ScreenMessages.PostScreenMessage("Special cache found at " + anomalyName, kMessageDisplayTime, ScreenMessageStyle.UPPER_CENTER); 
                
                debugLog("Adding new lode entry to " + FlightGlobals.currentMainBody.name + " with flight global index " + FlightGlobals.currentMainBody.flightGlobalsIndex);
                debugLog("Biome: " + biomeName);
                debugLog("Lon/Lat: " + this.part.vessel.longitude + "/" + this.part.vessel.latitude);
                lode = scenario.AddLode(planetID, biomeName,
                    this.part.vessel.longitude, this.part.vessel.latitude, this.part.vessel.altitude, resourceName, resourceAmount);
            }

            //Setup an asteroid lode
            else
            {
                debugLog("Setting up asteroid lode");
                float abundance = 0.01f;
                
                //Get the resource module for the lode.
                ModuleAsteroidResource lodeResource = null;
                ModuleAsteroidResource[] resourceModules = asteroid.part.FindModulesImplementing<ModuleAsteroidResource>().ToArray();
                for (int index = 0; index < resourceModules.Length; index++)
                {
                    if (resourceModules[index].resourceName == resourceName)
                    {
                        debugLog("ModuleAsteroidResource found for " + resourceName);
                        lodeResource = resourceModules[index];
                        break;
                    }
                }
                if (lodeResource == null)
                {
                    debugLog("ModuleAsteroidResource NOT found for " + resourceName);
                    return;
                }

                //Adjust abundance for motherlode
                if (analysisRoll <= (successTargetNumber * kMotherlodeFactor))
                    abundance *= strikeData.motherlodeMultiplier;
                debugLog("abundance increase: " + abundance);

                //Display appropriate message
                ScreenMessages.PostScreenMessage(string.Format("Congratulations! A careful scan of {0:s} has revealed an increased abundance of {1:s}", asteroid.AsteroidName, resourceName),
                    kMessageDisplayTime, ScreenMessageStyle.UPPER_CENTER);

                //Update resource module
                debugLog("Old abundance: " + lodeResource.abundance);
                lodeResource.abundance += abundance;
                lodeResource.displayAbundance += abundance;
                debugLog("New abundance: " + lodeResource.abundance);

                debugLog("Adding new lode entry for asteroid: " + asteroid.AsteroidName);
                lode = scenario.AddLode(asteroid, resourceName, lodeResource.displayAbundance);
            }

            //Update any drills in the area.
            scenario.UpdateDrillLodes(asteroid);

            //Set waypoint
            if (lode != null && asteroid == null)
                WBIGoldStrikeScenario.Instance.setWaypoint(resourceName, lode);
        }

        public bool SituationIsValid()
        {
            debugLog("SituationIsValid: checking...");
            GoldStrikeLode lode = null;

            //Do we have enough crew?
            if (minimumCrew > 0)
            {
                if (this.part.protoModuleCrew.Count < minimumCrew)
                {
                    ScreenMessages.PostScreenMessage(this.part.partInfo.title + "Must be staffed with at least " + minimumCrew + " crewmembers.", kMessageDisplayTime, ScreenMessageStyle.UPPER_CENTER);
                    status = Localizer.Format(statusInsufficientCrewName);
                    return false;
                }
            }

            //Can we prospect the location?
            switch (WBIGoldStrikeScenario.Instance.GetProspectSituation(this.part.vessel, out lode, out prospectResources))
            {
                case ProspectSituations.NoResourcesToProspect:
                    status = Localizer.Format(statusNoResources);
                    ScreenMessages.PostScreenMessage("No resource lodes detected here. Try traveling further.", kMessageDisplayTime, ScreenMessageStyle.UPPER_CENTER);
                    return false;

                case ProspectSituations.InvalidVesselSituation:
                    debugLog("Vessel situation not valid");
                    ScreenMessages.PostScreenMessage("Vessel must be landed, splashed, or in orbit/docked with an asteroid attached", kMessageDisplayTime, ScreenMessageStyle.UPPER_CENTER);
                    status = Localizer.Format(statusNAName);
                    return false;

                case ProspectSituations.LodeAlreadyExists:
                    if (lode != null)
                    {
                        debugLog("Situation not valid, existing lode found: " + lode.ToString());
                        string message = string.Format("You already found a vein of {0:s} at this location. It has {1:f2} units remaining.", lode.resourceName, lode.amountRemaining);
                        ScreenMessages.PostScreenMessage(message, kMessageDisplayTime, ScreenMessageStyle.UPPER_CENTER);
                        status = Localizer.Format(statusAlreadyProspectedName);
                    }
                    return false;

                case ProspectSituations.AsteroidProspected:
                    debugLog("Asteroid has already been prospected");
                    ScreenMessages.PostScreenMessage("Asteroid has already been prospected.", kMessageDisplayTime, ScreenMessageStyle.UPPER_CENTER);
                    status = Localizer.Format(statusAlreadyProspectedName);
                    if (!WBIGoldStrikeScenario.debugProspectAlwaysSuccessful)
                    {
                        return false;
                    }
                    else
                    {
                        debugLog("Debug: Prospect is guaranteed");
                        return true;
                    }

                case ProspectSituations.LodeLimitsReached:
                    ScreenMessages.PostScreenMessage("Prospecting limits reached for this location.", kMessageDisplayTime, ScreenMessageStyle.UPPER_CENTER);
                    status = Localizer.Format(statusNAName);
                    return false;

                //A-OK
                default:
                    status = Localizer.Format(statusReadyName);
                    return true;
            }

        }

        public float GetProspectBonus()
        {
            //skillBonus: multiplied by the EVA prospector's skill level. Default is 1.0.
            //labBonus: For each geology lab in the vicinity, bonus is 0.5 * crew skill per crewmember that has the prospectSkill.
            //anomalyBonus: If the prospector is near an anomaly, then they get an anomaly bonus.
            //asteroidBonus: If the prospector has captured an asteroid, then they get an asteroid bonus.
            //Ex: A 3-star scientist on EVA makes a prospect check. skillBonus = 3; prospectSkillBonus = 1.0. Total skill bonus = 3.0.
            //Inside the Bison there are two level-1 scientists staffing a geology lab. labBonus = 2 * 0.5 = 1

            float skillBonus = GoldStrikeSettings.BonusPerSkillPoint;
            float prospectBonus = 0f;
            float labBonus = 0f;
            float asteroidBonus = 0f;
            Vessel[] vessels;
            Vessel vessel;
            ProtoCrewMember[] crewMembers;
            ProtoCrewMember crewMember;
            WBIGeoLab[] geoLabs;
            WBIGeoLab geoLab;
            ProtoVessel protoVessel;

            //If an experienced prospector is taking the core sample, then the prospector's experience will
            //affect the analysis.
            if (FlightGlobals.ActiveVessel.isEVA && Utils.IsExperienceEnabled())
            {
                vessel = FlightGlobals.ActiveVessel;
                ProtoCrewMember astronaut = vessel.GetVesselCrew()[0];

                if (astronaut.HasEffect(prospectSkill))
                    prospectBonus = astronaut.experienceTrait.CrewMemberExperienceLevel() * skillBonus;
            }

            //Factor in lab bonus
            //Bonus is 0.5 * crew skill (max 2.5) per crewmember
            vessels = FlightGlobals.VesselsLoaded.ToArray();
            for (int index = 0; index < vessels.Length; index++)
            {
                vessel = vessels[index];

                //Geology lab
                List<WBIGeoLab> geologyLabs = vessel.FindPartModulesImplementing<WBIGeoLab>();
                if (geologyLabs.Count > 0)
                {
                    geoLabs = geologyLabs.ToArray();
                    for (int labIndex = 0; labIndex < geoLabs.Length; labIndex++)
                    {
                        geoLab = geoLabs[labIndex];
                        if (geoLab.part.protoModuleCrew.Count > 0)
                        {
                            crewMembers = geoLab.part.protoModuleCrew.ToArray();
                            for (int crewIndex = 0; crewIndex < crewMembers.Length; crewIndex++)
                            {
                                crewMember = crewMembers[crewIndex];
                                if (crewMember.HasEffect(prospectSkill))
                                {
                                    labBonus += kLabSkillBonus * crewMember.experienceTrait.CrewMemberExperienceLevel();
                                }
                            }
                        }
                    }
                }

                //GoldStrike bonus: Some parts give a bonus- but it costs science data to do so.
                List<WBIGoldStrikeBonus> goldStrikeBonuses = vessel.FindPartModulesImplementing<WBIGoldStrikeBonus>();
                WBIGoldStrikeBonus goldStrikeBonus;
                if (goldStrikeBonuses.Count > 0)
                {
                    for (int bonusIndex = 0; bonusIndex < goldStrikeBonuses.Count; bonusIndex++)
                    {
                        goldStrikeBonus = goldStrikeBonuses[bonusIndex];
                        if (goldStrikeBonus.prospectingDataAmount >= goldStrikeBonus.dataCostPerBonus && goldStrikeBonus.prospectingDataAmount > 0f)
                        {
                            labBonus += (goldStrikeBonus.prospectingDataAmount / goldStrikeBonus.dataCostPerBonus) * goldStrikeBonus.Bonus;
                            goldStrikeBonus.prospectingDataAmount = 0f;
                        }
                    }
                }
            }

            //Unloaded vessels lab bonus
            vessels = FlightGlobals.VesselsUnloaded.ToArray();
            float calculatedBonus = 0f;
            float prospectingDataAmount = 0f;
            float dataCostPerBonus = 1.0f;
            for (int index = 0; index < vessels.Length; index++)
            {
                vessel = vessels[index];
                protoVessel = vessel.protoVessel;

                //Only applies to vessels of the same body that are landed.
                if (vessel.mainBody != this.part.vessel.mainBody)
                    continue;
                if (vessel.situation != Vessel.Situations.LANDED && vessel.situation != Vessel.Situations.PRELAUNCH)
                    continue;

                //Find all the geology labs & parts that give a bonus to Gold Strike
                foreach (ProtoPartSnapshot partSnapshot in protoVessel.protoPartSnapshots)
                {
                    foreach (ProtoPartModuleSnapshot moduleSnapshot in partSnapshot.modules)
                    {
                        calculatedBonus = 0f;
                        prospectingDataAmount = 0f;
                        dataCostPerBonus = 1.0f;

                        if (moduleSnapshot.moduleName == "WBIGeologyLab" || moduleSnapshot.moduleName == "WBIGeoLab")
                        {
                            foreach (ProtoCrewMember protoCrewMember in partSnapshot.protoModuleCrew)
                            {
                                if (protoCrewMember.HasEffect(prospectSkill))
                                    labBonus += kLabSkillBonus * protoCrewMember.experienceTrait.CrewMemberExperienceLevel();
                            }
                        }

                        else if (moduleSnapshot.moduleName == "WBIGoldStrikeBonus")
                        {
                            if (moduleSnapshot.moduleValues.HasValue("calculatedBonus"))
                                calculatedBonus = float.Parse(moduleSnapshot.moduleValues.GetValue("calculatedBonus"));
                            if (moduleSnapshot.moduleValues.HasValue("prospectingDataAmount"))
                                prospectingDataAmount = float.Parse(moduleSnapshot.moduleValues.GetValue("prospectingDataAmount"));
                            if (moduleSnapshot.moduleValues.HasValue("dataCostPerBonus"))
                                dataCostPerBonus = float.Parse(moduleSnapshot.moduleValues.GetValue("dataCostPerBonus"));

                            if (prospectingDataAmount >= dataCostPerBonus && prospectingDataAmount > 0)
                            {
                                labBonus += (prospectingDataAmount / dataCostPerBonus) * calculatedBonus;
                                prospectingDataAmount = 0f;
                                moduleSnapshot.moduleValues.SetValue("prospectingDataAmount", 0f);
                            }
                        }
                    }
                }
            }

            //Location bonus
            anomalyBonus = 0;
            anomalyBonus = getAnomalyBonus();

            //Asteroid bonus
            if (asteroid != null)
                asteroidBonus = kAsteroidBonus;

            debugLog("Prospector bonus: " + prospectBonus + " labBonus: " + labBonus + " anomalyBonus: " + anomalyBonus + " asteroidBonus: " + asteroidBonus);
            return Mathf.RoundToInt(prospectBonus + labBonus + anomalyBonus + asteroidBonus);
        }

        protected float getAnomalyBonus()
        {
            CelestialBody mainBody = this.part.vessel.mainBody;
            PQSSurfaceObject[] anomalies = mainBody.pqsSurfaceObjects;
            double longitude;
            double latitude;
            double distance = 0f;
            ConfigNode[] anomalyBlacklists = GameDatabase.Instance.GetConfigNodes(kAnomalyBlacklistNode);
            string blacklist = kAnomalyBlacklist;

            //We've covered the stock anomalies, now make sure we can blacklist anomalies from mods.
            try
            {
                foreach (ConfigNode blacklistNode in anomalyBlacklists)
                    blacklist += ";" + blacklistNode.GetValue("anomalies");
            }
            catch { }

            if (this.part.vessel.situation == Vessel.Situations.LANDED || this.part.vessel.situation == Vessel.Situations.PRELAUNCH)
            {
                for (int index = 0; index < anomalies.Length; index++)
                {
                    //If the anomaly is on the blacklist, skip it.
                    if (blacklist.Contains(anomalies[index].SurfaceObjectName))
                        continue;

                    //Get the longitude and latitude of the anomaly
                    longitude = mainBody.GetLongitude(anomalies[index].transform.position);
                    latitude = mainBody.GetLatitude(anomalies[index].transform.position);

                    //Get the distance (in kilometers) from the anomaly.
                    distance = Utils.HaversineDistance(longitude, latitude,
                        this.part.vessel.longitude, this.part.vessel.latitude, this.part.vessel.mainBody);

                    //If we're near the anomaly, then we get a big location bonus.
                    if (distance < kMinAnomalyDistance)
                    {
                        anomalyName = anomalies[index].SurfaceObjectName;
                        return kAnomalyBonus;
                    }
                }
            }

            //No bonus
            anomalyName = string.Empty;
            return 0f;
        }

        protected void playJingle()
        {
            if (!string.IsNullOrEmpty(lodeStrikeSound))
            {
                jingle = gameObject.AddComponent<AudioSource>();
                jingle.clip = GameDatabase.Instance.GetAudioClip(lodeStrikeSound);
                jingle.volume = GameSettings.SHIP_VOLUME;
                jingle.loop = false;
            }

            if (jingle != null && jingle.isPlaying == false)
                jingle.Play();
        }

        protected double getDistanceFromLastLocation()
        {
            double distance = 0f;

            if (vesselModule == null)
                return 0f;

            //If we have no last prospect then the distance is zero.
            if (!vesselModule.HasLastProspectLocation())
            {
                vesselModule.UpdateLastProspectLocation();
                return 0f;
            }

            //In kilometers
            distance = Utils.HaversineDistance(vesselModule.lastProspectLongitude, vesselModule.lastProspectLatitude,
                this.part.vessel.longitude, this.part.vessel.latitude, this.part.vessel.mainBody);

            return distance;
        }

        #region IOpsView
        public List<string> GetButtonLabels()
        {
            List<string> labels = new List<string>();

            labels.Add("Gold Strike");

            return labels;
        }

        public void DrawOpsWindow(string buttonLabel)
        {
            GUILayout.BeginVertical();

            if (HighLogic.LoadedSceneIsFlight == false)
            {
                GUILayout.Label("<color=yellow><b>Feature only available during flight.</b></color>");
                GUILayout.EndVertical();
                return;
            }

            GUILayout.Label(string.Format("Last location: {0:f2}km", travelDistance));

            if (GUILayout.Button("Prospect for resources"))
                CheckGoldStrike();

            GUILayout.EndVertical();
        }

        public void SetParentView(IParentView parentView)
        {
        }

        public void SetContextGUIVisible(bool isVisible)
        {
            Events["CheckGoldStrike"].guiActive = isVisible;
            Events["CheckGoldStrike"].guiActiveUncommand = isVisible;
        }

        public string GetPartTitle()
        {
            return this.part.partInfo.title;
        }
        #endregion
    }
}
