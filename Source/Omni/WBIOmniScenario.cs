using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSP.IO;
using FinePrint;
using Upgradeables;
using KSP.UI.Screens;
using KSP.Localization;

namespace WBIResources
{
    #region AnomalyResource
    public class AnomalyResource
    {
        public string name;
        public string resourceName;
        public double minAbundance;
        public double maxAbundance;
        public double abundance;
        public double minAmount;
        public double maxAmount;
        public double currentAmount;

        public AnomalyResource(ConfigNode node)
        {
            if (node.HasValue("name"))
                name = node.GetValue("name");

            if (node.HasValue("resourceName"))
                resourceName = node.GetValue("resourceName");

            if (node.HasValue("minAbundance"))
                double.TryParse(node.GetValue("minAbundance"), out minAbundance);
            if (node.HasValue("maxAbundance"))
                double.TryParse(node.GetValue("maxAbundance"), out maxAbundance);

            if (node.HasValue("abundance"))
                double.TryParse(node.GetValue("abundance"), out abundance);
            else if (minAbundance > 0 && maxAbundance > 0)
            {
                abundance = UnityEngine.Random.Range((float)minAbundance, (float)maxAbundance);
            }
            else if (maxAbundance > 0)
            {
                abundance = maxAbundance;
            }
            else if (minAbundance > 0)
            {
                abundance = minAbundance;
            }

            if (node.HasValue("minAmount"))
                double.TryParse(node.GetValue("minAmount"), out minAmount);
            if (node.HasValue("maxAmount"))
                double.TryParse(node.GetValue("maxAmount"), out maxAmount);

            if (node.HasValue("currentAmount"))
            {
                double.TryParse(node.GetValue("currentAmount"), out currentAmount);
            }
            else if (minAmount > 0 && maxAmount > 0)
            {
                currentAmount = UnityEngine.Random.Range((float)minAmount, (float)maxAmount);
            }
            else if (maxAmount > 0)
            {
                currentAmount = maxAmount;
            }
            else if (minAmount > 0)
            {
                currentAmount = minAmount;
            }
        }

        public void Save(ConfigNode node)
        {
            node.SetValue("name", name, true);
            node.SetValue("resourceName", resourceName, true);
            node.SetValue("abundance", abundance.ToString(), true);
            node.SetValue("minAmount", minAmount.ToString(), true);
            node.SetValue("maxAmount", maxAmount.ToString(), true);
            node.SetValue("currentAmount", currentAmount.ToString(), true);
        }
    }
    #endregion

    /// <summary>
    /// The purpose of this class is to run converters in the background, meaning that the vessel is currently unloaded. This is primarily to drive life support systems,
    /// but other types of converters also benefit.
    /// </summary>
    [KSPScenario(ScenarioCreationOptions.AddToAllGames, GameScenes.SPACECENTER, GameScenes.EDITOR, GameScenes.FLIGHT, GameScenes.TRACKSTATION)]
    public class WBIOmniScenario : ScenarioModule
    {
        #region Constants
        public double secondsPerCycle = 3600;
        #endregion

        #region Housekeeping
        public static WBIOmniScenario Instance;

        public bool debugMode = false;
        public double cycleStartTime;
        public Dictionary<Vessel, List<WBIBackgroundConverter>> backgroundConverters;
        public List<Part> createdParts;
        public Dictionary<string, float> originalResourceCosts;
        public Dictionary<string, Dictionary<string, AnomalyResource>> anomalyResources;
        #endregion

        #region Background processing
        public void FixedUpdate()
        {
            double currentTime = Planetarium.GetUniversalTime();
            if (cycleStartTime <= 0f)
            {
                cycleStartTime = currentTime;
                return;
            }

            double elapsedTime = currentTime - cycleStartTime;
            if (elapsedTime < secondsPerCycle)
                return;

            // Reset the timer even when there are no converters. Otherwise a newly
            // activated converter inherits all the elapsed time since the last one ran.
            cycleStartTime = currentTime;

            // Rebuild the wrappers each cycle so newly unloaded/activated converters
            // are discovered and transient missing-resource/full-container flags reset.
            refreshBackgroundConverters();
            if (backgroundConverters == null || backgroundConverters.Count == 0)
                return;

            Vessel vessel;
            int count = FlightGlobals.Vessels.Count;
            for (int vesselIndex = 0; vesselIndex < count; vesselIndex++)
            {
                vessel = FlightGlobals.Vessels[vesselIndex];

                //Skip vessel types that we're not interested in.
                if (vessel.vesselType == VesselType.Debris ||
                    vessel.vesselType == VesselType.Flag ||
                    vessel.vesselType == VesselType.SpaceObject ||
                    vessel.vesselType == VesselType.Unknown)
                    continue;

                //Run background converters
                if (!vessel.loaded)
                    runBackgroundConverters(vessel, elapsedTime);
            }
        }

        protected void runBackgroundConverters(Vessel vessel, double elapsedTime)
        {
            List<WBIBackgroundConverter> converters;
            if (backgroundConverters.ContainsKey(vessel))
                converters = backgroundConverters[vessel];
            else
                return;
            int count = converters.Count;
            WBIBackgroundConverter converter;

            for (int index = 0; index < count; index++)
            {
                converter = converters[index];

                if (converter.IsActivated && !converter.isMissingResources && !converter.isContainerFull)
                    StartCoroutine(runConverter(converter, elapsedTime, vessel.protoVessel));
            }
        }

        protected IEnumerator<YieldInstruction> runConverter(WBIBackgroundConverter converter, double elapsedTime, ProtoVessel protoVessel)
        {
            //Get ready to process
            converter.PrepareToProcess(protoVessel);
            yield return new WaitForFixedUpdate();

            //Check required
            converter.CheckRequiredResources(protoVessel, elapsedTime);
            yield return new WaitForFixedUpdate();

            //Consume inputs
            converter.ConsumeInputResources(protoVessel, elapsedTime);
            yield return new WaitForFixedUpdate();

            //Produce outputs
            converter.ProduceOutputResources(protoVessel, elapsedTime);
            yield return new WaitForFixedUpdate();

            //Produce yields
            converter.ProduceYieldResources(protoVessel);
            yield return new WaitForFixedUpdate();

            //Post process
            converter.PostProcess(protoVessel);
            yield return new WaitForFixedUpdate();
        }

        #endregion

        #region API
        public float GetOriginalResourceCost(Part part)
        {
            if (originalResourceCosts.ContainsKey(part.partInfo.name))
            {
                float cost = originalResourceCosts[part.partInfo.name];

                if (debugMode)
                    Debug.Log(string.Format("[WBIOmniScenario] original resource cost: {0:n2}", cost));

                return cost;
            }

            if (debugMode)
                Debug.Log(string.Format("[WBIOmniScenario] {0:s} part cost: {1:n2}", part.partInfo.name, part.partInfo.cost));

            float resourceCost = ResourceHelper.GetResourceCost(part, true);
            if (debugMode)
                Debug.Log(string.Format("[WBIOmniScenario] original resource cost: {0:n2}", resourceCost));

            originalResourceCosts.Add(part.partInfo.name, resourceCost);
            return resourceCost;
        }

        public bool WasRecentlyCreated(Part part)
        {
            if (createdParts == null)
                createdParts = new List<Part>();
            return createdParts.Contains(part);
        }
        #endregion

        #region Overrides
        internal void Start()
        {
            Instance = this;
            refreshBackgroundConverters();
        }

        public override void OnAwake()
        {
            Instance = this;
            GameEvents.onVesselChange.Add(onVesselChange);
            GameEvents.onVesselDestroy.Add(onVesselDestroy);
            GameEvents.onEditorPartEvent.Add(onEditorPartEvent);

            if (cycleStartTime <= 0)
                cycleStartTime = Planetarium.GetUniversalTime();

            originalResourceCosts = new Dictionary<string, float>();
        }

        public override void OnLoad(ConfigNode node)
        {
            //Housekeeping
            double.TryParse(node.GetValue("cycleStartTime"), out cycleStartTime);

            if (originalResourceCosts == null)
                originalResourceCosts = new Dictionary<string, float>();

            if (node.HasNode("OriginalResourceCost"))
            {
                ConfigNode[] costNodes = node.GetNodes("OriginalResourceCost");
                string partName = string.Empty;
                float dryCost = 0f;
                foreach (ConfigNode costNode in costNodes)
                {
                    if (costNode.HasValue("partName") && costNode.HasValue("cost"))
                    {
                        partName = costNode.GetValue("partName");
                        float.TryParse(costNode.GetValue("cost"), out dryCost);
                        if (!originalResourceCosts.ContainsKey(partName))
                            originalResourceCosts.Add(partName, dryCost);
                    }
                }
            }

            // Load current definitions first, then overlay saved depletion state.
            // This preserves existing games while still admitting newly installed
            // anomaly-resource definitions.
            anomalyResources = new Dictionary<string, Dictionary<string, AnomalyResource>>();
            loadAnomalyResources(GameDatabase.Instance.GetConfigNodes("ANOMALY_RESOURCE"), false);
            loadAnomalyResources(node.GetNodes("ANOMALY_RESOURCE"), true);
        }

        public override void OnSave(ConfigNode node)
        {
            //Housekeeping
            node.AddValue("cycleStartTime", cycleStartTime);

            foreach (string key in originalResourceCosts.Keys)
            {
                ConfigNode costNode = new ConfigNode("OriginalResourceCost");
                costNode.AddValue("partName", key);
                costNode.AddValue("cost", originalResourceCosts[key].ToString());
                node.AddNode(costNode);
            }

            Dictionary<string, AnomalyResource> resources;
            ConfigNode anomalyResourceNode;
            foreach (string key in anomalyResources.Keys)
            {
                resources = anomalyResources[key];
                foreach (string resourceKey in resources.Keys)
                {
                    anomalyResourceNode = new ConfigNode("ANOMALY_RESOURCE");
                    resources[resourceKey].Save(anomalyResourceNode);
                    node.AddNode(anomalyResourceNode);
                }
            }
        }

        public void OnDestroy()
        {
            GameEvents.onVesselDestroy.Remove(onVesselDestroy);
            GameEvents.onVesselChange.Remove(onVesselChange);
            GameEvents.onEditorPartEvent.Remove(onEditorPartEvent);

            if (Instance == this)
                Instance = null;
        }

        protected void onVesselChange(Vessel vessel)
        {
            refreshBackgroundConverters();
        }

        protected void onVesselDestroy(Vessel vessel)
        {
            if (backgroundConverters != null)
                backgroundConverters.Remove(vessel);
        }

        public void onEditorPartEvent(ConstructionEventType eventType, Part part)
        {
            if (!HighLogic.LoadedSceneIsEditor)
                return;
            if (createdParts == null)
                createdParts = new List<Part>();

            switch (eventType)
            {
                case ConstructionEventType.PartCreated:
                    if (!createdParts.Contains(part))
                        createdParts.Add(part);
                    break;

                case ConstructionEventType.PartDeleted:
                    if (createdParts.Contains(part))
                        createdParts.Remove(part);
                    break;
            }
        }

        private void refreshBackgroundConverters()
        {
            backgroundConverters = WBIBackgroundConverter.GetBackgroundConverters();
        }

        private void loadAnomalyResources(ConfigNode[] resourceNodes, bool replaceExisting)
        {
            for (int index = 0; index < resourceNodes.Length; index++)
            {
                AnomalyResource anomalyResource = new AnomalyResource(resourceNodes[index]);
                if (string.IsNullOrEmpty(anomalyResource.name) ||
                    string.IsNullOrEmpty(anomalyResource.resourceName))
                    continue;

                Dictionary<string, AnomalyResource> resources;
                if (!anomalyResources.TryGetValue(anomalyResource.name, out resources))
                {
                    resources = new Dictionary<string, AnomalyResource>();
                    anomalyResources.Add(anomalyResource.name, resources);
                }

                if (replaceExisting || !resources.ContainsKey(anomalyResource.resourceName))
                    resources[anomalyResource.resourceName] = anomalyResource;
            }
        }

        #endregion
    }
}
