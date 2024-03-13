﻿using ER.Parser;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;
using static ER.Resource.GameResource;

namespace ER.Resource
{
    /// <summary>
    /// 游戏资源(GameResource 的静态替身), 没有额外作用, 只是方便调用
    /// </summary>
    public static class GR
    {
        public static void Clear()
        {
            GameResource.Instance.Clear();
        }

        public static void UnLoad(params string[] keys)
        {
            GameResource.Instance.Unload(keys);
        }

        public static void Load(ResourceType type, Action callback = null, params string[] keys)
        {
            GameResource.Instance.Load(type, callback, keys);
        }

        public static void ELoad(ResourceType type, Action callback = null, params string[] keys)
        {
            GameResource.Instance.ELoad(type, callback, keys);
        }

        public static Sprite GetSprite(string key)
        {
            return GameResource.Instance.GetSprite(key);
        }

        public static string GetText(string key)
        {
            return GameResource.Instance.GetText(key);
        }

        public static AudioClip GetAudioClip(string key)
        {
            return GameResource.Instance.GetAudioClip(key);
        }

        public static string GetTextPart(string key_all)
        {
            return GameResource.Instance.GetTextPart(key_all);
        }

        public static string GetTextPart(string sectionName, string key)
        {
            return GameResource.Instance.GetTextPart(sectionName, key);
        }
    }

    /// <summary>
    /// 游戏资源: 用于 管理 和 缓存 游戏所需要的各种资源
    /// #1: 使用资源注册名 访问指定资源
    /// #2: 返回封装后的资源
    /// #3: 注册名: 资源头:模组名:地址名
    /// #4: 文本等效资源头, 列表中资源类型将会以指定资源的形式加载
    /// #5: 不属于 wav,txt,img 且没有填写等效资源头 的资源默 认以txt形式加载
    /// </summary>
    public class GameResource : MonoSingletonAutoCreate<GameResource>
    {
        /// <summary>
        /// 资源类型
        /// </summary>
        public enum ResourceType
        {
            /// <summary>
            /// 文本资源
            /// </summary>
            Text,

            /// <summary>
            /// 图片资源
            /// </summary>
            Sprite,

            /// <summary>
            /// 音频资源
            /// </summary>
            AudioClip,

            /// <summary>
            /// 任意资源
            /// </summary>
            Any,
        }

        private Dictionary<string,SpriteResource> sprites = new Dictionary<string, SpriteResource> ();//缓存的sprite资源
        private Dictionary<string, TextResource> texts = new Dictionary<string, TextResource>();//缓存的文本资源(未加工)
        private Dictionary<string, AudioResource> audios = new Dictionary<string, AudioResource>();//缓存的audioclip资源

        private List<LoadProgress> loadProgresses = new List<LoadProgress>();//资源加载任务表
        /// <summary>
        /// 判断是否是资源注册名
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        public static bool IsRegistryName(string str)
        {
            return Regex.IsMatch(str, @"^[a-z_\/]+:[a-z_\/]+:[a-z_\/]+$");
        }
        /// <summary>
        /// 获取资源注册名的模组名
        /// </summary>
        /// <param name="registryName"></param>
        /// <returns></returns>
        public static string GetModName(string registryName)
        {
            string[] parts = registryName.Split (':');  
            return parts[1];
        }
        /// <summary>
        /// 获取注册名的资源头
        /// </summary>
        /// <param name="registryName"></param>
        /// <returns></returns>
        public static string GetTypeName(string registryName)
        {
            string[] parts = registryName.Split(':');
            return parts[0];
        }
        /// <summary>
        /// 获取注册名的全路径
        /// </summary>
        /// <param name="registryName"></param>
        /// <returns></returns>
        public static string GetAddressAll(string registryName)
        {
            string[] parts = registryName.Split(':');
            return ERinbone.Combine(parts[0],parts[2]);
        }


        /// <summary>
        /// 清除所有资源缓存
        /// </summary>
        public void Clear()
        {
            sprites.Clear();
            texts.Clear();
            audios.Clear();
        }

        /// <summary>
        /// 卸载资源缓存
        /// 特别的: 不支持卸载单独的文本片段资源, 如果需要, 请使用该资源的 sectionName
        /// </summary>
        /// <param name="keys"></param>
        public void Unload(params string[] keys)
        {
            foreach (string key in keys)
            {
                if (texts.ContainsKey(key))
                {
                    texts.Remove(key);
                }
                if (sprites.ContainsKey(key))
                {
                    sprites.Remove(key);
                }
                if (audios.ContainsKey(key))
                {
                    audios.Remove(key);
                }
            }
        }

        /// <summary>
        /// 将制定资源组加入缓存
        /// </summary>
        /// <param name="callback">整个资源组加载完毕后回调</param>
        /// <param name="registryName">资源注册名</param>
        public void Load(Action callback = null, params string[] registryName)
        {
            if (registryName.Length == 0)
            {
                callback?.Invoke();
                return;
            }
            Action progressAdd = null;//子回调函数
            if (callback != null)
            {
                for(int i=0;i<loadProgresses.Count;i++)//清空加载任务列表中已经完成的任务表
                {
                    if (loadProgresses[i].done)
                    {
                        loadProgresses.RemoveAt(i);
                        i--;
                    }
                }
                //创建新的加载任务表
                LoadProgress progress = new LoadProgress();
                progress.loaded = 0;
                progress.count = registryName.Length;
                progress.callback = callback;
                progress.done = false;
                loadProgresses.Add(progress);
                progressAdd = () =>
                {
                    progress.AddProgress();
                };
            }
            LoadResource(progressAdd, registryName);
        }
        private void LoadResource(Action callback, params string[] registryName)
        {
            for(int i=0;i< registryName.Length;i++)
            {
                bool defRes;
                string res_path = ResourceIndexer.Instance.Convert(registryName[i],out defRes);//获取资源路径
                switch(GetTypeName(registryName[i]))
                {
                    case "wav":
                        LoadAudio(defRes, res_path, callback);
                        break;
                    case "img":
                        LoadSprite(defRes,res_path,callback);
                        break;
                    case "txt":
                    default:
                        LoadText(defRes, res_path, callback);
                        break;
                }
            }
        }
        private void LoadText(bool defRes,string loadPath,Action callback)
        {

        }
        private void LoadSprite(bool defRes, string loadPath, Action callback)
        {

        }
        private void LoadAudio(bool defRes, string loadPath, Action callback)
        {

        }


        /// <summary>
        /// 加载资源缓存
        /// 在加载之前, 会检查该资源是否已经在缓存内(匹配资源键名), 这将避免重复加载同一资源
        /// </summary>
        public void ELoad(ResourceType type, Action callback = null, params string[] keys)
        {
            List<string> load = new List<string>();
            foreach (string key in keys)
            {
                if (!LoadedExist(key))
                {
                    load.Add(key);
                }
            }
            Load(type, callback, load.ToArray());
        }

        /// <summary>
        /// 将文本解析为 文本片段, 并写入 ini资源缓存
        /// </summary>
        /// <param name="txt"></param>
        private void TextParseToINI(string txt)
        {
            INIParser parser = new INIParser();
            parser.ParseINIText(txt);
            string[] sections = parser.GetSectionKeys();
            foreach (var sec in sections)
            {
                var dic = parser.GetSection(sec);
                foreach (var item in dic)
                {
                    if(!kvText.ContainsKey(sec))
                    {
                        kvText[sec] = new Dictionary<string, string>();
                    }
                    kvText[sec][item.Key] = item.Value;
                }
            }
        }

        private void LoadText(Action callback, string[] keys)
        {
            foreach (var key in keys)
            {
                if (ResourceIndexer.Instance.TryGetURL(key, out string url))
                {
                    if (url.StartsWith('@'))//@开头的url表示游戏内部资源, 使用 Addressables 加载
                    {
                        Addressables.LoadAssetAsync<TextAsset>(url).Completed += (handle) =>
                        {
                            Debug.Log("进度增加");
                            if (handle.Status == AsyncOperationStatus.Succeeded)
                            {
                                texts[key] = handle.Result.text;
                            }
                            else
                            {
                                Debug.LogError("加载默认资源失败!");
                            }
                            callback?.Invoke();
                        };

                    }
                    else// 外部资源使用特殊方法加载
                    {
                        StartCoroutine(LoadFileText(key, url, callback));
                    }
                }
                else
                {
                    callback?.Invoke();
                }
            }
        }

        private void LoadINI(Action callback, string[] keys)
        {
            foreach (var key in keys)
            {
                Debug.Log("检查key是否存在:"+(key));
                if (ResourceIndexer.Instance.TryGetURL(key, out string url))
                {
                    if (url.StartsWith('@'))//@开头的url表示游戏内部资源, 使用 Addressables 加载
                    {
                        Addressables.LoadAssetAsync<TextAsset>("url").Completed += (handle) =>
                        {
                            Debug.Log("进度增加");
                            if (handle.Status == AsyncOperationStatus.Succeeded)
                            {
                                TextParseToINI(handle.Result.text);
                            }
                            else
                            {
                                Debug.LogError("加载默认资源失败!");
                            }
                            callback?.Invoke();
                        };
                    }
                    else// 外部资源使用特殊方法加载
                    {
                        Debug.Log("加载INI资源");
                        StartCoroutine(LoadFileINI(key, url, callback));
                    }
                }
                else
                {
                    callback?.Invoke();
                }
            }
        }

        private void LoadAudioClip(Action callback, string[] keys)
        {
            foreach (var key in keys)
            {
                if (ResourceIndexer.Instance.TryGetURL(key, out string url))
                {
                    if (url.StartsWith('@'))//@开头的url表示游戏内部资源, 使用 Addressables 加载
                    {
                        Addressables.LoadAssetAsync<AudioClip>("url").Completed += (handle) =>
                        {
                            Debug.Log("进度增加");
                            if (handle.Status == AsyncOperationStatus.Succeeded)
                            {
                                clips[key] = handle.Result;
                            }
                            else
                            {
                                Debug.LogError("加载默认资源失败!");
                            }
                            callback?.Invoke();
                        };
                    }
                    else// 外部资源使用特殊方法加载
                    {
                        if (url.EndsWith(".wav"))
                        {
                            StartCoroutine(LoadFileAudioClip(key, url, AudioType.WAV, callback));
                        }
                        else if (url.EndsWith(".ogg"))
                        {
                            StartCoroutine(LoadFileAudioClip(key, url, AudioType.OGGVORBIS, callback));
                        }
                    }
                }
                else
                {
                    callback?.Invoke();
                }
            }
        }

        private void LoadSprite(Action callback, string[] keys)
        {
            foreach (var key in keys)
            {
                if (ResourceIndexer.Instance.TryGetURL(key, out string url))
                {
                    if (url.StartsWith('@'))//@开头的url表示游戏内部资源, 使用 Addressables 加载
                    {
                        Addressables.LoadAssetAsync<Texture2D>("url").Completed += (handle) =>
                        {
                            Debug.Log("进度增加");
                            if (handle.Status == AsyncOperationStatus.Succeeded)
                            {
                                sprites[key] = handle.Result.TextureToSprite();
                            }
                            else
                            {
                                Debug.LogError("加载默认资源失败!");
                            }
                            callback?.Invoke();
                        };
                    }
                    else// 外部资源使用特殊方法加载
                    {
                        StartCoroutine(LoadFileSprite(key, url, callback));
                    }
                }
                else
                {
                    callback?.Invoke();
                }
            }
        }

        /// <summary>
        /// 加载文本资源
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        private IEnumerator LoadFileText(string key, string url, Action callback)
        {
            Debug.Log("加载文本(LoadFileText)");
            UnityWebRequest request = UnityWebRequest.Get(url);
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
            {
                texts[key] = request.downloadHandler.text;
            }
            else
            {
                Debug.Log(request.error);
            }

            Debug.Log("进度增加");
            callback?.Invoke();
        }

        /// <summary>
        /// 加载指定INI资源
        /// </summary>
        /// <param name="key"></param>
        /// <param name="url"></param>
        /// <param name="callback"></param>
        /// <returns></returns>
        private IEnumerator LoadFileINI(string key, string url, Action callback)
        {
            Debug.Log("加载文本(LoadFileINI):"+url);
            UnityWebRequest request = UnityWebRequest.Get(url);
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
            {
                TextParseToINI(request.downloadHandler.text);
            }
            else
            {
                Debug.Log(request.error);
            }
            Debug.Log("进度增加");
            callback?.Invoke();
        }

        /// <summary>
        /// 加载音频资源
        /// </summary>
        /// <param name="url"></param>
        /// <param name="audioType"></param>
        /// <returns></returns>
        private IEnumerator LoadFileAudioClip(string key, string url, AudioType audioType, Action callback)
        {
            Debug.Log("加载音频(LoadFileAudioClip)");
            UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(url, audioType);
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
            {
                clips[key] = DownloadHandlerAudioClip.GetContent(request);
            }
            else
            {
                Debug.Log(request.error);
            }
            Debug.Log("进度增加");
            callback?.Invoke();
        }

        /// <summary>
        /// 加载图片资源
        /// </summary>
        /// <returns></returns>
        private IEnumerator LoadFileSprite(string key, string url, Action callback)
        {
            Debug.Log("加载图片(LoadFileSprite)");
            UnityWebRequest request = UnityWebRequestTexture.GetTexture(url);
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
            {
                sprites[key] = DownloadHandlerTexture.GetContent(request).TextureToSprite();
            }
            else
            {
                Debug.Log(request.error);
            }
            Debug.Log("进度增加");
            callback?.Invoke();
        }

        /// <summary>
        /// 获取指定sprite资源
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public Sprite GetSprite(string key)
        {
            if (sprites.TryGetValue(key, out Sprite sp))
            {
                return sp;
            }
            return null;
        }

        /// <summary>
        /// 获取指定文本资源
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public string GetText(string key)
        {
            if (texts.TryGetValue(key, out string st))
            {
                return st;
            }
            return null;
        }

        /// <summary>
        /// 获取指定音频资源
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public AudioClip GetAudioClip(string key)
        {
            if (clips.TryGetValue(key, out AudioClip ac))
            {
                return ac;
            }
            return null;
        }

        /// <summary>
        /// 获取指定文本片段(ini中的 key-value pair)
        /// 键(全称) = 节段名 + 键名
        /// </summary>
        /// <param name="key_all">片段资源键(全称)</param>
        /// <returns></returns>
        public string GetTextPart(string key_all)
        {
            string[] keys = key_all.Split('.');
            return GetTextPart(keys[0], keys[1]);
        }

        /// <summary>
        /// 获取指定文本片段
        /// </summary>
        /// <param name="sectionName"></param>
        /// <param name="key"></param>
        /// <returns></returns>
        public string GetTextPart(string sectionName, string key)
        {
            if (kvText.TryGetValue(sectionName, out var dic))
            {
                if (dic.TryGetValue(key, out string part))
                {
                    return part;
                }
            }
            return null;
        }

        /// <summary>
        /// 判断指定资源是否已经加载
        /// 特别的: 如果检测一个单独的资源片段是否加载, 则会检测它所在的section是否加载
        /// </summary>
        /// <param name="key"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        public bool LoadedExist(string key, ResourceType type = ResourceType.Any)
        {
            switch (type)
            {
                case ResourceType.Text:
                    return texts.ContainsKey(key);

                case ResourceType.Sprite:
                    return sprites.ContainsKey(key);

                case ResourceType.AudioClip:
                    return clips.ContainsKey(key);

                case ResourceType.INI:
                    return TextPartContains(key);

                case ResourceType.Any:
                default:
                    return texts.ContainsKey(key) || sprites.ContainsKey(key) || clips.ContainsKey(key) || TextPartContains(key);
            }
        }

        /// <summary>
        /// 判断指定文本片段资源是否已经加载
        /// </summary>
        /// <returns></returns>
        private bool TextPartContains(string key)
        {
            string[] keys = key.Split('.');
            return kvText.ContainsKey(keys[0]);
        }

        /// <summary>
        /// 获取文本片段资源键(全称)
        /// </summary>
        /// <param name="sectionName">节段名称</param>
        /// <param name="keyName">键名</param>
        /// <returns></returns>
        public static string GetINIKeyAll(string sectionName, string keyName)
        {
            return sectionName + "." + keyName;
        }
    }

    public struct LoadProgress
    {
        public int loaded;
        public int count;
        public Action callback;
        public bool done;

        public void AddProgress()
        {
            loaded++;
            if (loaded >= count)
            {
                done = true;
                callback?.Invoke();
            }
        }
    }
}