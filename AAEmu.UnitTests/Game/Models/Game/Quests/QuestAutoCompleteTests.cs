using System.Reflection;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

[NotInParallel]
public class QuestAutoCompleteTests
{
    private readonly List<(FieldInfo Field, object Previous)> _singletons = [];
    private QuestManager _manager;
    private bool _debugInfo;

    [Before(Test)]
    public void Setup()
    {
        _debugInfo = AppConfiguration.Instance.DebugInfo;
        AppConfiguration.Instance.DebugInfo = false;
        _manager = new QuestManager(Mock.Of<ITaskManager>().Object, Mock.Of<IZoneManager>().Object);
        ReplaceStatic(typeof(Singleton<QuestManager>), "s_instance", _manager);
        var requirements = new UnitRequirementsGameData();
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE unit_reqs (id INTEGER, owner_id INTEGER, owner_type TEXT, kind_id INTEGER, value1 INTEGER, value2 INTEGER);
            INSERT INTO unit_reqs VALUES (1, 0, 'QuestComponent', 1, 0, 0);
            """;
        command.ExecuteNonQuery();
        requirements.Load(connection);
        ReplaceStatic(typeof(Singleton<UnitRequirementsGameData>), "s_instance", requirements);
        var ids = new QuestIdManager();
        typeof(IdManager).GetField("_freeIds", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(ids, new BitSet(100));
        ReplaceStatic(typeof(QuestIdManager), "_instance", ids);
    }

    [After(Test)]
    public void Cleanup()
    {
        AppConfiguration.Instance.DebugInfo = _debugInfo;
        foreach (var (field, previous) in _singletons)
            field.SetValue(null, previous);
    }

    [Test]
    public async Task MonsterGroupHunt_ThirdKill_AutoCompletesThroughEvaluationQueue()
    {
        var quest = CreateQuest(false);
        quest.QuestInitialized();
        quest.StartingEvaluation();
        for (var kill = 1; kill <= 3; kill++)
        {
            quest.Owner.Events.OnMonsterGroupHunt(quest.Owner, new OnMonsterGroupHuntArgs { NpcId = 469, Count = 1 });
            _manager.DoQueuedEvaluations();
            await Assert.That(quest.Owner.Quests.HasQuestCompleted(3419)).IsEqualTo(kill == 3);
        }
        await Assert.That(quest.Owner.Quests.HasQuest(3419)).IsFalse();
    }

    [Test]
    public async Task RunCurrentStep_ScoreBasedWithoutReport_StillAdvancesAtRequiredScore()
    {
        var quest = CreateQuest(false);
        ((QuestTemplate)quest.Template).Score = 3;
        quest.QuestSteps[QuestComponentKind.Progress].Components[21285].Acts[0].Template.Count = 1;
        quest.Objectives[0] = 3;

        await Assert.That(quest.RunCurrentStep()).IsTrue();
        await Assert.That(quest.Step).IsEqualTo(QuestComponentKind.Reward);
    }

    [Test]
    [Arguments(2, false)]
    [Arguments(3, true)]
    public async Task RunCurrentStep_CountBasedWithoutReport_RequiresThreeKillsAndCompletes(int kills, bool completes)
    {
        var quest = CreateQuest(false);
        quest.Objectives[0] = kills;

        var advanced = quest.RunCurrentStep();

        await Assert.That(advanced).IsEqualTo(completes);
        if (completes)
        {
            await Assert.That(quest.Step).IsEqualTo(QuestComponentKind.Reward);
            quest.RunCurrentStep();
            await Assert.That(quest.Owner.Quests.HasQuestCompleted(3419)).IsTrue();
            await Assert.That(quest.Owner.Quests.HasQuest(3419)).IsFalse();
        }
        else
        {
            await Assert.That(quest.Step).IsEqualTo(QuestComponentKind.Progress);
            await Assert.That(quest.Owner.Quests.HasQuestCompleted(3419)).IsFalse();
        }
    }

    [Test]
    public async Task RunCurrentStep_ReportRequired_DoesNotAutoCompleteAtThreeKills()
    {
        var quest = CreateQuest(true);
        quest.Objectives[0] = 3;

        await Assert.That(quest.RunCurrentStep()).IsFalse();
        await Assert.That(quest.Status).IsEqualTo(QuestStatus.Ready);
        await Assert.That(quest.Step).IsEqualTo(QuestComponentKind.Progress);
    }

    [Test]
    public async Task RunCurrentStep_MultipleRequiredObjectives_WaitsForEveryObjective()
    {
        var quest = CreateQuest(false, true);
        quest.Objectives[0] = 3;
        quest.Objectives[1] = 2;

        await Assert.That(quest.RunCurrentStep()).IsFalse();
        await Assert.That(quest.Step).IsEqualTo(QuestComponentKind.Progress);
        quest.Objectives[1] = 3;
        await Assert.That(quest.RunCurrentStep()).IsTrue();
        await Assert.That(quest.Step).IsEqualTo(QuestComponentKind.Reward);
    }

    private Quest CreateQuest(bool reportRequired, bool multipleObjectives = false)
    {
        // Mirror 3419: LetItDone, zero score, three group kills and an auto-complete reward.
        var template = new QuestTemplate { Id = 3419, LetItDone = true };
        var progress = new QuestComponentTemplate(template) { Id = 21285, KindId = QuestComponentKind.Progress };
        progress.ActTemplates.Add(new QuestActObjMonsterGroupHunt(progress)
            { Count = 3, QuestMonsterGroupId = 469, ThisComponentObjectiveIndex = 0 });
        if (multipleObjectives)
            progress.ActTemplates.Add(new QuestActObjMonsterGroupHunt(progress)
                { Count = 3, QuestMonsterGroupId = 470, ThisComponentObjectiveIndex = 1 });
        var reward = new QuestComponentTemplate(template) { Id = 14677, KindId = QuestComponentKind.Reward };
        reward.ActTemplates.Add(new QuestActConAutoComplete(reward));
        template.Components.Add(progress.Id, progress);
        template.Components.Add(reward.Id, reward);
        if (reportRequired)
            template.Components.Add(99, new QuestComponentTemplate(template) { Id = 99, KindId = QuestComponentKind.Ready });
        var owner = new Character(null) { Name = "tester" };
        owner.Quests = new CharacterQuests(owner);
        var quest = new Quest(null, owner, _manager, Mock.Of<ITaskManager>().Object, Mock.Of<ISkillManager>().Object,
            Mock.Of<IExpressTextManager>().Object, Mock.Of<IWorldManager>().Object)
            { Id = 1, TemplateId = template.Id, Template = template };
        quest.CreateQuestSteps();
        foreach (var step in quest.QuestSteps.Values)
            foreach (var component in step.Components.Values)
                component.Acts.AddRange(component.Template.ActTemplates.Select(act => new QuestAct(component, act)));
        owner.Quests.ActiveQuests.Add(template.Id, quest);
        quest.Step = QuestComponentKind.Progress;
        return quest;
    }

    private void ReplaceStatic(Type type, string name, object replacement)
    {
        var field = type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!;
        _singletons.Add((field, field.GetValue(null)));
        field.SetValue(null, replacement);
    }
}
