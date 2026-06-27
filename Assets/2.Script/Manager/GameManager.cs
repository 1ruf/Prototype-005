using System;
using System.Collections.Generic;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }
    private readonly List<GameControllerBase> _gameControllers = new List<GameControllerBase>();
    private readonly Dictionary<Type, GameControllerBase> _controllerMap = new Dictionary<Type, GameControllerBase>();
    private bool initialized;

    private void Awake()
    {
        CreateManagerInstance();
    }

    private void Start()
    {
        InitializeControllers();
    }

    private void Update()
    {
        for (int i = 0; i < _gameControllers.Count; i++)
        {
            GameControllerBase controller = _gameControllers[i];
            controller.Update();
        }
    }

    private void FixedUpdate()
    {
        for (int i = 0; i < _gameControllers.Count; i++)
        {
            GameControllerBase controller = _gameControllers[i];
            controller.FixedUpdate();
        }
    }

    private void OnDestroy()
    {
        for (int i = _gameControllers.Count - 1; i >= 0; i--)
        {
            GameControllerBase controller = _gameControllers[i];
            controller.OnDestroy();
        }

        _gameControllers.Clear();
        _controllerMap.Clear();

        if (Instance == this)
            Instance = null;
    }

    private void CreateManagerInstance()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public static GameManager EnsureInstance()
    {
        if (Instance != null)
            return Instance;

        Instance = FindFirstObjectByType<GameManager>();
        if (Instance != null)
            return Instance;

        GameObject gameManagerObject = new GameObject("GameManager");
        Instance = gameManagerObject.AddComponent<GameManager>();
        return Instance;
    }

    public static T RegisterController<T>(T controller) where T : GameControllerBase
    {
        return EnsureInstance().Register(controller);
    }

    public static void UnregisterController<T>() where T : GameControllerBase
    {
        if (Instance != null)
            Instance.RemoveController<T>();
    }

    public T GetController<T>() where T : GameControllerBase
    {
        if (_controllerMap.TryGetValue(typeof(T), out GameControllerBase controller))
            return (T)controller;

        Debug.LogError($"{typeof(T)} -> type missmatch");
        return null;
    }

    private T Register<T>(T controller) where T : GameControllerBase
    {
        if (controller == null)
            return null;

        Type type = typeof(T);
        if (_controllerMap.TryGetValue(type, out GameControllerBase oldController))
        {
            oldController.OnDestroy();
            _gameControllers.Remove(oldController);
        }

        _controllerMap[type] = controller;
        _gameControllers.Add(controller);

        if (initialized)
            controller.Init();

        return controller;
    }

    private void RemoveController<T>() where T : GameControllerBase
    {
        Type type = typeof(T);
        if (!_controllerMap.TryGetValue(type, out GameControllerBase controller))
            return;

        controller.OnDestroy();
        _controllerMap.Remove(type);
        _gameControllers.Remove(controller);
    }

    private void InitializeControllers()
    {
        if (initialized)
            return;

        initialized = true;
        for (int i = 0; i < _gameControllers.Count; i++)
            _gameControllers[i].Init();
    }
}
