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
    public delegate void PreviewTemplate(string templateName);
    public delegate void SetTemplate(string template);
    public delegate void SetupView();

    public class ConvertibleStorageView : Dialog<ConvertibleStorageView>
    {
        public string info;
        public Texture decal;
        public string templateName;
        public string templateTitle;
        public int templateCount = -1;
        public string requiredSkill = string.Empty;
        public TemplateManager templateManager;
        public Part part;
        public bool updateSymmetry = true;
        public Dictionary<string, double> inputList;


        public PreviewTemplate previewTemplate;
        public SetTemplate setTemplate;
        public SetupView setupView;

        private Vector2 _scrollPos;
        private Vector2 _scrollPosTemplates;
        private bool confirmReconfigure;
        private GUILayoutOption[] buttonOption = new GUILayoutOption[] { GUILayout.Width(48), GUILayout.Height(48) };
        private string requiredTraits = string.Empty;

        public ConvertibleStorageView() :
        base("Configure Storage", 640, 480)
        {
            Resizable = false;
            _scrollPos = new Vector2(0, 0);
        }

        public override void SetVisible(bool newValue)
        {
            base.SetVisible(newValue);
        }

        protected override void DrawWindowContents(int windowId)
        {
            DrawView();
        }

        string prevRequiredSkill = string.Empty;

        protected void getRequiredTraits()
        {
            if (requiredSkill == prevRequiredSkill)
                return;

            prevRequiredSkill = requiredSkill;

            string[] traits = Utils.GetTraitsWithEffect(requiredSkill);

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
                requiredTraits = requiredSkill;
            }
        }

        public void DrawView()
        {
            if (templateCount == -1 && setupView != null)
                setupView();
            string buttonLabel;
            string panelName;
            Texture buttonDecal;

            getRequiredTraits();

            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(new GUILayoutOption[] { GUILayout.Width(250) });

            if (string.IsNullOrEmpty(templateTitle) == false)
                GUILayout.Label("<color=white>Configuration: " + templateTitle + "</color>");
            else
                GUILayout.Label("<color=white>Configuration: " + templateName + "</color>");

            if (inputList != null)
            {
                GUILayout.Label("<color=white>Resource Costs</color>");
                string[] resourceNames = inputList.Keys.ToArray();
                int count = resourceNames.Length;
                for (int index = 0; index < count; index++)
                {
                    GUILayout.Label(string.Format("<color=white>{0:s}: ({1:f2})</color>", resourceNames[index], inputList[resourceNames[index]]));
                }
            }

            if (string.IsNullOrEmpty(requiredSkill) == false)
                GUILayout.Label("<color=white>Reconfigure Trait: " + requiredTraits + "</color>");
            else
                GUILayout.Label("<color=white>Reconfigure Trait: Any</color>");

            //Templates
            _scrollPosTemplates = GUILayout.BeginScrollView(_scrollPosTemplates);
            foreach (ConfigNode nodeTemplate in this.templateManager.templateNodes)
            {
                //Button label
                if (nodeTemplate.HasValue("title"))
                    buttonLabel = nodeTemplate.GetValue("title");
                else
                    buttonLabel = nodeTemplate.GetValue("name");

                //Icon
                panelName = nodeTemplate.GetValue("logoPanel");
                if (panelName != null)
                    buttonDecal = GameDatabase.Instance.GetTexture(panelName, false);
                else
                    buttonDecal = null;

                //Button
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(buttonDecal, buttonOption))
                {
                    previewTemplate(nodeTemplate.GetValue("name"));
                }
                if (TemplateManager.TemplateTechResearched(nodeTemplate))
                    GUILayout.Label("<color=white>" + buttonLabel + "</color>");
                else
                    GUILayout.Label("<color=#959595>" + buttonLabel + "\r\nNeeds " + TemplateManager.GetTechTreeTitle(nodeTemplate) + "</color>");
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            //Update symmetry
            if (HighLogic.LoadedSceneIsFlight && this.part.symmetryCounterparts.Count > 0)
                updateSymmetry = GUILayout.Toggle(updateSymmetry, "Update symmetry parts");

            //Reconfigure button
            if (GUILayout.Button("Reconfigure") && setTemplate != null)
            {
                ConfigNode node = templateManager[templateName];
                if (confirmReconfigure || HighLogic.LoadedSceneIsEditor)
                {
                    if (TemplateManager.TemplateTechResearched(node))
                    {
                        setTemplate(templateName);
                        confirmReconfigure = false;
                    }
                    else
                    {
                        ScreenMessages.PostScreenMessage("Unable to use " + templateName + ". Research " + TemplateManager.GetTechTreeTitle(node) + " first.", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                    }
                }

                else
                {
                    ScreenMessages.PostScreenMessage("Click again to confirm reconfiguration.", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                    confirmReconfigure = true;
                }
            }

            GUILayout.EndVertical();

            GUILayout.BeginVertical(new GUILayoutOption[] { GUILayout.Width(390) });
            _scrollPos = GUILayout.BeginScrollView(_scrollPos);

            if (decal != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Label(decal, new GUILayoutOption[] { GUILayout.Width(128), GUILayout.Height(128) });
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            GUILayout.Label(info);

            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }
    }
}
