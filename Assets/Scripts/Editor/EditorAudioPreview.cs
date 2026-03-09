using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace KalBeat.Charting.Editor
{
    internal static class EditorAudioPreview
    {
        private static readonly Type AudioUtilType = FindAudioUtilType();
        private static readonly MethodInfo PlayPreviewClipMethod = FindMethod(
            "PlayPreviewClip",
            new[] { typeof(AudioClip), typeof(int), typeof(bool) },
            new[] { typeof(AudioClip), typeof(int), typeof(bool), typeof(bool) },
            new[] { typeof(AudioClip), typeof(int) },
            new[] { typeof(AudioClip) });
        private static readonly MethodInfo StopAllPreviewClipsMethod = FindMethod("StopAllPreviewClips", Type.EmptyTypes);
        private static readonly MethodInfo PausePreviewClipMethod = FindMethod("PausePreviewClip", Type.EmptyTypes, new[] { typeof(AudioClip) });
        private static readonly MethodInfo ResumePreviewClipMethod = FindMethod("ResumePreviewClip", Type.EmptyTypes, new[] { typeof(AudioClip) });
        private static readonly MethodInfo IsPreviewClipPlayingMethod = FindMethod("IsPreviewClipPlaying", Type.EmptyTypes, new[] { typeof(AudioClip) });
        private static readonly MethodInfo GetPreviewClipPositionMethod = FindMethod("GetPreviewClipPosition", Type.EmptyTypes, new[] { typeof(AudioClip) });
        private static readonly MethodInfo GetPreviewClipSamplePositionMethod = FindMethod("GetPreviewClipSamplePosition", Type.EmptyTypes, new[] { typeof(AudioClip) });
        private static readonly MethodInfo SetPreviewClipSamplePositionMethod = FindMethod(
            "SetPreviewClipSamplePosition",
            new[] { typeof(int) },
            new[] { typeof(AudioClip), typeof(int) });

        internal static bool IsAvailable => AudioUtilType != null && PlayPreviewClipMethod != null && StopAllPreviewClipsMethod != null;

        internal static void Play(AudioClip clip, int startSample, bool loop)
        {
            if (!IsAvailable || clip == null)
            {
                return;
            }

            Stop();

            object[] args = BuildPlayArguments(clip, startSample, loop);
            if (args != null)
            {
                PlayPreviewClipMethod.Invoke(null, args);
            }
        }

        internal static void Stop()
        {
            if (StopAllPreviewClipsMethod != null)
            {
                StopAllPreviewClipsMethod.Invoke(null, null);
            }
        }

        internal static void Pause(AudioClip clip)
        {
            if (PausePreviewClipMethod == null)
            {
                Stop();
                return;
            }

            InvokeWithOptionalClip(PausePreviewClipMethod, clip);
        }

        internal static void Resume(AudioClip clip)
        {
            if (ResumePreviewClipMethod == null || clip == null)
            {
                return;
            }

            InvokeWithOptionalClip(ResumePreviewClipMethod, clip);
        }

        internal static bool IsPlaying(AudioClip clip)
        {
            if (IsPreviewClipPlayingMethod == null)
            {
                return false;
            }

            ParameterInfo[] parameters = IsPreviewClipPlayingMethod.GetParameters();
            object result;

            if (parameters.Length == 0)
            {
                result = IsPreviewClipPlayingMethod.Invoke(null, null);
            }
            else if (parameters.Length == 1)
            {
                result = IsPreviewClipPlayingMethod.Invoke(null, new object[] { clip });
            }
            else
            {
                return false;
            }

            return result is bool isPlaying && isPlaying;
        }

        internal static float GetTimeSeconds(AudioClip clip)
        {
            if (clip == null)
            {
                return 0f;
            }

            if (GetPreviewClipPositionMethod != null)
            {
                object result = InvokeWithOptionalClip(GetPreviewClipPositionMethod, clip);
                if (result is float timeSeconds)
                {
                    return Mathf.Clamp(timeSeconds, 0f, clip.length);
                }

                if (result is double timeSecondsDouble)
                {
                    return Mathf.Clamp((float)timeSecondsDouble, 0f, clip.length);
                }
            }

            if (GetPreviewClipSamplePositionMethod != null)
            {
                object result = InvokeWithOptionalClip(GetPreviewClipSamplePositionMethod, clip);
                if (result is int samplePosition)
                {
                    return Mathf.Clamp(samplePosition / (float)clip.frequency, 0f, clip.length);
                }
            }

            return 0f;
        }

        internal static void SetTimeSeconds(AudioClip clip, float timeSeconds)
        {
            if (clip == null || SetPreviewClipSamplePositionMethod == null)
            {
                return;
            }

            int clampedSample = Mathf.Clamp(Mathf.RoundToInt(timeSeconds * clip.frequency), 0, Math.Max(0, clip.samples - 1));
            ParameterInfo[] parameters = SetPreviewClipSamplePositionMethod.GetParameters();

            if (parameters.Length == 1)
            {
                SetPreviewClipSamplePositionMethod.Invoke(null, new object[] { clampedSample });
            }
            else if (parameters.Length == 2)
            {
                SetPreviewClipSamplePositionMethod.Invoke(null, new object[] { clip, clampedSample });
            }
        }

        private static object[] BuildPlayArguments(AudioClip clip, int startSample, bool loop)
        {
            ParameterInfo[] parameters = PlayPreviewClipMethod.GetParameters();

            if (parameters.Length == 1)
            {
                return new object[] { clip };
            }

            if (parameters.Length == 2)
            {
                return new object[] { clip, startSample };
            }

            if (parameters.Length == 3)
            {
                return new object[] { clip, startSample, loop };
            }

            if (parameters.Length == 4)
            {
                return new object[] { clip, startSample, loop, false };
            }

            return null;
        }

        private static object InvokeWithOptionalClip(MethodInfo methodInfo, AudioClip clip)
        {
            ParameterInfo[] parameters = methodInfo.GetParameters();

            if (parameters.Length == 0)
            {
                return methodInfo.Invoke(null, null);
            }

            if (parameters.Length == 1)
            {
                return methodInfo.Invoke(null, new object[] { clip });
            }

            return null;
        }

        private static Type FindAudioUtilType()
        {
            Type type = Type.GetType("UnityEditor.AudioUtil, UnityEditor.CoreModule", false);
            if (type != null)
            {
                return type;
            }

            type = Type.GetType("UnityEditor.AudioUtil, UnityEditor", false);
            if (type != null)
            {
                return type;
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly assembly = assemblies[i];
                string assemblyName = assembly.GetName().Name;
                if (assemblyName != null && assemblyName.StartsWith("UnityEditor", StringComparison.Ordinal))
                {
                    type = assembly.GetType("UnityEditor.AudioUtil", false);
                    if (type != null)
                    {
                        return type;
                    }
                }
            }

            return null;
        }

        private static MethodInfo FindMethod(string methodName, params Type[][] signatures)
        {
            if (AudioUtilType == null)
            {
                return null;
            }

            BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            for (int i = 0; i < signatures.Length; i++)
            {
                Type[] signature = signatures[i];
                MethodInfo method = AudioUtilType.GetMethod(methodName, flags, null, signature, null);
                if (method != null)
                {
                    return method;
                }
            }

            MethodInfo[] methods = AudioUtilType.GetMethods(flags);
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name == methodName)
                {
                    return methods[i];
                }
            }

            return null;
        }
    }
}
