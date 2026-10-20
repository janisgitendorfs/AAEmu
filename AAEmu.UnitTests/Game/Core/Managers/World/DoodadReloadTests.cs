using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.UnitTests.Game.Core.Managers.World;

public class DoodadReloadTests
{
    [Test]
    public async Task ReadDoodadReloadSpawns_DuplicatePositionsAndOtherTemplates_ReturnsOnlyUniqueRequestedSpawns()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, """
                [
                  { "UnitId": 3848, "Position": { "X": 9063.3, "Y": 9087.7, "Z": 196.5, "Yaw": -53.4 } },
                  { "UnitId": 3848, "Position": { "X": 9063.3, "Y": 9087.7, "Z": 196.5 } },
                  { "UnitId": 1, "Position": { "X": 1 } },
                  { "UnitId": 3848, "Position": { "X": 9087.8, "Y": 9070.8, "Z": 192.2 } }
                ]
                """);
            var spawns = SpawnManager.ReadDoodadReloadSpawns([file], 3848);
            await Assert.That(spawns.Count).IsEqualTo(2);
            await Assert.That(spawns[0].Position.Z).IsEqualTo(196.5f);
            await Assert.That(spawns[0].Position.Yaw).IsEqualTo(-53.4f);
            await Assert.That(spawns[0].RespawnTime).IsEqualTo(15);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Test]
    [Arguments("not json")]
    [Arguments("null")]
    [Arguments("[null]")]
    [Arguments("[]")]
    [Arguments("[{\"UnitId\":3848}]")]
    [Arguments("[{\"UnitId\":3848,\"Position\":{\"Z\":\"NaN\"}}]")]
    public async Task ReadDoodadReloadSpawns_InvalidOrMissingEntries_RejectsReload(string contents)
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, contents);
            Exception error = null;
            try
            {
                SpawnManager.ReadDoodadReloadSpawns([file], 3848);
            }
            catch (InvalidOperationException exception)
            {
                error = exception;
            }
            await Assert.That(error).IsNotNull();
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Test]
    public async Task Spawn_RetiredSpawner_DoesNotCreateDoodad()
    {
        var spawner = new DoodadSpawner { UnitId = 3848 };
        spawner.Retire();
        await Assert.That(spawner.Spawn(0)).IsNull();
        spawner.DoSpawn(); // Must also ignore any old scheduled spawn callbacks.
    }
}
