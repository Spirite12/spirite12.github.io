using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using Debug = UnityEngine.Debug;
using FileUtil = Utility.FileUtil;

public class AnimationOpEditor : EditorWindow {
    [MenuItem("Assets/Optimize/Animation", false, 100)]
    public static void WinProject() {
        Rect rect = new Rect(0, 0, 400, 200);
        AnimationOpEditor window = (AnimationOpEditor)EditorWindow.GetWindowWithRect(typeof(AnimationOpEditor), rect, true, "Animation Optimize");
        window.Init();
        window.Show();
    }

    [MenuItem("Assets/Optimize/Animation", true, 100)]
    public static bool WinProjectValidate() {
        selectObject = Selection.activeObject;
        currentPath = AssetDatabase.GetAssetPath(selectObject);
        bool isAnim = Path.GetExtension(currentPath) == ".anim";
        bool isFolderPath = !Path.HasExtension(currentPath);
        enumPopEnum = isFolderPath ? EnumPopEnum.Folder : EnumPopEnum.Anim; 
        return isAnim || isFolderPath;
    }

    private void OnGUI() {
        GUILayout.Space(12);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Select Type:", GUILayout.Width(150));
        enumPopEnum = (EnumPopEnum)EditorGUILayout.EnumPopup(enumPopEnum);
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(12);
        if (enumPopEnum == EnumPopEnum.Anim) {
            animationClip = EditorGUILayout.ObjectField("Select Anim :", selectObject, typeof(AnimationClip), false) as AnimationClip;
            selectObject = animationClip;
        } else {
            currentPath = EditorGUILayout.TextField("Select FilePath :", currentPath);
        }

        GUILayout.Space(12);
        EditorGUILayout.LabelField("Optimize Option", GUILayout.Width(150));
        EditorGUILayout.BeginHorizontal();
        isScale = GUILayout.Toggle(isScale, new GUIContent("Scale", "Delete Scale key frames,Careful operation \n剔除Scale关键帧，谨慎操作"));
        isFloat = GUILayout.Toggle(isFloat, new GUIContent("Float F2", "The decimal point is accurate to two digits \n浮点小数点精确到2位"));
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(12);
        EditorGUILayout.LabelField("Other Setting", GUILayout.Width(150));
        if (enumPopEnum == EnumPopEnum.Folder) {
            isOutPut = GUILayout.Toggle(isOutPut, new GUIContent("Export File", "Is Export Optimize Content To File \n是否导出优化内容到文件内"));
        }
        else {
            isOutPut = GUILayout.Toggle(isOutPut, new GUIContent("Export Console", "Is Export Optimize Content To Console \n是否导出优化内容到控制台内"));
        }

        GUILayout.Space(10);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Start")) {
            StartDealClip();
        }
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>
    /// 开始处理动效
    /// </summary>
    private void StartDealClip() {
        if (enumPopEnum == EnumPopEnum.Anim) {
            if (isOutPut) {
                currentPath = AssetDatabase.GetAssetPath(selectObject);
                string outConsole = GetOpClipOutContent(animationClip);
                Debug.LogWarning(outConsole);
                return;
            }
            OptimizeAnimationClip(animationClip);
        } else if (enumPopEnum == EnumPopEnum.Folder) {
            DirectoryInfo direction = new DirectoryInfo(currentPath);
            FileInfo[] files = direction.GetFiles("*", SearchOption.AllDirectories);
            string outConsole = "";
            string originPath = currentPath;
            foreach (var file in files) {
                if (file.Name.EndsWith(".anim")) {
                    currentPath = originPath + "/" + file.Name;
                    animationClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(currentPath);
                    outConsole += GetOpClipOutContent(animationClip);
                }
            }
            currentPath = originPath;
            var time = DateTime.Now.ToUniversalTime().ToString("yyyyMMddHHmmss");
            var Path = "\\Record\\AnimationOp\\";
            var filePath = Path + $"{time}.txt";
            FileUtil.WriteFile(filePath, outConsole);
            Thread newThread = new Thread(CmdOpenDirectory);
            newThread.Start(FileUtil.GetPrefixPath() + Path);
        }
    }

    /// <summary>
    /// 获取输出内容
    /// </summary>
    /// <param name="clip"></param>
    /// <returns></returns>
    private string GetOpClipOutContent(AnimationClip clip) {
        string outConsole = "Animation Clip : " + clip.name + "\n";
        outConsole += LogInfo("Optimize Before :\n");
        string opContent = OptimizeAnimationClip(animationClip);
        outConsole += LogInfo("Optimize After :\n");
        if (opContent.Length > 0) {
            outConsole += "\n" + opContent + "\n";
        }
        outConsole += "==========================================\n";
        return outConsole;
    }

    /// <summary>
    /// 处理动效
    /// </summary>
    private string OptimizeAnimationClip(AnimationClip clip) {
        string opInfo = "";

        void DetailInfo(ref bool hasDeal) {
            if (hasDeal) {
                return;
            }

            hasDeal = true;
            opInfo += "Detail Info : \n  " + clip.name;
        }

        try {
            var curves = AnimationUtility.GetCurveBindings(clip);
            bool hasDeal = false;
            foreach (var curveData in curves) {
                var curve = AnimationUtility.GetEditorCurve(clip, curveData);
                if (curve?.keys == null) {
                    continue;
                }

                if (curve.keys.Length <= 0) {
                    DetailInfo(ref hasDeal);
                    opInfo += "\n\t -- delete";
                    AnimationUtility.SetEditorCurve(clip, curveData, null);
                    continue;
                }

                // 处理小数点
                if (isFloat) {
                    var keys = curve.keys;
                    for (int i = 0; i < keys.Length; i++) {
                        var key = keys[i];
                        key.value = float.Parse(key.value.ToString("f2"));
                        key.inTangent = float.Parse(key.inTangent.ToString("f2"));
                        key.outTangent = float.Parse(key.outTangent.ToString("f2"));
                        keys[i] = key;
                    }

                    curve.keys = keys;
                    clip.SetCurve(curveData.path, curveData.type, curveData.propertyName, curve);
                }

                // 处理Scale曲线
                if (isScale) {
                    var name = curveData.propertyName.ToLower();
                    if (name.Contains("scale")) {
                        DetailInfo(ref hasDeal);
                        opInfo += "\n\t -- delete scale : " + curveData.propertyName;
                        AnimationUtility.SetEditorCurve(clip, curveData, null);
                    }
                }
            }
        } catch (Exception e) {
            Debug.LogError($"CompressAnimationClip Failed !!! animationPath : {clip.name} error: {e}");
            throw;
        }

        return opInfo;
    }

    private void Init() {
        Assembly asm = Assembly.GetAssembly(typeof(UnityEditor.Editor));
        getClipStats = typeof(AnimationUtility).GetMethod("GetAnimationClipStats", BindingFlags.Static | BindingFlags.NonPublic);
        Type aniclipstats = asm.GetType("UnityEditor.AnimationClipStats");
        sizeInfo = aniclipstats.GetField("size", BindingFlags.Public | BindingFlags.Instance);
    }

    private string LogInfo(string title) {
        var fileSize = new FileInfo(currentPath).Length;
        var memorySize = Profiler.GetRuntimeMemorySizeLong(animationClip);
        var stats = getClipStats.Invoke(null, new object[] { animationClip });
        var inspectorSize = (int) sizeInfo.GetValue(stats);
        return $"{title} FileSize：{fileSize}, MemorySize：{memorySize}, InspectorSize：{inspectorSize}\n";
    }

    /// <summary>
    /// 打开文件夹
    /// </summary>
    /// <param name="obj"></param>
    private static void CmdOpenDirectory(object obj) {
        Process p = new Process {
            StartInfo = {
                FileName = "cmd.exe",
                Arguments = "/c start " + obj.ToString(),
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        p.Start();
        p.WaitForExit();
        p.Close();
    }

    private enum EnumPopEnum {
        Anim,
        Folder
    }
    /// <summary>
    /// 当前选择的弹窗类型
    /// </summary>
    private static EnumPopEnum enumPopEnum;
    /// <summary>
    /// 当前文件路径
    /// </summary>
    private static string currentPath;

    private static UnityEngine.Object selectObject;
    private MethodInfo getClipStats;
    private FieldInfo sizeInfo;
    private AnimationClip animationClip;

    private bool isScale = false;
    private bool isFloat = true;
    private bool isOutPut = true;
}