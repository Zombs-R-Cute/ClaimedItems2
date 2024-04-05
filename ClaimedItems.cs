using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Newtonsoft.Json;
using Rocket.Core.Plugins;
using Rocket.Unturned;
using Rocket.Unturned.Items;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;


namespace Shauna.ClaimedItems
{
    public class ClaimedItems : RocketPlugin<ClaimedItemsConfiguration>
    {
        string filename = "Plugins/ClaimedItems2/ID-Name.json";

        private int _dictionarySaveInterval = 60000;
        private Dictionary<string, string> _DisplayNames = new Dictionary<string, string>();

        protected override void Load()
        {
            Logger.Log("Starting ClaimedItems");
            Level.onPrePreLevelLoaded += OnPrePreLevelLoaded;
            ;
            BarricadeManager.onHarvestPlantRequested += (CSteamID steamid, byte x, byte y, ushort plant, ushort index,
                    ref bool shouldallow) =>
                OnHarvestOrSalvageRequested(steamid, x, y, plant, index, ref shouldallow, true);
            BarricadeManager.onSalvageBarricadeRequested += (CSteamID steamid, byte x, byte y, ushort plant,
                    ushort index,
                    ref bool shouldallow) =>
                OnHarvestOrSalvageRequested(steamid, x, y, plant, index, ref shouldallow, false);
            U.Events.OnPlayerConnected += EventsOnOnPlayerConnected;

            _dictionarySaveInterval = Configuration.Instance.BackgroundIDDictionarySaveIntervalInMinutes * 60000;
            LoadID2NameDictionary();

            if (_dictionarySaveInterval == 0)
            {
                Logger.Log(
                    "Background saving of the 'id to display name' dictionary disabled!  Set to a value > 0(minutes) to enable. (60 should be good)");
                return;
            }

            Thread backgroundSaveThread = new Thread(DisplayNameBackgroundThread);
            backgroundSaveThread.Start();
            Level.onPostLevelLoaded += level =>
            {
                if (!Configuration.Instance.RemoveOrphanedApprovalSigns)
                {
                    Logger.Log("Automatic orphaned approved sign removal disabled");
                    return;
                }

                approvalSignCheckAndRemove();
            };
            VehicleManager.onVehicleCarjacked += (InteractableVehicle vehicle, Player player, ref bool allow,
                    ref Vector3 force, ref Vector3 torque) =>
                OnVehicleCarjacked(vehicle, player, ref allow, ref force, ref torque);
        }

        private void OnPrePreLevelLoaded(int level)
        {
            Asset[] AssetList = Assets.find(EAssetType.ITEM);
            BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.NonPublic;
            foreach (var asset in AssetList)
            {
                if (asset is ItemStorageAsset)
                {
                    ItemStorageAsset storageAsset = asset as ItemStorageAsset;
                    if (Configuration.Instance.FreeStorageIds.Contains(storageAsset.id))
                    {
                        Logger.Log("Storage id: " + storageAsset.id + " is unlocked to use as a Free storage box.");
                        continue;
                    }

                    if(storageAsset.id == Configuration.Instance.AirdropCrateID)
                    {
                        Logger.Log("Storage id: " + storageAsset.id + " is an unlocked Airdrop crate.");
                        continue;
                    }
                        
                    if ((storageAsset.isDisplay && !storageAsset.isLocked) ||
                        (!storageAsset.isLocked && Configuration.Instance.LockStorage )) //
                    {
                        Logger.Log("Set storage id:" + storageAsset.id + " to locked.");

                        storageAsset.GetType().GetField("_isLocked", bindingFlags).SetValue(storageAsset, true);
                    }
                }
            }
        }

        private void OnVehicleCarjacked(InteractableVehicle vehicle, Player instigatingPlayer, ref bool allow,
            ref Vector3 force, ref Vector3 torque)
        {
            UnturnedPlayer player = UnturnedPlayer.FromPlayer(instigatingPlayer);
            allow = true;


            if (player.IsAdmin && Configuration.Instance.EnableAdminOverride
                || Configuration.Instance.AllowCarjackingOfVehiclesOfOwnerOnOwnersClaimByOthers)
                return;

            if (PlayerAllowedToBuild(player, player.Player.transform.position))
                return;

            if (vehicle.lockedOwner != player.CSteamID &&
                vehicle.lockedGroup != player.SteamGroupID && player.SteamGroupID != CSteamID.Nil)
                return;

            allow = false;
        }


        /// <summary>
        /// Checks that all the metal signs/placards are within a claim flag.
        /// Removes the ones that aren't. 
        /// </summary>
        private void approvalSignCheckAndRemove()
        {
            List<BarricadeData> approvalSigns = new List<BarricadeData>(0);
            List<BarricadeData> claimFlags = new List<BarricadeData>(0);
            Dictionary<BarricadeData, Transform> data2Transform = new Dictionary<BarricadeData, Transform>(0);
            Dictionary<BarricadeData, InteractableSign> data2sign = new Dictionary<BarricadeData, InteractableSign>(0);
            List<string> signText = new List<string>(0);
            foreach (var barricadeRegion in BarricadeManager.regions)
            {
                foreach (BarricadeData barricadeData in barricadeRegion.barricades)
                {
                    if (barricadeData.barricade.id == 1158)
                    {
                        claimFlags.Add(barricadeData);
                        continue;
                    }

                    if (!Configuration.Instance.ApprovalSignIds.Contains(barricadeData.barricade.id))
                        continue;

                    foreach (var drop in barricadeRegion.drops)
                    {
                        if (!(drop.interactable is InteractableSign))
                            continue;

                        var interactableSign = drop.interactable as InteractableSign;
                        if (!interactableSign.text.Contains(Configuration.Instance.SignTextToSearchFor))
                            continue;

                        if (approvalSigns.Contains(barricadeData))
                            continue;

                        if (signText.Contains(interactableSign.text))
                            continue;

                        signText.Add(interactableSign.text);

                        approvalSigns.Add(barricadeData);
                        data2Transform[barricadeData] = drop.model;
                        data2sign[barricadeData] = interactableSign;
                    }
                }
            }

            List<BarricadeData> signsNearFlags = new List<BarricadeData>(0);
            float radiusOfClaim = 5.25f * 6; //5.25 tiles, 6 meters wide
            foreach (var flag in claimFlags)
            {
                foreach (var sign in approvalSigns)
                {
                    if (Vector3.Distance(flag.point, data2Transform[sign].position) < radiusOfClaim)
                    {
                        signsNearFlags.Add(sign);
                        break;
                    }
                }
            }

            Logger.Log(approvalSigns.Count + " total approval signs");
            Logger.Log("There is " + signsNearFlags.Count + " sign(s) within a claim");
            foreach (var sign in signsNearFlags)
                approvalSigns.Remove(sign);


            Logger.Log(approvalSigns.Count + " to be destroyed");
            foreach (var sign in approvalSigns)
            {
                if (BarricadeManager.tryGetInfo(data2Transform[sign], out byte x, out byte y, out ushort plant,
                        out ushort index, out BarricadeRegion region))
                {
                    BarricadeManager.damage(data2Transform[sign], 65000, 65000, true, (CSteamID)sign.owner,
                        EDamageOrigin.Charge_Explosion);
                }
            }
        }


        protected override void Unload()
        {
            SaveID2NameDictionary();
        }

        private void DisplayNameBackgroundThread()
        {
            for (;;)
            {
                Thread.Sleep(_dictionarySaveInterval);
                SaveID2NameDictionary();
            }
        }


        private void LoadID2NameDictionary()
        {
            _DisplayNames = new Dictionary<string, string>();

            if (!File.Exists(filename))
            {
                return;
            }

            string text = File.ReadAllText(filename);
            _DisplayNames = JsonConvert.DeserializeObject<Dictionary<string, string>>(text);

            Logger.Log("Loaded ID to DisplayName dictionary: " + _DisplayNames.Count + " Entries");
        }


        private void SaveID2NameDictionary()
        {
            string output = "";
            int count = 0;

            lock (_DisplayNames)
            {
                output = JsonConvert.SerializeObject(_DisplayNames);
                count = _DisplayNames.Count;
            }

            File.WriteAllText(filename, output);
            Logger.Log("Saved ID to DisplayName dictionary: " + count + " Entries");
        }


        private void EventsOnOnPlayerConnected(UnturnedPlayer player)
        {
            lock (_DisplayNames)
            {
                _DisplayNames[player.CSteamID.ToString()] = player.DisplayName;
            }
        }


        private void LogPlayersAction(ushort id, UnturnedPlayer player, CSteamID owner, bool attempted)
        {
            string ownerStr = owner.ToString();
            if (_DisplayNames.ContainsKey(owner.ToString()))
                ownerStr = _DisplayNames[owner.ToString()];

            Logger.LogWarning(
                String.Format("{0} from {1} by: {2}, steam id:{3} item: {4}: {5}",
                    attempted ? "Attempted to take" : "Taken",
                    ownerStr,
                    player.DisplayName,
                    player.CSteamID,
                    UnturnedItems.GetItemAssetById(id).itemName,
                    id)
            );
        }


        private void OnHarvestOrSalvageRequested(CSteamID steamid, byte x, byte y, ushort plant, ushort index,
            ref bool shouldallow, bool isHarvest)
        {
            shouldallow = true;
            if (BarricadeManager.tryGetRegion(x, y, plant, out BarricadeRegion region))
            {
                BarricadeData data = region.barricades[index];

                UnturnedPlayer player = UnturnedPlayer.FromCSteamID(steamid);
                if (player.IsAdmin && Configuration.Instance.EnableAdminOverride)
                    return;

                if ((CSteamID)data.owner ==
                    player.CSteamID) //in Arid notepads can be placed anywhere, allow them to be collected if it's the owner's
                    return;

                if (!PlayerAllowedToBuild(player, data.point))
                {
                    if (isHarvest)
                    {
                        if (Configuration.Instance.LogHarvest)
                            LogPlayersAction(data.barricade.id, player, (CSteamID)data.owner,
                                Configuration.Instance.PreventHarvest);

                        if (Configuration.Instance.PreventHarvest)
                            shouldallow = false;
                    }
                    else
                    {
                        if (Configuration.Instance.LogSalvage)
                            LogPlayersAction(data.barricade.id, player, (CSteamID)data.owner,
                                Configuration.Instance.PreventSalvage);

                        if (Configuration.Instance.PreventSalvage)
                            shouldallow = false;
                    }
                }
            }
        }


        private bool PlayerAllowedToBuild(UnturnedPlayer player, Vector3 location)
        {
            if (ClaimManager.checkCanBuild(location, player.CSteamID, player.Player.quests.groupID, false))
                return true;
            return false;
        }
    }
}