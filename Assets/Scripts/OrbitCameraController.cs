using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace UMTDemo
{
    /// <summary>
    /// Drives the shared showcase camera when the user is in control. Rotates the <c>CameraTarget</c> pivot with a mouse drag and zooms the child <c>Camera</c> along its local Z with the scroll wheel.
    /// Runs at a high execution order and writes in <see cref="LateUpdate"/> so that, while <see cref="controlEnabled"/> is true, it overwrites whatever the Timeline's camera track evaluated this frame effectively detaching the camera from the timeline without swapping cameras. When <see cref="controlEnabled"/> is false the controller does nothing and the timeline's camera animation is what the viewer sees.
    /// The <c>CameraRig</c> the pivot lives under is expected to sit at the origin with an identity transform so that the local-space values used here line up with the VMD camera animation, which also drives local transforms.
    /// </summary>
    [DefaultExecutionOrder(20000)]
    public sealed class OrbitCameraController : MonoBehaviour
    {
        [Header("Rig")]
        [Tooltip("The CameraTarget transform that this controller rotates (the orbit pivot).")]
        [SerializeField] private Transform m_Pivot;
        [Tooltip("The Camera child that is offset along local -Z by the zoom distance.")]
        [SerializeField] private Camera m_Camera;

        [Header("Input")]
        [SerializeField] private float m_RotateSpeed = 0.25f;
        [SerializeField] private float m_ZoomSpeed = 0.0015f;

        [Header("Defaults / Limits")]
        [SerializeField] private float m_DefaultPitch = 0.0f;
        [SerializeField] private float m_DefaultYaw = 180.0f;
        [SerializeField] private float m_DefaultDistance = 10.0f;
        [SerializeField] private float m_DefaultFieldOfView = 30.0f;
        [SerializeField] private Vector3 m_DefaultPivotCenter = new Vector3(0.0f, 1.0f, 0.0f);
        [SerializeField] private float m_MinPitch = -85.0f;
        [SerializeField] private float m_MaxPitch = 85.0f;
        [SerializeField] private float m_MinDistance = 0.2f;
        [SerializeField] private float m_MaxDistance = 100.0f;


        private float m_Pitch;
        private float m_Yaw;
        private float m_Distance;
        private Vector3 m_PivotCenter;
        private bool m_ControlEnabled = true;
        private bool m_Dragging;

        /// <summary>When true the controller drives the camera; when false the Timeline's camera track is left untouched.</summary>
        public bool controlEnabled
        {
            get => m_ControlEnabled;
            set => m_ControlEnabled = value;
        }

        private void Awake()
        {
            m_Yaw = m_DefaultYaw;
            m_Pitch = m_DefaultPitch;
            m_Distance = m_DefaultDistance;
            m_PivotCenter = m_DefaultPivotCenter;
        }

        /// <summary>Re-centres the pivot on a world-space point and picks a distance that frames a sphere of the given radius.</summary>
        public void FrameTarget(Vector3 worldCenter, float radius)
        {
            m_PivotCenter = ToPivotLocal(worldCenter);
            float halfFov = m_DefaultFieldOfView * 0.5f * Mathf.Deg2Rad;
            float fitDistance = radius / Mathf.Max(0.0001f, Mathf.Tan(halfFov));
            m_Distance = Mathf.Clamp(fitDistance * 1.2f, m_MinDistance, m_MaxDistance);
            m_Yaw = m_DefaultYaw;
            m_Pitch = m_DefaultPitch;
            Apply();
        }

        /// <summary>Restores the default orbit angles, distance, field of view, and re-applies the framed pivot centre.</summary>
        public void ResetPose()
        {
            m_Yaw = m_DefaultYaw;
            m_Pitch = m_DefaultPitch;
            m_Distance = m_DefaultDistance;
            Apply();
        }

        private void LateUpdate()
        {
            if (!m_ControlEnabled)
            {
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                bool overUI = IsPointerOverUI();

                // Only begin a drag when the press lands outside the UI; once started, keep
                // dragging even if the cursor passes over a panel, and end it on release.
                if (mouse.leftButton.wasPressedThisFrame)
                {
                    m_Dragging = !overUI;
                }
                else if (!mouse.leftButton.isPressed)
                {
                    m_Dragging = false;
                }

                if (m_Dragging)
                {
                    Vector2 delta = mouse.delta.ReadValue();
                    m_Yaw += delta.x * m_RotateSpeed;
                    m_Pitch -= delta.y * m_RotateSpeed;
                    m_Pitch = Mathf.Clamp(m_Pitch, m_MinPitch, m_MaxPitch);
                }

                // Ignore the scroll wheel while the pointer is hovering the UI so it can scroll panels.
                if (!overUI)
                {
                    float scroll = mouse.scroll.ReadValue().y;
                    if (Mathf.Abs(scroll) > 0.01f)
                    {
                        m_Distance -= scroll * m_ZoomSpeed;
                        m_Distance = Mathf.Clamp(m_Distance, m_MinDistance, m_MaxDistance);
                    }
                }
            }

            Apply();
        }

        private void Apply()
        {
            if (m_Pivot != null)
            {
                m_Pivot.localPosition = m_PivotCenter;
                m_Pivot.localRotation = Quaternion.Euler(m_Pitch, m_Yaw, 0.0f);
            }

            if (m_Camera != null)
            {
                m_Camera.transform.localPosition = new Vector3(0.0f, 0.0f, -m_Distance);
                m_Camera.transform.localRotation = Quaternion.identity;
                m_Camera.fieldOfView = m_DefaultFieldOfView;
            }
        }

        /// <summary>True when the mouse is over an interactable uGUI element, so camera input should be ignored.</summary>
        private static bool IsPointerOverUI()
        {
            EventSystem eventSystem = EventSystem.current;
            return eventSystem != null && eventSystem.IsPointerOverGameObject();
        }

        private Vector3 ToPivotLocal(Vector3 world)
        {
            if (m_Pivot != null && m_Pivot.parent != null)
            {
                return m_Pivot.parent.InverseTransformPoint(world);
            }
            return world;
        }
    }
}
