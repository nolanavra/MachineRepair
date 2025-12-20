using MachineRepair.Fluid;
using UnityEditor;
using UnityEngine;

namespace MachineRepair.Editor
{
    [CustomEditor(typeof(PipeFlowTool))]
    public class PipeFlowToolEditor : UnityEditor.Editor
    {
        private SerializedProperty deltaP;
        private SerializedProperty length;
        private SerializedProperty diameter;
        private SerializedProperty roughness;
        private SerializedProperty preset;
        private SerializedProperty rho;
        private SerializedProperty mu;
        private SerializedProperty minorKs;
        private SerializedProperty kSum;
        private SerializedProperty qMaxHint;
        private SerializedProperty relTol;
        private SerializedProperty absTol;
        private SerializedProperty maxIters;
        private SerializedProperty solveOnValidate;

        private SerializedProperty q;
        private SerializedProperty qLmin;
        private SerializedProperty velocity;
        private SerializedProperty reynolds;
        private SerializedProperty friction;
        private SerializedProperty computedK;
        private SerializedProperty predictedDp;
        private SerializedProperty successProp;
        private SerializedProperty lastError;

        private void OnEnable()
        {
            deltaP = serializedObject.FindProperty("deltaP_Pa");
            length = serializedObject.FindProperty("length_m");
            diameter = serializedObject.FindProperty("diameter_m");
            roughness = serializedObject.FindProperty("roughness_m");

            preset = serializedObject.FindProperty("waterPreset");
            rho = serializedObject.FindProperty("rho_kgm3");
            mu = serializedObject.FindProperty("mu_Pa_s");

            minorKs = serializedObject.FindProperty("minorKs");
            kSum = serializedObject.FindProperty("kSum");

            qMaxHint = serializedObject.FindProperty("qMaxHint");
            relTol = serializedObject.FindProperty("relTol");
            absTol = serializedObject.FindProperty("absTol");
            maxIters = serializedObject.FindProperty("maxIters");
            solveOnValidate = serializedObject.FindProperty("solveOnValidate");

            q = serializedObject.FindProperty("q_m3s");
            qLmin = serializedObject.FindProperty("q_Lmin");
            velocity = serializedObject.FindProperty("velocity_ms");
            reynolds = serializedObject.FindProperty("reynolds");
            friction = serializedObject.FindProperty("frictionFactor");
            computedK = serializedObject.FindProperty("computedKSum");
            predictedDp = serializedObject.FindProperty("predictedDeltaP");
            successProp = serializedObject.FindProperty("success");
            lastError = serializedObject.FindProperty("lastError");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Pipe Flow Tool", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);

            EditorGUILayout.LabelField("Inputs", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(deltaP, new GUIContent("ΔP (Pa)"));
            EditorGUILayout.PropertyField(length, new GUIContent("Length (m)"));
            EditorGUILayout.PropertyField(diameter, new GUIContent("Diameter (m)"));
            EditorGUILayout.PropertyField(roughness, new GUIContent("Roughness ε (m)"));

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Fluid", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(preset, new GUIContent("Water Preset"));
            EditorGUI.indentLevel++;
            if ((PipeFlowTool.WaterPreset)preset.enumValueIndex == PipeFlowTool.WaterPreset.Custom)
            {
                EditorGUILayout.PropertyField(rho, new GUIContent("ρ (kg/m³)"));
                EditorGUILayout.PropertyField(mu, new GUIContent("μ (Pa·s)"));
            }
            else
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.PropertyField(rho, new GUIContent("ρ (kg/m³)"));
                    EditorGUILayout.PropertyField(mu, new GUIContent("μ (Pa·s)"));
                }
            }
            EditorGUI.indentLevel--;

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Minor Losses", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(minorKs, new GUIContent("K Terms"), true);
            EditorGUILayout.PropertyField(kSum, new GUIContent("Extra ΣK"));

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Solver Tuning", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(qMaxHint, new GUIContent("qMax Hint (m³/s)"));
            EditorGUILayout.PropertyField(relTol, new GUIContent("Relative Tol"));
            EditorGUILayout.PropertyField(absTol, new GUIContent("Absolute Tol"));
            EditorGUILayout.PropertyField(maxIters, new GUIContent("Max Iterations"));
            EditorGUILayout.PropertyField(solveOnValidate, new GUIContent("Solve On Validate"));

            EditorGUILayout.Space(6f);
            if (GUILayout.Button("Solve Now"))
            {
                serializedObject.ApplyModifiedProperties();
                var tool = (PipeFlowTool)target;
                tool.Solve();
                EditorUtility.SetDirty(target);
                serializedObject.Update();
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Outputs", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(successProp, new GUIContent("Success"));
                EditorGUILayout.PropertyField(q, new GUIContent("Q (m³/s)"));
                EditorGUILayout.PropertyField(qLmin, new GUIContent("Q (L/min)"));
                EditorGUILayout.PropertyField(velocity, new GUIContent("Velocity (m/s)"));
                EditorGUILayout.PropertyField(reynolds, new GUIContent("Reynolds"));
                EditorGUILayout.PropertyField(friction, new GUIContent("Friction Factor f"));
                EditorGUILayout.PropertyField(computedK, new GUIContent("Computed ΣK"));
                EditorGUILayout.PropertyField(predictedDp, new GUIContent("Predicted ΔP (Pa)"));
                EditorGUILayout.PropertyField(lastError, new GUIContent("Last Error"));
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
