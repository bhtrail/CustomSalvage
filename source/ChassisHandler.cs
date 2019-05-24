﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BattleTech;
using BattleTech.Data;
using BattleTech.UI;
using CustomComponents;
using Harmony;
using HBS.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using Object = System.Object;
using Random = System.Random;

namespace CustomSalvage
{
    public static class ChassisHandler
    {
        private static int min_parts = 0;
        private static int min_parts_special = 0;
        
        public class mech_info
        {
            public int MinParts;
            public float PriceMult;
            public bool Excluded;

            public mech_info()
            {
                MinParts = min_parts;
                PriceMult = 1f;
                Excluded = false;
            }
        }


        private static Dictionary<string, MechDef> ChassisToMech = new Dictionary<string, MechDef>();
        private static Dictionary<string, int> PartCount = new Dictionary<string, int>();
        private static Dictionary<string, List<MechDef>> Compatible = new Dictionary<string, List<MechDef>>();

        private static Dictionary<string, mech_info> Proccesed = new Dictionary<string, mech_info>();

        public static void RegisterMechDef(MechDef mech, int part_count = 0)
        {
            int max_parts = UnityGameInstance.BattleTechGame.Simulation.Constants.Story.DefaultMechPartMax;
            min_parts = Mathf.CeilToInt(max_parts * Control.Settings.MinPartsToAssembly);
            min_parts_special = Mathf.CeilToInt(max_parts * Control.Settings.MinPartsToAssemblySpecial);

            ChassisToMech[mech.ChassisID] = mech;
            if (part_count > 0)
                PartCount[mech.Description.Id] = part_count;
            string id = mech.Description.Id;

            if (!Proccesed.ContainsKey(id))
            {
                var assembly = mech.Chassis.GetComponent<AssemblyVariant>();

                var info = new mech_info();

                if (assembly != null && assembly.Exclude)
                    info.Excluded = true;
                else if (assembly != null && assembly.Include)
                    info.Excluded = false;
                else if (Control.Settings.ExclideVariants.Contains(id))
                    info.Excluded = true;
                else
                    if (Control.Settings.ExcludeTags.Any(extag => mech.MechTags.Contains(extag)))
                        info.Excluded = true;

                if(Control.Settings.SpecialTags != null && Control.Settings.SpecialTags.Length > 0)


                foreach (var tag_info in Control.Settings.SpecialTags)
                {
                    if (mech.MechTags.Contains(tag_info.Tag))
                    {
                        info.MinParts = min_parts_special;
                        info.PriceMult *= tag_info.Mod;
                    }
                }

                if (assembly != null)
                {
                    if (assembly.ReplacePriceMult)
                        info.PriceMult = assembly.PriceMult;
                    else
                        info.PriceMult *= assembly.PriceMult;

                    if (assembly.PartsMin >= 0)
                        info.MinParts = Mathf.CeilToInt(max_parts * assembly.PartsMin);
                }

                if (!info.Excluded && Control.Settings.AssemblyVariants)
                {
                    string prefabid = GetPrefabId(mech);

                    if (!Compatible.TryGetValue(prefabid, out var list))
                    {
                        list = new List<MechDef>();
                        Compatible[prefabid] = list;
                    }

                    if (list.All(i => i.Description.Id != id))
                        list.Add(mech);
                }

                Control.LogDebug($"Registring {mech.Description.Id}({mech.Description.UIName}) => {mech.ChassisID}");
                Control.LogDebug($"-- Exclude:{info.Excluded} MinParts:{info.MinParts} PriceMult:{info.PriceMult}");
                if (assembly != null)
                    Control.LogDebug($"-- PrefabID:{assembly.PrefabID} Exclude:{assembly.Exclude} include:{assembly.Include} Mult:{assembly.PriceMult} Parts:{assembly.PartsMin}");

                Proccesed[id] = info;
            }
        }

        private static string GetPrefabId(MechDef mech)
        {
            if (mech.Chassis.Is<AssemblyVariant>(out var a) && !string.IsNullOrEmpty(a.PrefabID))
                return a.PrefabID;
            return mech.Chassis.PrefabIdentifier;
        }

        public static MechDef GetMech(string chassisid)
        {
            return ChassisToMech[chassisid];
        }

        public static bool IfExcluded(string chassisid)
        {
            return Proccesed[ChassisToMech[chassisid].Description.Id].Excluded;
        }

        public static mech_info GetInfo(string chassisid)
        {
            return Proccesed[ChassisToMech[chassisid].Description.Id];
        }

        public static void ClearParts()
        {
            PartCount.Clear();
        }

        public static List<MechDef> GetCompatible(string chassisid)
        {

            var mech = ChassisToMech[chassisid];
            if (Proccesed[mech.Description.Id].Excluded)
                return null;

            var prefabid = GetPrefabId(mech);
            return Compatible[prefabid];
        }

        public static int GetCount(string mechid)
        {
            return PartCount.TryGetValue(mechid, out var num) ? num : 0;
        }

        public static void ShowInfo()
        {
            Control.LogDebug("================= CHASSIS TO MECH ===================");
            foreach (var mechDef in ChassisToMech)
                Control.LogDebug($"{mechDef.Key} => {mechDef.Value.Description.Id}");

            Control.LogDebug("================= EXCLUDED ===================");
            foreach (var info in Proccesed.Where(i => i.Value.Excluded))
                Control.LogDebug($"{info.Key}");

            Control.LogDebug("================= GROUPS ===================");
            foreach (var list in Compatible)
            {
                Control.LogDebug($"{list.Key}");

                foreach (var item in list.Value)
                    Control.LogDebug($"--- {item.Description.Id}");
            }
            Control.LogDebug("================= INVENTORY ===================");
            foreach (var mechDef in PartCount)
                Control.LogDebug($"{mechDef.Key}: [{mechDef.Value:00}]");
            Control.LogDebug("============================================");
        }

        public class parts_info
        {
            public int count;
            public int used;
            public int cbills;
            public string mechname;
            public string mechid;

            public parts_info(int c, int u, int cb, string mn, string mi)
            {
                count = c;
                used = u;
                cbills = cb;
                mechname = mn;
                mechid = mi;
            }
        }

        private static MechBayPanel mechBay;
        private static MechBayChassisUnitElement unitElement;
        private static MechBayChassisInfoWidget infoWidget;
        private static ChassisDef chassis;
        private static MechDef mech;
        private static List<parts_info> used_parts;
        private static SimGameEventTracker eventTracker = new SimGameEventTracker();
        private static bool _hasInitEventTracker = false;
        public static int page = 0;

        public static void OnChassisReady()
        {
            mechBay.OnReadyMech(unitElement);
            infoWidget.SetData(mechBay, null);
        }

        public static void PreparePopup(ChassisDef chassisDef, MechBayPanel mechbay, MechBayChassisInfoWidget widget, MechBayChassisUnitElement unitElement)
        {
            mechBay = mechbay;
            ChassisHandler.unitElement = unitElement;
            infoWidget = widget;
            chassis = chassisDef;
            mech = ChassisToMech[chassis.Description.Id];
        }

        public static void OnPartsAssembly()
        {
            infoWidget.SetData(mechBay, null);
            RemoveMechPart(mech.Description.Id, chassis.MechPartMax);
            mechBay.RefreshData(false);
            MakeMech();
        }

        private static void MakeMech()
        {
            Control.LogDebug($"Mech Assembly started for {mech.Description.UIName}");
            MechDef new_mech = new MechDef(mech, mechBay.Sim.GenerateSimGameUID(), true);

            if (Control.Settings.UnEquipedMech)
            {
                Control.LogDebug($"-- Clear Inventory");
                new_mech.SetInventory(DefaultHelper.ClearInventory(new_mech, mechBay.Sim));
            }

            if (Control.Settings.BrokenMech)
            {
                Control.LogDebug($"-- broke parts");
                var rnd = new Random();

                Control.LogDebug($"--- RepairMechLimbsChance: {Control.Settings.RepairMechLimbsChance}, RepairMechLimbs: {Control.Settings.RepairMechLimbs} ");
                float roll = 0;
                //hd
                roll = (float)rnd.NextDouble();
                Control.LogDebug($"--- HeadRepaired: {Control.Settings.HeadRepaired}, roll: {roll} ");
                if (!Control.Settings.HeadRepaired && (!Control.Settings.RepairMechLimbs ||
                                                      roll > Control.Settings.RepairMechLimbsChance))
                    new_mech.Head.CurrentInternalStructure = 0f;
                else if (Control.Settings.RandomStructureOnRepairedLimbs)
                    new_mech.Head.CurrentInternalStructure *= Math.Min(Control.Settings.MinStructure, (float)rnd.NextDouble());

                //ct
                roll = (float)rnd.NextDouble();
                Control.LogDebug($"--- CentralTorsoRepaired: {Control.Settings.CentralTorsoRepaired}, roll: {roll} ");
                if (!Control.Settings.CentralTorsoRepaired && (!Control.Settings.RepairMechLimbs ||
                                                               roll > Control.Settings.RepairMechLimbsChance))
                    new_mech.CenterTorso.CurrentInternalStructure = 0f;
                else if (Control.Settings.RandomStructureOnRepairedLimbs)
                    new_mech.CenterTorso.CurrentInternalStructure *= Math.Min(Control.Settings.MinStructure, (float)rnd.NextDouble());

                //rt
                roll = (float)rnd.NextDouble();
                Control.LogDebug($"--- RightTorsoRepaired: {Control.Settings.RightTorsoRepaired}, roll: {roll} ");
                if (!Control.Settings.RightTorsoRepaired && (!Control.Settings.RepairMechLimbs ||
                                                             roll > Control.Settings.RepairMechLimbsChance))
                    new_mech.RightTorso.CurrentInternalStructure = 0f;
                else if (Control.Settings.RandomStructureOnRepairedLimbs)
                    new_mech.RightTorso.CurrentInternalStructure *= Math.Min(Control.Settings.MinStructure, (float)rnd.NextDouble());

                //lt
                roll = (float)rnd.NextDouble();
                Control.LogDebug($"--- LeftTorsoRepaired: {Control.Settings.LeftTorsoRepaired}, roll: {roll} ");
                if (!Control.Settings.LeftTorsoRepaired && (!Control.Settings.RepairMechLimbs ||
                                                            roll > Control.Settings.RepairMechLimbsChance))
                    new_mech.LeftTorso.CurrentInternalStructure = 0f;
                else if (Control.Settings.RandomStructureOnRepairedLimbs)
                    new_mech.LeftTorso.CurrentInternalStructure *= Math.Min(Control.Settings.MinStructure, (float)rnd.NextDouble());

                //ra
                roll = (float)rnd.NextDouble();
                Control.LogDebug($"--- RightArmRepaired: {Control.Settings.RightArmRepaired}, roll: {roll} ");
                if (!Control.Settings.RightArmRepaired && (!Control.Settings.RepairMechLimbs ||
                                                           roll > Control.Settings.RepairMechLimbsChance))
                    new_mech.RightArm.CurrentInternalStructure = 0f;
                else if (Control.Settings.RandomStructureOnRepairedLimbs)
                    new_mech.RightArm.CurrentInternalStructure *= Math.Min(Control.Settings.MinStructure, (float)rnd.NextDouble());

                //la
                roll = (float)rnd.NextDouble();
                Control.LogDebug($"--- LeftArmRepaired: {Control.Settings.LeftArmRepaired}, roll: {roll} ");
                if (!Control.Settings.LeftArmRepaired && (!Control.Settings.RepairMechLimbs ||
                                                          roll > Control.Settings.RepairMechLimbsChance))
                    new_mech.LeftArm.CurrentInternalStructure = 0f;
                else if (Control.Settings.RandomStructureOnRepairedLimbs)
                    new_mech.LeftArm.CurrentInternalStructure *= Math.Min(Control.Settings.MinStructure, (float)rnd.NextDouble());

                //rl
                roll = (float)rnd.NextDouble();
                Control.LogDebug($"--- RightLegRepaired: {Control.Settings.RightLegRepaired}, roll: {roll} ");
                if (!Control.Settings.RightLegRepaired && (!Control.Settings.RepairMechLimbs ||
                                                           roll > Control.Settings.RepairMechLimbsChance))
                    new_mech.RightLeg.CurrentInternalStructure = 0f;
                else if (Control.Settings.RandomStructureOnRepairedLimbs)
                    new_mech.RightLeg.CurrentInternalStructure *= Math.Min(Control.Settings.MinStructure, (float)rnd.NextDouble());

                //ll
                Control.LogDebug($"--- LeftLegRepaired: {Control.Settings.LeftLegRepaired}, roll: {roll} ");
                roll = (float)rnd.NextDouble();
                if (!Control.Settings.LeftLegRepaired && (!Control.Settings.RepairMechLimbs ||
                                                          roll > Control.Settings.RepairMechLimbsChance))
                    new_mech.LeftLeg.CurrentInternalStructure = 0f;
                else if (Control.Settings.RandomStructureOnRepairedLimbs)
                    new_mech.LeftLeg.CurrentInternalStructure *= Math.Min(Control.Settings.MinStructure, (float)rnd.NextDouble());

                Control.LogDebug($"-- broke equipment");

                foreach (var cref in new_mech.Inventory)
                {
                    if (new_mech.IsLocationDestroyed(cref.MountedLocation))
                    {
                        Control.LogDebug($"---- {cref.ComponentDefID} - location destroyed");
                        cref.DamageLevel = ComponentDamageLevel.Destroyed;
                    }
                    else if (Control.Settings.RepairMechComponents)
                    {
                        roll = (float)rnd.NextDouble();

                        if (roll < Control.Settings.RepairComponentsFunctionalThreshold)
                        {
                            Control.LogDebug(
                                $"---- {cref.ComponentDefID} - {roll} vs {Control.Settings.RepairComponentsFunctionalThreshold} - repaired ");
                            cref.DamageLevel = ComponentDamageLevel.Functional;
                        }
                        else if (roll < Control.Settings.RepairComponentsNonFunctionalThreshold)
                        {
                            Control.LogDebug(
                                $"---- {cref.ComponentDefID} - {roll} vs {Control.Settings.RepairComponentsNonFunctionalThreshold} - broken ");
                            cref.DamageLevel = ComponentDamageLevel.NonFunctional;
                        }
                        else
                        {
                            Control.LogDebug(
                                $"---- {cref.ComponentDefID} - {roll} vs {Control.Settings.RepairComponentsNonFunctionalThreshold} - fubar ");
                            cref.DamageLevel = ComponentDamageLevel.Destroyed;
                        }
                    }
                }
            }

            mechBay.Sim.AddMech(0, new_mech, true, false, true, null);
            mechBay.Sim.MessageCenter.PublishMessage(new SimGameMechAddedMessage(new_mech, chassis.MechPartMax, true));
        }

        private static void RemoveMechPart(string id, int count)
        {
            var method = mechBay.Sim.GetType()
                .GetMethod("RemoveItemStat", BindingFlags.NonPublic | BindingFlags.Instance);

            for (int i = 0; i < count; i++)
            {
                method.Invoke(mechBay.Sim, new Object[] { id, "MECHPART", false });
            }
        }

        public static void StartDialog()
        {
            ShowInfo();

            var list = GetCompatible(chassis.Description.Id);
            used_parts = new List<parts_info>();
            used_parts.Add(new parts_info(GetCount(mech.Description.Id), GetCount(mech.Description.Id), 0,
                mech.Description.UIName, mech.Description.Id));
            var info = Proccesed[mech.Description.Id];

            float cb = Control.Settings.AdaptPartBaseCost * mech.Description.Cost / chassis.MechPartMax;
            Control.LogDebug($"base part price for {mech.Description.UIName}({mech.Description.Id}): {cb}. mechcost: {mech.Description.Cost} ");
            Control.LogDebug($"-- setting:{Control.Settings.AdaptPartBaseCost}, maxparts:{chassis.MechPartMax}, minparts:{info.MinParts}, pricemult: {info.PriceMult}");

            foreach (var mechDef in list)
            {
                int num = GetCount(mechDef.Description.Id);
                if (num == 0)
                    continue;
                var id = mechDef.Description.Id;


                if (id == mech.Description.Id)
                    continue;
                else
                {
                    float mod = 1 + Mathf.Abs(mech.Description.Cost - mechDef.Description.Cost) /
                                (float)mech.Description.Cost * Control.Settings.AdaptModWeight;
                    if (mod > Control.Settings.MaxAdaptMod)
                        mod = Control.Settings.MaxAdaptMod;

                    var info2 = Proccesed[mechDef.Description.Id];
                    var price = (int) (cb * mod * info.PriceMult * (Control.Settings.ApplyPartPriceMod ? info2.PriceMult : 1));

                    Control.LogDebug($"-- price for {mechDef.Description.UIName}({mechDef.Description.Id}) mechcost: {mechDef.Description.Cost}. price mod: {mod:0.000}, tag mod:{info2.PriceMult:0.000}  adopt price: {price}");
                    used_parts.Add(new parts_info(num, 0, price, mechDef.Description.UIName, mechDef.Description.Id));
                }
            }

            var options = new SimGameEventOption[4];


            for (int i = 0; i < 4; i++)
            {
                options[i] = new SimGameEventOption()
                {
                    Description = new BaseDescriptionDef($"test_{i}", $"test_{i}", $"test_{i}", ""),
                    RequirementList = null,
                    ResultSets = null
                };
            }

            var eventDef = new SimGameEventDef(
                SimGameEventDef.EventPublishState.PUBLISHED,
                SimGameEventDef.SimEventType.UNSELECTABLE,
                EventScope.Company,
                new DescriptionDef(
                    "CustomSalvageAssemblyEvent",
                    "Mech Assembly",
                    GetCurrentDescription(),
                    "uixTxrSpot_YangWorking.png",
                    0, 0, false, "", "", ""),
                new RequirementDef { Scope = EventScope.Company },
                new RequirementDef[0],
                new SimGameEventObject[0],
                options.ToArray(),
                1);

            if (!_hasInitEventTracker)
            {
                eventTracker.Init(new[] { EventScope.Company }, 0, 0, SimGameEventDef.SimEventType.NORMAL, mechBay.Sim);
                _hasInitEventTracker = true;
            }

            mechBay.Sim.InterruptQueue.QueueEventPopup(eventDef, EventScope.Company, eventTracker);

        }

        public static string GetCurrentDescription()
        {
            var result = "Assembling <b><color=#20ff20>" + mech.Description.UIName + "</color></b> Using `Mech Parts:\n";

            foreach (var info in used_parts)
            {
                if (info.used > 0)
                {
                    result +=
                        $"\n  <b>{info.mechname}</b>: <color=#20ff20>{info.used}</color> {(info.used == 1 ? "part" : "parts")}";
                    if (info.cbills > 0)
                    {
                        result += $", <color=#ffff00>{SimGameState.GetCBillString(info.cbills * info.used)}</color>";
                    }
                }
            }

            int cbills = used_parts.Sum(i => i.used * i.cbills);
            result += $"\n\n  <b>Total:</b> <color=#ffff00>{SimGameState.GetCBillString(cbills)}</color>";
            int left = chassis.MechPartMax - used_parts.Sum(i => i.used);

            if (left > 0)
                result += $"\n\nNeed <color=#ff2020>{left}</color> more {(left == 1 ? "part" : "parts")}";
            else
                result += $"\n\nPreparations complete. Proceed?";
            return result;
        }

        public static void MakeOptions(TextMeshProUGUI eventDescription, SGEventPanel sgEventPanel, DataManager dataManager, RectTransform optionParent, List<SGEventOption> optionsList)
        {
            void set_info(SGEventOption option, string text, UnityAction<SimGameEventOption> action)
            {
                Traverse.Create(option).Field<TextMeshProUGUI>("description").Value.SetText(text);
                option.OptionSelected.RemoveAllListeners();
                option.OptionSelected.AddListener(action);
            }

            void set_add_part(SGEventOption option, int num)
            {
                if (num < used_parts.Count)
                {
                    var info = used_parts[num];
                    if (info.used < info.count)
                    {
                        set_info(option, $"Add <color=#20ff20>{info.mechname}</color> for <color=#ffff00>{SimGameState.GetCBillString(info.cbills)}</color> C-Bills, {info.count - info.used} {(info.count - info.used == 1 ? "part" : "parts") } parts left",
                            arg =>
                            {
                                info.used += 1;
                                MakeOptions(eventDescription, sgEventPanel, dataManager, optionParent, optionsList);
                            });
                    }
                    else
                    {
                        set_info(option, $"<i><color=#a0a0a0a>{info.mechname} : All parts used</color></i>", arg => { });
                    }
                }
                else
                {
                    set_info(option, "---", arg => { });
                }
            }

            int count = used_parts.Sum(i => i.used);

            eventDescription.SetText(GetCurrentDescription());


            if (count < mechBay.Sim.Constants.Story.DefaultMechPartMax)
            {
                if (used_parts.Count > 5)
                {
                    set_add_part(optionsList[0], 1 + page * 3);
                    set_add_part(optionsList[1], 2 + page * 3);
                    set_add_part(optionsList[2], 3 + page * 3);
                    set_info(optionsList[3], "Next Page >>", arg =>
                    {
                        page = (page + 1) % ((used_parts.Count - 1) / 3 + 1);
                        MakeOptions(eventDescription, sgEventPanel, dataManager, optionParent, optionsList);
                    });

                }
                else
                {
                    set_add_part(optionsList[0], 1);
                    set_add_part(optionsList[1], 2);
                    set_add_part(optionsList[2], 3);
                    set_add_part(optionsList[3], 4);
                }
            }
            else
            {
                int funds = mechBay.Sim.Funds;
                int total = used_parts.Sum(i => i.cbills * i.used);
                if (funds >= total) 
                    set_info(optionsList[0], "Confirm", arg => { CompeteMech(); sgEventPanel.Dismiss(); });
                else
                    set_info(optionsList[0], "<color=#ff2020><i>Not enough C-Bills</i></color>", arg => { sgEventPanel.Dismiss(); });
                set_info(optionsList[1], "Cancel", arg => { sgEventPanel.Dismiss(); });
                set_info(optionsList[2], "---", arg => { });
                set_info(optionsList[3], "---", arg => { });

            }
        }

        private static void CompeteMech()
        {
            infoWidget.SetData(mechBay, null);
            foreach (var info in used_parts)
            {
                if (info.used > 0)
                    RemoveMechPart(info.mechid, info.used);
            }
            int total = used_parts.Sum(i => i.cbills * i.used);
            mechBay.Sim.AddFunds(-total);
            RemoveMechPart(mech.Description.Id, chassis.MechPartMax);
            used_parts.Clear();
            mechBay.RefreshData(false);
            MakeMech();
        }
    }
}