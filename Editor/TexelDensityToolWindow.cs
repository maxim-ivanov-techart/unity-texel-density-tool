using UnityEditor;
using UnityEngine;

public class TexelDensityToolWindow : EditorWindow
{
	private GameObject _targetObject;
	private int _textureResolution = 2048;
	private string _resultText = "";
	private Vector2 _scroll;
	private float _targetTexelDensity = 2048f;

	private const float TOLERANCE_PERCENT = 10f;

	[MenuItem("Tools/Texel Density/Calculator")]
	public static void Open()
	{
		GetWindow<TexelDensityToolWindow>("Texel Density");
	}
	
	public void OnGUI()
	{
		EditorGUILayout.LabelField("Texel Density Calculator", EditorStyles.boldLabel);
		_targetObject = (GameObject)EditorGUILayout.ObjectField(
			"Target Object", _targetObject, typeof(GameObject), true);
		
		_textureResolution = EditorGUILayout.IntPopup(
			"Texture Resolution", _textureResolution,
			new[] { "512", "1024", "2048", "4096" },
			new[] { 512, 1024, 2048, 4096 });

		_targetTexelDensity = EditorGUILayout.FloatField(
			"Target Texel Density (px/m)", _targetTexelDensity);

		GUILayout.Space(10);

		using (new EditorGUI.DisabledScope(_targetObject == null))
		{
			if (GUILayout.Button("Calculate"))
			{
				Calculate();
			}
		}
		
		GUILayout.Space(10);
		
		if (string.IsNullOrEmpty(_resultText))
		{
			return;
		}
		
		EditorGUILayout.LabelField("Result",  EditorStyles.boldLabel);
		_scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(200));
		EditorGUILayout.TextArea(_resultText);
		EditorGUILayout.EndScrollView();
	}

	private void Calculate()
	{
		if (_targetObject == null)
		{
			return;
		}
		
		_resultText = "";
		float totalWorldArea = 0f;
		float totalUvArea = 0f;
		int validMeshCount = 0;
		
		MeshFilter[] meshFilters = _targetObject.GetComponentsInChildren<MeshFilter>();
		if (meshFilters.Length == 0)
		{
			Debug.LogWarning("No MeshFilters found under target object.");
			return;
		}

		foreach (MeshFilter meshFilter in meshFilters)
		{
			if (meshFilter.sharedMesh == null)
			{
				continue;
			}

			Mesh mesh = meshFilter.sharedMesh;

			float worldArea = CalculateWorldArea(mesh, meshFilter.transform.localToWorldMatrix);
			float uvArea = CalculateUvArea(mesh);

			if (worldArea <= 0f || uvArea <= 0f)
			{
				Debug.LogWarning($"[{meshFilter.gameObject.name}] Invalid area (World:{worldArea}, UV:{uvArea})");
				continue;
			}

			totalWorldArea += worldArea;
			totalUvArea += uvArea;
			validMeshCount++;
			float texelDensity = _textureResolution * Mathf.Sqrt(uvArea / worldArea);

			string status = GetTexelDensityStatus(
				texelDensity,
				_targetTexelDensity,
				TOLERANCE_PERCENT
			);
			
			_resultText +=
				$"{meshFilter.gameObject.name} | " + 
				$"TD = {texelDensity:F2} px/m | " + 
				$"Target = {_targetTexelDensity:F0} | " + 
				$"Status = {status}\n";
		}
		
		_resultText += "\n--- Overall ---\n";
		if (validMeshCount == 0 || totalWorldArea <= 0f || totalUvArea <= 0f)
		{
			_resultText += "Overall TD: N/A (no valid meshes)\n";
		}
		else
		{
			float overallTd = _textureResolution * Mathf.Sqrt(totalUvArea / totalWorldArea);
			string overallStatus = GetTexelDensityStatus(
				overallTd,
				_targetTexelDensity,
				TOLERANCE_PERCENT
			);
			
			_resultText +=
				$"Meshes counted: {validMeshCount}\n" + 
				$"Total World Area: {totalWorldArea:F3} m²\n" + 
				$"Total UV Area: {totalUvArea:F3}\n" + 
				$"Overall TD: {overallTd:F2} px/m\n" + 
				$"Target TD: {_targetTexelDensity:F0} px/m\n" + 
				$"Status: {overallStatus}\n";
		}
	}

	private static float CalculateWorldArea(Mesh mesh, Matrix4x4 localToWorld)
	{
		Vector3[] vertices = mesh.vertices;
		int[] triangles = mesh.triangles;
		double area = 0.0;
		
		for (int i = 0; i < triangles.Length; i += 3)
		{
			Vector3 triangleVertex0 = localToWorld.MultiplyPoint3x4(vertices[triangles[i]]);
			Vector3 triangleVertex1 = localToWorld.MultiplyPoint3x4(vertices[triangles[i + 1]]);
			Vector3 triangleVertex2 = localToWorld.MultiplyPoint3x4(vertices[triangles[i + 2]]);
			
			area += 0.5 * Vector3.Cross(triangleVertex1 - triangleVertex0,
				triangleVertex2 - triangleVertex0).magnitude;
		}
		return (float)area;
	}
	
	private static float CalculateUvArea(Mesh mesh)
	{
		Vector2[] uv = mesh.uv;
		if (uv == null || uv.Length == 0)
		{
			return 0f;
		}
		
		int[] triangles = mesh.triangles;
		double area = 0.0;
		
		for (int i = 0; i < triangles.Length; i += 3)
		{
			Vector2 a = uv[triangles[i + 1]] - uv[triangles[i]];
			Vector2 b = uv[triangles[i + 2]] - uv[triangles[i]];

			double cross = (double)a.x * b.y - (double)a.y * b.x;
			area += 0.5 * System.Math.Abs(cross);
		}
		return (float)area;
	}

	private string GetTexelDensityStatus(float texelDensity, float targetTexelDensity, float tolerancePercent)
	{
		float differencePercent =
			(texelDensity - targetTexelDensity) / targetTexelDensity * 100f;

		if (differencePercent < -tolerancePercent)
		{
			return "LOW";
		}
		
		return differencePercent > tolerancePercent ? "HIGH" : "OK";
	}
}
