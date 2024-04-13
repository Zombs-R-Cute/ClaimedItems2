using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using Rocket.API;
using Steamworks;

namespace Shauna.ClaimedItems
{
    public class ClaimedItemsConfiguration : IRocketPluginConfiguration
    {
        public bool EnableAdminOverride;
        public bool LockStorage;
        public bool PreventHarvest;
        public bool LogHarvest;
        public bool PreventSalvage;
        public bool LogSalvage;
        public int BackgroundIDDictionarySaveIntervalInMinutes;
        public bool RemoveOrphanedApprovalSigns;
        [XmlArray(ElementName = "ApprovalSignIds"), XmlArrayItem(ElementName = "Item")]
        public ushort[] ApprovalSignIds;
        public string SignTextToSearchFor;
        public bool AllowCarjackingOfVehiclesOfOwnerOnOwnersClaimByOthers;

        public void LoadDefaults()
        {
            EnableAdminOverride = true;
            LockStorage = true;
            PreventHarvest = true;
            LogHarvest = false;
            PreventSalvage = true;
            LogSalvage = false;
            BackgroundIDDictionarySaveIntervalInMinutes = 60;
            RemoveOrphanedApprovalSigns = true;
            ApprovalSignIds = new ushort[] {1098, 1470}; // Metal Sign, Metal Placard
            SignTextToSearchFor = "APPROVED";
            AllowCarjackingOfVehiclesOfOwnerOnOwnersClaimByOthers = false;
        }
    }
}