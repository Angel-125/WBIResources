﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSP.IO;

/*
Source code copyright 2018, by Michael Billard (Angel-125)
License: GPLV3

Wild Blue Industries is trademarked by Michael Billard and may be used for non-commercial purposes. All other rights reserved.
Note that Wild Blue Industries is a ficticious entity 
created for entertainment purposes. It is in no way meant to represent a real entity.
Any similarity to a real entity is purely coincidental.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/
namespace WBIResources
{
    public struct ComboRatio
    {
        public float ratio;
        public double maxAmountMultiplier;
    }

    [KSPModule("Omni Storage")]
    public class WBIOmniStorage : PartModule, IOpsView, IPartCostModifier
    {
        const string kKISResource = "KIS Inventory";
        const string kDefaultBlacklist = "GeoEnergy;ElectroPlasma;CoreHeat;wbiAtmosphere;CompressedAtmosphere;LabTime;ExposureTime;ScopeTime;SolarReports;SimulatorTime;GravityWaves;IntakeLqd;IntakeAir;StaticCharge;EVA Propellant;Plants;CoreSamples;MJPropellant;SOCSFuel";
        const string kDefaultRestrictedList = "Graviolium";

        #region Fields
        /// <summary>
        /// Debug flag.
        /// </summary>
        public bool debugMode = true;

        /// <summary>
        /// Amount of storage volume in liters. Standard volume is 9000 liters,
        /// corresponding to the Titan 1800 upon which the standard template system was based.
        /// </summary>
        [KSPField(isPersistant = true)]
        public float storageVolume = 9000.0f;

        /// <summary>
        /// Flag to indicate whether or not the storage adjusts its volume based on the resource switcher's capacity factor.
        /// </summary>
        [KSPField(isPersistant = true)]
        public bool accountForSwitcherCapacity = false;

        /// <summary>
        /// Flag to indicate that the container is devoid of resources.
        /// </summary>
        [KSPField(isPersistant = true)]
        public bool isEmpty = false;

        /// <summary>
        /// Skill required to reconfigure the container.
        /// </summary>
        [KSPField]
        public string reconfigureSkill = "ConverterSkill";

        /// <summary>
        /// Minimum skill rank required to reconfigure the container.
        /// </summary>
        [KSPField]
        public int reconfigureRank = 0;

        /// <summary>
        /// Resource required to reconfigure the container.
        /// </summary>
        [KSPField]
        public string requiredResource = "Equipment";

        /// <summary>
        /// Amount of the resource required, in units, that's needed to reconfigure the container.
        /// </summary>
        [KSPField]
        public float requiredAmount;

        /// <summary>
        /// List of resources that the container cannot contain. Separate resources by a semicolon.
        /// </summary>
        [KSPField]
        public string resourceBlacklist = kDefaultBlacklist;

        /// <summary>
        /// List of resources that cannot be set to the max amount in the editor. Seperate resources by a semicolon.
        /// </summary>
        [KSPField]
        public string restrictedResourceList = kDefaultRestrictedList;

        /// <summary>
        /// Title of the GUI window.
        /// </summary>
        [KSPField]
        public string opsViewTitle = "Reconfigure Storage";

        /// <summary>
        /// Configuration of resources owned by the OmniStorage.
        /// </summary>
        [KSPField(isPersistant = true)]
        public string omniResources = string.Empty;

        /// <summary>
        /// When we have no DEFAULT_RESOURCE we'll need to look to the part itself to see what it has. This field tracks what the part originally had.
        /// It's used to remove part resources.
        /// </summary>
        [KSPField(isPersistant = true)]
        public string defaultResourceNames = string.Empty;

        /// <summary>
        /// Unique ID of the converter. Used to identify it during background processing.
        /// </summary>
        [KSPField(isPersistant = true)]
        public string ID = "none";

        /// <summary>
        /// List of resources that cannot be altered.
        /// </summary>
        [KSPField]
        public string lockedResources = string.Empty;

        /// <summary>
        /// If a stock inventory module is present, how much volume should it have?
        /// </summary>
        [KSPField(isPersistant = true)]
        public float packedVolumeLimit = -1f;
        #endregion

        #region Housekeeping
        public static Texture deleteIcon = null;
        public static Texture copyIcon = null;
        public static Texture pasteIcon = null;
        GUILayoutOption[] buttonOptions = new GUILayoutOption[] { GUILayout.Width(32), GUILayout.Height(32) };

        //Clipboard
        public static Dictionary<string, double> clipboardResources = new Dictionary<string, double>();
        public static Dictionary<string, float> clipboardRatios = new Dictionary<string, float>();

        protected float adjustedVolume = 0.0f;
        protected Dictionary<string, double> resourceAmounts = new Dictionary<string, double>();
        protected Dictionary<string, double> previewResources = new Dictionary<string, double>();
        protected Dictionary<string, float> previewRatios = new Dictionary<string, float>();
        protected bool confirmedReconfigure;
        protected ModuleInventoryPart stockInventory = null;
        protected float stockInventoryVolumeUpdate = 0f;
        protected float inventoryAdjustedVolume = 0f;
        protected float originalVolumeLimit = 0f;
        protected PartResourceDefinitionList definitions;

        private Vector2 scrollPos, scrollPosResources;
        private WBIAffordableSwitcher switcher;
        private List<String> sortedResourceNames;
        private WBISingleOpsView opsView;
        private List<Dictionary<string, ComboRatio>> resourceCombos = new List<Dictionary<string, ComboRatio>>();
        private string searchText = string.Empty;
        private bool updateSymmetry;
        private bool synchronizeComboResources = true;
        private ConfigNode partConfigNode = null;
        #endregion

        #region Events
        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Jettison contents", guiActiveUnfocused = true, unfocusedRange = 3.0f, groupName = "Omni", groupDisplayName = "Omni", groupStartCollapsed = true)]
        public virtual void DumpResources()
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (confirmedReconfigure == false)
                {
                    ScreenMessages.PostScreenMessage("Existing resources will be removed. Click a second time to confirm.", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                    confirmedReconfigure = true;
                    return;
                }

                confirmedReconfigure = false;
            }

            foreach (PartResource resource in this.part.Resources)
            {
                if (resource.resourceName != "ElectricCharge" && resource.flowState)
                {
                    resource.amount = 0;
                    GameEvents.onPartResourceNonemptyEmpty.Fire(resource);
                }
            }
        }

        [KSPAction("Dump Resources")]
        public void DumpResourcesAction(KSPActionParam param)
        {
            DumpResources();
        }
        
        [KSPEvent(guiActive = true, guiActiveEditor = true, guiName = "Reconfigure Storage", groupName = "Omni", groupDisplayName = "Omni", groupStartCollapsed = true)]
        public void ToggleStorageView()
        {
            //Setup the ops view
            if (opsView == null)
            {
                opsView = new WBISingleOpsView();
                opsView.WindowTitle = opsViewTitle;
                opsView.buttonLabel = "Resources";
                opsView.opsView = this;
            }
            opsView.SetVisible(!opsView.IsVisible());
        }

        #endregion

        #region API
        public void UpdateStorageVolume(float volume)
        {
            // Set new adjusted volume.
            adjustedVolume = volume;

            // If we haven't started yet then just return. We can tell this if our definitions list is empty.
            if (definitions == null)
                return;

            if (stockInventory != null)
            {
                // Get the current ratio between cargo volume and resource volume.
                float cargoToResourcesRatio = packedVolumeLimit / inventoryAdjustedVolume;

                // Set the new max volumes
                originalVolumeLimit = volume;
                inventoryAdjustedVolume = volume;

                // Calculate the new volume allocated to inventory.
                stockInventoryVolumeUpdate = inventoryAdjustedVolume * cargoToResourcesRatio;
                packedVolumeLimit = stockInventoryVolumeUpdate;
                stockInventory.packedVolumeLimit = stockInventoryVolumeUpdate;

                // Calculate the new volume allocated to resources.
                adjustedVolume = inventoryAdjustedVolume - stockInventoryVolumeUpdate;
            }

            recalculateMaxAmounts();

            if (HighLogic.LoadedSceneIsEditor)
                reconfigureStorage();
            else if (HighLogic.LoadedSceneIsFlight)
                updatePartMaxAmounts();
        }
        #endregion

        #region Overrides
        public void Destroy()
        {
            if (opsView != null)
            {
                opsView.SetVisible(false);
                opsView = null;
            }
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            loadOmniResourceConfigs();
        }

        public override void OnSave(ConfigNode node)
        {
            buildOmniResourceConfigs();
            base.OnSave(node);
        }

        //This avoids a problem where you revert a flight and then create a new part of the same type as the host part, and you get a duplicate loadout for the storage unit instead of a blank storage.
        public virtual void ResetSettings()
        {
            isEmpty = true;
            omniResources = string.Empty;
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            loadOmniResourceConfigs();

            if (string.IsNullOrEmpty(ID) || ID == "none" )
                ID = Guid.NewGuid().ToString();
            else if (HighLogic.LoadedSceneIsEditor && WBIOmniManager.Instance.WasRecentlyCreated(this.part))
                ResetSettings();

            //Setup the delete icon
            if (deleteIcon == null)
            {
                deleteIcon = GameDatabase.Instance.GetTexture("WildBlueIndustries/WBIResources/Icons/TrashCan", false);
                copyIcon = GameDatabase.Instance.GetTexture("WildBlueIndustries/WBIResources/Icons/CopyIcon", false);
                pasteIcon = GameDatabase.Instance.GetTexture("WildBlueIndustries/WBIResources/Icons/PasteIcon", false);
            }

            //Get the switcher
            if (adjustedVolume <= 0)
                adjustedVolume = storageVolume;
            switcher = this.part.FindModuleImplementing<WBIAffordableSwitcher>();
            if (switcher != null)
            {
                //Rename our reconfigure event to avoid name collisions
                Events["ToggleStorageView"].guiName = "Setup Omni Storage";

                //Adjust storage volume to account for storage capacity
                if (accountForSwitcherCapacity)
                    adjustedVolume = storageVolume * switcher.capacityFactor;

                //Setup events
                switcher.RemoveReconfigureDelegate(ID, GetRequiredResources);
                switcher.AddReconfigureDelegate(ID, GetRequiredResources);

                //Add any resources that we should keep to the blacklist.
                if (!string.IsNullOrEmpty(switcher.resourcesToKeep))
                    resourceBlacklist += switcher.resourcesToKeep;
            }

            //Get our resource definitions.
            definitions = PartResourceLibrary.Instance.resourceDefinitions;
            List<String> resourceNames = new List<string>();
            foreach (PartResourceDefinition def in definitions)
                resourceNames.Add(def.name);
            sortedResourceNames = resourceNames.OrderBy(q => q).ToList();

            //Load resource combos. Overrides first.
            loadResourceCombos(getPartComboNodes());
            loadResourceCombos(GameDatabase.Instance.GetConfigNodes("OMNIRESOURCECOMBO"));

            //Setup default resources if needed
            Debug.Log("[WBIOmniStorage] - OnStart called. resourceAmounts.Count: " + resourceAmounts.Count);
            if (isEmpty)
            {
                this.previewRatios.Clear();
                this.previewResources.Clear();
                this.part.Resources.Clear();
            }
            else if (resourceAmounts.Count == 0 && !isEmpty)
                setupDefaultResources();
            else
                clearDefaultResources();

            //Add any resources that are restricted.
            addRestrictedResources();

            // Setup stock inventory module
            stockInventory = this.part.FindModuleImplementing<ModuleInventoryPart>();
            if (stockInventory != null)
            {
                originalVolumeLimit = stockInventory.packedVolumeLimit;

                if (packedVolumeLimit < 0)
                    packedVolumeLimit = stockInventory.packedVolumeLimit;
                else
                    stockInventory.packedVolumeLimit = packedVolumeLimit;

                stockInventoryVolumeUpdate = stockInventory.packedVolumeLimit;
                inventoryAdjustedVolume = adjustedVolume;
                adjustedVolume = inventoryAdjustedVolume - stockInventoryVolumeUpdate;
            }
        }
        #endregion

        #region IOpsView
        public List<string> GetButtonLabels()
        {
            List<string> buttonLabels = new List<string>();
            buttonLabels.Add("OmniStorage");
            return buttonLabels;
        }

        public void DrawOpsWindow(string buttonLabel)
        {
            GUILayout.BeginHorizontal();

            //Left pane
            GUILayout.BeginVertical(new GUILayoutOption[] { GUILayout.Width(250) });

            //Directions
            GUILayout.Label("<color=white>Click on a resource button below to add the resource to the container.</color>");

            //List of resources
            drawResourceList();

            //Reconfigure button
            drawReconfigureButton();

            //Done with left pane
            GUILayout.EndVertical();

            //Right pane
            GUILayout.BeginVertical();

            //Capacity
            GUILayout.Label(string.Format("<color=white><b>Resource Capacity: </b>{0:f2}L</color>", adjustedVolume));
            if (stockInventory != null)
                GUILayout.Label(string.Format("<color=white><b>Inventory Capacity:</b> {0:n2}L</color>", stockInventoryVolumeUpdate));
            GUILayout.Label("<color=white><b>NOTE:</b> The maximum number of units for any given resource depends upon how the resource defines how many liters occupies one unit of storage.</color>");

            // Stock inventory
            drawStockInventoryControls();

            //Resource ratios
            drawResourceRatios();

            //Done with right pane
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        public void SetParentView(IParentView parentView)
        {
        }

        public void SetContextGUIVisible(bool isVisible)
        {
            Events["ToggleStorageView"].active = isVisible;
            Events["DumpResources"].active = isVisible;
        }

        public string GetPartTitle()
        {
            return this.part.partInfo.title;
        }
        #endregion

        #region Logging
        public virtual void Log(object message)
        {
            if (HighLogic.LoadedScene == GameScenes.LOADING || HighLogic.LoadedScene == GameScenes.LOADINGBUFFER ||
                HighLogic.LoadedScene == GameScenes.PSYSTEM || HighLogic.LoadedScene == GameScenes.SETTINGS)
                return;

            if (!WBIResourcesSettings.EnableDebugLogging)
                return;

            Debug.Log("[" + this.ClassName + "] - " + message);
        }
        #endregion

        #region Drawing
        protected void drawStockInventoryControls()
        {
            if (stockInventory == null)
                return;

            float update = GUILayout.HorizontalSlider(stockInventoryVolumeUpdate, stockInventory.volumeCapacity, originalVolumeLimit);

            if (Mathf.Abs(stockInventoryVolumeUpdate - update) > 0.00001)
            {
                stockInventoryVolumeUpdate = update;
                adjustedVolume = inventoryAdjustedVolume - stockInventoryVolumeUpdate;
                recalculateMaxAmounts();
            }
        }

        protected void drawResourceRatios()
        {
            string resourceName;
            string displayName;
            List<string> doomedResources = new List<string>();

            scrollPosResources = GUILayout.BeginScrollView(scrollPosResources);

            string[] keys = previewResources.Keys.ToArray();
            PartResourceDefinition definition;
            for (int index = 0; index < keys.Length; index++)
            {
                //First item is the KIS storage if the part has one.
                if (keys[index] == kKISResource)
                {
                    displayName = kKISResource;
                    resourceName = kKISResource;
                    if (isComboResource(kKISResource))
                        displayName += "*";
                }
                else
                {
                    definition = definitions[keys[index]];
                    displayName = definition.displayName;
                    if (string.IsNullOrEmpty(displayName))
                        displayName = definition.name;
                    resourceName = definition.name;
                    if (isComboResource(definition.name))
                        displayName += "*";
                }

                //Display label
                GUILayout.BeginHorizontal();
                GUILayout.Label("<color=white><b>" + displayName + ": </b>" + string.Format("{0:f2}U", previewResources[keys[index]]) + "</color>");
                GUILayout.FlexibleSpace();

                //Delete button
                if (GUILayout.Button(deleteIcon, buttonOptions))
                    doomedResources.Add(resourceName);
                GUILayout.EndHorizontal();

                //Restricted resource warning
                if (HighLogic.LoadedSceneIsEditor && HighLogic.CurrentGame.Mode == Game.Modes.CAREER)
                {
                    if (restrictedResourceList.Contains(keys[index]))
                        GUILayout.Label("<color=yellow>" + displayName + " cannot be added in the VAB/SPH, it will be added at launch.</color>");
                }

                //Slider to adjust max amount
                float ratio = GUILayout.HorizontalSlider(previewRatios[resourceName], 0.01f, 1.0f);
                if (ratio != previewRatios[resourceName])
                {
                    previewRatios[resourceName] = ratio;
                    updateMaxAmounts(resourceName);
                }
            }
            GUILayout.EndScrollView();

            //Decouple resource combo toggle
            synchronizeComboResources = GUILayout.Toggle(synchronizeComboResources, "* Synchronize combo resources");

            //Clean up any resources removed from the lineup.
            int doomedCount = doomedResources.Count;
            if (doomedCount > 0)
            {
                for (int index = 0; index < doomedCount; index++)
                {
                    previewResources.Remove(doomedResources[index]);
                    previewRatios.Remove(doomedResources[index]);
                }
                recalculateMaxAmounts();
            }
        }

        public void RemoveAllResources()
        {
            if (updateSymmetry)
            {
                WBIOmniStorage symmetryStorage;
                foreach (Part symmetryPart in this.part.symmetryCounterparts)
                {
                    symmetryStorage = symmetryPart.GetComponent<WBIOmniStorage>();
                    if (symmetryStorage != null)
                    {
                        symmetryStorage.storageVolume = this.storageVolume;
                        symmetryStorage.isEmpty = this.isEmpty;

                        symmetryStorage.previewRatios.Clear();
                        symmetryStorage.previewResources.Clear();
                        symmetryStorage.part.Resources.Clear();
                    }
                }
            }

            this.part.Resources.Clear();
            resourceAmounts.Clear();
            previewRatios.Clear();
            previewResources.Clear();

            //Clear the switcher's list of omni storage resources
            if (switcher != null)
                switcher.omniStorageResources = string.Empty;

            //Dirty the GUI
            MonoUtilities.RefreshContextWindows(this.part);
            GameEvents.onPartResourceListChange.Fire(this.part);
        }

        protected void drawReconfigureButton()
        {
            if (HighLogic.LoadedSceneIsFlight)
                updateSymmetry = GUILayout.Toggle(updateSymmetry, "Update symmetrical parts");
            else
                updateSymmetry = true;

            //Delete resources button
            if (GUILayout.Button("Remove all resourecs"))
            {
                if (HighLogic.LoadedSceneIsFlight)
                {
                    if (confirmedReconfigure == false)
                    {
                        ScreenMessages.PostScreenMessage("Existing resources will be removed. Click a second time to confirm.", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                        confirmedReconfigure = true;
                        return;
                    }

                    confirmedReconfigure = false;
                }

                RemoveAllResources();
            }

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Reconfigure"))
            {
                //In flight mode, we need to make sure that we can afford to reconfigure the container and that
                //the user has confirmed the operation.
                if (HighLogic.LoadedSceneIsFlight)
                {
                    //Can we afford to reconfigure the container?
                    if (canAffordReconfigure() && hasSufficientSkill())
                    {
                        //Confirm reconfigure.
                        if (!confirmedReconfigure)
                        {
                            ScreenMessages.PostScreenMessage("Click again to confirm reconfiguration.", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                            confirmedReconfigure = true;
                        }

                        else
                        {
                            payForReconfigure();
                            reconfigureStorage(updateSymmetry);
                        }
                    }
                }

                //Not in flight so just reconfigure
                else
                {
                    reconfigureStorage(updateSymmetry);
                }
            }

            //Copy to clipboard
            if (GUILayout.Button(copyIcon, buttonOptions))
            {
                //Clear the clipboard
                clipboardRatios.Clear();
                clipboardResources.Clear();

                //Add the resource ratios
                foreach (string key in previewRatios.Keys)
                    clipboardRatios.Add(key, previewRatios[key]);

                //Add the resource amounts
                foreach (string key in previewResources.Keys)
                    clipboardResources.Add(key, previewResources[key]);
            }

            //Paste from clipboard
            if (GUILayout.Button(pasteIcon, buttonOptions))
            {
                if (clipboardRatios.Keys.Count == 0)
                    return;

                //Clear our cache
                previewResources.Clear();
                previewRatios.Clear();

                //Load from clipboard
                foreach (string key in clipboardRatios.Keys)
                    previewRatios.Add(key, clipboardRatios[key]);

                foreach (string key in clipboardResources.Keys)
                    previewResources.Add(key, clipboardResources[key]);

                //Update max amounts
                foreach (string key in previewRatios.Keys)
                    updateMaxAmounts(key);

                //Finally, reconfigure storage
                reconfigureStorage(updateSymmetry);
            }

            GUILayout.EndHorizontal();
        }

        protected void drawResourceList()
        {
            //Search field
            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=white>Search</color>");
            searchText = GUILayout.TextField(searchText).ToLower();
            GUILayout.EndHorizontal();

            scrollPos = GUILayout.BeginScrollView(scrollPos);

            string resourceName;
            definitions = PartResourceLibrary.Instance.resourceDefinitions;
            int definitionCount = definitions.Count;
            PartResourceDefinition def;
            foreach (string sortedResourceName in sortedResourceNames)
            {
                def = definitions[sortedResourceName];

                //Ignore items on the blacklist, locked resources list, and resources not shown to the user.
                if (resourceBlacklist.Contains(sortedResourceName) || def.isVisible == false || lockedResources.Contains(sortedResourceName))
                    continue;

                //Get button name
                resourceName = def.displayName;
                if (string.IsNullOrEmpty(resourceName))
                    resourceName = def.name;

                //Skip resource if it doesn't contain the search text.
                if (!string.IsNullOrEmpty(searchText))
                {
                    if (!resourceName.ToLower().Contains(searchText))
                        continue;
                }

                //Add resource to our list if needed.
                if (GUILayout.Button(resourceName))
                {
                    //Add the preview resource
                    if (previewResources.ContainsKey(def.name) == false)
                    {
                        previewResources.Add(def.name, 0);
                        previewRatios.Add(def.name, 1.0f);
                    }

                    //recalculate max amounts
                    recalculateMaxAmounts();
                }
            }
            GUILayout.EndScrollView();
        }
        #endregion

        #region Helpers
        protected void loadOmniResourceConfigs()
        {
            if (!string.IsNullOrEmpty(omniResources))
            {
                resourceAmounts.Clear();
                previewRatios.Clear();
                previewResources.Clear();

                double maxAmount;
                float ratio;
                string resourceName;
                string[] resourceConfigs = omniResources.Split(new char[] { ';' });
                string resourceConfig;
                string[] resourceParams;
                for (int index = 0; index < resourceConfigs.Length; index++)
                {
                    resourceConfig = resourceConfigs[index];
                    resourceParams = resourceConfig.Split(new char[] { ',' });
                    resourceName = resourceParams[0];
                    if (lockedResources.Contains(resourceName))
                        continue;
                    double.TryParse(resourceParams[1], out maxAmount);
                    float.TryParse(resourceParams[2], out ratio);
                    resourceAmounts.Add(resourceName, maxAmount);
                    previewResources.Add(resourceName, maxAmount);
                    previewRatios.Add(resourceName, ratio);
                }
            }
        }

        protected void buildOmniResourceConfigs()
        {
            if (resourceAmounts.Keys.Count == 0)
                return;
            string[] keys = resourceAmounts.Keys.ToArray();
            StringBuilder resourceConfigs = new StringBuilder();
            for (int index = 0; index < keys.Length; index++)
            {
                if (!previewRatios.ContainsKey(keys[index]))
                    continue;

                resourceConfigs.Append(keys[index]);
                resourceConfigs.Append(",");
                resourceConfigs.Append(resourceAmounts[keys[index]]);
                resourceConfigs.Append(",");
                resourceConfigs.Append(previewRatios[keys[index]]);
                resourceConfigs.Append(";");
            }

            if (!string.IsNullOrEmpty(resourceConfigs.ToString()))
            {
                omniResources = resourceConfigs.ToString();
                omniResources = omniResources.Substring(0, omniResources.Length - 1);
            }
        }

        protected void loadResourceCombos(ConfigNode[] comboNodes)
        {
            ConfigNode[] resourceNodes;
            ConfigNode comboNode = null;
            ConfigNode resourceNode = null;
            string resourceName = null;
            float ratio = 0.0f;
            double maxAmountMultiplier = 1.0;
            ComboRatio comboRatio;
            Dictionary<string, ComboRatio> comboRatios = null;
            List<string> existingComboKeys = new List<string>();
            string comboKey = string.Empty;

            //Build the list of existing combos
            int count = resourceCombos.Count;
            string[] keys;
            for (int index = 0; index < count; index++)
            {
                comboRatios = resourceCombos[index];
                comboKey = string.Empty;
                keys = comboRatios.Keys.ToArray();

                for (int keyIndex = 0; keyIndex < keys.Length; keyIndex++)
                {
                    comboKey += keys[keyIndex];
                }

                existingComboKeys.Add(comboKey);
            }

            //Now add the new ones
            for (int index = 0; index < comboNodes.Length; index++)
            {
                comboNode = comboNodes[index];

                resourceNodes = comboNode.GetNodes("RESOURCE");
                comboRatios = new Dictionary<string, ComboRatio>();
                comboKey = string.Empty;
                for (int nodeIndex = 0; nodeIndex < resourceNodes.Length; nodeIndex++)
                {
                    //Get the resource node
                    resourceNode = resourceNodes[nodeIndex];

                    //Get the name
                    if (!resourceNode.HasValue("name"))
                        continue;
                    resourceName = resourceNode.GetValue("name");

                    //Get the ratio
                    if (!resourceNode.HasValue("ratio"))
                        continue;
                    float.TryParse(resourceNode.GetValue("ratio"), out ratio);

                    //Get the multiplier
                    maxAmountMultiplier = 1.0;
                    if (resourceNode.HasValue("maxAmountMultiplier"))
                        double.TryParse(resourceNode.GetValue("maxAmountMultiplier"), out maxAmountMultiplier);

                    //Add the resource to the dictionary
                    comboRatio = new ComboRatio();
                    comboRatio.ratio = ratio;
                    comboRatio.maxAmountMultiplier = maxAmountMultiplier;
                    comboRatios.Add(resourceName, comboRatio);

                    // Update the comboKey
                    comboKey += resourceName;
                }

                //Add the combo to the list.
                if (comboRatios.Keys.Count > 0 && !string.IsNullOrEmpty(comboKey) && !existingComboKeys.Contains(comboKey))
                {
                    resourceCombos.Add(comboRatios);
                    existingComboKeys.Add(comboKey);
                }
            }
        }

        protected bool isComboResource(string resourceName)
        {
            int comboCount = resourceCombos.Count;
            if (comboCount == 0)
            {
                return false;
            }

            Dictionary<string, ComboRatio> comboRatios = null;
            for (int index = 0; index < comboCount; index++)
            {
                comboRatios = resourceCombos[index];

                if (comboRatios.Keys.Contains(resourceName))
                    return true;
            }

            return false;
        }

        protected Dictionary<string, ComboRatio> findComboPattern()
        {
            int comboCount = resourceCombos.Count;
            if (comboCount == 0)
            {
                Debug.Log("no combos loaded");
                return null;
            }
            Dictionary<string, ComboRatio> comboRatios = null;
            bool foundMatch;
            string[] comboResourceNames;

            //Loop through all the combos and see if we can find a match.
            for (int index = 0; index < comboCount; index++)
            {
                comboRatios = resourceCombos[index];

                //If just one of the resources in the combo isn't in our preview list then skip to the next combo.
                foundMatch = true;
                comboResourceNames = comboRatios.Keys.ToArray();
                for (int comboIndex = 0; comboIndex < comboResourceNames.Length; comboIndex++)
                {
                    if (!previewResources.Keys.Contains(comboResourceNames[comboIndex]))
                    {
                        foundMatch = false;
                        break;
                    }
                }

                //If we found a match then return the combo
                if (foundMatch)
                    return comboRatios;
            }

            //Nothing to see here...
            return null;
        }

        protected void applyComboPatternRatios()
        {
            //Find a resource combo that matches our current resource set
            Dictionary<string, ComboRatio> comboRatios = findComboPattern();
            if (comboRatios == null)
                return;

            //Tally up the total units
            string[] keys = comboRatios.Keys.ToArray();
            double totalUnits = 0;
            for (int index = 0; index < keys.Length; index++)
                totalUnits += previewResources[keys[index]];

            //Go through all the combo pattern ratios and adjust the preview units
            ComboRatio comboRatio;
            string resourceName;
            for (int index = 0; index < keys.Length; index++)
            {
                resourceName = keys[index];
                comboRatio = comboRatios[resourceName];
                previewResources[keys[index]] = (totalUnits * comboRatio.ratio) * comboRatio.maxAmountMultiplier;
            }
        }

        protected void updateResourceComboRatios(string updatedResource)
        {
            //Find a resource combo that matches our current resource set
            Dictionary<string, ComboRatio> comboRatios = findComboPattern();
            if (comboRatios == null)
                return;
            if (!comboRatios.ContainsKey(updatedResource))
                return;
            if (!synchronizeComboResources)
                return;

            float ratio = previewRatios[updatedResource];
            string[] keys = comboRatios.Keys.ToArray();
            for (int index = 0; index < keys.Length; index++)
                previewRatios[keys[index]] = ratio;
        }

        protected ConfigNode getPartConfigNode()
        {
            if (partConfigNode != null)
                return partConfigNode;
            if (!HighLogic.LoadedSceneIsEditor && !HighLogic.LoadedSceneIsFlight)
                return null;
            if (this.part.partInfo.partConfig == null)
                return null;
            ConfigNode[] nodes = this.part.partInfo.partConfig.GetNodes("MODULE");
            ConfigNode storageNode = null;
            ConfigNode node = null;
            string moduleName;

            //Get the switcher config node.
            for (int index = 0; index < nodes.Length; index++)
            {
                node = nodes[index];
                if (node.HasValue("name"))
                {
                    moduleName = node.GetValue("name");
                    if (moduleName == this.ClassName)
                    {
                        storageNode = node;
                        break;
                    }
                }
            }

            partConfigNode = storageNode;
            return storageNode;
        }

        protected ConfigNode[] getPartComboNodes()
        {
            ConfigNode storageNode = getPartConfigNode();
            if (storageNode == null)
                return new ConfigNode[] { };

            if (storageNode.HasNode("OMNIRESOURCECOMBO"))
                return storageNode.GetNodes("OMNIRESOURCECOMBO");
            else
                return new ConfigNode[] { };
        }

        protected void clearDefaultResources()
        {
            ConfigNode[] nodes = null;
            ConfigNode storageNode = getPartConfigNode();
            ConfigNode node = null;
            List<string> defaultResources = new List<string>();
            string resourceName;

            if (storageNode == null)
                return;

            //Get the nodes we're interested in and add to our list of resources
            nodes = storageNode.GetNodes("DEFAULT_RESOURCE");
            for (int index = 0; index < nodes.Length; index++)
            {
                node = nodes[index];
                if (node.HasValue("name"))
                {
                    resourceName = node.GetValue("name");
                    defaultResources.Add(resourceName);
                }
            }
            if (!string.IsNullOrEmpty(defaultResourceNames))
            {
                string[] names = defaultResourceNames.Split(new char[] { ';' });
                for (int index = 0; index < names.Length; index++)
                {
                    defaultResources.Add(names[index]);
                }
            }

            //Finally, clear any default resources.
            int count = defaultResources.Count;
            for (int index = 0; index < count; index++)
            {
                resourceName = defaultResources[index];
                if (this.part.Resources.Contains(resourceName) && !resourceAmounts.ContainsKey(resourceName))
                    part.Resources.Remove(resourceName);
            }
        }

        protected void setupDefaultResources()
        {
            if (!HighLogic.LoadedSceneIsEditor && !HighLogic.LoadedSceneIsFlight)
                return;
            if (isEmpty)
                return;
            ConfigNode[] nodes = null;
            ConfigNode storageNode = getPartConfigNode();
            ConfigNode node = null;
            string moduleName;

            if (storageNode == null)
                return;

            //Get the nodes we're interested in
            nodes = storageNode.GetNodes("DEFAULT_RESOURCE");
            PartResourceDefinition definition;
            string resourceName;
            double maxAmount;
            float ratio;
            for (int index = 0; index < nodes.Length; index++)
            {
                node = nodes[index];
                if (node.HasValue("name"))
                {
                    resourceName = nodes[index].GetValue("name");

                    if (resourceName != kKISResource)
                    {
                        definition = definitions[resourceName];
                        if (definition == null)
                            continue;
                    }

                    if (!node.HasValue("maxAmount"))
                        continue;
                    if (!double.TryParse(node.GetValue("maxAmount"), out maxAmount))
                        continue;

                    if (!node.HasValue("ratio"))
                        continue;
                    if (!float.TryParse(node.GetValue("ratio"), out ratio))
                        continue;

                    //Setup our caches
                    resourceAmounts.Add(resourceName, maxAmount);
                    previewResources.Add(resourceName, maxAmount);
                    previewRatios.Add(resourceName, ratio);

                    ratio = 1.0f;
                }
            }

            //If part resources are default, then use them.
            if (resourceAmounts.Count == 0 && previewResources.Count == 0 && previewRatios.Count == 0 && this.part.Resources.Count > 0)
            {
                int count = this.part.Resources.Count;
                PartResource resource;
                for (int index = 0; index < count; index ++)
                {
                    resource = this.part.Resources[index];
                    resourceName = resource.resourceName;
                    if (lockedResources.Contains(resourceName))
                        continue;
                    maxAmount = resource.maxAmount;

                    resourceAmounts.Add(resourceName, maxAmount);
                    previewResources.Add(resourceName, maxAmount);
                    previewRatios.Add(resourceName, 1.0f);
                    defaultResourceNames += resourceName + ";";
                }
            }
        }

        protected void onDeployStateChanged(bool deployed)
        {
            //Change max storage amounts based on deployed state
            string resourceName;
            string[] keys = resourceAmounts.Keys.ToArray();
            for (int index = 0; index < keys.Length; index++)
            {
                resourceName = keys[index];

                if (this.part.Resources.Contains(resourceName))
                {
                    if (deployed)
                    {
                        this.part.Resources[resourceName].maxAmount = resourceAmounts[resourceName];
                    }
                    else
                    {
                        this.part.Resources[resourceName].maxAmount = 1.0f;
                        this.part.Resources[resourceName].amount = 0.0f;
                    }
                }
            }
        }

        protected void updatePartMaxAmounts()
        {
            string[] resourceNames = previewResources.Keys.ToArray();
            string resourceName;
            double maxAmount = 0;

            for (int index = 0; index < resourceNames.Length; index++)
            {
                resourceName = resourceNames[index];
                if (resourceName == kKISResource || !part.Resources.Contains(resourceName))
                    continue;
                maxAmount = previewResources[resourceName];
                part.Resources[resourceName].maxAmount = maxAmount;
            }
        }

        protected void updateMaxAmounts(string updatedResource)
        {
            float litersPerResource = 0;
            string[] resourceNames = previewResources.Keys.ToArray();
            string resourceName;
            PartResourceDefinition definition;

            //Don't adjust max amounts if we only have one resource.
            if (resourceNames.Length == 1)
            {
                previewRatios[updatedResource] = 1.0f;
                return;
            }

            //Update ratios for resource combos
            updateResourceComboRatios(updatedResource);

            //Determine the number of liters per resource
            litersPerResource = adjustedVolume / resourceNames.Length;

            //Calculate liters based on current ratios
            float totalLitersAllocated = 0.0f;
            for (int index = 0; index < resourceNames.Length; index++)
                totalLitersAllocated += litersPerResource * previewRatios[resourceNames[index]];
            
            //From the remaining volume, calculate the extra to give to each resource
            float litersDelta = (adjustedVolume - totalLitersAllocated) / resourceNames.Length;

            //Update max amounts
            for (int index = 0; index < resourceNames.Length; index++)
            {
                resourceName = resourceNames[index];

                //Get the resource definition
                definition = definitions[resourceName];

                //Calculate the liters
                previewResources[resourceName] = (litersPerResource * previewRatios[resourceName]) + litersDelta;

                //Account for units per liter
                if (resourceName != kKISResource)
                    previewResources[resourceName] = (previewResources[resourceName] / definition.volume) * getMaxAmountMultiplier(resourceName);
            }

            //Adjust units to account for resource combos.
            applyComboPatternRatios();
        }

        protected void recalculateMaxAmounts()
        {
            float litersPerResource = 0;
            string[] resourceNames = previewResources.Keys.ToArray();
            string resourceName;
            PartResourceDefinition definition;

            //Determine the number of liters per resource
            litersPerResource = resourceNames.Length;
            litersPerResource = adjustedVolume / litersPerResource;

            //The max number of units for a resource depends upon the resouce's volume in liters per unit.
            //We'll need to get the definition for each resource and set max amount accordingly.
            previewRatios.Clear();
            for (int index = 0; index < resourceNames.Length; index++)
            {
                resourceName = resourceNames[index];

                if (resourceName == kKISResource)
                {
                    //Set max amount
                    previewResources[resourceName] = litersPerResource;

                    //Set ratio
                    previewRatios.Add(resourceName, 1.0f);
                }
                else
                {
                    //Get the resource definition
                    definition = definitions[resourceName];

                    //Set max amount
                    previewResources[resourceName] = (litersPerResource / definition.volume) * getMaxAmountMultiplier(resourceName);

                    //Set ratio
                    previewRatios.Add(resourceName, 1.0f);
                }
            }

            //Adjust units to account for resource combos.
            applyComboPatternRatios();
        }


        protected void addRestrictedResources()
        {
            if (!HighLogic.LoadedSceneIsFlight)
                return;

            string[] keys = resourceAmounts.Keys.ToArray();
            string resourceName;
            for (int index = 0; index < keys.Length; index++)
            {
                resourceName = keys[index];
                if (!restrictedResourceList.Contains(resourceName))
                    continue;

                if (switcher != null)
                {
                    if (this.part.Resources.Contains(resourceName))
                        this.part.Resources[resourceName].maxAmount = resourceAmounts[resourceName];
                    else if ((switcher.isInflatable && switcher.isDeployed) || !switcher.isInflatable)
                        AddResource(resourceName, 0, resourceAmounts[resourceName], this.part);
                    else
                        AddResource(resourceName, 0, 1.0f, this.part);
                }

                else
                {
                    if (this.part.Resources.Contains(resourceName))
                        this.part.Resources[resourceName].maxAmount = resourceAmounts[resourceName];
                    else
                        AddResource(resourceName, 0, resourceAmounts[resourceName], this.part);
                }
            }
        }

        protected void updateSymmetryParts()
        {
            WBIOmniStorage symmetryStorage;
            foreach (Part symmetryPart in this.part.symmetryCounterparts)
            {
                symmetryStorage = symmetryPart.GetComponent<WBIOmniStorage>();
                if (symmetryStorage != null)
                {
                    symmetryStorage.storageVolume = this.storageVolume;
                    symmetryStorage.isEmpty = this.isEmpty;

                    symmetryStorage.previewRatios.Clear();
                    symmetryStorage.previewResources.Clear();
                    foreach (string key in this.previewRatios.Keys)
                    {
                        symmetryStorage.previewResources.Add(key, previewResources[key]);
                        symmetryStorage.previewRatios.Add(key, this.previewRatios[key]);
                    }

                    symmetryStorage.reconfigureStorage(false);
                }
            }
        }

        public void reconfigureStorage(bool reconfigureSymmetryParts = false)
        {
            if (reconfigureSymmetryParts)
                updateSymmetryParts();

            //Clear any resources we own.
            string[] keys = resourceAmounts.Keys.ToArray();
            string resourceName;
            for (int index = 0; index < keys.Length; index++)
            {
                resourceName = keys[index];
                if (resourceName == kKISResource)
                    continue;

                if (part.Resources.Contains(resourceName))
                    part.Resources.Remove(resourceName);
            }

            //Clear the switcher's list of omni storage resources
            if (switcher != null)
                switcher.omniStorageResources = string.Empty;

            //Transfer the preview resources to our resource amounts map
            //and add the resource.
            isEmpty = true;
            resourceAmounts.Clear();
            keys = previewResources.Keys.ToArray();
            double currentAmount = 0;
            for (int index = 0; index < keys.Length; index++)
            {
                isEmpty = false;
                resourceName = keys[index];
                resourceAmounts.Add(resourceName, previewResources[resourceName]);

                //If the resource is on the restricted list then it'll be added in flight.
                if (restrictedResourceList.Contains(resourceName) && HighLogic.LoadedSceneIsEditor && HighLogic.CurrentGame.Mode == Game.Modes.CAREER)
                {
                    if (switcher != null)
                    {
                        //Add to the list of omni storage resources
                        if (string.IsNullOrEmpty(switcher.omniStorageResources))
                            switcher.omniStorageResources = resourceName;
                        else
                            switcher.omniStorageResources += resourceName + ";";

                        switcher.buildInputList(switcher.CurrentTemplateName);
                    }
                    continue;
                }

                //Set current amount.
                if (HighLogic.LoadedSceneIsEditor)
                    currentAmount = resourceAmounts[resourceName];
                else
                    currentAmount = 0.0f;

                //Skip the KIS inventory for now...
                if (resourceName == kKISResource)
                    continue;

                //Account for unassembled parts.
                if (switcher != null)
                {
                    //Add to the list of omni storage resources
                    if (string.IsNullOrEmpty(switcher.omniStorageResources))
                        switcher.omniStorageResources = resourceName;
                    else
                        switcher.omniStorageResources += resourceName + ";";

                    if (this.part.Resources.Contains(resourceName))
                        this.part.Resources[resourceName].maxAmount = resourceAmounts[resourceName];
                    else if ((switcher.isInflatable && switcher.isDeployed) || !switcher.isInflatable)
                        AddResource(resourceName, currentAmount, resourceAmounts[resourceName], this.part);
                    else
                        AddResource(resourceName, 0, 1.0f, this.part);
                }
                else
                {
                    if (this.part.Resources.Contains(resourceName))
                        this.part.Resources[resourceName].maxAmount = resourceAmounts[resourceName];
                    else
                        AddResource(resourceName, currentAmount, resourceAmounts[resourceName], this.part);
                }
            }

            // Update stock inventory
            if (stockInventory != null)
            {
                stockInventory.packedVolumeLimit = stockInventoryVolumeUpdate;
                packedVolumeLimit = stockInventoryVolumeUpdate;
            }

            //Build the sorage configuration
            buildOmniResourceConfigs();

            //Dirty the GUI
            MonoUtilities.RefreshContextWindows(this.part);
            GameEvents.onPartResourceListChange.Fire(this.part);
            if (HighLogic.LoadedSceneIsEditor)
                GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
        }

        protected void GetRequiredResources(float materialModifier)
        {
            if (string.IsNullOrEmpty(requiredResource))
                return;

            if (switcher.inputList.ContainsKey(requiredResource))
                switcher.inputList[requiredResource] += requiredAmount * materialModifier;
            else
                switcher.inputList.Add(requiredResource, requiredAmount * materialModifier);
        }

        protected virtual void payForReconfigure()
        {
            //If we don't pay to reconfigure then we're done.
            if (!WBIResourcesSettings.PayToReconfigure)
                return;

            //If we have a switcher, tell it to pay the cost. It might be able to ask resource distributors.
            if (switcher != null)
            {
                switcher.payForReconfigure(requiredResource, requiredAmount);
                return;
            }

            //We have to pay for it ourselves.
            this.part.RequestResource(requiredResource, requiredAmount, ResourceFlowMode.STAGE_PRIORITY_FLOW);
        }

        protected bool hasSufficientSkill()
        {
            if (string.IsNullOrEmpty(reconfigureSkill))
                return true;

            if (WBIResourcesSettings.RequiresSkillCheck)
            {
                // Check repair bot
                int count = FlightGlobals.ActiveVessel.parts.Count;
                Part vesselpart;
                int moduleCount;
                for (int index = 0; index < count; index++)
                {
                    vesselpart = FlightGlobals.ActiveVessel.parts[index];
                    moduleCount = vesselpart.Modules.Count;
                    for (int moduleIndex = 0; moduleIndex < moduleCount; moduleIndex++)
                    {
                        if (vesselpart.Modules[moduleIndex].moduleName == "WBIModuleEVABotRepairs")
                            return true;
                    }
                }

                //Check the crew roster
                ProtoCrewMember[] vesselCrew = vessel.GetVesselCrew().ToArray();
                for (int index = 0; index < vesselCrew.Length; index++)
                {
                    if (vesselCrew[index].HasEffect(reconfigureSkill))
                    {
                        if (reconfigureRank > 0)
                        {
                            int crewRank = vesselCrew[index].experienceTrait.CrewMemberExperienceLevel();
                            if (crewRank >= reconfigureRank)
                                return true;
                        }
                        else
                        {
                            return true;
                        }
                    }
                }

                //Insufficient skill
                ScreenMessages.PostScreenMessage("Insufficient skill to reconfigure the converter.");
                return false;
            }

            return true;
        }

        protected virtual bool canAffordReconfigure()
        {
            if (string.IsNullOrEmpty(requiredResource))
                return true;
            if (!WBIResourcesSettings.PayToReconfigure)
                return true;

            if (switcher != null)
            {
                //If we're inflatable and not inflated then we're done.
                if (switcher.isInflatable && !switcher.isDeployed)
                    return true;

                //Ask the switcher if we can afford it. It might be able to ask distributed resource containers...
                return switcher.canAffordResource(requiredResource, requiredAmount);
            }

            //Check the available amount of the resource.
            double amountAvailable = GetTotalResourceAmount(requiredResource, this.part.vessel);
            if (amountAvailable >= requiredAmount)
                return true;

            //Can't afford it.
            PartResourceDefinitionList definitions = PartResourceLibrary.Instance.resourceDefinitions;
            PartResourceDefinition resourceDef = definitions[requiredResource];
            ScreenMessages.PostScreenMessage("Insufficient " + resourceDef.displayName + " to reconfigure the converter.");
            return false;
        }
        
        protected string getRequiredTraits()
        {
            string[] traits = GetTraitsWithEffect(reconfigureSkill);
            string requiredTraits = "";

            if (traits.Length == 1)
            {
                requiredTraits = traits[0];
            }

            else if (traits.Length > 1)
            {
                for (int index = 0; index < traits.Length - 1; index++)
                    requiredTraits += traits[index] + ",";
                requiredTraits += " or " + traits[traits.Length - 1];
            }

            else
            {
                requiredTraits = reconfigureSkill;
            }

            return requiredTraits;
        }

        protected double getMaxAmountMultiplier(string resourceName)
        {
            ConfigNode configNode = getPartConfigNode();
            if (configNode != null && configNode.HasNode("MAX_RESOURCE_MULTIPLIER"))
            {
                double multiplier = getMaxAmountMultiplier(resourceName, configNode.GetNodes("MAX_RESOURCE_MULTIPLIER"), -1.0);
                if (multiplier > 0)
                    return multiplier;
            }

            return getMaxAmountMultiplier(resourceName, GameDatabase.Instance.GetConfigNodes("MAX_RESOURCE_MULTIPLIER"));
        }

        protected double getMaxAmountMultiplier(string resourceName, ConfigNode[] maxAmountNodes, double defaultMultiplier = 1.0)
        {
            ConfigNode node;
            double maxAmountMultiplier = 0.0f;

            for (int index = 0; index < maxAmountNodes.Length; index++)
            {
                node = maxAmountNodes[index];

                if (!node.HasValue("name") || !node.HasValue("maxAmountMultiplier"))
                    continue;

                if (node.GetValue("name") == resourceName)
                {
                    maxAmountMultiplier = 1.0;
                    double.TryParse(node.GetValue("maxAmountMultiplier"), out maxAmountMultiplier);
                    return maxAmountMultiplier;
                }
            }

            return defaultMultiplier;
        }

        public void AddResource(string resourceName, double amount, double maxAmount, Part part)
        {
            PartResourceDefinitionList definitions = PartResourceLibrary.Instance.resourceDefinitions;

            //First, does the resource definition exist?
            if (definitions.Contains(resourceName))
            {
                part.Resources.Add(resourceName, amount, maxAmount, true, true, false, true, PartResource.FlowMode.Both);
            }
        }

        public double GetTotalResourceAmount(string resourceName, Vessel vessel)
        {
            PartResourceDefinitionList definitions = PartResourceLibrary.Instance.resourceDefinitions;
            List<Part> parts;
            double totalResources = 0f;

            //First, does the resource definition exist?
            if (definitions.Contains(resourceName))
            {
                //The definition exists, now see if the vessel has the resource
                parts = vessel.parts;
                foreach (Part part in parts)
                {
                    if (part.Resources.Count > 0)
                    {
                        foreach (PartResource res in part.Resources)
                            if (res.resourceName == resourceName)
                                totalResources += res.amount;
                    }
                }
            }

            return totalResources;
        }

        public float GetResourceCost(Part part, bool maxAmount = false)
        {
            PartResourceDefinitionList definitions = PartResourceLibrary.Instance.resourceDefinitions;
            PartResourceList resources = part.Resources;
            PartResourceDefinition resourceDef;
            float totalCost = 0f;

            foreach (PartResource resource in resources)
            {
                //Find definition
                resourceDef = definitions[resource.resourceName];
                if (resourceDef != null)
                {
                    if (!maxAmount)
                        totalCost += (float)(resourceDef.unitCost * resource.amount);
                    else
                        totalCost += (float)(resourceDef.unitCost * resource.maxAmount);
                }

            }

            return totalCost;
        }

        public string[] GetTraitsWithEffect(string effectName)
        {
            List<string> traits;
            traits = GameDatabase.Instance.ExperienceConfigs.GetTraitsWithEffect(effectName);

            if (traits == null)
            {
                traits = new List<string>();
            }

            return traits.ToArray();
        }
        #endregion

        #region IPartCostModifier
        public float GetModuleCost(float defaultCost, ModifierStagingSituation sit)
        {
            return (WBIOmniManager.Instance.GetOriginalResourceCost(this.part) * -1f) + GetResourceCost(this.part, true);
        }

        public ModifierChangeWhen GetModuleCostChangeWhen()
        {
            return ModifierChangeWhen.FIXED;
        }
        #endregion
    }
}
