﻿using AAEmu.Game.Models.Game.Quests.Static;

namespace AAEmu.Game.Models.Game.Quests;

// класс, определяющий контекст
// QuestContext: представляет объект, поведение которого должно динамически изменяться в соответствии с состоянием.
// Выполнение же конкретных действий делегируется объекту состояния
// class that defines the context
// QuestContext: Represents an object whose behavior should change dynamically based on state.
// Execution of specific actions is delegated to the state object

public class QuestContext
{
    public QuestState State { get; set; }

    public QuestContext(Quest quest, QuestState state, QuestComponentKind questComponentKind)
    {
        State = state;
        State.UpdateContext(quest, state, this, questComponentKind);
    }
}
