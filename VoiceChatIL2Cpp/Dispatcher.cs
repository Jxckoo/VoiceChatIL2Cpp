using System;
using System.Collections.Concurrent;
using UnityEngine;

public class UnityMainThreadDispatcher : MonoBehaviour
{
    private static readonly ConcurrentQueue<Action> _executionQueue = new ConcurrentQueue<Action>();

    private static UnityMainThreadDispatcher _instance;

    public static UnityMainThreadDispatcher Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("UnityMainThreadDispatcher");
                _instance = go.AddComponent<UnityMainThreadDispatcher>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }

    void Update()
    {
        while (_executionQueue.Count > 0)
        {
            if (_executionQueue.TryDequeue(out var action))
            {
                action.Invoke();
            }
        }
    }

    public void Enqueue(Action action)
    {
        _executionQueue.Enqueue(action);
    }
}
