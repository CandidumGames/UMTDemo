using UMT;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace UMTDemo
{
    /// <summary>
    /// Owns the single <see cref="PlayableDirector"/> that plays VMD-derived animation through Unity Timeline. Builds a runtime <see cref="TimelineAsset"/> with one <see cref="VMDModelPlayableTrack"/> for the model body and, when present, one <see cref="VMDCameraPlayableTrack"/> for the camera rig. These custom tracks evaluate the converted <see cref="VMDModelClipData"/> / <see cref="VMDCameraClipData"/> curves each frame and write the transforms directly &#8212; the built-in <c>AnimationTrack</c> cannot be used because runtime VMD playback has no <see cref="AnimationClip"/> (<c>SetCurve</c> is editor-only for non-legacy clips and Timeline rejects legacy clips).
    /// The camera track is always part of the timeline; the user/VMD camera switch is handled by <see cref="OrbitCameraController"/> overwriting the camera transforms in <c>LateUpdate</c>, so no graph rebuild is needed when switching modes. Exposes transport (play/pause) plus scrubbing by normalized time or frame.
    /// </summary>
    public sealed class VMDTimelinePlayer : MonoBehaviour
    {
        [Tooltip("The PlayableDirector that drives playback. Lives on the shared timeline root.")]
        [SerializeField] private PlayableDirector m_Director;

        private TimelineAsset m_Timeline;
        private float m_FrameRate = 30.0f;
        private double m_Duration;
        private bool m_HasCamera;
        private bool m_IsPlaying;

        /// <summary>True when the built timeline contains a bound camera track.</summary>
        public bool hasCamera => m_HasCamera;

        /// <summary>Whether the director is currently advancing on its own.</summary>
        public bool isPlaying => m_IsPlaying;

        /// <summary>Frame rate of the converted clips, used to map between time and frame numbers.</summary>
        public float frameRate => m_FrameRate;

        /// <summary>Total duration of the timeline in seconds.</summary>
        public double duration => m_Duration;

        /// <summary>Current playback time in seconds.</summary>
        public double time => m_Director != null ? m_Director.time : 0.0;

        /// <summary>Total number of frames in the timeline.</summary>
        public int frameTotal => Mathf.Max(0, Mathf.RoundToInt((float)duration * m_FrameRate));

        /// <summary>The current frame number.</summary>
        public int frameCurrent => Mathf.Clamp(Mathf.RoundToInt((float)time * m_FrameRate), 0, frameTotal);

        /// <summary>True once a timeline has been built and is ready to play.</summary>
        public bool hasTimeline => m_Timeline != null;

        /// <summary>
        /// Builds and binds the runtime timeline from the converted curve data. A null body or camera clip simply omits that track. The body curves resolve their bone/renderer paths against <paramref name="bodyRoot"/> (the transform the body Animator sits on); the camera curves resolve their rig-relative paths against <paramref name="cameraRoot"/>. Resets playback to the start without auto-playing; call <see cref="Play"/> to begin.
        /// </summary>
        public void Build(VMDModelClipData body, Transform bodyRoot, VMDCameraClipData camera, Transform cameraRoot, float frameRate)
        {
            if (m_Director == null)
            {
                Debug.LogError("VMDTimelinePlayer has no PlayableDirector assigned.", this);
                return;
            }

            m_FrameRate = frameRate > 0.0f ? frameRate : 30.0f;

            m_Director.Stop();

            m_Timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            m_Timeline.name = "VMDTimeline";
            m_Duration = 0.0;

            if (body != null && bodyRoot != null)
            {
                VMDModelPlayableTrack bodyTrack = m_Timeline.CreateTrack<VMDModelPlayableTrack>(null, "Body");
                VMDModelPlayableAsset asset = ScriptableObject.CreateInstance<VMDModelPlayableAsset>();
                asset.clipData = body;
                asset.root = bodyRoot;
                double bodyDuration = ComputeModelDuration(body);
                CreateSpanningClip<VMDModelPlayableAsset>(bodyTrack, asset, bodyDuration);
                m_Director.SetGenericBinding(bodyTrack, bodyRoot.gameObject);
                m_Duration = System.Math.Max(m_Duration, bodyDuration);
            }

            m_HasCamera = camera != null && cameraRoot != null;
            if (m_HasCamera)
            {
                VMDCameraPlayableTrack cameraTrack = m_Timeline.CreateTrack<VMDCameraPlayableTrack>(null, "Camera");
                VMDCameraPlayableAsset asset = ScriptableObject.CreateInstance<VMDCameraPlayableAsset>();
                asset.clipData = camera;
                asset.root = cameraRoot;
                double cameraDuration = ComputeCameraDuration(camera);
                CreateSpanningClip<VMDCameraPlayableAsset>(cameraTrack, asset, cameraDuration);
                m_Director.SetGenericBinding(cameraTrack, cameraRoot.gameObject);
                m_Duration = System.Math.Max(m_Duration, cameraDuration);
            }

            m_Director.playableAsset = m_Timeline;
            m_Director.extrapolationMode = DirectorWrapMode.Loop;
            m_Director.timeUpdateMode = DirectorUpdateMode.GameTime;
            m_Director.RebuildGraph();
            m_Director.time = 0.0;
            m_Director.Evaluate();
            m_IsPlaying = false;
        }

        // Creates one timeline clip on the track that spans the whole duration and carries the given playable asset.
        // T is the track's registered clip type; CreateClip<T> makes a placeholder asset that we replace with the
        // pre-populated one so the behaviour receives our curve data and resolved root.
        private static void CreateSpanningClip<T>(TrackAsset track, PlayableAsset asset, double duration)
            where T : ScriptableObject, IPlayableAsset
        {
            TimelineClip clip = track.CreateClip<T>();
            clip.asset = asset;
            clip.start = 0.0;
            clip.duration = duration > 0.0 ? duration : 1.0;
            clip.displayName = asset.name;
        }

        // Latest end time across all baked bone / morph curves.
        private static double ComputeModelDuration(VMDModelClipData data)
        {
            double max = 0.0;
            if (data.bones != null)
            {
                max = System.Math.Max(max, MaxCurveTime(data.bones.curves));
            }
            if (data.morphs != null)
            {
                max = System.Math.Max(max, MaxCurveTime(data.morphs.curves));
            }
            return max;
        }

        // Latest end time across all camera curves.
        private static double ComputeCameraDuration(VMDCameraClipData data)
        {
            double max = 0.0;
            max = System.Math.Max(max, CurveEndTime(data.targetPositionX));
            max = System.Math.Max(max, CurveEndTime(data.targetPositionY));
            max = System.Math.Max(max, CurveEndTime(data.targetPositionZ));
            max = System.Math.Max(max, CurveEndTime(data.targetRotationX));
            max = System.Math.Max(max, CurveEndTime(data.targetRotationY));
            max = System.Math.Max(max, CurveEndTime(data.targetRotationZ));
            max = System.Math.Max(max, CurveEndTime(data.targetRotationW));
            max = System.Math.Max(max, CurveEndTime(data.cameraLocalPositionZ));
            max = System.Math.Max(max, CurveEndTime(data.fieldOfView));
            return max;
        }

        private static double MaxCurveTime(AnimationCurve[] curves)
        {
            double max = 0.0;
            if (curves == null)
            {
                return max;
            }
            for (int i = 0; i < curves.Length; ++i)
            {
                max = System.Math.Max(max, CurveEndTime(curves[i]));
            }
            return max;
        }

        private static double CurveEndTime(AnimationCurve curve)
        {
            if (curve == null || curve.length == 0)
            {
                return 0.0;
            }
            return curve[curve.length - 1].time;
        }

        /// <summary>Starts (or resumes) automatic playback.</summary>
        public void Play()
        {
            if (m_Director == null || m_Timeline == null)
            {
                return;
            }
            m_Director.timeUpdateMode = DirectorUpdateMode.GameTime;
            m_Director.Play();
            m_IsPlaying = true;
        }

        /// <summary>Pauses playback while keeping the current time so the pose can be scrubbed.</summary>
        public void Pause()
        {
            if (m_Director == null || m_Timeline == null)
            {
                return;
            }
            m_Director.Pause();
            m_IsPlaying = false;
        }

        /// <summary>Toggles between play and pause.</summary>
        public void TogglePlay()
        {
            if (m_IsPlaying)
            {
                Pause();
            }
            else
            {
                Play();
            }
        }

        /// <summary>Seeks to a normalized [0,1] position and evaluates immediately, leaving the play/pause state unchanged.</summary>
        public void Seek(float normalized)
        {
            if (m_Director == null || m_Timeline == null)
            {
                return;
            }
            double target = Mathf.Clamp01(normalized) * duration;
            m_Director.time = target;
            m_Director.Evaluate();
        }

        /// <summary>Seeks to a specific frame and evaluates immediately.</summary>
        public void SeekFrame(int frame)
        {
            if (m_Director == null || m_Timeline == null || m_FrameRate <= 0.0f)
            {
                return;
            }
            double target = Mathf.Clamp(frame, 0, frameTotal) / m_FrameRate;
            m_Director.time = target;
            m_Director.Evaluate();
        }
    }
}
