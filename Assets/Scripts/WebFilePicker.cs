using System;
using System.IO;
using System.Threading.Tasks;
using SFB;
using UnityEngine;

namespace UMTDemo
{
    /// <summary>
    /// Picks files across platforms and returns paths the runtime can read with <see cref="File"/>.
    /// On Editor/Standalone this wraps the synchronous <see cref="StandaloneFileBrowser.OpenFilePanel"/>. On the Web
    /// platform that API is unavailable (the browser exposes no filesystem and file dialogs are user-gesture/callback
    /// driven), so a self-contained jslib (<c>Plugins/WebGL/WebFilePicker.jslib</c>) injects a hidden
    /// <c>&lt;input type=file&gt;</c>, writes the chosen file(s) into the MEMFS under
    /// <see cref="Application.persistentDataPath"/>, and reports their names back  which we resolve to absolute paths.
    /// Callers <c>await</c> the returned task; empty string means the dialog was cancelled.
    /// </summary>
    public static class WebFilePicker
    {
#if !UNITY_EDITOR && UNITY_WEBGL
        // One hidden file input per file extension, keyed by extension. Each is a GameObject named so the jslib can
        // SendMessage its callbacks back to the WebFileInput component that owns it.
        private static readonly System.Collections.Generic.Dictionary<string, WebFileInput> s_Inputs =
            new System.Collections.Generic.Dictionary<string, WebFileInput>();
        private static WebFolderInput s_FolderInput;
#endif

        /// <summary>
        /// Opens a native file dialog filtered to a single extension and resolves with the absolute path of the chosen
        /// file, or an empty string if the user cancelled.
        /// </summary>
        /// <param name="title">Dialog title (used on standalone; ignored by the browser file picker).</param>
        /// <param name="extension">File extension without the dot, for example <c>"pmx"</c> or <c>"vmd"</c>.</param>
        public static Task<string> PickAsync(string title, string extension)
        {
#if !UNITY_EDITOR && UNITY_WEBGL
            return PickWebAsync(extension);
#else
            string[] paths = StandaloneFileBrowser.OpenFilePanel(
                title,
                string.Empty,
                new[] { new ExtensionFilter(extension.ToUpperInvariant(), extension) },
                false);

            string path = paths != null && paths.Length > 0 ? paths[0] : string.Empty;
            return Task.FromResult(string.IsNullOrEmpty(path) ? string.Empty : path);
#endif
        }

        /// <summary>
        /// Picks a PMX model together with its sibling resources and resolves with the absolute path of the chosen
        /// <c>.pmx</c>, or an empty string if cancelled or none was found.
        /// </summary>
        /// <remarks>
        /// On the Web platform a single-file picker can't supply the sibling <c>textures/</c> folder PMX models
        /// reference by relative path, so this opens a directory picker (<c>webkitdirectory</c>); every selected file is
        /// written into the MEMFS under <see cref="Application.persistentDataPath"/> preserving its relative path, so the
        /// importer's texture resolver finds them. On Editor/Standalone it falls back to a normal single-file dialog,
        /// since those platforms already have the real folder on disk.
        /// </remarks>
        /// <param name="title">Dialog title (used on standalone; ignored by the browser folder picker).</param>
        public static Task<string> PickFolderForPMXAsync(string title)
        {
#if !UNITY_EDITOR && UNITY_WEBGL
            if (s_FolderInput == null)
            {
                GameObject go = new GameObject("WebFolderInput");
                UnityEngine.Object.DontDestroyOnLoad(go);
                s_FolderInput = go.AddComponent<WebFolderInput>();
            }

            return s_FolderInput.PickAsync();
#else
            return PickAsync(title, "pmx");
#endif
        }

#if !UNITY_EDITOR && UNITY_WEBGL
        private static Task<string> PickWebAsync(string extension)
        {
            if (!s_Inputs.TryGetValue(extension, out WebFileInput input) || input == null)
            {
                GameObject go = new GameObject($"WebFileInput_{extension}");
                UnityEngine.Object.DontDestroyOnLoad(go);
                input = go.AddComponent<WebFileInput>();
                input.Initialize(extension);
                s_Inputs[extension] = input;
            }

            return input.PickAsync();
        }
#endif
    }

#if !UNITY_EDITOR && UNITY_WEBGL
    /// <summary>
    /// Drives a hidden single-file browser input for one extension and bridges its asynchronous callbacks into an
    /// awaitable <see cref="Task{String}"/>. The jslib writes the chosen file into MEMFS under persistentDataPath and
    /// reports its name; the resolved string is the file's absolute path, or empty on cancel/failure.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WebFileInput : MonoBehaviour
    {
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void WebFilePickerPickFile(string baseDir, string extension, string callbackObject, string okMethod, string failMethod);

        private string m_Extension;
        private TaskCompletionSource<string> m_Pending;

        public void Initialize(string extension)
        {
            m_Extension = extension;

            // The jslib addresses callbacks by GameObject name; keep it stable and unique.
            name = $"WebFileInput_{extension}_{GetInstanceID()}";
        }

        public Task<string> PickAsync()
        {
            // A picker that's already open wins; ignore re-entrant requests for the same extension.
            if (m_Pending != null && !m_Pending.Task.IsCompleted)
            {
                return m_Pending.Task;
            }

            m_Pending = new TaskCompletionSource<string>();
            WebFilePickerPickFile(Application.persistentDataPath, m_Extension, name, nameof(OnFilePicked), nameof(OnFileFailed));
            return m_Pending.Task;
        }

        /// <summary>Called from JavaScript with the chosen file's name once it is written to MEMFS.</summary>
        public void OnFilePicked(string fileName)
        {
            Debug.Log($"WebFileInput.OnFilePicked('{fileName}') pending={(m_Pending != null && !m_Pending.Task.IsCompleted)}");
            if (m_Pending == null || m_Pending.Task.IsCompleted)
            {
                return;
            }

            string fullPath = Path.Combine(Application.persistentDataPath, fileName);
            bool exists = File.Exists(fullPath);
            Debug.Log($"WebFileInput.OnFilePicked resolve: fullPath='{fullPath}' exists={exists}");
            if (exists)
            {
                m_Pending.TrySetResult(fullPath);
            }
            else
            {
                Debug.LogError($"WebFileInput: chosen file not found on the filesystem: {fullPath}");
                m_Pending.TrySetResult(string.Empty);
            }
        }

        /// <summary>Called from JavaScript when the file pick is cancelled or fails; argument is a short reason.</summary>
        public void OnFileFailed(string reason)
        {
            if (m_Pending == null || m_Pending.Task.IsCompleted)
            {
                return;
            }

            // "cancelled" is the normal no-selection case; only log genuine failures as errors.
            if (!string.Equals(reason, "cancelled", StringComparison.Ordinal))
            {
                Debug.LogError($"WebFileInput: file pick failed: {reason}");
            }
            m_Pending.TrySetResult(string.Empty);
        }
    }

    /// <summary>
    /// Drives the browser directory picker (<c>webkitdirectory</c>) and bridges its asynchronous callbacks into an
    /// awaitable <see cref="Task{String}"/>. All selected files are written into the MEMFS under persistentDataPath by
    /// the jslib; the resolved string is the absolute path of the chosen <c>.pmx</c>, or empty on cancel/failure.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WebFolderInput : MonoBehaviour
    {
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void WebFilePickerPickFolder(string baseDir, string callbackObject, string okMethod, string failMethod);

        private TaskCompletionSource<string> m_Pending;

        private void Awake()
        {
            // The jslib addresses callbacks by GameObject name; keep it stable and unique.
            name = $"WebFolderInput_{GetInstanceID()}";
        }

        public Task<string> PickAsync()
        {
            // A picker that's already open wins; ignore re-entrant requests.
            if (m_Pending != null && !m_Pending.Task.IsCompleted)
            {
                return m_Pending.Task;
            }

            m_Pending = new TaskCompletionSource<string>();
            WebFilePickerPickFolder(Application.persistentDataPath, name, nameof(OnFolderPicked), nameof(OnFolderFailed));
            return m_Pending.Task;
        }

        /// <summary>Called from JavaScript with the relative path of the chosen .pmx once all files are written to MEMFS.</summary>
        public void OnFolderPicked(string pmxRelativePath)
        {
            Debug.Log($"WebFolderInput.OnFolderPicked('{pmxRelativePath}') pending={(m_Pending != null && !m_Pending.Task.IsCompleted)}");
            if (m_Pending == null || m_Pending.Task.IsCompleted)
            {
                return;
            }

            string fullPath = Path.Combine(Application.persistentDataPath, pmxRelativePath);
            if (File.Exists(fullPath))
            {
                m_Pending.TrySetResult(fullPath);
            }
            else
            {
                Debug.LogError($"WebFolderInput: chosen .pmx not found on the filesystem: {fullPath}");
                m_Pending.TrySetResult(string.Empty);
            }
        }

        /// <summary>Called from JavaScript when the folder pick is cancelled or fails; argument is a short reason.</summary>
        public void OnFolderFailed(string reason)
        {
            if (m_Pending == null || m_Pending.Task.IsCompleted)
            {
                return;
            }

            // "cancelled" is the normal no-selection case; only log genuine failures as errors.
            if (!string.Equals(reason, "cancelled", StringComparison.Ordinal))
            {
                Debug.LogError($"WebFolderInput: folder pick failed: {reason}");
            }
            m_Pending.TrySetResult(string.Empty);
        }
    }
#endif
}
