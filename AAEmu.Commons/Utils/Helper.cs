using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

// Source: https://www.cyberforum.ru/blogs/529033/blog3833.html
/*
 * Статический класс является расширением для всех объектов типа object и содержит один метод - CheckInterval.
 * В этот метод передается интервал времени в миллисекундах, а сам метод возвращает тип bool.
 * Метод автоматически запоминает время своего последнего срабатывания и возвращает false если время еще не наступило,
 * или возвращает true если интервал времени уже прошел. При возврате true метод запоминает текущее время и в следующий
 * раз возвратит true не ранее чем через указанный промежуток времени.
 * Хелпер устроен таким образом, что он автоматически запоминает время срабатывания для каждого объекта,
 * вызывающего CheckInterval. Кроме того, если у вас в одном классе есть несколько вызовов CheckInterval,
 * то для каждого из них будет создан отдельный счетчик времени. Это достигается использованием атрибута [CallerLineNumber]
 * который передает в метод номер строки кода, откуда произошел вызов метода CheckInterval.
 *
 *  Поэтому вот такой код также будет вполне корректно работать:
 *  void DoSomething()
 *  {
 *      // действие 1 делаем не чаще чем раз в 1 секунду
 *      if(this.CheckInterval(1000))
 *          DoAction1();      
 *      // действие 2 делаем не чаще чем раз в 3 секунды
 *      if(this.CheckInterval(3000))
 *          DoAction2();
 *  }
 *
 * Статический класс содержит один метод - Triggered. В него передается аргумент типа bool и сам метод также возвращает bool.
 * Метод Triggered автоматически запоминает предыдущее значение своего аргумента и возвращает true, если параметр изменился
 * с false на true.
 * Как и в предыдущем хелпере, Triggered автоматически хранит предыдущее значение условия для каждого объекта
 * и для каждой строчки кода, которая его вызывает. Поэтому мы можем делать несколько вызовов Triggered
 * из одного метода - для каждого вызова будет создан отдельный флажок состояния:
 *
 * void CheckDistanceAndSayHello()
 *  {
 *      // вычисляем расстояние между НПС и игроком
 *      var dist = CalcDist(player, npc);      
 *      // если расстояние стало меньше 1 метра - один раз говорим Привет
 *      if (this.Triggered(dist < 1))
 *          SayHello();      
 *      // если расстояние стало больше 2 метров - один раз говорим Пока
 *      if (this.Triggered(dist > 2))
 *          SayBay();
 *  }
 */

namespace AAEmu.Commons.Utils
{
    public static class Helper
    {
        static Dictionary<Tuple<object, int>, DateTime> intervals = new();

        /// <summary>
        /// Возвращает true если прошло не менее указанного интервала времени (после предыдущего срабатывания)
        /// </summary>
        public static bool CheckInterval(this object caller, int interval = 1000, [CallerLineNumber] int lineNumber = 0)
        {
            // получаем текущее время
            var now = DateTime.UtcNow;

            // формируем ключ состоящий из вызывающего объекта и номера строки из которой вызывается метод
            var key = new Tuple<object, int>(caller, lineNumber);

            // получаем время следующего срабатывания для данного ключа
            DateTime next;
            if (!intervals.TryGetValue(key, out next))
                next = now;// если в словаре еще нет времени - считаем что сработать нужно сейчас

            // время еще не пришло?
            if (next > now)
                return false;

            // формируем время следующего срабатывания
            intervals[key] = now.AddMilliseconds(interval);

            // время пришло - возвращаем true
            return true;
        }

        static ConcurrentDictionary<Tuple<object, int>, bool> conditions = new();

        /// <summary>
        /// Возвращает true если условие изменилось с false на true
        /// </summary>
        public static bool Triggered(this object sender, bool condition, [CallerLineNumber] int lineNumber = 0)
        {
            // формируем ключ состоящий из вызывающего объекта и номера строки из которой вызывается метод
            var key = new Tuple<object, int>(sender, lineNumber);

            // получаем предыдущее значение условия
            bool old;
            if (!conditions.TryGetValue(key, out old))
                old = false;

            // запоминаем новое состояние
            conditions[key] = condition;

            // если сейчас условие выполняется, а раньше - не выполнялось - возвращаем true
            if (condition && !old)
                return true;

            // иначе - возвращаем false
            return false;
        }
    }
}
