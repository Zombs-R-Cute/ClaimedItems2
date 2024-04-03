using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    public class PlayerItemState
    {
        private byte _sStateIndex = 0;
        private byte _dStateIndex = 1;

        public enum ItemState
        {
            Normal, // Nothing to undo
            UndoMove, // A thief trying to steal from a crate
            UndoSwap,
            UndoSwapInProgress
        }

        private byte[] _sPages = new byte[2];
        private byte[] _dPages = new byte[2];
        private byte[] _dIndicies = new byte[2];
        private ItemJar[] _sJars = new ItemJar[2];
        private ItemJar[] _dJars = new ItemJar[2];
        private ItemState _itemState = ItemState.Normal;

        public byte sPage
        {
            get { return _sPages[_sStateIndex]; }
        }

        public byte dPage
        {
            get { return _dPages[_sStateIndex]; }
        }

        public ItemJar sJar
        {
            get { return _sJars[_sStateIndex]; }
        }

        public ItemState itemState
        {
            get { return _itemState; }
            set { _itemState = value; }
        }

        public bool hasSourceItem
        {
            get { return _sJars[_sStateIndex] != null; }
        }

        public bool isReadyToSwapBack
        {
            get { return _sStateIndex == 1 && _dStateIndex == 0; }
        }

        public bool IsPageStorage
        {
            get
            {
                for (int i = 0; i < 2; i++)
                    if (_dPages[i] == 7 || _sPages[i] == 7)
                        return true;

                return false;
            }
        }


        public void SetSource(byte page, ItemJar jar)
        {
           if (hasSourceItem) //if there's an item already stored assume swap
            {
                if (_sPages[0] == 7 || page == 7)
                {
                    _sStateIndex = 1;
                    itemState = ItemState.UndoSwap;
                }
            }

            _sPages[_sStateIndex] = page;
            _sJars[_sStateIndex] = jar;
        }

        public void SetDest(byte page, byte index, ItemJar jar)
        {
            if (_dJars[1] != null)
                _dStateIndex = 0;

            _dIndicies[_dStateIndex] = index;
            _dPages[_dStateIndex] = page;
            _dJars[_dStateIndex] = jar;
        }

        public void ResetState()
        {
            for (int i = 0; i < 2; i++)
            {
                _sJars[i] = null;
                _dJars[i] = null;
                _sPages[i] = 0;
                _dPages[i] = 0;
            }

            _itemState = ItemState.Normal;
            _sStateIndex = 0;
            _dStateIndex = 1;
        }

        public void GetSwappedSourceAndDest(out byte item0sPage, out ItemJar item0sJar, out byte item0dPage,
            out ItemJar item0dJar, out byte item0dIndex, out byte item1sPage, out ItemJar item1sJar,
            out byte item1dPage,
            out ItemJar item1dJar, out byte item1dIndex)
        {
            item0sPage = _sPages[0];
            item0sJar = _sJars[0];
            item0dPage = _dPages[1];
            item0dJar = _dJars[1];
            item0dIndex = _dIndicies[1];
            item1sPage = _sPages[1];
            item1sJar = _sJars[1];
            item1dPage = _dPages[0];
            item1dJar = _dJars[0];
            item1dIndex = _dIndicies[0];
        }
    }

    public class ClaimedItems : RocketPlugin<ClaimedItemsConfiguration>
    {
        string filename = "Plugins/ClaimedItems2/ID-Name.json";

        /// <summary>
        /// Maintains the state of items moved for preventing theft by essentially locking storage
        /// </summary>
        private Dictionary<CSteamID, PlayerItemState> _PlayerState = new Dictionary<CSteamID, PlayerItemState>(0);

        private int _dictionarySaveInterval = 60000;
        private Dictionary<string, string> _DisplayNames = new Dictionary<string, string>();

        protected override void Load()
        {
            Logger.Log("Starting ClaimedItems");
            BarricadeManager.onHarvestPlantRequested += (CSteamID steamid, byte x, byte y, ushort plant, ushort index,
                    ref bool shouldallow) =>
                OnHarvestOrSalvageRequested(steamid, x, y, plant, index, ref shouldallow, true);
            BarricadeManager.onSalvageBarricadeRequested += (CSteamID steamid, byte x, byte y, ushort plant,
                    ushort index,
                    ref bool shouldallow) =>
                OnHarvestOrSalvageRequested(steamid, x, y, plant, index, ref shouldallow, false);
            U.Events.OnPlayerConnected += EventsOnOnPlayerConnected;
            U.Events.OnPlayerDisconnected += EventsOnOnPlayerDisconnected;

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
            _PlayerState[player.CSteamID] = new PlayerItemState();
            PlayerSubscribeToOnInventoryAdded(player);
            PlayerSubscribeToOnInventoryRemoved(player);
            PlayerSubscribeToDropRequested(player);
            lock (_DisplayNames)
            {
                _DisplayNames[player.CSteamID.ToString()] = player.DisplayName;
            }
        }


        private void EventsOnOnPlayerDisconnected(UnturnedPlayer player)
        {
            PlayerUnsubscribeToOnInventoryAdded(player);
            PlayerUnsubscribeToOnInventoryRemoved(player);
            PlayerUnsubscribeToDropRequested(player);
        }


        private void PlayerSubscribeToDropRequested(UnturnedPlayer player)
        {
            player.Player.inventory.onDropItemRequested +=
                (PlayerInventory inventory, Item item, ref bool allow) =>
                    ONDropItemRequested(inventory, item, ref allow, player);
        }


        private void PlayerUnsubscribeToDropRequested(UnturnedPlayer player)
        {
            player.Player.inventory.onDropItemRequested -=
                (PlayerInventory inventory, Item item, ref bool allow) =>
                    ONDropItemRequested(inventory, item, ref allow, player);
        }


        private void PlayerSubscribeToOnInventoryAdded(UnturnedPlayer player)
        {
            player.Player.inventory.onInventoryAdded +=
                (page, index, jar) => OnInventoryAdded(page, index, jar, player);
        }

        private void PlayerSubscribeToOnInventoryRemoved(UnturnedPlayer player)
        {
            player.Player.inventory.onInventoryRemoved +=
                (page, index, jar) => ONInventoryRemoved(page, index, jar, player);
        }

        private void PlayerUnsubscribeToOnInventoryAdded(UnturnedPlayer player)
        {
            player.Player.inventory.onInventoryAdded -=
                (page, index, jar) => OnInventoryAdded(page, index, jar, player);
        }

        private void PlayerUnsubscribeToOnInventoryRemoved(UnturnedPlayer player)
        {
            player.Player.inventory.onInventoryRemoved -=
                (page, index, jar) => ONInventoryRemoved(page, index, jar, player);
        }


        private void ONDropItemRequested(PlayerInventory inventory, Item item, ref bool shouldAllow,
            UnturnedPlayer player)
        {
            if (inventory.storage == null) // apparently personal storage is null
                return;

            if (inventory.storage.name.Equals(Configuration.Instance.AirdropCrateID))
                return;

            if (!PlayerAllowedToBuild(player, player.Player.transform.position))
            {
                shouldAllow = false;
            }
        }

        private void ONInventoryRemoved(byte page, byte index, ItemJar jar, UnturnedPlayer player)
        {
            PlayerItemState playerItemState = _PlayerState[player.CSteamID];

            switch (playerItemState.itemState)
            {
                case PlayerItemState.ItemState.Normal:
                    playerItemState.SetSource(page, jar);
                    break;

                case PlayerItemState.ItemState.UndoMove:
                    playerItemState.ResetState();
                    break;
            }
        }


        private void OnInventoryAdded(byte page, byte index, ItemJar jar, UnturnedPlayer player)
        {
            PlayerItemState playerItemState = _PlayerState[player.CSteamID]; // grab a local

            switch (playerItemState.itemState)
            {
                case PlayerItemState.ItemState.Normal:
                    if (!IsAllowed(player, playerItemState))
                    {
                        if (playerItemState.sJar.item.id == Configuration.Instance.UnlockedStorageItemId ||
                            playerItemState.IsPageStorage)
                            playerItemState.itemState = PlayerItemState.ItemState.UndoMove;

                        if (Configuration.Instance.LockStorage &&
                            playerItemState.itemState == PlayerItemState.ItemState.UndoMove)
                        {
                            player.Inventory.tryAddItem(playerItemState.sJar.item, playerItemState.sJar.x,
                                playerItemState.sJar.y, playerItemState.sPage,
                                playerItemState.sJar
                                    .rot); //restore the item to it's original position which triggers an recursive event

                            player.Inventory.removeItem(page, index);
                        }

                        if (Configuration.Instance.LogStorageAction)
                        {
                            if (player.Inventory.storage != null)
                            {
                                var ownerCSteamID = player.Inventory.storage.owner;

                                LogPlayersAction(jar.item.id, player, ownerCSteamID,
                                    Configuration.Instance.LockStorage);
                            }
                        }
                    }

                    playerItemState.ResetState();
                    break;

                case PlayerItemState.ItemState.UndoSwap:

                    playerItemState.SetDest(page, index, jar);

                    if (!playerItemState.isReadyToSwapBack)
                        return;

                    if (!IsAllowed(player, playerItemState) && playerItemState.IsPageStorage)
                    {
                        playerItemState.GetSwappedSourceAndDest(out byte item0sPage, out ItemJar item0sJar,
                            out byte item0dPage,
                            out ItemJar item0dJar, out byte item0dIndex, out byte item1sPage, out ItemJar item1sJar,
                            out byte item1dPage,
                            out ItemJar item1dJar, out byte item1dIndex);

                        if (Configuration.Instance.LockStorage)
                        {
                            playerItemState.itemState = PlayerItemState.ItemState.UndoSwapInProgress;

                            player.Inventory.removeItem(item0dPage, item0dIndex);
                            player.Inventory.removeItem(item1dPage, item1dIndex);


                            player.Inventory.tryAddItem(item0sJar.item, item0sJar.x,
                                item0sJar.y, item0sPage,
                                item0sJar.rot);
                            player.Inventory.tryAddItem(item1sJar.item, item1sJar.x,
                                item1sJar.y, item1sPage,
                                item1sJar.rot);
                        }

                        if (Configuration.Instance.LogStorageAction)
                        {
                            var ownerCSteamID = player.Inventory.storage.owner;
                            if (item0sPage == 7)
                                jar = item0sJar;
                            else if (item1sPage == 7)
                                jar = item1sJar;

                            LogPlayersAction(jar.item.id, player, ownerCSteamID, Configuration.Instance.LockStorage);
                        }

                        playerItemState.ResetState();
                    }

                    break;
            }
        }

        private bool IsAllowed(UnturnedPlayer player, PlayerItemState playerItemState)
        {
            if (!playerItemState.hasSourceItem)
                return true;

            if (Configuration.Instance.EnableAdminOverride && player.IsAdmin)
                return true;

            if (player.Inventory.storage != null &&
                player.Inventory.storage.name.Equals(Configuration.Instance.AirdropCrateID))
                return true;

            if (player.IsInVehicle) // ignore vehicle storage
                return true;

            // no vehicle, see if on own claim 
            if (PlayerAllowedToBuild(player, player.Player.transform.position)) // ignore own claim
                return true;

            if (isFreeCrate(player) &&
                playerItemState.sJar.item.id != Configuration.Instance.UnlockedStorageItemId)
                return true;

            return false;
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


        private bool isFreeCrate(UnturnedPlayer player)
        {
            byte itemCount = player.Player.inventory.getItemCount(7);
            for (byte index = 0; index < itemCount; index++)
            {
                if (player.Player.inventory.getItem(7, index).item.id == Configuration.Instance.UnlockedStorageItemId)
                {
                    return true;
                }
            }

            return false;
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