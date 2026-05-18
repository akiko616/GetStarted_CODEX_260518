#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class DefineSwitcher
{
    // 제어할 심볼 이름들
    private const string DEV_MODE = "DEV_MODE";
    private const string GAMEINSTANCE = "GAMEINSTANCE";
    private const string DRILLSERGEANT = "DrillSergeant";

    // 1. 훈련생(클라이언트) 모드 버튼
    [MenuItem("Build Mode/1.Trainee (Dev) Mode", false, 1)]
    public static void SetTraineeMode()
    {
        // DEV_MODE 켜기, GAMEINSTANCE 끄기
        UpdateDefines(enableDevMode: true);
        Debug.Log("<color=cyan>[모드 전환] 훈련생(Client) 모드로 변경되었습니다. (DEV_MODE 활성화)</color>");
    }

    // 2. 훈련생용 클라이언트 빌드(exe) 뽑을 때
    [MenuItem("Build Mode/2.Trainee (Build) Mode", false, 2)]
    public static void SetTraineeReleaseMode()
    {
        UpdateDefines();
        Debug.Log("<color=cyan>[모드 전환] 훈련생 빌드 모드 (DEV_MODE: OFF / GAMEINSTANCE: OFF)</color>");
    }

    // 3. 교관석용 클라이언트 빌드(exe) 뽑을 때
    [MenuItem("Build Mode/4.Instructor (Build) Mode", false, 3)]
    public static void SetInstructorReleaseMode()
    {
        UpdateDefines(enableDevMode: false, enableGameInstance: false, enableDrillSergeant: true);
        Debug.Log("<color=yellow>[모드 전환] 교관석 빌드 모드 (DRILL_SERGEANT 활성화)</color>");
    }

    // 4. 데디케이티드 서버 모드 버튼
    [MenuItem("Build Mode/4.Server (GameInstance) Mode", false, 4)]
    public static void SetServerMode()
    {
        // DEV_MODE 끄기, GAMEINSTANCE 켜기
        UpdateDefines(enableDevMode: false, enableGameInstance: true);
        Debug.Log("<color=green>[모드 전환] 서버 모드로 변경되었습니다. (GAMEINSTANCE 활성화)</color>");
    }

    // 핵심 로직: 기존 심볼을 보존하면서 원하는 것만 교체
    private static void UpdateDefines(bool enableDevMode = false, bool enableGameInstance = false, bool enableDrillSergeant = false)
    {
        NamedBuildTarget buildTarget = NamedBuildTarget.Standalone;
        string currentDefines = PlayerSettings.GetScriptingDefineSymbols(buildTarget);
        List<string> defineList = currentDefines.Split(';').ToList();

        // 💡 헬퍼 함수를 사용하여 코드 가독성 극대화
        ToggleDefine(defineList, DEV_MODE, enableDevMode);
        ToggleDefine(defineList, GAMEINSTANCE, enableGameInstance);
        ToggleDefine(defineList, DRILLSERGEANT, enableDrillSergeant);

        string newDefines = string.Join(";", defineList);
        PlayerSettings.SetScriptingDefineSymbols(buildTarget, newDefines);
    }

    private static void ToggleDefine(List<string> defineList, string defineName, bool enable)
    {
        if (enable && !defineList.Contains(defineName))
            defineList.Add(defineName);
        else if (!enable && defineList.Contains(defineName))
            defineList.Remove(defineName);
    }
}
#endif
