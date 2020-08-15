#region Authors
/**
 * MetaWind (http://metawind.net)
 */
#endregion

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace AAEmu.Commons.Generics
{
    /// <summary>
    /// Simple thread-safe objects pool
    /// </summary>
    /// <typeparam name="TPoolObject"></typeparam>
    public class ObjectsPool<TPoolObject>
    {
        /// <summary>
        /// Free objects
        /// </summary>
        private readonly Queue<TPoolObject> _poolFree;

        /// <summary>
        /// Objects in use
        /// </summary>
        private readonly HashSet<TPoolObject> _used;

        /// <summary>
        /// This pool lock
        /// </summary>
        private readonly object _poolLock = new object();

        /// <summary>
        /// Initialize pool by objects collection
        /// </summary>
        /// <param name="emptyObjects">Empty objects collections</param>
        public ObjectsPool(IEnumerable<TPoolObject> emptyObjects)
        {
            _poolFree = new Queue<TPoolObject>(emptyObjects);
            _used = new HashSet<TPoolObject>();
        }

        /// <summary>
        /// Initialize pool by specified size
        /// WARNING: Only for types, that have parameterless constructor
        /// </summary>
        /// <param name="size">Pool size</param>
        public ObjectsPool(int size)
        {
            if (size <= 0)
                throw new ArgumentException("Pool size can't be lesser or equal zero", "size");

            _poolFree = new Queue<TPoolObject>(size);
            for (int i = 0; i < size; i++)
                _poolFree.Enqueue(Activator.CreateInstance<TPoolObject>());

            _used = new HashSet<TPoolObject>();
        }

        /// <summary>
        /// Get pool object
        /// </summary>
        /// <returns>Return free instance of TPoolObject or NULL if pool limit is reached</returns>
        public TPoolObject Get()
        {
            lock (_poolLock)
            {
                if (_poolFree.Count == 0)
                    return default(TPoolObject);

                var ret = _poolFree.Dequeue();
                _used.Add(ret);

                return ret;
            }
        }

        /// <summary>
        /// Release object from poll
        /// </summary>
        /// <param name="obj">Object that must be released</param>
        public void Release(TPoolObject obj)
        {
            lock (_poolLock)
            {
                if (!_used.Remove(obj))
                {
                    Debug.Print("Pool not contain object {0} and it can't be released", obj);
                    return;
                }

                _poolFree.Enqueue(obj);
            }
        }

        /// <summary>
        /// Get objects in use
        /// </summary>
        /// <returns>Return collection of used objects</returns>
        public List<TPoolObject> GetUsedObjects()
        {
            lock (_poolLock)
                return new List<TPoolObject>(_used);
        }
    }
}
