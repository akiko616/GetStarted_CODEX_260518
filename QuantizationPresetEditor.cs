#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace ReplaySystem.Compression.Editor
{
    /// <summary>양자화 프리셋 커스텀 에디터입니다.</summary>
    [CustomEditor(typeof(QuantizationPreset))]
    public class QuantizationPresetEditor : UnityEditor.Editor
    {
        private bool showPositionDetails;
        private bool showRotationDetails;
        private bool showScaleDetails;
        private bool showVelocityDetails;
        private bool showAngularVelocityDetails;
        private bool showPreview;

        private SerializedProperty presetNameProp;
        private SerializedProperty descriptionProp;
        private SerializedProperty positionProp;
        private SerializedProperty rotationProp;
        private SerializedProperty scaleProp;
        private SerializedProperty useLogScaleProp;
        private SerializedProperty velocityProp;
        private SerializedProperty angularVelocityProp;

        private void OnEnable()
        {
            presetNameProp = serializedObject.FindProperty("presetName");
            descriptionProp = serializedObject.FindProperty("description");
            positionProp = serializedObject.FindProperty("position");
            rotationProp = serializedObject.FindProperty("rotation");
            scaleProp = serializedObject.FindProperty("scale");
            useLogScaleProp = serializedObject.FindProperty("useLogScale");
            velocityProp = serializedObject.FindProperty("velocity");
            angularVelocityProp = serializedObject.FindProperty("angularVelocity");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var preset = (QuantizationPreset)target;

            DrawHeader();
            DrawBasicInfo();

            EditorGUILayout.Space(10);

            DrawChannelSection("Position", positionProp, ref showPositionDetails, preset.Position, "위치");
            DrawRotationSection(ref showRotationDetails, preset.Rotation);
            DrawScaleSection(ref showScaleDetails, preset.Scale, preset.UseLogScale);
            DrawChannelSection("Velocity", velocityProp, ref showVelocityDetails, preset.Velocity, "속도");
            DrawChannelSection("Angular Velocity", angularVelocityProp, ref showAngularVelocityDetails, preset.AngularVelocity, "각속도");

            EditorGUILayout.Space(10);

            DrawSummary(preset);
            DrawPreview(preset);
            DrawButtons();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("Quantization Preset", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);
        }

        private void DrawBasicInfo()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(presetNameProp, new GUIContent("프리셋 이름"));
            EditorGUILayout.PropertyField(descriptionProp, new GUIContent("설명"));

            EditorGUILayout.EndVertical();
        }

        private void DrawChannelSection(string label, SerializedProperty prop, ref bool foldout, Vector3QuantizationSettings settings, string korLabel)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            foldout = EditorGUILayout.Foldout(foldout, $"{label} ({korLabel})", true);

            if (foldout)
            {
                EditorGUI.indentLevel++;

                var uniformProp = prop.FindPropertyRelative("useUniform");
                EditorGUILayout.PropertyField(uniformProp, new GUIContent("균일 설정"));

                EditorGUILayout.Space(5);

                if (uniformProp.boolValue)
                {
                    DrawAxisSettings(prop.FindPropertyRelative("x"), "All Axes");
                }
                else
                {
                    DrawAxisSettings(prop.FindPropertyRelative("x"), "X");
                    DrawAxisSettings(prop.FindPropertyRelative("y"), "Y");
                    DrawAxisSettings(prop.FindPropertyRelative("z"), "Z");
                }

                EditorGUILayout.Space(5);

                var error = settings.GetChannel(0);
                EditorGUILayout.LabelField($"예상 오차: ±{error.MaxError:F6}", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"총 비트: {settings.TotalBits} bits ({settings.TotalBytes} bytes)", EditorStyles.miniLabel);

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawAxisSettings(SerializedProperty axisProp, string label)
        {
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField(label, GUILayout.Width(60));

            var enabledProp = axisProp.FindPropertyRelative("enabled");
            var minProp = axisProp.FindPropertyRelative("minValue");
            var maxProp = axisProp.FindPropertyRelative("maxValue");
            var bitsProp = axisProp.FindPropertyRelative("bitCount");

            enabledProp.boolValue = EditorGUILayout.Toggle(enabledProp.boolValue, GUILayout.Width(20));

            EditorGUI.BeginDisabledGroup(!enabledProp.boolValue);

            EditorGUILayout.LabelField("Min:", GUILayout.Width(30));
            minProp.floatValue = EditorGUILayout.FloatField(minProp.floatValue, GUILayout.Width(70));

            EditorGUILayout.LabelField("Max:", GUILayout.Width(30));
            maxProp.floatValue = EditorGUILayout.FloatField(maxProp.floatValue, GUILayout.Width(70));

            EditorGUILayout.LabelField("Bits:", GUILayout.Width(30));
            bitsProp.intValue = EditorGUILayout.IntSlider(bitsProp.intValue, 8, 16, GUILayout.Width(100));

            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawRotationSection(ref bool foldout, QuaternionQuantizationSettings settings)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            foldout = EditorGUILayout.Foldout(foldout, "Rotation (회전)", true);

            if (foldout)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.PropertyField(rotationProp.FindPropertyRelative("useSmallest3"), new GUIContent("Smallest-3 사용"));
                EditorGUILayout.PropertyField(rotationProp.FindPropertyRelative("bitsPerComponent"), new GUIContent("컴포넌트당 비트"));

                EditorGUILayout.Space(5);

                EditorGUILayout.LabelField($"예상 각도 오차: ±{settings.MaxAngleError:F2}°", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"총 비트: {settings.TotalBits} bits ({settings.TotalBytes} bytes)", EditorStyles.miniLabel);

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawScaleSection(ref bool foldout, Vector3QuantizationSettings settings, bool useLog)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            foldout = EditorGUILayout.Foldout(foldout, "Scale (크기)", true);

            if (foldout)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.PropertyField(useLogScaleProp, new GUIContent("로그 스케일"));

                var uniformProp = scaleProp.FindPropertyRelative("useUniform");
                EditorGUILayout.PropertyField(uniformProp, new GUIContent("균일 설정"));

                EditorGUILayout.Space(5);

                if (uniformProp.boolValue)
                {
                    DrawAxisSettings(scaleProp.FindPropertyRelative("x"), "All Axes");
                }
                else
                {
                    DrawAxisSettings(scaleProp.FindPropertyRelative("x"), "X");
                    DrawAxisSettings(scaleProp.FindPropertyRelative("y"), "Y");
                    DrawAxisSettings(scaleProp.FindPropertyRelative("z"), "Z");
                }

                EditorGUILayout.Space(5);

                if (useLog)
                {
                    EditorGUILayout.LabelField("로그 양자화 사용 중 (비선형 분포)", EditorStyles.miniLabel);
                }

                var error = settings.GetChannel(0);
                EditorGUILayout.LabelField($"예상 오차: ±{error.MaxError:F4} ({error.MaxError / error.Range * 100:F2}%)", EditorStyles.miniLabel);

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawSummary(QuantizationPreset preset)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField("Summary", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Transform Size:", GUILayout.Width(120));
            EditorGUILayout.LabelField($"{preset.TransformByteSize} bytes", EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Rigidbody Size:", GUILayout.Width(120));
            EditorGUILayout.LabelField($"{preset.RigidbodyByteSize} bytes", EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            float originalTransform = 40f;
            float originalRigidbody = 24f;
            float transformSavings = (1f - preset.TransformByteSize / originalTransform) * 100f;
            float rigidbodySavings = (1f - preset.RigidbodyByteSize / originalRigidbody) * 100f;

            EditorGUILayout.LabelField($"Transform 압축률: {transformSavings:F1}% 절감 (원본 40 bytes)", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Rigidbody 압축률: {rigidbodySavings:F1}% 절감 (원본 24 bytes)", EditorStyles.miniLabel);

            EditorGUILayout.EndVertical();
        }

        private void DrawPreview(QuantizationPreset preset)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            showPreview = EditorGUILayout.Foldout(showPreview, "Error Preview (오차 미리보기)", true);

            if (showPreview)
            {
                var error = preset.GetErrorInfo();

                EditorGUILayout.LabelField($"Position: ±({error.positionError.x:F4}, {error.positionError.y:F4}, {error.positionError.z:F4})");
                EditorGUILayout.LabelField($"Rotation: ±{error.rotationError:F2}°");
                EditorGUILayout.LabelField($"Scale: ±({error.scaleError.x:F4}, {error.scaleError.y:F4}, {error.scaleError.z:F4})");
                EditorGUILayout.LabelField($"Velocity: ±({error.velocityError.x:F4}, {error.velocityError.y:F4}, {error.velocityError.z:F4})");
                EditorGUILayout.LabelField($"AngVel: ±({error.angularVelocityError.x:F4}, {error.angularVelocityError.y:F4}, {error.angularVelocityError.z:F4})");

                EditorGUILayout.Space(5);

                int objectCount = 100;
                int fps = 60;
                int seconds = 600;

                float transformPerSecond = preset.TransformByteSize * objectCount * fps;
                float totalMB = transformPerSecond * seconds / (1024f * 1024f);

                EditorGUILayout.LabelField($"예상 용량 ({objectCount}개, {fps}fps, {seconds / 60}분): {totalMB:F1} MB", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawButtons()
        {
            EditorGUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("FPS 프리셋 적용"))
            {
                ApplyBuiltInPreset(QuantizationPreset.CreateFPSPreset());
            }

            if (GUILayout.Button("Racing 프리셋 적용"))
            {
                ApplyBuiltInPreset(QuantizationPreset.CreateRacingPreset());
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Space 프리셋 적용"))
            {
                ApplyBuiltInPreset(QuantizationPreset.CreateSpacePreset());
            }

            if (GUILayout.Button("VR 프리셋 적용"))
            {
                ApplyBuiltInPreset(QuantizationPreset.CreateVRPreset());
            }

            if (GUILayout.Button("Micro 프리셋 적용"))
            {
                ApplyBuiltInPreset(QuantizationPreset.CreateMicroPreset());
            }

            EditorGUILayout.EndHorizontal();
        }

        private void ApplyBuiltInPreset(QuantizationPreset source)
        {
            Undo.RecordObject(target, "Apply Built-in Preset");

            var targetPreset = (QuantizationPreset)target;

            EditorUtility.CopySerialized(source, targetPreset);

            DestroyImmediate(source);

            EditorUtility.SetDirty(target);
        }
    }

    /// <summary>양자화 오버라이드 커스텀 에디터입니다.</summary>
    [CustomEditor(typeof(QuantizationOverride))]
    public class QuantizationOverrideEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var overrideComp = (QuantizationOverride)target;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Quantization Override", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("이 컴포넌트로 오브젝트별 양자화 설정을 오버라이드할 수 있습니다.", MessageType.Info);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            DrawDefaultInspector();

            EditorGUILayout.Space(10);

            if (overrideComp.Preset != null)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("할당된 프리셋 정보", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"이름: {overrideComp.Preset.PresetName}");
                EditorGUILayout.LabelField($"설명: {overrideComp.Preset.Description}");
                EditorGUILayout.EndVertical();
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
