using System.Collections.Generic;
using Rocket.API;
using Steamworks;

namespace Shauna.ClaimedItems
{
    public class ClaimedItemsConfiguration : IRocketPluginConfiguration
    {
        public bool EnableAdminOverride;
        public bool LockStorage;
        public bool LogStorageAction;
        public ushort UnlockedStorageItemId;
        public bool PreventHarvest;
        public bool LogHarvest;
        public bool PreventSalvage;
        public bool LogSalvage;
        public int BackgroundIDDictionarySaveIntervalInMinutes;
        public bool RemoveOrphanedApprovalSigns;
        public ushort[] ApprovalSignIds;
        public string SignTextToSearchFor;
        public bool AllowCarjackingOfVehiclesOfOwnerOnOwnersClaimByOthers;
        public string AirdropCrateID;
        
        
        public void LoadDefaults()
        {
            EnableAdminOverride = true;
            LockStorage = true;
            LogStorageAction = false;
            UnlockedStorageItemId = 38;
            PreventHarvest = true;
            LogHarvest = false;
            PreventSalvage = true;
            LogSalvage = false;
            BackgroundIDDictionarySaveIntervalInMinutes = 60;
            RemoveOrphanedApprovalSigns = true;
            ApprovalSignIds = new ushort[] {1098, 1470}; // Metal Sign, Metal Placard
            SignTextToSearchFor = "APPROVED";
            AllowCarjackingOfVehiclesOfOwnerOnOwnersClaimByOthers = false;
            AirdropCrateID = "59708"; //arid
        }
    }
}