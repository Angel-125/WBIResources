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
    public class WBIInflatablePartModule : ExtendedPartModule
    {
        [KSPField()]
        public string animationName = string.Empty;

        [KSPField()]
        public string startEventGUIName = string.Empty;

        [KSPField()]
        public string endEventGUIName = string.Empty;

        [KSPField()]
        public bool flightAnimationOnly;

        [KSPField(isPersistant = true)]
        public bool isDeployed = false;

        [KSPField()]
        public bool isInflatable = false;

        [KSPField()]
        public string inflatableColliders = string.Empty;

        [KSPField()]
        public bool overridePartAttachRestriction = false;

        [KSPField()]
        public int inflatedCrewCapacity = 0;

        [KSPField]
        public bool isOneShot = false;

        //Helper objects
        public Animation anim;
        protected bool isMoving;
        private ModuleInventoryPart inventory;
        private float packedVolumeLimit = 0;

        #region User Events & API
        [KSPEvent(guiActive = true, guiActiveEditor = true, guiName = "ToggleInflation", externalToEVAOnly = false, unfocusedRange = 3.0f, guiActiveUnfocused = true)]
        public virtual void ToggleInflation()
        {
            //If the module is inflatable, deployed, and has kerbals inside, then don't allow the module to be deflated.
            if (isInflatable && isDeployed && this.part.protoModuleCrew.Count() > 0)
            {
                ScreenMessages.PostScreenMessage(this.part.name + " has crew aboard. Vacate the module before deflating it.", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            //make sure we don't have parts radially attached
            if (isInflatable && isDeployed && !overridePartAttachRestriction)
            {
                if (this.part.children.Count > 0)
                {
                    foreach (Part childPart in this.part.children)
                        if (childPart.attachMode == AttachModes.SRF_ATTACH)
                        {
                            ScreenMessages.PostScreenMessage(this.part.name + " has parts attached to it. Please remove them before deflating.", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                            return;
                        }
                }
            }

            //Play animation for current state
            isMoving = true;
            PlayAnimation(isDeployed);
            
            //Toggle state
            isDeployed = !isDeployed;
            if (isDeployed)
            {
                this.part.CrewCapacity = inflatedCrewCapacity;
                this.part.crewTransferAvailable = true;
                if (HighLogic.LoadedSceneIsFlight)
                    this.part.SpawnIVA();
                Events["ToggleInflation"].guiName = endEventGUIName;

                if (inventory != null)
                    inventory.packedVolumeLimit = packedVolumeLimit;
            }
            else
            {
                this.part.CrewCapacity = 0;
                this.part.crewTransferAvailable = false;
                if (HighLogic.LoadedSceneIsFlight)
                    this.part.DespawnIVA();
                Events["ToggleInflation"].guiName = startEventGUIName;

                if (inventory != null)
                    inventory.packedVolumeLimit = 0;
            }

            //If this is a one-shot then hide the animation button.
            if (isOneShot && isDeployed && HighLogic.LoadedSceneIsEditor == false)
                Events["ToggleInflation"].active = false;

            Log("Animation toggled new gui name: " + Events["ToggleInflation"].guiName);
            MonoUtilities.RefreshContextWindows(this.part);
            GameEvents.onPartResourceListChange.Fire(this.part);
        }
        #endregion

        #region Overrides
        public virtual void OnToggleStateCompleted()
        {
        }

        public override void OnUpdate()
        {
            base.OnUpdate();
            if (anim == null)
                return;
            if (!anim.isPlaying && isMoving)
            {
                isMoving = false;
                OnToggleStateCompleted();
            }
        }

        public override void OnLoad(ConfigNode node)
        {
            //string value;
            base.OnLoad(node);

            try
            {
                SetupAnimations();

                //KIS inventories
//                if (isInflatable && this.part.CrewCapacity == 0 && inflatedCrewCapacity > 0 && WBIKISWrapper.IsKISInstalled())
//                    WBIKISAddonConfig.AddPodInventories(this.part, inflatedCrewCapacity);
            }

            catch (Exception ex)
            {
                Log("Error encountered while attempting to setup animations: " + ex.ToString());
            }
        }

        protected override void getProtoNodeValues(ConfigNode protoNode)
        {
            base.getProtoNodeValues(protoNode);
            string value;

            //isInflatable
            value = protoNode.GetValue("isInflatable");
            if (string.IsNullOrEmpty(value) == false)
                isInflatable = bool.Parse(value);

            animationName = protoNode.GetValue("animationName");

            endEventGUIName = protoNode.GetValue("endEventGUIName");

            startEventGUIName = protoNode.GetValue("startEventGUIName");

            value = protoNode.GetValue("inflatedCrewCapacity");
            if (string.IsNullOrEmpty(value) == false)
            {
                inflatedCrewCapacity = int.Parse(value);
                if (isInflatable && isDeployed && HighLogic.LoadedSceneIsFlight)
                    this.part.CrewCapacity = inflatedCrewCapacity;
            }
        }

        
        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            SetupAnimations();
            setupColliders();
            inventory = part.FindModuleImplementing<ModuleInventoryPart>();
            if (inventory != null)
            {
                packedVolumeLimit = inventory.packedVolumeLimit;
                if (isInflatable && !isDeployed)
                {
                    inventory.packedVolumeLimit = 0;
                }
            }
            //setupInventories();
//            if (isInflatable && isDeployed == false && HighLogic.LoadedSceneIsFlight)
//                this.part.DespawnIVA();

            if (string.IsNullOrEmpty(animationName))
            {
                this.Events["ToggleInflation"].guiActive = false;
                this.Events["ToggleInflation"].guiActiveEditor = false;
                this.Events["ToggleInflation"].guiActiveUnfocused = false;
            }

            else if (flightAnimationOnly)
                Events["ToggleInflation"].guiActiveEditor = false;

            //If this is a one-shot then hide the animation button.
            if (isOneShot && isDeployed && HighLogic.LoadedSceneIsEditor == false)
                Events["ToggleInflation"].active = false;
        }
         
        #endregion

        #region Helpers
         
        protected virtual void setupColliders()
        {
            if (isInflatable == false)
                return;
            if (string.IsNullOrEmpty(inflatableColliders))
                return;

            string[] colliders = inflatableColliders.Split(new char[] { ';' });

            foreach (string collider in colliders)
            {
                setColliderLayer(this.part.FindModelTransform(collider));
            }
        }

        protected virtual void setColliderLayer(Transform collider)
        {
            if (collider != null)
            {
                if (isDeployed)
                {
                    collider.gameObject.layer = 0;
                }

                else
                {
                    collider.gameObject.layer = 26;
                }
            }
        }

        public virtual void SetupAnimations()
        {
            Log("SetupAnimations called.");

            //Show the toggle animation button
            //and set up the animation
            if (isInflatable)
            {
                Log("Part is inflatable, looking for animations.");
                Animation[] animations = this.part.FindModelAnimators(animationName);
                if (animations == null)
                    return;

                Animation anim = animations[0];
                if (anim == null)
                    return;

                //Set layer
                anim[animationName].layer = 1;

                //Set toggle button
                Events["ToggleInflation"].guiActive = true;
                Events["ToggleInflation"].guiActiveEditor = true;
                Events["ToggleInflation"].guiActiveUnfocused = true;

                if (isDeployed)
                {
                    Events["ToggleInflation"].guiName = endEventGUIName;

                    //Make sure the inflatable module is fully deployed.
                    anim[animationName].normalizedTime = 1.0f;
                    anim[animationName].speed = 10000f;
                    if (HighLogic.LoadedSceneIsFlight)
                        this.part.CrewCapacity = inflatedCrewCapacity;
                }
                else
                {
                    Events["ToggleInflation"].guiName = startEventGUIName;

                    //Make sure the inflatable module is fully retracted.
                    anim[animationName].normalizedTime = 0f;
                    anim[animationName].speed = -10000f;
                    if (HighLogic.LoadedSceneIsFlight)
                        this.part.CrewCapacity = 0;
                }
                anim.Play(animationName);
            }

            //Hide toggle button
            else
            {
                Log("Part is not inflatable.");
                Events["ToggleInflation"].guiActive = false;
                Events["ToggleInflation"].guiActiveEditor = false;
                Events["ToggleInflation"].guiActiveUnfocused = false;
            }
        }

        public virtual void PlayAnimation(bool playInReverse = false)
        {
            float animationSpeed = playInReverse == false ? 1.0f : -1.0f;
            anim = this.part.FindModelAnimators(animationName)[0];

            if (HighLogic.LoadedSceneIsFlight)
                anim[animationName].speed = animationSpeed;
            else
                anim[animationName].speed = animationSpeed * 100;

            if (playInReverse)
                anim[animationName].time = anim[animationName].length;

            anim.Play(animationName);
        }
        #endregion
    }
}
