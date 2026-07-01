using System;
using System.IO;
using System.Threading.Tasks;
using UMT;
using UnityEngine;
using UnityEngine.Playables;

namespace UMTDemo
{
    /// <summary>
    /// Orchestrates the runtime PMX/VMD showcase: picks files with the native dialog, imports the PMX model live, converts VMD motion + camera to animation clips, drives them through <see cref="VMDTimelinePlayer"/>, and switches the single camera between user (orbit) and VMD (timeline) control.
    /// The scene must provide the documented hierarchy: a timeline root holding the <see cref="PlayableDirector"/>, a <c>CameraRig</c> (Animator) → <c>CameraTarget</c> → <c>Camera</c>, and a <c>ModelRoot</c> the imported model is parented under. Assign the corresponding references below.
    /// </summary>
    public sealed class MMDShowcaseController : MonoBehaviour
    {
        [Header("Scene references")]
        [SerializeField] private Animator m_CameraRigAnimator;
        [SerializeField] private Transform m_ModelRoot;
        [SerializeField] private OrbitCameraController m_Orbit;
        [SerializeField] private VMDTimelinePlayer m_Player;
        [SerializeField] private float m_FrameRate = 60.0f;
        [Tooltip("Run the live Bullet physics solver at runtime, layered on top of the baked-FK motion clip.")]
        [SerializeField] private bool m_RuntimePhysics = true;
        [Tooltip("Bake physics into the motion clip during VMD conversion. Independent of the live runtime solver; read the next time a motion is converted.")]
        [SerializeField] private bool m_BakePhysics;
        [Tooltip("Run GPU SDEF skinning for meshes that contain SDEF vertices.")]
        [SerializeField] private bool m_SDEFSkinning = true;
        [Tooltip("UMTResources asset providing the rename/romanization lists. Leave empty to load it from the package Resources folder at runtime.")]
        [SerializeField] private UMTResources m_Resources;

        private PMXImportResult m_ImportResult;
        private GameObject m_CurrentModel;
        private VMDModelClipData m_BodyClip;
        private VMDCameraClipData m_CameraClip;
        private string m_ModelName = string.Empty;
        private string m_MotionName = string.Empty;
        private string m_CameraName = string.Empty;
        private bool m_VMDCameraMode;
        private bool m_BodyClipHasBakedPhysics; // the loaded body clip carries baked physics; live runtime physics is locked off while true
        private bool m_Busy;
        private UMTFrameBudget m_FrameBudget = new UMTFrameBudget(10.0); // 10 ms per frame for long-running tasks
        private PMXAnimationPaths m_Paths = new PMXAnimationPaths(); // precomputed bone/morph paths for the loaded model
        private MMDTransformManager m_TransformManager;
        private SkinnedMeshRenderer[] m_Renderers;

        /// <summary>Raised with a human-readable status string for the UI.</summary>
        public event Action<string> StatusChanged;

        /// <summary>Raised when a long operation starts (true) or ends (false) so the UI can show/hide the progress bar.</summary>
        public event Action<bool> BusyChanged;

        /// <summary>Raised with a normalized [0,1] progress value during loading and conversion.</summary>
        public event Action<float> ProgressChanged;

        /// <summary>Raised when the active camera mode changes; argument is true for VMD camera, false for user camera.</summary>
        public event Action<bool> CameraModeChanged;

        /// <summary>Raised when the live runtime physics mode changes; argument is true when the Bullet solver runs at runtime.</summary>
        public event Action<bool> RuntimePhysicsChanged;

        /// <summary>Raised when the physics-baking mode changes; argument is true when physics is baked into the clip.</summary>
        public event Action<bool> BakePhysicsChanged;

        /// <summary>Raised when GPU SDEF skinning is toggled; argument is true when SDEF skinning runs.</summary>
        public event Action<bool> SDEFChanged;

        /// <summary>Raised when the loaded model, motion, or camera name changes.</summary>
        public event Action InfoChanged;

        /// <summary>The timeline player the UI binds its transport controls to.</summary>
        public VMDTimelinePlayer player => m_Player;

        /// <summary>True once a PMX model has been imported.</summary>
        public bool modelLoaded => m_ImportResult != null;

        /// <summary>True when the showcase is currently importing or converting (dialogs should be ignored).</summary>
        public bool busy => m_Busy;

        /// <summary>True when the loaded motion includes a camera track.</summary>
        public bool hasCamera => m_Player != null && m_Player.hasCamera;

        /// <summary>True when the camera is currently being driven by the VMD timeline.</summary>
        public bool vmdCameraMode => m_VMDCameraMode;

        /// <summary>True when the live Bullet physics solver runs at runtime.</summary>
        public bool runtimePhysics => m_RuntimePhysics;

        /// <summary>True when the loaded clip carries baked physics, so live runtime physics is locked off until the animation is reset or a non-baked motion is loaded.</summary>
        public bool runtimePhysicsLocked => m_BodyClipHasBakedPhysics;

        /// <summary>True when physics is baked into the motion clip during VMD conversion.</summary>
        public bool bakePhysics => m_BakePhysics;

        /// <summary>True when GPU SDEF skinning runs for SDEF meshes.</summary>
        public bool sdefSkinning => m_SDEFSkinning;

        /// <summary>Name of the loaded PMX model, or empty when none is loaded.</summary>
        public string modelName => m_ModelName;

        /// <summary>Name of the loaded VMD motion, or empty when none is loaded.</summary>
        public string motionName => m_MotionName;

        /// <summary>Name of the loaded VMD camera, or empty when none is loaded.</summary>
        public string cameraName => m_CameraName;

        /// <summary>Opens the native file dialog to pick a PMX file and imports it.</summary>
        public async void LoadPMX()
        {
            if (m_Busy)
            {
                return;
            }

            // PMX models reference sibling textures by relative path, so on the Web platform we pick the whole model
            // folder (the picker writes every file into MEMFS preserving structure). Standalone falls back to a file dialog.
            string path = await WebFilePicker.PickFolderForPMXAsync("Open PMX Model");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            LoadPMXAsync(path);
        }

        /// <summary>Opens the native file dialog to pick a VMD body/morph motion and converts it. Requires a loaded model.</summary>
        public async void LoadMotion()
        {
            if (m_Busy)
            {
                return;
            }

            if (m_ImportResult == null)
            {
                SetStatus("Load a PMX model first.");
                return;
            }

            string path = await WebFilePicker.PickAsync("Open VMD Motion", "vmd");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            LoadMotionAsync(path);
        }

        /// <summary>Opens the native file dialog to pick a VMD camera motion and converts it. Does not require a model.</summary>
        public async void LoadCamera()
        {
            if (m_Busy)
            {
                return;
            }

            string path = await WebFilePicker.PickAsync("Open VMD Camera", "vmd");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            LoadCameraAsync(path);
        }

        /// <summary>Switches who drives the single camera. VMD mode is ignored when the motion has no camera track.</summary>
        public void SetCameraMode(bool vmdCamera)
        {
            bool useVMD = vmdCamera && hasCamera;
            m_VMDCameraMode = useVMD;
            if (m_Orbit != null)
            {
                m_Orbit.controlEnabled = !useVMD;
            }
            CameraModeChanged?.Invoke(useVMD);
        }

        /// <summary>Toggles the live Bullet physics solver. Applies immediately to the loaded model and re-settles the simulation.</summary>
        public void SetRuntimePhysics(bool value)
        {
            // A clip baked with physics already carries the simulation; live physics stays locked off until the
            // animation is reset or a non-baked motion is loaded, so ignore attempts to turn it back on.
            if (m_Busy || m_RuntimePhysics == value || (value && m_BodyClipHasBakedPhysics))
            {
                return;
            }

            m_RuntimePhysics = value;
            ApplyPhysicsMode();
            // Re-settle the live simulation (or clear it) so the new mode takes effect from the current pose.
            ResetPhysics();
            RuntimePhysicsChanged?.Invoke(value);
        }

        /// <summary>Toggles whether physics is baked into the motion clip during VMD conversion.</summary>
        public void SetBakePhysics(bool value)
        {
            // The mode is read the next time a motion is converted; changing it does not affect an already-loaded clip.
            if (m_Busy || m_BakePhysics == value)
            {
                return;
            }

            m_BakePhysics = value;
            BakePhysicsChanged?.Invoke(value);
        }

        /// <summary>Toggles GPU SDEF skinning. Applies immediately to the loaded model.</summary>
        public void SetSDEF(bool value)
        {
            if (m_SDEFSkinning == value)
            {
                return;
            }

            m_SDEFSkinning = value;
            if (m_TransformManager != null)
            {
                m_TransformManager.doSDEFSkinning = value;
            }
            SDEFChanged?.Invoke(value);
        }

        /// <summary>Re-frames the user camera on the model.</summary>
        public void ResetView()
        {
            if (m_Orbit != null)
            {
                m_Orbit.ResetPose();
            }
        }

        /// <summary>Rewinds playback to the first frame and pauses.</summary>
        public void ResetPlayback()
        {
            if (m_Player != null)
            {
                m_Player.Pause();
                m_Player.SeekFrame(0);
            }
            ResetPhysics();
        }

        /// <summary>Seeks to a normalized [0,1] position, resetting live physics so the simulation re-settles from the new pose.</summary>
        public void Seek(float normalized)
        {
            if (m_Player != null)
            {
                m_Player.Seek(normalized);
            }
            ResetPhysics();
        }

        /// <summary>Seeks to a specific frame, resetting live physics so the simulation re-settles from the new pose.</summary>
        public void SeekFrame(int frame)
        {
            if (m_Player != null)
            {
                m_Player.SeekFrame(frame);
            }
            ResetPhysics();
        }

        // Re-settles the live Bullet simulation to the model's current pose after a seek. Jumping playback time
        // teleports the bones, so without this the soft-body/rigid-body state lags one frame behind and visibly
        // snaps. Safe to call when physics is baked or absent  ResetPhysics no-ops without a physics manager.
        private void ResetPhysics()
        {
            if (m_TransformManager != null)
            {
                m_TransformManager.ResetPhysics();
            }
        }

        /// <summary>Clears the loaded motion and returns the model to its default (bind) pose.</summary>
        public void ResetAnimation()
        {
            if (m_ImportResult == null)
            {
                return;
            }

            m_BodyClip = null;
            m_MotionName = string.Empty;

            // The baked clip is gone, so live physics is no longer locked; restore the requested mode.
            if (m_BodyClipHasBakedPhysics)
            {
                m_BodyClipHasBakedPhysics = false;
                RuntimePhysicsChanged?.Invoke(m_RuntimePhysics);
            }

            // Idle the solver, drop the body track, and restore the model to its default stance.

            if (m_TransformManager != null)
            {
                m_TransformManager.transformEnabled = false;
                m_TransformManager.ResetToBindPose();
            }
            ResetBlendShapes();
            RebuildTimeline();

            InfoChanged?.Invoke();
            SetStatus("Animation reset to default pose.");
        }

        private void ResetBlendShapes()
        {
            if (m_Renderers == null)
            {
                return;
            }
            foreach (SkinnedMeshRenderer renderer in m_Renderers)
            {
                Mesh mesh = renderer.sharedMesh;
                if (mesh == null)
                {
                    continue;
                }
                for (int i = 0; i < mesh.blendShapeCount; ++i)
                {
                    renderer.SetBlendShapeWeight(i, 0.0f);
                }
            }
        }

        private async void LoadPMXAsync(string path)
        {
            SetBusy(true);
            try
            {
                UMTResources resources = m_Resources;
                if (resources == null)
                {
                    Debug.LogWarning("UMTResources asset not found; importing without bone/morph renames.", this);
                }

                // Each stage updates the UI then yields a frame so the progress bar repaints before the blocking work runs.
                SetProgress(0.1f, $"Reading {Path.GetFileName(path)} …");
                await m_FrameBudget.YieldIfNeeded();
                PMXModel model;

                FileStream stream = File.OpenRead(path);
                model = await PMXReader.ReadAsync(m_FrameBudget, stream, true);
                stream.Dispose();
                PMXRenameResult renameResult = null;
                if (resources != null)
                {
                    SetProgress(0.4f, "Renaming bones and morphs …");
                    PMXRenameLists renameLists = await PMXRenameUtilities.LoadRenameListsJsonAsync(m_FrameBudget, resources.GetPMXRenameListsJson());
                    renameResult = await PMXRenameUtilities.RenameAsync(m_FrameBudget, model, renameLists, resources);
                }

                SetProgress(0.7f, "Building model …");
                await m_FrameBudget.YieldIfNeeded();
                PMXImportOptions options = new PMXImportOptions
                {
                    sourcePath = path, // base directory for resolving the model's relative texture paths
                    createAvatar = false,
                    applyRenames = false, // renamed above
                    umtResources = resources,
                    parent = m_ModelRoot,
                };
                PMXImportResult result = await PMXImporter.BuildUnityObjectsAsync(m_FrameBudget, model, options);
                result.renameResult = renameResult;

                SetProgress(0.95f, "Finalizing …");
                await m_FrameBudget.YieldIfNeeded();

                if (m_CurrentModel != null)
                {
                    Destroy(m_CurrentModel);
                }

                m_ImportResult = result;
                m_CurrentModel = result.root;
                m_Renderers = m_CurrentModel.GetComponentsInChildren<SkinnedMeshRenderer>();
                for (int i = 0; i < m_Renderers.Length; ++i)
                {
                    m_Renderers[i].updateWhenOffscreen = true;
                }
                m_BodyClip = null;        // a previous motion was baked for the old model
                m_BodyClipHasBakedPhysics = false; // no clip yet, so live physics is not locked
                m_ModelName = Path.GetFileNameWithoutExtension(path);
                m_MotionName = string.Empty; // body clip cleared with the new model

                // Keep the solver idle until a motion is loaded; the physics mode is applied at VMD-load time.
                m_TransformManager = result.mmdTransformResult.transformManager;
                if (m_TransformManager != null)
                {
                    m_TransformManager.transformEnabled = true;
                    m_TransformManager.doSDEFSkinning = m_SDEFSkinning;
                }

                FrameModel(result.root);
                RebuildTimeline();
                InfoChanged?.Invoke();

                SetProgress(1.0f, $"Loaded {Path.GetFileName(path)}. Now load a VMD motion.");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                SetProgress(0.0f, $"Failed to load model: {e.Message}");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void LoadMotionAsync(string path)
        {
            SetBusy(true);
            try
            {
                Debug.Log($"LoadMotionAsync start: path='{path}' exists={File.Exists(path)}");
                SetProgress(0.0f, "Reading motion …");
                await m_FrameBudget.YieldIfNeeded();
                VMDAnimation animation = await VMDReader.ReadAsync(m_FrameBudget, ReadAllBytesAsync(path));
                Debug.Log($"LoadMotionAsync read: bones={animation.boneFrames.Length} morphs={animation.morphFrames.Length}");
                if (animation.boneFrames.Length == 0 && animation.morphFrames.Length == 0)
                {
                    SetProgress(0.0f, "Selected VMD contained no bone or morph frames.");
                    return;
                }

                VMDAnimationClipOptions options = new VMDAnimationClipOptions
                {
                    frameRate = m_FrameRate,
                    bakeIKToFK = true,
                    bakePhysicsToFK = m_BakePhysics,
                };
                await m_FrameBudget.YieldIfNeeded();
                Debug.Log("LoadMotionAsync: converting …");
                VMDModelClipData clip = await VMDAnimationClipConverter.ConvertAsync(m_FrameBudget, animation, m_ImportResult.model, m_Paths, options, OnConvertProgress);
                Debug.Log($"LoadMotionAsync: converted clip={(clip != null)}");

                m_BodyClip = clip;
                m_MotionName = Path.GetFileNameWithoutExtension(path);

                // A clip baked with physics carries the simulation itself; force the live solver off and lock it
                // there so the two cannot double up, until the animation is reset or a non-baked motion is loaded.
                m_BodyClipHasBakedPhysics = m_BakePhysics;
                if (m_BodyClipHasBakedPhysics && m_RuntimePhysics)
                {
                    m_RuntimePhysics = false;
                    RuntimePhysicsChanged?.Invoke(false);
                }

                ApplyPhysicsMode();
                RebuildTimeline();
                // RebuildTimeline rewinds to frame 0; re-settle physics to the new motion's first pose.
                ResetPhysics();
                InfoChanged?.Invoke();

                SetProgress(1.0f, "Motion ready. Press Play.");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                SetProgress(0.0f, $"Failed to convert motion: {e.Message}");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void LoadCameraAsync(string path)
        {
            SetBusy(true);
            try
            {
                SetProgress(0.0f, "Reading camera …");
                await m_FrameBudget.YieldIfNeeded();
                VMDAnimation animation = await VMDReader.ReadAsync(m_FrameBudget, ReadAllBytesAsync(path));
                if (animation.cameraFrames.Length == 0)
                {
                    SetProgress(0.0f, "Selected VMD contained no camera frames.");
                    return;
                }
                await m_FrameBudget.YieldIfNeeded();

                VMDCameraClipData clip = await VMDAnimationClipConverter.ConvertCameraAsync(m_FrameBudget, animation, m_FrameRate, null, OnConvertProgress);

                m_CameraClip = clip;
                m_CameraName = Path.GetFileNameWithoutExtension(path);
                RebuildTimeline();
                SetCameraMode(true);
                InfoChanged?.Invoke();

                SetProgress(1.0f, "Camera ready. Press Play.");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                SetProgress(0.0f, $"Failed to convert camera: {e.Message}");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void OnConvertProgress(VMDAnimationClipConverter.Stage stage, int frame, int totalFrames)
        {
            float value = totalFrames > 0 ? (float)frame / totalFrames : 0.0f;
            SetProgress(value, totalFrames > 0 ? $"{stage}: frame {frame} / {totalFrames}" : stage.ToString());
        }

        // Reads a file as bytes for the VMD reader. WebGL is single-threaded, so File.ReadAllBytesAsync schedules its
        // completion on a thread pool that never runs and the await hangs; read synchronously there (a VMD is small).
        // Other platforms keep the genuinely async read so large files don't block the main thread.
        private static Task<byte[]> ReadAllBytesAsync(string path)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return Task.FromResult(File.ReadAllBytes(path));
#else
            return File.ReadAllBytesAsync(path);
#endif
        }

        // Configures the runtime solver to match the runtime-physics toggle. A motion clip always bakes IK and
        // constraints to FK, so with live physics on the solver only layers physics on top; with it off the clip
        // (or, when none is loaded, a plain constraint/IK solve) is authoritative and the physics solver stays idle.
        private void ApplyPhysicsMode()
        {
            if (m_TransformManager == null)
            {
                return;
            }

            MMDPhysicsManager physicsManager = m_TransformManager.physicsManager;

            if (m_RuntimePhysics)
            {
                m_TransformManager.solveConstraints = false;
                m_TransformManager.solveIK = false;
                m_TransformManager.livePhysics = true;
                m_TransformManager.transformEnabled = true;

                // The native Bullet context is created in MMDTransformManager.OnEnable, but during the runtime build that
                // OnEnable fired before its physicsManager field was wired, so Initialize() never ran. Cycle enabled now
                // (with physicsManager set) to force a clean OnEnable -> InitializePhysics; otherwise LateUpdate's
                // ResetPhysics hits an uninitialized native handle and crashes the player.
                if (physicsManager != null && m_TransformManager.gameObject.activeInHierarchy)
                {
                    m_TransformManager.enabled = true;
                }
                return;
            }

            // Runtime physics off: keep the physics solver idle. A loaded clip drives the whole pose, so idle the
            // solver entirely; with no clip, keep solving constraints/IK so the model still holds a valid pose.
            m_TransformManager.livePhysics = false;
            if (m_BodyClip != null)
            {
                m_TransformManager.transformEnabled = false;
            }
            else
            {
                m_TransformManager.solveConstraints = true;
                m_TransformManager.solveIK = true;
                m_TransformManager.transformEnabled = true;
            }
        }

        // Rebuilds the timeline from whichever clips are currently loaded, then plays from the start.
        private void RebuildTimeline()
        {
            // The body bone paths resolve against the transform the body Animator sits on (the same binding the old
            // AnimationTrack used); the camera paths resolve against the camera rig transform.
            Transform bodyRoot = null;
            if (m_ImportResult != null)
            {
                Animator bodyAnimator = m_TransformManager != null
                    ? m_TransformManager.animator
                    : m_ImportResult.root.GetComponent<Animator>();
                bodyRoot = bodyAnimator != null ? bodyAnimator.transform : m_ImportResult.root.transform;
            }

            Transform cameraRoot = m_CameraRigAnimator != null ? m_CameraRigAnimator.transform : null;

            m_Player.Build(m_BodyClip, bodyRoot, m_CameraClip, cameraRoot, m_FrameRate);

            if (!m_Player.hasCamera)
            {
                SetCameraMode(false);
            }

            // Leave playback paused at the first frame; the user presses Play to start.
        }

        private void FrameModel(GameObject root)
        {
            if (m_Orbit == null)
            {
                return;
            }

            if (TryComputeBounds(root, out Bounds bounds))
            {
                m_Orbit.FrameTarget(bounds.center, bounds.extents.magnitude);
            }
        }

        private static bool TryComputeBounds(GameObject root, out Bounds bounds)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
            bounds = default;
            bool hasBounds = false;
            foreach (Renderer renderer in renderers)
            {
                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            return hasBounds;
        }

        private void SetBusy(bool busy)
        {
            m_Busy = busy;
            BusyChanged?.Invoke(busy);
        }

        private void SetProgress(float value, string label)
        {
            ProgressChanged?.Invoke(Mathf.Clamp01(value));
            SetStatus(label);
        }

        private void SetStatus(string message)
        {
            StatusChanged?.Invoke(message);
        }
    }
}
