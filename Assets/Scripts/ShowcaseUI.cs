using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace UMTDemo
{
    /// <summary>
    /// Builds the runtime showcase UI in code (uGUI + TextMeshPro) and wires it to <see cref="MMDShowcaseController"/> and its <see cref="VMDTimelinePlayer"/>: Load PMX / Load Motion / Load Camera buttons, a runtime-physics toggle, a bake-physics toggle, an SDEF toggle, a camera-mode toggle, a reset button, a transport with play/pause, a scrub slider, an editable frame number, and a Hide-UI toggle (also the <c>H</c> key).
    /// </summary>
    public sealed class ShowcaseUI : MonoBehaviour
    {
        [SerializeField] private MMDShowcaseController m_Controller;

        [Header("Style")]
        [SerializeField] private Color m_BarColor = new Color(0.05f, 0.05f, 0.07f, 0.78f);
        [SerializeField] private Color m_ButtonColor = new Color(0.18f, 0.20f, 0.26f, 1.0f);
        [SerializeField] private Color m_TextColor = Color.white;
        [SerializeField] private Color m_TrackColor = new Color(0.25f, 0.25f, 0.30f, 1.0f);
        [SerializeField] private Color m_FillColor = new Color(0.30f, 0.55f, 0.95f, 1.0f);
        [SerializeField] private Color m_HandleColor = Color.white;

        private CanvasGroup m_PanelGroup;
        private TextMeshProUGUI m_StatusLabel;
        private TextMeshProUGUI m_InfoLabel;
        private TextMeshProUGUI m_PlayPauseLabel;
        private TextMeshProUGUI m_CameraLabel;
        private TextMeshProUGUI m_FrameTotalLabel;
        private Button m_CameraButton;
        private Button m_RuntimePhysicsButton;
        private Button m_BakePhysicsButton;
        private Button m_SDEFButton;
        private Button m_ResetAnimButton;
        private Button m_PlayPauseButton;
        private Button m_RewindButton;
        private TextMeshProUGUI m_RuntimePhysicsLabel;
        private TextMeshProUGUI m_BakePhysicsLabel;
        private TextMeshProUGUI m_SDEFLabel;
        private Slider m_Slider;
        private TMP_InputField m_FrameInput;
        private Image m_ProgressFill;
        private GameObject m_ProgressBar;
        private Sprite m_WhiteSprite;
        private bool m_Scrubbing;
        private bool m_UIVisible = true;

        private void Awake()
        {
            EnsureEventSystem();
            BuildUI();
        }

        private void OnEnable()
        {
            if (m_Controller != null)
            {
                m_Controller.StatusChanged += OnStatusChanged;
                m_Controller.CameraModeChanged += OnCameraModeChanged;
                m_Controller.RuntimePhysicsChanged += OnRuntimePhysicsChanged;
                m_Controller.BakePhysicsChanged += OnBakePhysicsChanged;
                m_Controller.SDEFChanged += OnSDEFChanged;
                m_Controller.BusyChanged += OnBusyChanged;
                m_Controller.ProgressChanged += OnProgressChanged;
                m_Controller.InfoChanged += OnInfoChanged;
            }
        }

        private void OnDisable()
        {
            if (m_Controller != null)
            {
                m_Controller.StatusChanged -= OnStatusChanged;
                m_Controller.CameraModeChanged -= OnCameraModeChanged;
                m_Controller.RuntimePhysicsChanged -= OnRuntimePhysicsChanged;
                m_Controller.BakePhysicsChanged -= OnBakePhysicsChanged;
                m_Controller.SDEFChanged -= OnSDEFChanged;
                m_Controller.BusyChanged -= OnBusyChanged;
                m_Controller.ProgressChanged -= OnProgressChanged;
                m_Controller.InfoChanged -= OnInfoChanged;
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.hKey.wasPressedThisFrame)
            {
                ToggleUI();
            }

            if (m_Controller == null)
            {
                return;
            }

            VMDTimelinePlayer player = m_Controller.player;
            bool hasTimeline = player != null && player.hasTimeline;
            bool hasPlayback = hasTimeline && player.duration > 0.0001;

            if (m_PlayPauseLabel != null)
            {
                m_PlayPauseLabel.text = hasPlayback && player.isPlaying ? "Pause" : "Play";
            }

            if (m_CameraButton != null)
            {
                m_CameraButton.interactable = hasTimeline && player.hasCamera;
            }

            if (m_RuntimePhysicsButton != null)
            {
                // Locked off while the loaded clip carries baked physics, until the animation is reset.
                m_RuntimePhysicsButton.interactable = !m_Controller.busy && !m_Controller.runtimePhysicsLocked;
            }

            if (m_BakePhysicsButton != null)
            {
                m_BakePhysicsButton.interactable = !m_Controller.busy;
            }

            if (m_SDEFButton != null)
            {
                m_SDEFButton.interactable = !m_Controller.busy;
            }

            if (m_ResetAnimButton != null)
            {
                m_ResetAnimButton.interactable = m_Controller.modelLoaded && !m_Controller.busy;
            }

            // Transport is locked until a motion or camera with real duration is loaded.
            if (m_PlayPauseButton != null)
            {
                m_PlayPauseButton.interactable = hasPlayback;
            }
            if (m_RewindButton != null)
            {
                m_RewindButton.interactable = hasPlayback;
            }
            if (m_Slider != null)
            {
                m_Slider.interactable = hasPlayback;
            }
            if (m_FrameInput != null)
            {
                m_FrameInput.interactable = hasPlayback;
            }

            if (!hasPlayback)
            {
                if (m_Slider != null)
                {
                    m_Slider.SetValueWithoutNotify(0.0f);
                }
                if (m_FrameTotalLabel != null)
                {
                    m_FrameTotalLabel.text = "/ 0";
                }
                return;
            }

            if (!m_Scrubbing && m_Slider != null)
            {
                float total = (float)player.duration;
                float normalized = total > 0.0001f ? (float)(player.time / total) : 0.0f;
                m_Slider.SetValueWithoutNotify(normalized);
            }

            if (m_FrameTotalLabel != null)
            {
                m_FrameTotalLabel.text = $"/ {player.frameTotal}";
            }

            if (m_FrameInput != null && !m_FrameInput.isFocused)
            {
                m_FrameInput.SetTextWithoutNotify(player.frameCurrent.ToString(CultureInfo.InvariantCulture));
            }
        }

        private void OnStatusChanged(string message)
        {
            if (m_StatusLabel != null)
            {
                m_StatusLabel.text = message;
            }
        }

        private void OnCameraModeChanged(bool vmdCamera)
        {
            if (m_CameraLabel != null)
            {
                m_CameraLabel.text = vmdCamera ? "Camera: VMD" : "Camera: User";
            }
        }

        private void OnRuntimePhysicsChanged(bool on)
        {
            if (m_RuntimePhysicsLabel != null)
            {
                m_RuntimePhysicsLabel.text = on ? "Runtime Physics: On" : "Runtime Physics: Off";
            }
        }

        private void OnBakePhysicsChanged(bool baked)
        {
            if (m_BakePhysicsLabel != null)
            {
                m_BakePhysicsLabel.text = baked ? "Bake Physics: On" : "Bake Physics: Off";
            }
        }

        private void OnSDEFChanged(bool on)
        {
            if (m_SDEFLabel != null)
            {
                m_SDEFLabel.text = on ? "SDEF: On" : "SDEF: Off";
            }
        }

        private void OnBusyChanged(bool busy)
        {
            if (m_ProgressBar != null)
            {
                m_ProgressBar.SetActive(busy);
            }
        }

        private void OnProgressChanged(float value)
        {
            if (m_ProgressFill != null)
            {
                m_ProgressFill.fillAmount = value;
            }
        }

        private void OnInfoChanged()
        {
            if (m_InfoLabel == null)
            {
                return;
            }
            m_InfoLabel.text = $"Model: {Display(m_Controller.modelName)}\nMotion: {Display(m_Controller.motionName)}\nCamera: {Display(m_Controller.cameraName)}";
        }

        private static string Display(string value)
        {
            return string.IsNullOrEmpty(value) ? "-" : value;
        }

        private void ToggleUI()
        {
            m_UIVisible = !m_UIVisible;
            if (m_PanelGroup != null)
            {
                m_PanelGroup.alpha = m_UIVisible ? 1.0f : 0.0f;
                m_PanelGroup.interactable = m_UIVisible;
                m_PanelGroup.blocksRaycasts = m_UIVisible;
            }
        }

        private void BuildUI()
        {
            Canvas canvas = GetAttachedCanvas();
            if (canvas == null)
            {
                return;
            }

            // Everything except the always-visible hint goes under a CanvasGroup so it can be hidden together.
            GameObject panel = CreateChild("Panel", canvas.transform);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            Stretch(panelRect);
            m_PanelGroup = panel.AddComponent<CanvasGroup>();

            BuildTopBar(panel.transform);
            BuildBottomBar(panel.transform);
            BuildProgressBar(panel.transform);
            BuildInfoLabel(panel.transform);
            BuildHint(canvas.transform);

            OnCameraModeChanged(false);
            if (m_Controller != null)
            {
                OnRuntimePhysicsChanged(m_Controller.runtimePhysics);
                OnBakePhysicsChanged(m_Controller.bakePhysics);
                OnSDEFChanged(m_Controller.sdefSkinning);
                OnInfoChanged();
            }
        }

        private void BuildInfoLabel(Transform parent)
        {
            // A semi-transparent background rect keeps the Model/Motion/Camera names legible over any scene.
            GameObject background = CreateImage(parent, "InfoBackground", m_BarColor);
            RectTransform backgroundRect = background.GetComponent<RectTransform>();
            backgroundRect.anchorMin = new Vector2(0.0f, 1.0f);
            backgroundRect.anchorMax = new Vector2(0.0f, 1.0f);
            backgroundRect.pivot = new Vector2(0.0f, 1.0f);
            backgroundRect.anchoredPosition = new Vector2(12.0f, -72.0f); // below the top bar + progress bar
            backgroundRect.sizeDelta = new Vector2(420.0f, 78.0f);

            m_InfoLabel = CreateLabel(background.transform, string.Empty, 18.0f, TextAlignmentOptions.TopLeft, false);
            RectTransform rect = m_InfoLabel.rectTransform;
            Stretch(rect);
            rect.offsetMin = new Vector2(10.0f, 8.0f);
            rect.offsetMax = new Vector2(-10.0f, -8.0f);
            m_InfoLabel.color = new Color(1.0f, 1.0f, 1.0f, 0.85f);
        }

        private void BuildProgressBar(Transform parent)
        {
            GameObject container = CreateChild("ProgressBar", parent);
            RectTransform rect = container.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.0f, 1.0f);
            rect.anchorMax = new Vector2(1.0f, 1.0f);
            rect.pivot = new Vector2(0.5f, 1.0f);
            rect.sizeDelta = new Vector2(0.0f, 8.0f);
            rect.anchoredPosition = new Vector2(0.0f, -56.0f); // just under the top bar
            container.AddComponent<Image>().color = m_TrackColor;

            GameObject fillGo = CreateImage(container.transform, "Fill", m_FillColor);
            Stretch(fillGo.GetComponent<RectTransform>());
            Image fill = fillGo.GetComponent<Image>();
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 0.0f;

            m_ProgressFill = fill;
            m_ProgressBar = container;
            container.SetActive(false);
        }

        private void BuildTopBar(Transform parent)
        {
            GameObject bar = CreateBar("TopBar", parent, true);
            CreateButton(bar.transform, "Load PMX", 120.0f, () => m_Controller.LoadPMX());
            m_RuntimePhysicsButton = CreateButton(bar.transform, "Runtime Physics: On", 190.0f, () => m_Controller.SetRuntimePhysics(!m_Controller.runtimePhysics));
            m_RuntimePhysicsLabel = m_RuntimePhysicsButton.GetComponentInChildren<TextMeshProUGUI>();
            m_BakePhysicsButton = CreateButton(bar.transform, "Bake Physics: Off", 180.0f, () => m_Controller.SetBakePhysics(!m_Controller.bakePhysics));
            m_BakePhysicsLabel = m_BakePhysicsButton.GetComponentInChildren<TextMeshProUGUI>();
            m_SDEFButton = CreateButton(bar.transform, "SDEF: On", 120.0f, () => m_Controller.SetSDEF(!m_Controller.sdefSkinning));
            m_SDEFLabel = m_SDEFButton.GetComponentInChildren<TextMeshProUGUI>();
            CreateButton(bar.transform, "Load Motion", 120.0f, () => m_Controller.LoadMotion());
            CreateButton(bar.transform, "Load Camera", 120.0f, () => m_Controller.LoadCamera());
            m_ResetAnimButton = CreateButton(bar.transform, "Reset Anim", 120.0f, () => m_Controller.ResetAnimation());
            m_CameraButton = CreateButton(bar.transform, "Camera: User", 150.0f, () => m_Controller.SetCameraMode(!m_Controller.vmdCameraMode));
            m_CameraLabel = m_CameraButton.GetComponentInChildren<TextMeshProUGUI>();
            CreateButton(bar.transform, "Reset View", 120.0f, () => m_Controller.ResetView());
            CreateButton(bar.transform, "Hide UI (H)", 130.0f, ToggleUI);

            m_StatusLabel = CreateLabel(bar.transform, "Load a PMX model to begin.", 18.0f, TextAlignmentOptions.MidlineLeft, false);
            LayoutElement statusLayout = m_StatusLabel.gameObject.AddComponent<LayoutElement>();
            statusLayout.flexibleWidth = 1.0f;
            statusLayout.minWidth = 200.0f;
        }

        private void BuildBottomBar(Transform parent)
        {
            GameObject bar = CreateBar("BottomBar", parent, false);

            m_RewindButton = CreateButton(bar.transform, "Rewind", 90.0f, () => m_Controller.ResetPlayback());
            m_PlayPauseButton = CreateButton(bar.transform, "Play", 90.0f, () => m_Controller.player?.TogglePlay());
            m_PlayPauseLabel = m_PlayPauseButton.GetComponentInChildren<TextMeshProUGUI>();

            m_Slider = CreateSlider(bar.transform);
            LayoutElement sliderLayout = m_Slider.gameObject.AddComponent<LayoutElement>();
            sliderLayout.flexibleWidth = 1.0f;
            sliderLayout.minWidth = 200.0f;
            sliderLayout.preferredHeight = 24.0f;

            m_FrameInput = CreateFrameInput(bar.transform);
            m_FrameTotalLabel = CreateLabel(bar.transform, "/ 0", 18.0f, TextAlignmentOptions.MidlineLeft, false);
            LayoutElement totalLayout = m_FrameTotalLabel.gameObject.AddComponent<LayoutElement>();
            totalLayout.preferredWidth = 70.0f;
        }

        private void BuildHint(Transform parent)
        {
            TextMeshProUGUI hint = CreateLabel(parent, "H: toggle UI", 16.0f, TextAlignmentOptions.BottomRight, false);
            RectTransform rect = hint.rectTransform;
            rect.anchorMin = new Vector2(1.0f, 0.0f);
            rect.anchorMax = new Vector2(1.0f, 0.0f);
            rect.pivot = new Vector2(1.0f, 0.0f);
            rect.anchoredPosition = new Vector2(-12.0f, 8.0f);
            rect.sizeDelta = new Vector2(160.0f, 24.0f);
            hint.color = new Color(1.0f, 1.0f, 1.0f, 0.6f);
        }

        private Canvas GetAttachedCanvas()
        {
            Canvas canvas = GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = GetComponentInParent<Canvas>();
            }
            if (canvas == null)
            {
                Debug.LogError("ShowcaseUI requires a Canvas on its GameObject (or a parent). Attach it to your scene Canvas.", this);
                return null;
            }
            // Buttons need a raycaster to receive clicks.
            if (canvas.GetComponent<GraphicRaycaster>() == null)
            {
                canvas.gameObject.AddComponent<GraphicRaycaster>();
            }
            return canvas;
        }

        private GameObject CreateBar(string name, Transform parent, bool top)
        {
            GameObject bar = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
            bar.transform.SetParent(parent, false);
            bar.GetComponent<Image>().color = m_BarColor;

            RectTransform rect = bar.GetComponent<RectTransform>();
            rect.anchorMin = top ? new Vector2(0.0f, 1.0f) : new Vector2(0.0f, 0.0f);
            rect.anchorMax = new Vector2(1.0f, top ? 1.0f : 0.0f);
            rect.pivot = new Vector2(0.5f, top ? 1.0f : 0.0f);
            rect.sizeDelta = new Vector2(0.0f, top ? 56.0f : 64.0f);
            rect.anchoredPosition = Vector2.zero;

            HorizontalLayoutGroup layout = bar.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 8, 8);
            layout.spacing = 8.0f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            return bar;
        }

        private Button CreateButton(Transform parent, string label, float width, UnityEngine.Events.UnityAction onClick)
        {
            GameObject go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = m_ButtonColor;

            Button button = go.GetComponent<Button>();
            button.onClick.AddListener(onClick);

            LayoutElement layout = go.GetComponent<LayoutElement>();
            layout.preferredWidth = width;
            layout.preferredHeight = 40.0f;

            CreateLabel(go.transform, label, 18.0f, TextAlignmentOptions.Center, true);
            return button;
        }

        private TextMeshProUGUI CreateLabel(Transform parent, string text, float fontSize, TextAlignmentOptions alignment, bool stretch)
        {
            GameObject go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = m_TextColor;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;

            if (stretch)
            {
                Stretch(go.GetComponent<RectTransform>());
            }
            return label;
        }

        private Slider CreateSlider(Transform parent)
        {
            GameObject go = new GameObject("ScrubSlider", typeof(RectTransform), typeof(Slider));
            go.transform.SetParent(parent, false);
            Slider slider = go.GetComponent<Slider>();

            GameObject background = CreateImage(go.transform, "Background", m_TrackColor);
            RectTransform backgroundRect = background.GetComponent<RectTransform>();
            backgroundRect.anchorMin = new Vector2(0.0f, 0.35f);
            backgroundRect.anchorMax = new Vector2(1.0f, 0.65f);
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;

            GameObject fillArea = CreateChild("Fill Area", go.transform);
            RectTransform fillAreaRect = fillArea.GetComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0.0f, 0.35f);
            fillAreaRect.anchorMax = new Vector2(1.0f, 0.65f);
            fillAreaRect.offsetMin = new Vector2(5.0f, 0.0f);
            fillAreaRect.offsetMax = new Vector2(-15.0f, 0.0f);

            GameObject fill = CreateImage(fillArea.transform, "Fill", m_FillColor);
            RectTransform fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0.0f, 0.0f);
            fillRect.anchorMax = new Vector2(1.0f, 1.0f);
            fillRect.sizeDelta = new Vector2(10.0f, 0.0f);

            GameObject handleArea = CreateChild("Handle Slide Area", go.transform);
            RectTransform handleAreaRect = handleArea.GetComponent<RectTransform>();
            handleAreaRect.anchorMin = new Vector2(0.0f, 0.0f);
            handleAreaRect.anchorMax = new Vector2(1.0f, 1.0f);
            handleAreaRect.offsetMin = new Vector2(10.0f, 0.0f);
            handleAreaRect.offsetMax = new Vector2(-10.0f, 0.0f);

            GameObject handle = CreateImage(handleArea.transform, "Handle", m_HandleColor);
            RectTransform handleRect = handle.GetComponent<RectTransform>();
            handleRect.anchorMin = new Vector2(0.0f, 0.0f);
            handleRect.anchorMax = new Vector2(0.0f, 1.0f);
            handleRect.sizeDelta = new Vector2(16.0f, 0.0f);

            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0.0f;
            slider.maxValue = 1.0f;
            slider.value = 0.0f;
            slider.onValueChanged.AddListener(OnSliderChanged);

            AddPointerTrigger(go, EventTriggerType.PointerDown, OnSliderPointerDown);
            AddPointerTrigger(go, EventTriggerType.PointerUp, OnSliderPointerUp);
            return slider;
        }

        private TMP_InputField CreateFrameInput(Transform parent)
        {
            GameObject go = new GameObject("FrameInput", typeof(RectTransform), typeof(Image), typeof(TMP_InputField), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = m_ButtonColor;

            LayoutElement layout = go.GetComponent<LayoutElement>();
            layout.preferredWidth = 70.0f;
            layout.preferredHeight = 36.0f;

            GameObject textArea = CreateChild("Text Area", go.transform);
            RectTransform textAreaRect = textArea.GetComponent<RectTransform>();
            textAreaRect.anchorMin = Vector2.zero;
            textAreaRect.anchorMax = Vector2.one;
            textAreaRect.offsetMin = new Vector2(6.0f, 2.0f);
            textAreaRect.offsetMax = new Vector2(-6.0f, -2.0f);
            textArea.AddComponent<RectMask2D>();

            TextMeshProUGUI text = CreateLabel(textArea.transform, "0", 18.0f, TextAlignmentOptions.MidlineLeft, true);

            TMP_InputField input = go.GetComponent<TMP_InputField>();
            input.textViewport = textAreaRect;
            input.textComponent = text;
            input.contentType = TMP_InputField.ContentType.IntegerNumber;
            input.text = "0";
            input.onEndEdit.AddListener(OnFrameEntered);
            return input;
        }

        private void OnSliderChanged(float value)
        {
            if (m_Scrubbing)
            {
                m_Controller.Seek(value);
            }
        }

        private void OnSliderPointerDown(BaseEventData _)
        {
            m_Scrubbing = true;
            m_Controller.player?.Pause();
            if (m_Slider != null)
            {
                m_Controller.Seek(m_Slider.value);
            }
        }

        private void OnSliderPointerUp(BaseEventData _)
        {
            m_Scrubbing = false;
        }

        private void OnFrameEntered(string value)
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int frame))
            {
                m_Controller.SeekFrame(frame);
            }
        }

        private static void AddPointerTrigger(GameObject target, EventTriggerType type, System.Action<BaseEventData> callback)
        {
            EventTrigger trigger = target.GetComponent<EventTrigger>();
            if (trigger == null)
            {
                trigger = target.AddComponent<EventTrigger>();
            }
            EventTrigger.Entry entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(data => callback(data));
            trigger.triggers.Add(entry);
        }

        private GameObject CreateImage(Transform parent, string name, Color color)
        {
            GameObject go = CreateChild(name, parent);
            Image image = go.AddComponent<Image>();
            image.color = color;
            image.sprite = GetWhiteSprite(); // a sprite is required for Image.Type.Filled (the progress fill) to work
            return go;
        }

        private Sprite GetWhiteSprite()
        {
            if (m_WhiteSprite == null)
            {
                Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                };
                texture.SetPixel(0, 0, Color.white);
                texture.Apply();
                m_WhiteSprite = Sprite.Create(texture, new Rect(0.0f, 0.0f, 1.0f, 1.0f), new Vector2(0.5f, 0.5f), 100.0f);
            }
            return m_WhiteSprite;
        }

        private static GameObject CreateChild(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }
            GameObject go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }
    }
}
