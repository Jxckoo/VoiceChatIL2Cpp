using System;
using Il2CppSystem.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

public class MainThreadDispatcher : MonoBehaviour
{
    private static readonly Il2CppSystem.Collections.Generic.Queue<Action> _mainThreadActions = new Il2CppSystem.Collections.Generic.Queue<Action>();

    private static MainThreadDispatcher _instance;

    public static void Initialize()
    {
        if (_instance != null) return;

        var obj = new GameObject("MainThreadDispatcher");
        _instance = obj.AddComponent<MainThreadDispatcher>();
        Object.DontDestroyOnLoad(obj);
    }

    public static void RunOnMainThread(Action action)
    {
        if (action == null) return;
        lock (_mainThreadActions)
        {
            _mainThreadActions.Enqueue(action);
        }
    }

    private void Update()
    {
        lock (_mainThreadActions)
        {
            while (_mainThreadActions.Count > 0)
            {
                var action = _mainThreadActions.Dequeue();
                action?.Invoke();
            }
        }
    }
}
