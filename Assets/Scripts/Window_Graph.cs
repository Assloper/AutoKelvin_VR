using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Window_Graph : MonoBehaviour
{
    [DebugGUIGraph(min: 0, max: 4096, r: 0, g: 1, b: 0, autoScale: true)]
    float SinField;


    void Awake()
    {

        // Set up graph properties using our graph keys
        DebugGUI.SetGraphProperties("frameRate", "FPS", 0, 200, 2, new Color(1, 0.5f, 1), false);
    }

    void Update()
    {
        // Update the fields our attributes are graphing
        SinField = Mathf.Sin(Time.time * 6);

        // Manual logging
        if (Input.GetMouseButtonDown(0))
        {
            DebugGUI.Log(string.Format(
                "Mouse clicked! ({0}, {1})"
            ));
        }

        // Manual persistent logging
        DebugGUI.LogPersistent("smoothFrameRate", "SmoothFPS: " + (1 / Time.deltaTime).ToString("F3"));
        DebugGUI.LogPersistent("frameRate", "FPS: " + (1 / Time.smoothDeltaTime).ToString("F3"));

        if (Time.smoothDeltaTime != 0)
            DebugGUI.Graph("smoothFrameRate", 1 / Time.smoothDeltaTime);
        if (Time.deltaTime != 0)
            DebugGUI.Graph("frameRate", 1 / Time.deltaTime);
    }


    void OnDestroy()
    {
        // Clean up our logs and graphs when this object is destroyed
        DebugGUI.RemoveGraph("frameRate");
        DebugGUI.RemoveGraph("fixedFrameRateSin");

        DebugGUI.RemovePersistent("frameRate");
    }
}
