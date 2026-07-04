using System.Collections.Generic;
using UMT;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace UMTDemo
{
    /// <summary>
    /// Timeline track that plays VMD-derived <see cref="VMDModelClipData"/> (baked bone + morph curves) by evaluating
    /// the curves each frame and writing the model's bone transforms and blendshape weights directly. This replaces the
    /// built-in <c>AnimationTrack</c> because runtime VMD playback cannot use an <see cref="AnimationClip"/>:
    /// <see cref="AnimationClip.SetCurve"/> is editor-only for non-legacy clips, and Timeline rejects legacy clips.
    /// </summary>
    [TrackColor(0.2f, 0.6f, 0.9f)]
    [TrackClipType(typeof(VMDModelPlayableAsset))]
    public sealed class VMDModelPlayableTrack : TrackAsset
    {
    }

    /// <summary>
    /// Timeline track that plays a VMD camera path (<see cref="VMDCameraClipData"/>) by evaluating the camera-rig curves
    /// each frame and writing the look-at target transform, the camera child offset, and the camera field of view.
    /// </summary>
    [TrackColor(0.9f, 0.6f, 0.2f)]
    [TrackClipType(typeof(VMDCameraPlayableAsset))]
    public sealed class VMDCameraPlayableTrack : TrackAsset
    {
    }

    /// <summary>Playable asset wrapping the model curve data and the model root the curves are resolved against.</summary>
    public sealed class VMDModelPlayableAsset : PlayableAsset
    {
        /// <summary>Bone + morph curve data to play. Set programmatically before the graph is built.</summary>
        public VMDModelClipData clipData;

        /// <summary>Root transform whose children the bone/renderer paths resolve against (the same transform the Animator sat on).</summary>
        public Transform root;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            ScriptPlayable<VMDModelPlayableBehaviour> playable = ScriptPlayable<VMDModelPlayableBehaviour>.Create(graph);
            VMDModelPlayableBehaviour behaviour = playable.GetBehaviour();
            behaviour.clipData = clipData;
            behaviour.root = root;
            return playable;
        }
    }

    /// <summary>Playable asset wrapping the camera curve data and the camera rig root the curves are resolved against.</summary>
    public sealed class VMDCameraPlayableAsset : PlayableAsset
    {
        /// <summary>Camera-rig curve data to play. Set programmatically before the graph is built.</summary>
        public VMDCameraClipData clipData;

        /// <summary>Root transform the rig-relative camera paths resolve against (parent of the <c>CameraTarget</c> node).</summary>
        public Transform root;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            ScriptPlayable<VMDCameraPlayableBehaviour> playable = ScriptPlayable<VMDCameraPlayableBehaviour>.Create(graph);
            VMDCameraPlayableBehaviour behaviour = playable.GetBehaviour();
            behaviour.clipData = clipData;
            behaviour.root = root;
            return playable;
        }
    }

    /// <summary>
    /// Evaluates baked bone and morph curves each frame and writes them onto the resolved model transforms and
    /// skinned-mesh blendshapes. Bones use the 7-channel baked layout (localPosition x/y/z, localRotation x/y/z/w).
    /// </summary>
    public sealed class VMDModelPlayableBehaviour : PlayableBehaviour
    {
        private const int k_BakedBoneChannelCount = 7;

        public VMDModelClipData clipData;
        public Transform root;

        private bool m_Resolved;
        private Transform[] m_BoneTransforms;
        private MorphTarget[] m_MorphTargets;

        private struct MorphTarget
        {
            public SkinnedMeshRenderer renderer;
            public int blendShapeIndex;
            public AnimationCurve curve;
        }

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            if (clipData == null || root == null)
            {
                return;
            }

            if (!m_Resolved)
            {
                Resolve();
            }

            float time = (float)playable.GetTime();
            ApplyBones(time);
            ApplyMorphs(time);
        }

        private void Resolve()
        {
            VMDClipData bones = clipData.bones;
            if (clipData.baked)
            {
                FixBakedRotationContinuity(bones);
            }
            m_BoneTransforms = new Transform[bones.paths.Length];
            for (int i = 0; i < bones.paths.Length; ++i)
            {
                string path = bones.paths[i];
                m_BoneTransforms[i] = string.IsNullOrEmpty(path) ? null : root.Find(path);
            }

            VMDMorphClipData morphs = clipData.morphs;
            List<MorphTarget> targets = new List<MorphTarget>(morphs.paths.Length);
            for (int i = 0; i < morphs.paths.Length; ++i)
            {
                if (morphs.curves[i] == null)
                {
                    continue;
                }
                Transform rendererTransform = root.Find(morphs.paths[i]);
                if (rendererTransform == null)
                {
                    continue;
                }
                SkinnedMeshRenderer renderer = rendererTransform.GetComponent<SkinnedMeshRenderer>();
                if (renderer == null || renderer.sharedMesh == null)
                {
                    continue;
                }
                int blendShapeIndex = renderer.sharedMesh.GetBlendShapeIndex(morphs.names[i]);
                if (blendShapeIndex < 0)
                {
                    continue;
                }
                targets.Add(new MorphTarget
                {
                    renderer = renderer,
                    blendShapeIndex = blendShapeIndex,
                    curve = morphs.curves[i],
                });
            }
            m_MorphTargets = targets.ToArray();

            m_Resolved = true;
        }

        private void ApplyBones(float time)
        {
            VMDClipData bones = clipData.bones;
            for (int i = 0; i < m_BoneTransforms.Length; ++i)
            {
                Transform bone = m_BoneTransforms[i];
                if (bone == null)
                {
                    continue;
                }

                int channelStart = i * k_BakedBoneChannelCount;
                AnimationCurve posX = bones.curves[channelStart + 0];
                AnimationCurve posY = bones.curves[channelStart + 1];
                AnimationCurve posZ = bones.curves[channelStart + 2];
                if (posX != null && posY != null && posZ != null)
                {
                    bone.localPosition = new Vector3(posX.Evaluate(time), posY.Evaluate(time), posZ.Evaluate(time));
                }

                AnimationCurve rotX = bones.curves[channelStart + 3];
                AnimationCurve rotY = bones.curves[channelStart + 4];
                AnimationCurve rotZ = bones.curves[channelStart + 5];
                AnimationCurve rotW = bones.curves[channelStart + 6];
                if (rotX != null && rotY != null && rotZ != null && rotW != null)
                {
                    Quaternion rotation = new Quaternion(rotX.Evaluate(time), rotY.Evaluate(time), rotZ.Evaluate(time), rotW.Evaluate(time));
                    // The baked quaternion channels are sampled per-frame and may not be exactly unit-length once
                    // interpolated between keys, so normalize before assigning.
                    bone.localRotation = Normalize(rotation);
                }
            }
        }

        private void ApplyMorphs(float time)
        {
            for (int i = 0; i < m_MorphTargets.Length; ++i)
            {
                MorphTarget target = m_MorphTargets[i];
                target.renderer.SetBlendShapeWeight(target.blendShapeIndex, target.curve.Evaluate(time));
            }
        }

        /// <summary>
        /// Restores quaternion sign continuity on every bone's baked rotation channels. The bake can store adjacent
        /// keys as q and -q (the same rotation in the opposite hemisphere); the editor-built AnimationClip repairs
        /// this via <see cref="AnimationClip.EnsureQuaternionContinuity"/>, but the raw runtime curves never get that
        /// pass, and evaluating the four channels independently between sign-flipped keys collapses the normalized
        /// result toward identity for sub-key samples. Idempotent, so re-running on shared clip data is safe.
        /// </summary>
        private static void FixBakedRotationContinuity(VMDClipData bones)
        {
            for (int i = 0; i < bones.paths.Length; ++i)
            {
                int channelStart = i * k_BakedBoneChannelCount;
                EnsureQuaternionContinuity(
                    bones.curves[channelStart + 3],
                    bones.curves[channelStart + 4],
                    bones.curves[channelStart + 5],
                    bones.curves[channelStart + 6]);
            }
        }

        private static void EnsureQuaternionContinuity(AnimationCurve rotX, AnimationCurve rotY, AnimationCurve rotZ, AnimationCurve rotW)
        {
            if (rotX == null || rotY == null || rotZ == null || rotW == null)
            {
                return;
            }

            Keyframe[] keysX = rotX.keys;
            Keyframe[] keysY = rotY.keys;
            Keyframe[] keysZ = rotZ.keys;
            Keyframe[] keysW = rotW.keys;
            int keyCount = keysX.Length;
            if (keyCount < 2 || keysY.Length != keyCount || keysZ.Length != keyCount || keysW.Length != keyCount)
            {
                return;
            }

            bool anyFlipped = false;
            for (int i = 1; i < keyCount; ++i)
            {
                float dot = keysX[i].value * keysX[i - 1].value + keysY[i].value * keysY[i - 1].value + keysZ[i].value * keysZ[i - 1].value + keysW[i].value * keysW[i - 1].value;
                if (dot < 0.0f)
                {
                    keysX[i].value = -keysX[i].value;
                    keysY[i].value = -keysY[i].value;
                    keysZ[i].value = -keysZ[i].value;
                    keysW[i].value = -keysW[i].value;
                    anyFlipped = true;
                }
            }

            if (!anyFlipped)
            {
                return;
            }

            ApplyLinearTangents(keysX);
            ApplyLinearTangents(keysY);
            ApplyLinearTangents(keysZ);
            ApplyLinearTangents(keysW);
            rotX.keys = keysX;
            rotY.keys = keysY;
            rotZ.keys = keysZ;
            rotW.keys = keysW;
        }

        // Mirrors the converter's baked-key tangent scheme: each segment's slope becomes the previous key's
        // outTangent and the next key's inTangent; the first inTangent and last outTangent stay 0.
        private static void ApplyLinearTangents(Keyframe[] keyframes)
        {
            for (int i = 1; i < keyframes.Length; ++i)
            {
                float tangent = (keyframes[i].value - keyframes[i - 1].value) / (keyframes[i].time - keyframes[i - 1].time);
                keyframes[i - 1].outTangent = tangent;
                keyframes[i].inTangent = tangent;
            }
        }

        private static Quaternion Normalize(Quaternion q)
        {
            float magnitude = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (magnitude < 1e-6f)
            {
                return Quaternion.identity;
            }
            float inverse = 1.0f / magnitude;
            return new Quaternion(q.x * inverse, q.y * inverse, q.z * inverse, q.w * inverse);
        }
    }

    /// <summary>
    /// Evaluates the VMD camera curves each frame and writes the look-at target transform, the camera child's local Z
    /// offset (camera distance), and the camera field of view.
    /// </summary>
    public sealed class VMDCameraPlayableBehaviour : PlayableBehaviour
    {
        public VMDCameraClipData clipData;
        public Transform root;

        private bool m_Resolved;
        private Transform m_Target;
        private Transform m_Camera;
        private Camera m_CameraComponent;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            if (clipData == null || root == null)
            {
                return;
            }

            if (!m_Resolved)
            {
                Resolve();
            }

            float time = (float)playable.GetTime();

            if (m_Target != null)
            {
                if (clipData.targetPositionX != null && clipData.targetPositionY != null && clipData.targetPositionZ != null)
                {
                    m_Target.localPosition = new Vector3(
                        clipData.targetPositionX.Evaluate(time),
                        clipData.targetPositionY.Evaluate(time),
                        clipData.targetPositionZ.Evaluate(time));
                }

                if (clipData.targetRotationX != null && clipData.targetRotationY != null && clipData.targetRotationZ != null && clipData.targetRotationW != null)
                {
                    Quaternion rotation = new Quaternion(
                        clipData.targetRotationX.Evaluate(time),
                        clipData.targetRotationY.Evaluate(time),
                        clipData.targetRotationZ.Evaluate(time),
                        clipData.targetRotationW.Evaluate(time));
                    m_Target.localRotation = Normalize(rotation);
                }
            }

            if (m_Camera != null && clipData.cameraLocalPositionZ != null)
            {
                Vector3 localPosition = m_Camera.localPosition;
                localPosition.z = clipData.cameraLocalPositionZ.Evaluate(time);
                m_Camera.localPosition = localPosition;
            }

            if (m_CameraComponent != null && clipData.fieldOfView != null)
            {
                m_CameraComponent.fieldOfView = clipData.fieldOfView.Evaluate(time);
            }
        }

        private void Resolve()
        {
            m_Target = root.Find(VMDAnimationClipConverter.k_DefaultCameraTargetName);
            string cameraPath = VMDAnimationClipConverter.k_DefaultCameraTargetName + "/" + VMDAnimationClipConverter.k_DefaultCameraChildName;
            m_Camera = root.Find(cameraPath);
            m_CameraComponent = m_Camera != null ? m_Camera.GetComponent<Camera>() : null;
            m_Resolved = true;
        }

        private static Quaternion Normalize(Quaternion q)
        {
            float magnitude = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (magnitude < 1e-6f)
            {
                return Quaternion.identity;
            }
            float inverse = 1.0f / magnitude;
            return new Quaternion(q.x * inverse, q.y * inverse, q.z * inverse, q.w * inverse);
        }
    }
}
