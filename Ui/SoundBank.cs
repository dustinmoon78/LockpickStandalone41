// SPDX-License-Identifier: AGPL-3.0-or-later
using UnityEngine;

namespace LockpickStandalone.Ui
{
    /// <summary>程序化生成的提示音（不打包任何音频文件，全部运行期合成）。</summary>
    internal static class SoundBank
    {
        private const int Rate = 44100;
        private static AudioClip _click, _perfect, _fail, _win;

        internal static AudioClip Click { get { if (_click == null) _click = Tone("lps_click", 880f, 0.04f, 0.22f, 1.5f); return _click; } }
        internal static AudioClip Perfect { get { if (_perfect == null) _perfect = Tone("lps_perfect", 1320f, 0.10f, 0.26f, 0.6f); return _perfect; } }
        internal static AudioClip Fail { get { if (_fail == null) _fail = Tone("lps_fail", 180f, 0.18f, 0.30f, 2.5f); return _fail; } }
        internal static AudioClip Win { get { if (_win == null) _win = Tone("lps_win", 660f, 0.28f, 0.28f, 1.0f); return _win; } }

        private static AudioClip Tone(string name, float freq, float seconds, float amp, float harmonic)
        {
            int n = Mathf.Max(16, (int)(Rate * seconds));
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float env = 1f - (float)i / n;
                float s = Mathf.Sin(2f * Mathf.PI * freq * t) + 0.35f * Mathf.Sin(2f * Mathf.PI * freq * harmonic * t);
                data[i] = s * env * env * amp;
            }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}