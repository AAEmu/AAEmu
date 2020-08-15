#region Authors
/**
 * MetaWind (http://metawind.net)
 */
#endregion

using System.Collections.Generic;
using System.Threading.Tasks;

namespace AAEmu.Commons.Generics
{
    /// <summary>
    /// Low perfomanced thread-safe list wrapper
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class LockedList<T>
    {
        public delegate void ElementEventEventHandler(T element);

        public delegate void ElementsEventEventHandler(List<T> elements);

        public delegate TX ElementReturnedEventEventHandler<out TX>(List<T> element);

        public delegate void ElementAddedEventHandler(T element);

        public delegate void ElementsAddedEventHandler(List<T> element);

        public delegate void ElementRemovedEventHandler(T element);

        private readonly List<T> _objects;
        private readonly object _lock;

        public ElementAddedEventHandler ElementAddedEvent;
        public ElementsAddedEventHandler ElementsAddedEvent;

        public ElementRemovedEventHandler ElementRemovedEvent;

        public LockedList()
        {
            _objects = new List<T>();
            _lock = new object();
        }

        /// <summary>
        /// Create locked list from source IEnumerable
        /// </summary>
        /// <param name="source"></param>
        public LockedList(IEnumerable<T> source)
        {

            _lock = new object();
            lock (_lock)
                _objects = new List<T>(source);
        }

        /// <summary>
        /// Action on all collection elements
        /// </summary>
        /// <param name="elEvent"></param>
        public void Action(ElementEventEventHandler elEvent)
        {
            List<T> sourceElements = ElementsCopy;

            foreach (T t in sourceElements)
                elEvent.Invoke(t);
        }

        /// <summary>
        /// Action on all collection elements
        /// </summary>
        /// <param name="elEvent"></param>
        public void Action(ElementsEventEventHandler elEvent)
        {
            elEvent.Invoke(ElementsCopy);
        }

        /// <summary>
        /// Action on all collection elements
        /// </summary>
        /// <param name="elEvent"></param>
        public TX ActionGet<TX>(ElementReturnedEventEventHandler<TX> elEvent)
        {
            return elEvent.Invoke(ElementsCopy);
        }

        /// <summary>
        /// Parallel action an all elements
        /// </summary>
        /// <param name="elEvent"></param>
        public void ParallelAction(ElementEventEventHandler elEvent)
        {
            var sourceElements = ElementsCopy;
            Parallel.ForEach(sourceElements, elEvent.Invoke);
        }

        /// <summary>
        /// Add an element to end of collection
        /// </summary>
        /// <param name="element"></param>
        public void Add(T element)
        {
            lock (_lock)
                _objects.Add(element);

            if (ElementAddedEvent != null)
                ElementAddedEvent(element);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="element"></param>
        /// <returns>True if element was added</returns>
        public bool AddIfNotPresent(T element)
        {
            var result = false;

            lock (_lock)
            {
                if (!_objects.Contains(element))
                {
                    _objects.Add(element);
                    result = true;
                }
            }

            if (result && ElementAddedEvent != null)
                ElementAddedEvent(element);

            return result;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="elements"></param>
        /// <returns>True if element was added</returns>
        public bool AddIfNotPresent(List<T> elements)
        {
            var result = false;

            lock (_lock)
            {
                for (int i = 0; i < elements.Count; i++)
                {
                    var element = elements[i];

                    if (!_objects.Contains(element))
                    {
                        _objects.Add(element);
                        result = true;
                    }
                }
            }

            if (result && ElementAddedEvent != null)
                ElementsAddedEvent(elements);

            return result;
        }

        /// <summary>
        /// Remove element from collection
        /// </summary>
        /// <param name="element"></param>
        /// <returns>Operation result</returns>
        public bool Remove(T element)
        {
            bool result;
            lock (_lock)
                result = _objects.Remove(element);

            if (result && ElementRemovedEvent != null)
                ElementRemovedEvent(element);

            return result;
        }

        /// <summary>
        /// Return true if collection contains source element
        /// </summary>
        /// <param name="element"></param>
        /// <returns></returns>
        public bool Contains(T element)
        {
            bool result;

            lock (_lock)
                result = _objects.Contains(element);

            return result;
        }

        /// <summary>
        /// Return elements collection
        /// </summary>
        public List<T> ElementsCopy
        {
            get
            {
                List<T> copy;
                lock (_lock)
                    copy = new List<T>(_objects);

                return copy;
            }
        }

        public int Count
        {
            get
            {
                lock (_lock)
                    return _objects.Count;
            }
        }

        public void Clear()
        {
            lock (_lock)
                _objects.Clear();
        }
    }
}
