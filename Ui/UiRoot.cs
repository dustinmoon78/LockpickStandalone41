// SPDX-License-Identifier: AGPL-3.0-or-later
using UnityEngine;
using UnityEngine.UI;

namespace LockpickStandalone.Ui
{
    /// <summary>
    /// 本模组的 UI 根：纯代码创建的 Canvas + 音源，完全不依赖任何 AssetBundle。
    /// 上游实现是从 bundle 里取 Canvas/模型/音效，本模组改为运行期构建，故零外部资源依赖。
    /// </summary>
    internal static class UiRoot
    {
        private static GameObject _root;
        private static Canvas _canvas;
        private static AudioSource _audio;

        internal static Canvas Canvas
        {
            get { Ensure(); return _canvas; }
        }

        internal static AudioSource Audio
        {
            get { Ensure(); return _audio; }
        }

        internal static bool Ready { get { return _root != null; } }

        private static void Ensure()
        {
            if (_root != null) return;
            _root = new GameObject("LockpickStandaloneUI");
            Object.DontDestroyOnLoad(_root);
            _canvas = _root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 30000;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _root.AddComponent<GraphicRaycaster>();
            _audio = _root.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
            LpLog.Info("[Lockpick] UI 根已就绪（纯代码 Canvas + 程序化音效，无 bundle 依赖）");
        }

        internal static void Teardown()
        {
            if (_root == null) return;
            Object.Destroy(_root);
            _root = null;
            _canvas = null;
            _audio = null;
        }
    }
}