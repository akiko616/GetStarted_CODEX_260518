using NUnit.Framework;
using System.Collections.Generic;

namespace TRAINEE
{
    public class GameEventManager : Singleton<GameEventManager>
    {
        private Dictionary<EEventType, List<IEvent>> _subscribers = new Dictionary<EEventType, List<IEvent>>();
        private Queue<GameEvent> _queue = new Queue<GameEvent>();       // 이벤트 보관

        private int _limt = 1000;                                       // 이벤트 호출 갯수 제한

        protected override void Awake()
        {
            base.Awake();
            Init();
        }

        protected override void Start()
        {
            base.Start();
        }

        protected override void OnDestroy()
        {

            foreach (var e in _subscribers.Values)
            {
                e.Clear();
            }

            _subscribers.Clear();
            _queue.Clear();

            base.OnDestroy();
        }

        protected override void Init()
        {
            base.Init();
        }

        public void Subscribe(EEventType type, IEvent action)
        {
            if (action == null) return;

            List<IEvent> list = null;

            if (_subscribers.TryGetValue(type, out list))
            {
                list.Add(action);
                return;
            }

            list = new List<IEvent>();
            list.Add(action);
            _subscribers.Add(type, list);

        }

        public void UnSubscribe(EEventType type, IEvent action)
        {
            if (action == null) return;

            List<IEvent> list = null;

            if (_subscribers.TryGetValue(type, out list))
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i] == action)
                        list.RemoveAt(i);
                }
            }
        }

        public void Publish(GameEvent data)
        {
            _queue.Enqueue(data);
        }

        private void Update()
        {
            int limt = _limt;

            while (_queue.Count > 0 && limt > 0)
            {
                GameEvent data = _queue.Dequeue();
                DispatchEvent(data);
                limt--;
            }
        }

        private void DispatchEvent(GameEvent data)
        {
            List<IEvent> list = null;

            if (_subscribers.TryGetValue(data._type, out list))
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    list[i].OnEvent(data);
                }
            }
        }
    }
}
