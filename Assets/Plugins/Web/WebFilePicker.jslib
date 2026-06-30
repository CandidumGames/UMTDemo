mergeInto(LibraryManager.library, {
    // Opens a single-file picker filtered to one extension, writes the chosen file into MEMFS under baseDir, and
    // reports back its filename. Self-contained (no IndexedDB / netherlands3d sync), with the same focus-based cancel
    // detection as the folder picker so cancelling never leaves the C# Task pending (which would block reopening).
    //
    // baseDirPtr:     MEMFS base directory to write the file under (Application.persistentDataPath)
    // extensionPtr:   accepted extension WITHOUT a dot, e.g. "vmd"
    // callbackObj:    GameObject name to SendMessage on completion
    // okMethodPtr:    method called on success (arg: the chosen file's name)
    // failMethodPtr:  method called on failure/cancel (arg: short reason string)
    WebFilePickerPickFile: function (baseDirPtr, extensionPtr, callbackObjPtr, okMethodPtr, failMethodPtr) {
        var baseDir = UTF8ToString(baseDirPtr);
        var extension = UTF8ToString(extensionPtr);
        var callbackObj = UTF8ToString(callbackObjPtr);
        var okMethod = UTF8ToString(okMethodPtr);
        var failMethod = UTF8ToString(failMethodPtr);

        var settled = false;
        function fail(reason) {
            if (settled) { return; }
            settled = true;
            if (reason !== "cancelled") {
                console.error("WebFilePickerPickFile: " + reason);
            }
            console.log("[WebFilePicker] file fail(" + reason + ")");
            SendMessage(callbackObj, failMethod, reason);
        }
        function succeed(fileName) {
            if (settled) { return; }
            settled = true;
            console.log("[WebFilePicker] file succeed -> SendMessage(" + callbackObj + ", " + okMethod + ", " + fileName + ")");
            SendMessage(callbackObj, okMethod, fileName);
        }

        try {
            var inputId = "WebFilePickerFileInput_" + extension;
            var existing = document.getElementById(inputId);
            if (existing) {
                document.body.removeChild(existing);
            }

            var input = document.createElement("input");
            input.id = inputId;
            input.type = "file";
            input.accept = "." + extension.replace(".", "");
            input.style.cssText = "display:none;";

            // Cancel detection via the input's 'cancel' event  reliable and never races onchange (see folder picker).
            input.oncancel = function () {
                fail("cancelled");
            };

            input.onchange = function () {
                var files = input.files;
                console.log("[WebFilePicker] file onchange: " + (files ? files.length : 0) + " files");
                if (!files || files.length === 0) {
                    fail("cancelled");
                    return;
                }

                var file = files[0];
                var reader = new FileReader();
                reader.onload = function (e) {
                    try {
                        FS.mkdirTree(baseDir);
                        FS.writeFile(baseDir + "/" + file.name, new Uint8Array(e.target.result), { canOwn: true });
                    } catch (e2) {
                        fail("FS.writeFile error: " + e2);
                        return;
                    }
                    succeed(file.name);
                };
                reader.onerror = function () { fail("read error"); };
                reader.readAsArrayBuffer(file);
            };

            document.body.appendChild(input);

            // Click synchronously: this call runs inside the Unity UI button's click handler, which is a live user
            // gesture, so the browser allows opening the dialog. Deferring to a later mouseup would miss the gesture
            // that triggered us  the dialog wouldn't open until the user clicked somewhere else.
            input.click();
        } catch (e) {
            fail("exception: " + e);
        }
    },

    // Opens a directory picker (webkitdirectory), writes every selected file into MEMFS preserving its relative path
    // under baseDir, and reports back the relative path of the first .pmx found. PMX models reference sibling textures
    // by relative path, which a single-file picker can't supply  picking the whole model folder brings them along.
    //
    // baseDirPtr:     MEMFS base directory to write files under, e.g. "/idbfs/<hash>" (Application.persistentDataPath)
    // callbackObj:    GameObject name to SendMessage on completion
    // okMethodPtr:    method called on success (arg: relative path of the chosen .pmx, e.g. "<folder>/model.pmx")
    // failMethodPtr:  method called on failure/cancel (arg: short reason string)
    WebFilePickerPickFolder: function (baseDirPtr, callbackObjPtr, okMethodPtr, failMethodPtr) {
        var baseDir = UTF8ToString(baseDirPtr);
        var callbackObj = UTF8ToString(callbackObjPtr);
        var okMethod = UTF8ToString(okMethodPtr);
        var failMethod = UTF8ToString(failMethodPtr);

        // Guard so the result is reported exactly once: a folder pick fires onchange, but a CANCEL fires no event at
        // all  we detect that via a one-shot window focus handler. Without this, cancel leaves the C# Task pending
        // forever, and its re-entrancy guard then hands back the same stuck Task on every later click (picker never
        // reopens until a page refresh).
        var settled = false;

        function fail(reason) {
            if (settled) {
                return;
            }
            settled = true;
            if (reason !== "cancelled") {
                console.error("WebFilePickerPickFolder: " + reason);
            }
            SendMessage(callbackObj, failMethod, reason);
        }

        function succeed(pmxRelativePath) {
            if (settled) {
                return;
            }
            settled = true;
            console.log("[WebFilePicker] folder succeed -> SendMessage(" + callbackObj + ", " + okMethod + ", " + pmxRelativePath + ")");
            SendMessage(callbackObj, okMethod, pmxRelativePath);
        }

        try {
            // Replace any prior instance so re-picking always fires onchange.
            var existing = document.getElementById("WebFilePickerFolderInput");
            if (existing) {
                document.body.removeChild(existing);
            }

            var input = document.createElement("input");
            input.id = "WebFilePickerFolderInput";
            input.type = "file";
            input.setAttribute("webkitdirectory", "");
            input.setAttribute("directory", "");
            input.multiple = true;
            input.style.cssText = "display:none;";

            // Cancel detection: the input fires a 'cancel' event when the dialog is dismissed without a selection. This
            // is reliable and never races onchange (unlike a focus timeout, which could expire while the browser's
            // upload-confirmation prompt was still open and wrongly cancel a pick that then arrived via onchange).
            input.oncancel = function () {
                fail("cancelled");
            };

            input.onchange = function () {
                var files = input.files;
                console.log("[WebFilePicker] folder onchange: " + (files ? files.length : 0) + " files");
                if (!files || files.length === 0) {
                    fail("cancelled");
                    return;
                }

                var pending = files.length;
                var pmxRelativePath = null;
                var hadError = false;

                function finish() {
                    console.log("[WebFilePicker] folder finish: pmx=" + pmxRelativePath + " hadError=" + hadError);
                    if (hadError) {
                        return; // fail() already reported
                    }
                    if (!pmxRelativePath) {
                        fail("no .pmx file in the selected folder");
                        return;
                    }
                    succeed(pmxRelativePath);
                }

                var done = 0;
                var finished = false;
                function maybeFinish() {
                    if (!finished && done >= pending) {
                        finished = true;
                        finish();
                    }
                }
                function oneDone() {
                    ++done;
                    maybeFinish();
                }

                // Watchdog: if some FileReader never fires either callback, still finish with whatever was written so
                // the C# side always gets a result and never hangs.
                setTimeout(function () {
                    if (!finished) {
                        console.warn("[WebFilePicker] folder read watchdog fired: " + done + "/" + pending + " files done");
                        finished = true;
                        finish();
                    }
                }, 20000);

                for (var i = 0; i < files.length; i++) {
                    (function (file) {
                        // webkitRelativePath is "<pickedFolder>/<...>/<name>"; fall back to the bare name if absent.
                        var rel = file.webkitRelativePath || file.name;
                        if (/\.pmx$/i.test(rel) && pmxRelativePath === null) {
                            pmxRelativePath = rel;
                        }

                        var reader = new FileReader();
                        reader.onload = function (e) {
                            try {
                                var fullPath = baseDir + "/" + rel;
                                var slash = fullPath.lastIndexOf("/");
                                if (slash > 0) {
                                    FS.mkdirTree(fullPath.substring(0, slash));
                                }
                                FS.writeFile(fullPath, new Uint8Array(e.target.result), { canOwn: true });
                            } catch (e2) {
                                if (!hadError) {
                                    hadError = true;
                                    fail("FS.writeFile error for " + rel + ": " + e2);
                                }
                            }
                            oneDone();
                        };
                        reader.onerror = function () {
                            console.warn("[WebFilePicker] read error for " + rel + ", skipping");
                            oneDone();
                        };
                        reader.readAsArrayBuffer(file);
                    })(files[i]);
                }
            };

            document.body.appendChild(input);

            // Click synchronously: this runs inside the Unity UI button's click handler (a live user gesture), so the
            // browser allows opening the dialog. Deferring to a later mouseup would miss the gesture that triggered us.
            input.click();
        } catch (e) {
            fail("exception: " + e);
        }
    }
});
