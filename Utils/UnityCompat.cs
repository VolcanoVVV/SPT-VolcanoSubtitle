using System;
using UnityEngine;
using UnityEngine.Events;
#if GAME_IL2CPP
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
#endif

namespace Subtitle.Utils
{
    internal static class UnityCompat
    {
        internal static object ObjectIdentity(object instance)
        {
#if GAME_IL2CPP
            if (instance is IIl2CppObjectBase il2CppObject) return il2CppObject.Pointer;
#endif
            return instance;
        }

        internal static bool SameObject(object first, object second)
        {
#if GAME_IL2CPP
            if (first is IIl2CppObjectBase a && second is IIl2CppObjectBase b)
                return a.Pointer == b.Pointer;
#endif
            return ReferenceEquals(first, second);
        }

        internal static GameObject CreateGameObject(string name, params Type[] components)
        {
#if GAME_IL2CPP
            var types = new Il2CppReferenceArray<Il2CppSystem.Type>(components.Length);
            for (int i = 0; i < components.Length; i++)
                types[i] = Il2CppType.From(components[i]);
            return new GameObject(name, types);
#else
            return new GameObject(name, components);
#endif
        }

        internal static void AddManagedListener(this UnityEvent unityEvent, Action callback)
        {
#if GAME_IL2CPP
            unityEvent.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(callback));
#else
            unityEvent.AddListener(callback.Invoke);
#endif
        }

        internal static void AddManagedListener<T>(this UnityEvent<T> unityEvent, Action<T> callback)
        {
#if GAME_IL2CPP
            unityEvent.AddListener(DelegateSupport.ConvertDelegate<UnityAction<T>>(callback));
#else
            unityEvent.AddListener(callback.Invoke);
#endif
        }

        internal static T LoadTypedAsset<T>(this AssetBundle bundle, string name) where T : UnityEngine.Object
        {
#if GAME_IL2CPP
            return bundle.LoadAsset(name, Il2CppType.Of<T>())?.TryCast<T>();
#else
            return bundle.LoadAsset<T>(name);
#endif
        }

        internal static T[] LoadTypedAssets<T>(this AssetBundle bundle) where T : UnityEngine.Object
        {
#if GAME_IL2CPP
            var assets = bundle.LoadAllAssets(Il2CppType.Of<T>());
            var result = new T[assets.Length];
            for (int i = 0; i < assets.Length; i++)
                result[i] = assets[i]?.TryCast<T>();
            return result;
#else
            return bundle.LoadAllAssets<T>();
#endif
        }
    }
}
