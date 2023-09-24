//// Интерфейс, определяющий шаги

//using System;
//using System.Collections.Generic;
//using System.Linq;

//using AAEmu.Game.Models.Game.Char;
//using AAEmu.Game.Models.Game.Quests.Static;

//namespace AAEmu.Game.Models.Game.Quests;

//public interface IQuestStep
//{
//    public CurrentQuestComponent currentQuestComponent { get; set; }
//    public Quest quest { get; set; }
//    public CharacterQuests player { get; set; }
//    public QuestComponentKind step { get; set; }
//    //public List<QuestComponentKind> steps { get; set; }
//}

//public abstract class QuestStep : IQuestStep
//{
//    public CurrentQuestComponent currentQuestComponent { get; set; }
//    public Quest quest { get; set; }
//    public CharacterQuests player { get; set; }
//    public QuestComponentKind step { get; set; }
//    //public List<QuestComponentKind> steps { get; set; } // не нужен

//    public void InitializeStep()
//    {
//        // необходимо проверить, какие шаги имеетюся
//        for (step = QuestComponentKind.None; step <= QuestComponentKind.Reward; step++)
//        {
//            var questComponents = quest.Template.GetComponents(step);
//            if (questComponents.Length == 0) { continue; }
//            switch (step)
//            {
//                case QuestComponentKind.None:
//                    step = QuestComponentKind.Start;
//                    break;
//                case QuestComponentKind.Start:
//                    {
//                        step = QuestComponentKind.Start;
//                        break;
//                    }
//                case QuestComponentKind.Supply:
//                    step = QuestComponentKind.Supply;
//                    break;
//                case QuestComponentKind.Progress:
//                    step = QuestComponentKind.Progress;
//                    break;
//                //case QuestComponentKind.Fail:
//                //    step = QuestComponentKind.Fail;
//                //    break;
//                case QuestComponentKind.Ready:
//                    step = QuestComponentKind.Ready;
//                    break;
//                //case QuestComponentKind.Drop:
//                //    step = QuestComponentKind.Drop;
//                //    break;
//                case QuestComponentKind.Reward:
//                    step = QuestComponentKind.Reward;
//                    break;
//            }

//            // собираем компоненты для шага квеста
//            var components = quest.Template.GetComponents(step);
//            currentQuestComponent = new CurrentQuestComponent();
//            foreach (var component in components)
//            {
//                currentQuestComponent.Add(component);
//            }
//        }
//    }

//    public abstract void Execute();
//    public abstract void EnterStep();
//    public abstract void UpdateStep();
//    public abstract void ExitStep();
//}

//// Реализация состояния "QuestInProgress" (выполнение квеста)
//public class QuestStartStep : QuestStep
//{
//    //public override void InitializeStep()
//    //{
//    //    // необходимо проверить, какие шаги имеетюся
//    //    var listQuestComponentKinds = new List<QuestComponentKind>();
//    //    for (step = QuestComponentKind.None; step <= QuestComponentKind.Reward; step++)
//    //    {
//    //        var questComponents = quest.Template.GetComponents(step);
//    //        if (questComponents.Length == 0) { continue; }
//    //        switch (step)
//    //        {
//    //            //case QuestComponentKind.None:
//    //            //    step = QuestComponentKind.Start;
//    //            //    break;
//    //            case QuestComponentKind.Start:
//    //                {
//    //                    // собираем компоненты для шага квеста
//    //                    var components = quest.Template.GetComponents(step);
//    //                    currentQuestComponent = new CurrentQuestComponent();
//    //                    foreach (var component in components)
//    //                    {
//    //                        currentQuestComponent.Add(component);
//    //                    }
//    //                    step = QuestComponentKind.Start;
//    //                    break;
//    //                }
//    //                //case QuestComponentKind.Supply:
//    //                //    step = QuestComponentKind.Supply;
//    //                //    break;
//    //                //case QuestComponentKind.Progress:
//    //                //    step = QuestComponentKind.Progress;
//    //                //    break;
//    //                //case QuestComponentKind.Fail:
//    //                //    questComponentKind = QuestComponentKind.Fail;
//    //                //    break;
//    //                //case QuestComponentKind.Ready:
//    //                //    step = QuestComponentKind.Ready;
//    //                //    break;
//    //                //case QuestComponentKind.Drop:
//    //                //    questComponentKind = QuestComponentKind.Drop;
//    //                //    break;
//    //                //case QuestComponentKind.Reward:
//    //                //    step = QuestComponentKind.Reward;
//    //                //    break;
//    //        }

//    //        listQuestComponentKinds.Add(step);
//    //    }

//    //    // Change state based on quest component kind
//    //    foreach (var listQuestComponentKind in listQuestComponentKinds)
//    //    {
//    //        var questComponentKind = listQuestComponentKind;
//    //        switch (questComponentKind)
//    //        {
//    //            case QuestComponentKind.None:
//    //                questComponentKind = QuestComponentKind.Start;
//    //                break;
//    //            case QuestComponentKind.Start:
//    //                break;
//    //            case QuestComponentKind.Supply:
//    //                questComponentKind = QuestComponentKind.Supply;
//    //                break;
//    //            case QuestComponentKind.Progress:
//    //                questComponentKind = QuestComponentKind.Progress;
//    //                break;
//    //            //case QuestComponentKind.Fail:
//    //            //    questComponentKind = QuestComponentKind.Fail;
//    //            //    break;
//    //            case QuestComponentKind.Ready:
//    //                questComponentKind = QuestComponentKind.Ready;
//    //                break;
//    //            //case QuestComponentKind.Drop:
//    //            //    questComponentKind = QuestComponentKind.Drop;
//    //            //    break;
//    //            case QuestComponentKind.Reward:
//    //                questComponentKind = QuestComponentKind.Reward;
//    //                break;
//    //        }
//    //    }


//    //    foreach (var listQuestComponentKind in listQuestComponentKinds)
//    //    {
//    //        if (listQuestComponentKind == QuestComponentKind.Start)
//    //        {
//    //            _log.Debug("Нашли нужный шаг");
//    //            var components = quest.Template.GetComponents(QuestComponentKind.Start);

//    //            // собираем компоненты для шага квеста
//    //            currentQuestComponent = new CurrentQuestComponent();
//    //            foreach (var component in components)
//    //            {
//    //                currentQuestComponent.Add(component);
//    //            }
//    //        }
//    //        else
//    //        {
//    //            // нет такого шага для этого квеста
//    //        }

//    //        steps = listQuestComponentKinds;
//    //    }
//    //}

//    public override void EnterStep()
//    {
//        _log.Debug("Вход в начальное состояние");
//    }

//    public override void UpdateStep()
//    {
//        _log.Debug("Обновление начального состояния");
//        // Логика обработки начального состояния
//        // Если условие для перехода в следующее состояние выполнено, переходим в следующее состояние
//        // В противном случае остаемся в текущем состоянии

//        // Логика для выполнения квеста
//        _log.Debug("Выполняется квест...");

//        // Другая логика, связанная с выполнением квеста
//        // ...
//        // Переход в следующее состояние
//        //player.SetState(new QuestInProgressStep(), quest);
//        //EnterState();
//    }

//    public override void ExitStep()
//    {
//        _log.Debug("Выход из начального состояния");
//    }

//    public override void Execute()
//    {
//        // Логика для выполнения квеста
//        _log.Debug("Выполняется квест...");
//        var result = currentQuestComponent.Execute(quest.Owner, quest, 0);

//        // Другая логика, связанная с выполнением квеста
//        // ...
//        // Переход в следующее состояние
//        //player.SetState(new QuestInProgressStep(), quest);
//        //EnterState();
//    }
//}
//// Реализация состояния "QuestInProgress" (выполнение квеста)

//// Реализация состояния "QuestCompleted" (квест выполнен)
