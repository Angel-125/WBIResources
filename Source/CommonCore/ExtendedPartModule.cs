using System;
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
    public delegate void LogDelegate(object message);

    public class ExtendedPartModule : PartModule
    {
        /// <summary>
        /// ID of the module. Used to find the proper config node.
        /// </summary>
        [KSPField]
        public string moduleID = string.Empty;

        #region Logging
        public virtual void Log(object message)
        {
            if (HighLogic.LoadedScene == GameScenes.LOADING || HighLogic.LoadedScene == GameScenes.LOADINGBUFFER ||
                HighLogic.LoadedScene == GameScenes.PSYSTEM || HighLogic.LoadedScene == GameScenes.SETTINGS)
                return;

            if (!WBIResourcesSettings.EnableDebugLogging)
                return;

            Debug.Log(this.ClassName + " [" + this.GetInstanceID().ToString("X")
                + "][" + Time.time.ToString("0.0000") + "]: " + message);
        }

        public virtual void Log(object message, UnityEngine.Object context = null)
        {
            if (HighLogic.LoadedScene == GameScenes.LOADING || HighLogic.LoadedScene == GameScenes.LOADINGBUFFER ||
                HighLogic.LoadedScene == GameScenes.PSYSTEM || HighLogic.LoadedScene == GameScenes.SETTINGS)
                return;

            if (!WBIResourcesSettings.EnableDebugLogging)
                return;

            Debug.Log(this.ClassName + " [" + this.GetInstanceID().ToString("X")
                + "][" + Time.time.ToString("0.0000") + "]: " + message, context);
        }
        #endregion

        #region Overrides

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if (!HighLogic.LoadedSceneIsEditor && !HighLogic.LoadedSceneIsFlight)
                return;

            ConfigNode[] nodes = this.part.partInfo.partConfig.GetNodes("MODULE");
            ConfigNode node = null;

            for (int index = 0; index < nodes.Length; index++)
            {
                node = nodes[index];
                if (node.HasValue("name"))
                {
                    moduleName = node.GetValue("name");
                    if (moduleName == this.ClassName)
                    {
                        getProtoNodeValues(node);
                        break;
                    }
                }
            }
        }

        #endregion

        #region Helpers
        protected virtual void getProtoNodeValues(ConfigNode protoNode)
        {
            //Dummy
        }

        protected string getMyPartName()
        {
            //When first loaded as the game starts, this.part.name won't have extra info tacked on such as ship name or (Clone)
            //We like this name when creating the proto node.
            string partName = this.part.name;

            //After loading, we need the pre-loaded part name, which comes from part.partInfo.
            if (this.part.partInfo != null)
                partName = this.part.partInfo.name;

            //Account for Editor
            partName = partName.Replace("(Clone)", "");

            //Strip out invalid characters
            partName = string.Join("_", partName.Split(System.IO.Path.GetInvalidFileNameChars()));

            return partName;
        }

        protected void hideAllEmitters()
        {
            KSPParticleEmitter[] emitters = part.GetComponentsInChildren<KSPParticleEmitter>();

            if (emitters == null)
                return;

            foreach (KSPParticleEmitter emitter in emitters)
            {
                emitter.emit = false;
                emitter.enabled = false;
            }
        }

        protected void showOnlyEmittersInList(List<string> emittersToShow)
        {
            KSPParticleEmitter[] emitters = part.GetComponentsInChildren<KSPParticleEmitter>();

            if (emitters == null)
                return;

            foreach (KSPParticleEmitter emitter in emitters)
            {
                //If the emitter is on the list then show it
                if (emittersToShow.Contains(emitter.name))
                {
                    emitter.emit = true;
                    emitter.enabled = true;
                }

                //Emitter is not on the list, hide it.
                else
                {
                    emitter.emit = false;
                    emitter.enabled = false;
                }
            }
        }

        protected void hideEmittersInList(List<string> emittersToHide)
        {
            KSPParticleEmitter[] emitters = part.GetComponentsInChildren<KSPParticleEmitter>();

            if (emitters == null)
                return;

            foreach (KSPParticleEmitter emitter in emitters)
            {
                //If the emitter is on the list then hide it
                if (emittersToHide.Contains(emitter.name))
                {
                    emitter.emit = false;
                    emitter.enabled = false;
                }
            }
        }

        protected void showAndHideEmitters(List<string> emittersToShow, List<string> emittersToHide)
        {
            KSPParticleEmitter[] emitters = part.GetComponentsInChildren<KSPParticleEmitter>();

            if (emitters == null)
                return;

            foreach (KSPParticleEmitter emitter in emitters)
            {
                //If the emitter is on the show list then show it
                if (emittersToShow.Contains(emitter.name))
                {
                    emitter.emit = true;
                    emitter.enabled = true;
                }

                //Emitter is on the hide list, then hide it.
                else if (emittersToHide.Contains(emitter.name))
                {
                    emitter.emit = false;
                    emitter.enabled = false;
                }
            }
        }

        /// <summary>
        /// Retrieves the module's config node from the part config.
        /// </summary>
        /// <param name="className">Optional. The name of the part module to search for.</param>
        /// <returns>A ConfigNode for the part module.</returns>
        public ConfigNode getPartConfigNode(string className = "")
        {
            if (!HighLogic.LoadedSceneIsEditor && !HighLogic.LoadedSceneIsFlight)
                return null;
            if (this.part.partInfo.partConfig == null)
                return null;
            ConfigNode[] nodes = this.part.partInfo.partConfig.GetNodes("MODULE");
            ConfigNode partConfigNode = null;
            ConfigNode node = null;
            string moduleName;
            string nodeModuleID;

            //Get the config node.
            for (int index = 0; index < nodes.Length; index++)
            {
                node = nodes[index];
                if (node.HasValue("name"))
                {
                    moduleName = node.GetValue("name");
                    if (moduleName == this.ClassName || moduleName == className)
                    {
                        if (!string.IsNullOrEmpty(moduleID) && node.HasValue("moduleID"))
                        {
                            nodeModuleID = node.GetValue("moduleID");
                            if (moduleID == nodeModuleID)
                            {
                                partConfigNode = node;
                                break;
                            }
                        }
                        else
                        {
                            partConfigNode = node;
                            break;
                        }
                    }
                }
            }

            return partConfigNode;
        }
        #endregion
    }
}
