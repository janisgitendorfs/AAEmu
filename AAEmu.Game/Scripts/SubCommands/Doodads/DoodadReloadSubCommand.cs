using System.Drawing;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.SubCommands.Doodads;

public class DoodadReloadSubCommand : SubCommandBase
{
    public DoodadReloadSubCommand()
    {
        Title = "[Doodad Reload]";
        Description = "Reload file-backed spawns for one template in your current world instance. Player-owned doodads are preserved.";
        CallPrefix = $"{CommandManager.CommandPrefix}doodad reload";
        AddParameter(new NumericSubCommandParameter<uint>("templateId", "template id", true));
    }

    public override void Execute(ICharacter character, string triggerArgument,
        IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        uint templateId = parameters["templateId"];
        try
        {
            var count = ((Character)character).ParentWorld.SpawnManager.ReloadDoodadSpawns(templateId);
            SendMessage(messageOutput, $"Reloaded {count} file-backed spawns for doodad template {templateId} in this world instance.");
        }
        catch (Exception exception)
        {
            Logger.Error(exception, $"Failed to reload doodad template {templateId}");
            SendColorMessage(messageOutput, Color.Red, $"Doodad reload failed: {exception.Message}");
        }
    }
}
