using System.Collections.Generic;
using Rocket.API;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using SDG.Unturned;
using UnityEngine;

namespace Shauna.ClaimedItems
{
    public class DropIDCommand:IRocketCommand
    {
        public void Execute(IRocketPlayer caller, string[] command)
        {
            UnturnedPlayer player = (UnturnedPlayer) caller;
            if (!Physics.Raycast(player.Player.look.aim.position, player.Player.look.aim.forward,
                    out RaycastHit hit, 5, RayMasks.BARRICADE))
                return;
            
            if(hit.transform.GetComponentInParent<InteractableStorage>() == null)//not storage
                return;

            var storage = hit.transform.GetComponentInParent<InteractableStorage>();
            UnturnedChat.Say(player, "Drop ID: " + storage.name, Color.yellow);
        }

        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "dropid";
        public string Help => "Gets the airdrop id";
        public string Syntax => "dropid";
        public List<string> Aliases => new List<string>() {"did"};
        public List<string> Permissions => new List<string>() {"claimeditems.dropid"};
    }
}