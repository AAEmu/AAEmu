using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;

namespace AAEmu.UnitTests.Game.Core.Managers;

[NotInParallel]
public class SurveyFormReplyTests
{
    private static readonly DateTime InWindow = new(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);

    private static SurveyFormInfo OpenForm(uint id = 5) =>
        new(id,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
            new DateTime(2026, 12, 31, 23, 59, 0, DateTimeKind.Unspecified));

    private sealed class FakeSurveyReplyStore : ISurveyFormReplyStore
    {
        private readonly HashSet<(ulong AccountId, uint FormId)> _replies = [];

        public int Count => _replies.Count;

        public bool HasReplied(ulong accountId, uint surveyFormId) =>
            _replies.Contains((accountId, surveyFormId));

        public bool TryRecord(ulong accountId, uint surveyFormId, uint characterId, DateTime repliedAt) =>
            _replies.Add((accountId, surveyFormId));
    }

    [Test]
    public async Task Reply_OpenFormFirstSubmission_SucceedsAndPersists()
    {
        SurveyFormGameData.Instance.SetForTest([OpenForm()]);
        try
        {
            var store = new FakeSurveyReplyStore();
            var service = new SurveyFormService(store);

            var outcome = service.Reply(7, 5, 100, InWindow, 0);

            await Assert.That(outcome.Result).IsEqualTo(SurveyFormReplyResult.Success);
            await Assert.That(store.HasReplied(7, 5)).IsTrue();
            await Assert.That(store.Count).IsEqualTo(1);
        }
        finally
        {
            SurveyFormGameData.Instance.SetForTest([]);
        }
    }

    [Test]
    public async Task Reply_SecondSubmissionFromTheSameAccount_RefusedExactlyOnce()
    {
        SurveyFormGameData.Instance.SetForTest([OpenForm()]);
        try
        {
            var store = new FakeSurveyReplyStore();
            var service = new SurveyFormService(store);

            var first = service.Reply(7, 5, 100, InWindow, 0);
            var second = service.Reply(7, 5, 101, InWindow.AddHours(1), 0);
            var otherAccount = service.Reply(8, 5, 102, InWindow, 0);

            await Assert.That(first.Result).IsEqualTo(SurveyFormReplyResult.Success);
            await Assert.That(second.Result).IsEqualTo(SurveyFormReplyResult.AlreadyReplied);
            await Assert.That(otherAccount.Result).IsEqualTo(SurveyFormReplyResult.Success);
            await Assert.That(store.Count).IsEqualTo(2);
            await Assert.That(store.HasReplied(8, 5)).IsTrue();
        }
        finally
        {
            SurveyFormGameData.Instance.SetForTest([]);
        }
    }

    [Test]
    public async Task Reply_UnknownFormRow_RefusedDefinitively()
    {
        SurveyFormGameData.Instance.SetForTest([]);
        try
        {
            var store = new FakeSurveyReplyStore();
            var service = new SurveyFormService(store);

            var outcome = service.Reply(7, 9, 100, InWindow, 0);

            await Assert.That(outcome.Result).IsEqualTo(SurveyFormReplyResult.UnknownForm);
            await Assert.That(store.Count).IsEqualTo(0);
        }
        finally
        {
            SurveyFormGameData.Instance.SetForTest([]);
        }
    }

    [Test]
    public async Task Reply_OutsideTheFormWindow_RefusedDefinitively()
    {
        SurveyFormGameData.Instance.SetForTest([OpenForm()]);
        try
        {
            var store = new FakeSurveyReplyStore();
            var service = new SurveyFormService(store);

            var tooEarly = service.Reply(7, 5, 100, new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc), 0);
            var tooLate = service.Reply(7, 5, 100, new DateTime(2027, 6, 1, 0, 0, 0, DateTimeKind.Utc), 0);

            await Assert.That(tooEarly.Result).IsEqualTo(SurveyFormReplyResult.NotOpen);
            await Assert.That(tooLate.Result).IsEqualTo(SurveyFormReplyResult.NotOpen);
            await Assert.That(store.Count).IsEqualTo(0);
        }
        finally
        {
            SurveyFormGameData.Instance.SetForTest([]);
        }
    }
}
